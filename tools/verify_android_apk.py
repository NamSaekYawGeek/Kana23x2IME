#!/usr/bin/env python3
"""Verify the offline dictionary in the APK that will actually be distributed."""
import gzip
from pathlib import Path
import sys
import zipfile


def verify(apk: Path) -> None:
    with zipfile.ZipFile(apk) as archive:
        corrupt = archive.testzip()
        if corrupt:
            raise ValueError(f"Corrupt APK entry: {corrupt}")
        # AAPT may decompress .gz assets and remove the suffix during packaging.
        if "assets/skk_compact.tsv" in archive.namelist():
            data = archive.read("assets/skk_compact.tsv")
        else:
            data = gzip.decompress(archive.read("assets/skk_compact.tsv.gz"))
    rows = data.decode("utf-8").splitlines()
    if len(rows) < 100_000:
        raise ValueError("The release APK must contain the full SKK dictionary")
    japanese = next((row.split("\t")[1:] for row in rows if row.startswith("にほん\t")), [])
    if "日本" not in japanese:
        raise ValueError("Missing expected にほん → 日本 dictionary entry")
    print(f"APK dictionary verified: {len(rows):,} readings; にほん → 日本 available")


if __name__ == "__main__":
    verify(Path(sys.argv[1]))
