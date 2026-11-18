#!/usr/bin/env python3
"""Package a device build for SideStore, or verify the resulting unsigned IPA.

This checks bundle structure and resources, not Apple signing or installability.
It deliberately rejects simulator binaries, even arm64 simulator binaries.
"""
import argparse
from pathlib import Path, PurePosixPath
import plistlib
import stat
import struct
import tempfile
import zipfile


def verify_device_executable(data: bytes, name: str) -> None:
    """Accept arm64 iOS executables, including universal Mach-O containers."""
    # Xcode device builds normally contain a thin, little-endian arm64 Mach-O.
    if data[:4] in (b"\xca\xfe\xba\xbe", b"\xca\xfe\xba\xbf"):
        if len(data) < 8:
            raise ValueError(f"Truncated universal executable: {name}")
        count = struct.unpack_from(">I", data, 4)[0]
        is_64 = data[:4] == b"\xca\xfe\xba\xbf"
        entry_size = 32 if is_64 else 20
        if not count or 8 + count * entry_size > len(data):
            raise ValueError(f"Invalid universal executable: {name}")
        for index in range(count):
            position = 8 + index * entry_size
            offset, size = struct.unpack_from(">QQ" if is_64 else ">II", data, position + 8)
            if offset < 8 + count * entry_size or size == 0 or offset + size > len(data):
                raise ValueError(f"Invalid universal executable slice: {name}")
            verify_device_executable(data[offset:offset + size], name)
        return
    if len(data) < 32 or data[:4] != b"\xcf\xfa\xed\xfe":
        raise ValueError(f"Not a 64-bit Mach-O executable: {name}")
    _, cpu, _, kind, command_count, command_bytes, _, _ = struct.unpack_from("<8I", data)
    if cpu != 0x0100000C or kind != 2:
        raise ValueError(f"Expected an arm64 iOS executable: {name}")
    end = 32 + command_bytes
    if end > len(data):
        raise ValueError(f"Truncated Mach-O load commands: {name}")
    position = 32
    platforms = []
    for _ in range(command_count):
        if position + 8 > end:
            raise ValueError(f"Truncated Mach-O load command: {name}")
        command, size = struct.unpack_from("<II", data, position)
        if size < 8 or position + size > end:
            raise ValueError(f"Invalid Mach-O load command: {name}")
        if command == 0x32:  # LC_BUILD_VERSION: PLATFORM_IOS = 2, simulator = 7.
            if size < 24:
                raise ValueError(f"Invalid build-version command: {name}")
            platforms.append(struct.unpack_from("<I", data, position + 8)[0])
        elif command == 0x25:  # LC_VERSION_MIN_IPHONEOS, for older toolchains.
            if size < 16:
                raise ValueError(f"Invalid minimum-version command: {name}")
            platforms.append(2)
        position += size
    if position != end or not platforms or any(platform != 2 for platform in platforms):
        raise ValueError(f"Executable must target iOS devices, not a simulator: {name}")


def _verify_contents(read, modes: dict[str, int], app_name: str,
                     require_full_dictionary: bool) -> dict:
    def info(bundle: str, kind: str) -> dict:
        try:
            value = plistlib.loads(read(f"{bundle}/Info.plist"))
        except (plistlib.InvalidFileException, KeyError, FileNotFoundError) as error:
            raise ValueError(f"Missing or invalid Info.plist: {bundle}") from error
        if not isinstance(value, dict) or value.get("CFBundlePackageType") != kind:
            raise ValueError(f"Invalid bundle type: {bundle}")
        for key in ("CFBundleIdentifier", "CFBundleExecutable", "CFBundleVersion",
                    "CFBundleShortVersionString"):
            if not isinstance(value.get(key), str) or not value[key] or "$(" in value[key]:
                raise ValueError(f"Missing or unresolved {key}: {bundle}")
        executable = value["CFBundleExecutable"]
        if PurePosixPath(executable).name != executable or executable in (".", ".."):
            raise ValueError(f"Invalid executable name: {bundle}")
        name = f"{bundle}/{executable}"
        if not modes.get(name, 0) & 0o111:
            raise ValueError(f"Missing executable or execute permission: {name}")
        verify_device_executable(read(name), name)
        if value.get("CFBundleSupportedPlatforms") != ["iPhoneOS"]:
            raise ValueError(f"Bundle must be built for iPhoneOS: {bundle}")
        return value

    root = f"Payload/{app_name}"
    extension_root = f"{root}/PlugIns/KanaKeyboard.appex"
    host = info(root, "APPL")
    extension = info(extension_root, "XPC!")
    if not extension["CFBundleIdentifier"].startswith(host["CFBundleIdentifier"] + "."):
        raise ValueError("Keyboard Bundle ID must begin with the host Bundle ID plus '.'")
    for key in ("CFBundleVersion", "CFBundleShortVersionString"):
        if host[key] != extension[key]:
            raise ValueError(f"Host and keyboard {key} must match")
    metadata = extension.get("NSExtension", {})
    if (not isinstance(metadata, dict)
            or metadata.get("NSExtensionPointIdentifier") != "com.apple.keyboard-service"
            or not metadata.get("NSExtensionPrincipalClass")):
        raise ValueError("Embedded extension is not a configured keyboard service")
    readings = 0
    japanese = False
    for dictionary in ("skk_compact.tsv", "modern_terms.tsv"):
        name = f"{extension_root}/{dictionary}"
        try:
            rows = read(name).decode("utf-8").splitlines()
        except (KeyError, FileNotFoundError, UnicodeDecodeError) as error:
            raise ValueError(f"Missing or invalid UTF-8 dictionary: {name}") from error
        if not rows:
            raise ValueError(f"Empty dictionary: {name}")
        previous = None
        for row in rows:
            fields = row.split("\t")
            if len(fields) < 2 or any(not field for field in fields):
                raise ValueError(f"Invalid dictionary row: {name}")
            key = fields[0].encode("utf-8")
            if dictionary == "skk_compact.tsv":
                if previous is not None and key <= previous:
                    raise ValueError("SKK dictionary readings must be unique and UTF-8 sorted")
                previous = key
                readings += 1
                japanese |= fields[0] == "にほん" and "日本" in fields[1:]
    if require_full_dictionary and (readings < 100_000 or not japanese):
        raise ValueError("Full SKK dictionary required: at least 100,000 readings and にほん → 日本")
    return {"app": app_name, "bundle_id": host["CFBundleIdentifier"],
            "version": host["CFBundleShortVersionString"], "dictionary_readings": readings}


