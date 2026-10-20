#!/usr/bin/env python3
from __future__ import annotations

import gzip
import re
import urllib.request
from collections import OrderedDict
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

KEY_RE = re.compile(r"^[ぁ-ゖー]+[a-z]?$")


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
    if not raw or raw.startswith("[") or any(ord(ch) < 0x20 for ch in raw):
        return None
    return raw


def add(mapping: dict[str, list[str]], key: str, cand: str, max_per_key: int = 18) -> None:
    if not key or not cand:
        return
    vals = mapping.setdefault(key, [])
    if cand not in vals and len(vals) < max_per_key:
        vals.append(cand)


def parse_skk(text: str, mapping: dict[str, list[str]]) -> None:
    for line in text.splitlines():
        if not line or line.startswith(";"):
            continue
        try:
            key, body = line.split(" ", 1)
        except ValueError:
            continue
        if not KEY_RE.match(key) or "/" not in body:
            continue
        # Ordinary SKK candidate bodies are slash-delimited.
        for part in re.split(r"(?<!\\)/", body.strip().strip("/")):
            cand = clean_candidate(part)
            if cand:
                add(mapping, key, cand)


def main() -> None:
    ASSETS.mkdir(parents=True, exist_ok=True)
    mapping: dict[str, list[str]] = {}

    for name, url in SOURCES:
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

    # Deterministic gzip output.
    with OUT.open("wb") as raw:
        with gzip.GzipFile(filename="", mode="wb", fileobj=raw, mtime=0, compresslevel=9) as gz:
            for key in sorted(mapping):
                vals = mapping[key]
                line = key + "\t" + "\t".join(vals) + "\n"
                gz.write(line.encode("utf-8"))

    print(f"Wrote {OUT} with {len(mapping):,} readings ({OUT.stat().st_size:,} bytes)")


if __name__ == "__main__":
    main()
