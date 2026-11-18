"""Regression tests for SideStore archive structure and device-build validation."""
from pathlib import Path
import plistlib
import stat
import struct
import tempfile
import unittest
import zipfile

import package_ios_ipa as packer


def executable(platform=2, cpu=0x0100000C):
    command = struct.pack("<6I", 0x32, 24, platform, 0x000F0000, 0x00120000, 0)
    return struct.pack("<8I", 0xFEEDFACF, cpu, 0, 2, 1, len(command), 0, 0) + command


class IPATests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.app = self.root / "Kana23x2.app"
        self.extension = self.app / "PlugIns" / "KanaKeyboard.appex"
        self.extension.mkdir(parents=True)
        self.output = self.root / "Kana23x2-unsigned.ipa"
        for bundle, identifier, binary, kind in (
            (self.app, "dev.kana23x2.ios", "Kana23x2", "APPL"),
            (self.extension, "dev.kana23x2.ios.keyboard", "KanaKeyboard", "XPC!"),
        ):
            info = {"CFBundleIdentifier": identifier, "CFBundleExecutable": binary,
                    "CFBundlePackageType": kind, "CFBundleVersion": "2",
                    "CFBundleShortVersionString": "0.2.0",
                    "CFBundleSupportedPlatforms": ["iPhoneOS"]}
            if bundle == self.extension:
                info["NSExtension"] = {
                    "NSExtensionPointIdentifier": "com.apple.keyboard-service",
                    "NSExtensionPrincipalClass": "KanaKeyboard.KeyboardViewController"}
            (bundle / "Info.plist").write_bytes(plistlib.dumps(info, fmt=plistlib.FMT_BINARY))
            (bundle / binary).write_bytes(executable())
            (bundle / binary).chmod(0o755)
        (self.extension / "skk_compact.tsv").write_text("かな\t仮名\nにほん\t日本\n", encoding="utf-8")
        (self.extension / "modern_terms.tsv").write_text("えーあい\tAI\n", encoding="utf-8")

    def change_info(self, bundle, **updates):
        path = bundle / "Info.plist"
        info = plistlib.loads(path.read_bytes())
        info.update(updates)
        path.write_bytes(plistlib.dumps(info))

    def test_archive_has_payload_extension_dictionary_and_executable_modes(self):
        result = packer.package(self.app, self.output)
        self.assertEqual(result["dictionary_readings"], 2)
        with zipfile.ZipFile(self.output) as archive:
            root = "Payload/Kana23x2.app"
            for binary in (f"{root}/Kana23x2", f"{root}/PlugIns/KanaKeyboard.appex/KanaKeyboard"):
                self.assertEqual(stat.S_IMODE(archive.getinfo(binary).external_attr >> 16), 0o755)
            self.assertEqual(archive.read(f"{root}/PlugIns/KanaKeyboard.appex/skk_compact.tsv"),
                             "かな\t仮名\nにほん\t日本\n".encode())
        self.assertEqual(packer.verify_ipa(self.output), result)

    def test_rejects_missing_extension_and_leaves_existing_output_untouched(self):
        self.output.write_bytes(b"previous good build")
        (self.extension / "Info.plist").unlink()
        with self.assertRaisesRegex(ValueError, "Info.plist"):
            packer.package(self.app, self.output)
        self.assertEqual(self.output.read_bytes(), b"previous good build")

    def test_rejects_identifier_and_version_mismatches(self):
        for update, message in (({"CFBundleIdentifier": "dev.other.keyboard"}, "Bundle ID"),
                                ({"CFBundleVersion": "3"}, "CFBundleVersion"),
                                ({"CFBundleShortVersionString": "0.3.0"}, "CFBundleShortVersionString")):
            with self.subTest(update=update):
                original = (self.extension / "Info.plist").read_bytes()
                self.change_info(self.extension, **update)
                with self.assertRaisesRegex(ValueError, message):
                    packer.package(self.app, self.output)
                (self.extension / "Info.plist").write_bytes(original)

    def test_rejects_non_keyboard_extension(self):
        self.change_info(self.extension, NSExtension={"NSExtensionPointIdentifier": "com.apple.share-services"})
        with self.assertRaisesRegex(ValueError, "keyboard service"):
            packer.package(self.app, self.output)

    def test_rejects_simulator_even_when_arm64_and_plist_claims_iphoneos(self):
        (self.extension / "KanaKeyboard").write_bytes(executable(platform=7))
        with self.assertRaisesRegex(ValueError, "not a simulator"):
            packer.package(self.app, self.output)

    def test_rejects_simulator_plist_and_missing_execute_permission(self):
        self.change_info(self.app, CFBundleSupportedPlatforms=["iPhoneSimulator"])
        with self.assertRaisesRegex(ValueError, "iPhoneOS"):
            packer.package(self.app, self.output)
        self.change_info(self.app, CFBundleSupportedPlatforms=["iPhoneOS"])
        (self.app / "Kana23x2").chmod(0o644)
        with self.assertRaisesRegex(ValueError, "execute permission"):
            packer.package(self.app, self.output)

    def test_rejects_missing_empty_corrupt_and_unsorted_dictionary(self):
        path = self.extension / "skk_compact.tsv"
        for contents in (None, b"", b"\xff", b"invalid", "にほん\t日本\nかな\t仮名\n".encode()):
            with self.subTest(contents=contents):
                if contents is None:
                    path.unlink()
                else:
                    path.write_bytes(contents)
                with self.assertRaises(ValueError):
                    packer.package(self.app, self.output)

    def test_full_dictionary_gate_rejects_seed_but_accepts_full_fixture(self):
        with self.assertRaisesRegex(ValueError, "Full SKK"):
            packer.package(self.app, self.output, require_full_dictionary=True)
        rows = [f"a{index:06d}\t候補\n" for index in range(100_000)]
        rows.append("にほん\t日本\n")
        (self.extension / "skk_compact.tsv").write_text("".join(rows), encoding="utf-8")
        result = packer.package(self.app, self.output, require_full_dictionary=True)
        self.assertEqual(result["dictionary_readings"], 100_001)

    def test_full_dictionary_gate_requires_expected_japanese_candidate(self):
        rows = [f"a{index:06d}\t候補\n" for index in range(100_000)]
        (self.extension / "skk_compact.tsv").write_text("".join(rows), encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "にほん"):
            packer.package(self.app, self.output, require_full_dictionary=True)

    def test_rejects_symlinks_and_output_inside_bundle(self):
        (self.app / "outside").symlink_to(self.root / "secret")
        with self.assertRaisesRegex(ValueError, "symlink"):
            packer.package(self.app, self.output)
        (self.app / "outside").unlink()
        with self.assertRaisesRegex(ValueError, "outside"):
            packer.package(self.app, self.app / "nested.ipa")

    def test_verifies_archive_paths_and_does_not_extract(self):
        packer.package(self.app, self.output)
        with zipfile.ZipFile(self.output, "a") as archive:
            archive.writestr("Payload/../escaped", "not extracted")
        with self.assertRaisesRegex(ValueError, "Invalid IPA path"):
            packer.verify_ipa(self.output)
        self.assertFalse((self.root / "escaped").exists())

    def test_universal_and_truncated_macho_validation(self):
        thin = executable()
        fat = struct.pack(">7I", 0xCAFEBABE, 1, 0x0100000C, 0, 28, len(thin), 0) + thin
        packer.verify_device_executable(fat, "fat device")
        for data in (b"", thin[:12], thin[:40], executable(cpu=0x01000007), fat[:-1]):
            with self.subTest(data=data), self.assertRaises(ValueError):
                packer.verify_device_executable(data, "broken")


if __name__ == "__main__":
    unittest.main()
