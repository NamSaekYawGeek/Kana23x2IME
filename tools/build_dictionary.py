#!/usr/bin/env python3
from __future__ import annotations

import argparse
import gzip
import re
import unicodedata
import urllib.request
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
ASSETS = ROOT / "app" / "src" / "main" / "assets"
OUT = ASSETS / "skk_compact.tsv.gz"
MODERN = ASSETS / "modern_terms.tsv"

SOURCES = [
    ("SKK-JISYO.L", "https://raw.githubusercontent.com/skk-dev/dict/master/SKK-JISYO.L"),
    ("SKK-JISYO.JIS3_4", "https://raw.githubusercontent.com/skk-dev/dict/master/SKK-JISYO.JIS3_4"),
    ("SKK-JISYO.propernoun", "https://raw.githubusercontent.com/skk-dev/dict/master/SKK-JISYO.propernoun"),
]

KEY_RE = re.compile(r"^[ぁ-ゖー\u3099\u309a]+[a-z]?$")


def decode_skk(data: bytes) -> str:
    for enc in ("euc_jis_2004", "euc_jp", "utf-8"):
        try:
            return data.decode(enc)
        except UnicodeDecodeError:
            pass
    return data.decode("euc_jp", errors="replace")


def clean_candidate(raw: str) -> str | None:
    raw = raw.strip()
    if not raw or raw.startswith("("):
        return None
    # SKK annotation follows an unescaped semicolon.
    raw = re.split(r"(?<!\\);", raw, maxsplit=1)[0]
    raw = raw.replace(r"\;", ";").replace(r"\/", "/")
    # Drop dynamic/Lisp-ish candidates and controls.
    if not raw or raw.startswith(("[", "]")) or any(ord(ch) < 0x20 for ch in raw):
        return None
    return raw


def add(mapping: dict[str, list[str]], key: str, cand: str, max_per_key: int = 18) -> None:
    # Canonically equivalent kana must share one reading without losing voice marks.
    key = unicodedata.normalize("NFC", key)
    cand = unicodedata.normalize("NFC", cand)
    if not key or not cand:
        return
    if any(ord(ch) < 0x20 for ch in key + cand):
        return
    vals = mapping.setdefault(key, [])
    if cand not in vals and len(vals) < max_per_key:
        vals.append(cand)


def ordinary_candidates(body: str):
    """Yield slash-delimited candidates, excluding SKK okuri restriction blocks.

    /書/[く/書/]/ contains only one unrestricted candidate. Splitting solely on
    slashes incorrectly publishes the closing ']' as a conversion candidate.
    """
    current: list[str] = []
    depth = 0
    escaped = False
    for ch in body:
        if escaped:
            if not depth:
                current.extend(("\\", ch))
            escaped = False
        elif ch == "\\":
            escaped = True
        elif depth:
            if ch == "[":
                depth += 1
            elif ch == "]":
                depth -= 1
        elif ch == "[" and not current:
            depth = 1
        elif ch == "/":
            yield "".join(current)
            current.clear()
        else:
            current.append(ch)
    if escaped and not depth:
        current.append("\\")
    if current and not depth:
        yield "".join(current)


def parse_skk(text: str, mapping: dict[str, list[str]]) -> None:
    for line in text.splitlines():
        if not line or line.startswith(";"):
            continue
        try:
            key, body = line.split(maxsplit=1)
        except ValueError:
            continue
        key = unicodedata.normalize("NFC", key)
        if not KEY_RE.fullmatch(key) or "/" not in body:
            continue
        for part in ordinary_candidates(body.strip()):
            cand = clean_candidate(part)
            if cand:
                add(mapping, key, cand)


def write_dictionary(mapping: dict[str, list[str]], output: Path) -> None:
    output.parent.mkdir(parents=True, exist_ok=True)
    # UTF-8 order supports byte-wise binary search in the iOS memory-mapped file.
    rows = (
        (key + "\t" + "\t".join(mapping[key]) + "\n").encode("utf-8")
        for key in sorted(mapping, key=lambda key: key.encode("utf-8"))
    )
    with output.open("wb") as raw:
        if output.suffix == ".gz":
            with gzip.GzipFile(filename="", mode="wb", fileobj=raw, mtime=0, compresslevel=9) as gz:
                gz.writelines(rows)
        else:
            raw.writelines(rows)


def main() -> None:
    parser = argparse.ArgumentParser(description="Build offline Android/iOS kana dictionaries.")
    parser.add_argument("--output", type=Path, default=OUT,
                        help="Output path; .gz writes deterministic gzip, otherwise plain UTF-8 TSV.")
    parser.add_argument("--seed-only", action="store_true",
                        help="Use only the bundled modern terms, without network downloads (smoke tests).")
    args = parser.parse_args()
    mapping: dict[str, list[str]] = {}

    for name, url in ([] if args.seed_only else SOURCES):
        print(f"Downloading {name} …", flush=True)
        req = urllib.request.Request(url, headers={"User-Agent": "Kana23x2IME-CI"})
        with urllib.request.urlopen(req, timeout=120) as r:
            data = r.read()
        print(f"  {len(data):,} bytes", flush=True)
        parse_skk(decode_skk(data), mapping)

    if MODERN.exists():
        for line in MODERN.read_text(encoding="utf-8").splitlines():
            if not line or line.startswith("#") or "\t" not in line:
                continue
            key, cand = line.split("\t", 1)
            add(mapping, key.strip(), cand.strip(), 24)

    write_dictionary(mapping, args.output)
    print(f"Wrote {args.output} with {len(mapping):,} readings ({args.output.stat().st_size:,} bytes)")


if __name__ == "__main__":
    main()