def verify_ipa(path: Path, require_full_dictionary: bool = False) -> dict:
    """Verify actual archived bytes and UNIX executable permissions without extracting."""
    with zipfile.ZipFile(path) as archive:
        entries = archive.infolist()
        names = [entry.filename for entry in entries]
        if len(names) != len(set(names)):
            raise ValueError("IPA contains duplicate archive entries")
        apps = set()
        modes = {}
        for entry in entries:
            name = entry.filename
            parts = PurePosixPath(name).parts
            if (not parts or parts[0] != "Payload" or ".." in parts
                    or "\\" in name or name != "/".join(parts) + ("/" if entry.is_dir() else "")):
                raise ValueError(f"Invalid IPA path: {name}")
            if len(parts) == 1:
                if not entry.is_dir():
                    raise ValueError("Payload must be a directory")
                continue
            if not parts[1].endswith(".app"):
                raise ValueError(f"Unexpected Payload entry: {name}")
            apps.add(parts[1])
            mode = entry.external_attr >> 16
            if stat.S_ISLNK(mode):
                raise ValueError(f"IPA contains unsupported symlink: {name}")
            if not entry.is_dir():
                modes[name] = mode
        if len(apps) != 1:
            raise ValueError("IPA must contain exactly one Payload/*.app bundle")
        bad = archive.testzip()
        if bad:
            raise ValueError(f"Corrupt IPA entry: {bad}")
        return _verify_contents(archive.read, modes, apps.pop(), require_full_dictionary)


def package(app: Path, output: Path, require_full_dictionary: bool = False) -> dict:
    app = app.resolve()
    output = output.resolve()
    if app.suffix != ".app" or not app.is_dir():
        raise ValueError("--app must be a built device .app directory")
    if output.suffix != ".ipa" or output.is_relative_to(app):
        raise ValueError("--output must be an .ipa path outside the input app")
    paths = [app, *sorted(app.rglob("*"))]
    for path in paths:
        if path.is_symlink() or not (path.is_dir() or path.is_file()):
            raise ValueError(f"Unsupported symlink or special file in app: {path}")
    output.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.NamedTemporaryFile(dir=output.parent, suffix=".ipa", delete=False) as temporary:
        temporary_path = Path(temporary.name)
    try:
        with zipfile.ZipFile(temporary_path, "w", compression=zipfile.ZIP_DEFLATED) as archive:
            for path in paths:
                archive.write(path, "Payload/" + path.relative_to(app.parent).as_posix())
        summary = verify_ipa(temporary_path, require_full_dictionary)
        temporary_path.replace(output)
        return summary
    finally:
        temporary_path.unlink(missing_ok=True)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    mode = parser.add_mutually_exclusive_group(required=True)
    mode.add_argument("--app", type=Path, help="Built Release-iphoneos/Kana23x2.app")
    mode.add_argument("--verify", type=Path, help="Verify an existing IPA without extracting it")
    parser.add_argument("--output", type=Path, help="Output .ipa (required with --app)")
    parser.add_argument("--require-full-dictionary", action="store_true",
                        help="Reject seed-only dictionaries; require 100,000 readings and にほん → 日本")
    args = parser.parse_args()
    if (args.app is not None) != (args.output is not None):
        parser.error("--output is required with --app and cannot be used with --verify")
    try:
        if args.app:
            result = package(args.app, args.output, args.require_full_dictionary)
        else:
            result = verify_ipa(args.verify, args.require_full_dictionary)
    except (OSError, ValueError, zipfile.BadZipFile) as error:
        parser.exit(1, f"IPA validation failed: {error}\n")
    print(f"Verified {args.output or args.verify}: {result['bundle_id']} "
          f"v{result['version']}; {result['dictionary_readings']:,} dictionary readings")
    print("Unsigned packaging does not sign the app. SideStore must sign the host and keyboard extension.")


if __name__ == "__main__":
    main()
