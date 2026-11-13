"""Offline dictionary-format regressions shared by Android and iOS builds."""
import gzip
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

import build_dictionary as builder


class DictionaryTests(unittest.TestCase):
    def test_okuri_blocks_do_not_leak_candidates_or_closing_brackets(self):
        mapping = {}
        builder.parse_skk("かk /書/描;annotation/[く/書/]/[け/描/]/", mapping)
        self.assertEqual(mapping, {"かk": ["書", "描"]})

    def test_nested_and_unclosed_blocks_are_not_exposed(self):
        mapping = {}
        builder.parse_skk("かk /書/[く/[nested/漏/]/誤/]/描/\n"
                          "よm /読/[む/漏/", mapping)
        self.assertEqual(mapping, {"かk": ["書", "描"], "よm": ["読"]})

    def test_escaped_separators_and_annotations(self):
        mapping = {}
        builder.parse_skk(r"すらっしゅ /A\/B;note/C\;D/" "\n"
                          "どうてき /(concat something)/正常/", mapping)
        self.assertEqual(mapping, {"すらっしゅ": ["A/B", "C;D"], "どうてき": ["正常"]})

    def test_canonical_readings_merge_without_losing_dakuten(self):
        mapping = {}
        builder.parse_skk("か\u3099 /賀/\nが /賀/我/\nか /可/", mapping)
        self.assertEqual(mapping, {"が": ["賀", "我"], "か": ["可"]})

    def test_uncomposable_combining_marks_are_preserved(self):
        mapping = {}
        builder.add(mapping, "あ\u3099", "あ\u3099")
        self.assertEqual(mapping, {"あ\u3099": ["あ\u3099"]})

    def test_limits_and_invalid_tsv_controls(self):
        mapping = {}
        for candidate in ["一", "一", "二", "三"]:
            builder.add(mapping, "いち", candidate, max_per_key=2)
        builder.add(mapping, "かな", "broken\trow")
        self.assertEqual(mapping, {"いち": ["一", "二"]})

    def test_plain_and_gzip_are_equivalent_sorted_and_deterministic(self):
        mapping = {"ん": ["ン"], "か": ["可"], "あ": ["亜"]}
        with tempfile.TemporaryDirectory() as tmp:
            plain = Path(tmp) / "nested" / "dictionary.tsv"
            zipped = Path(tmp) / "dictionary.tsv.gz"
            second = Path(tmp) / "second.tsv.gz"
            builder.write_dictionary(mapping, plain)
            builder.write_dictionary(mapping, zipped)
            builder.write_dictionary(mapping, second)
            self.assertEqual(plain.read_text(), "あ\t亜\nか\t可\nん\tン\n")
            self.assertEqual(gzip.decompress(zipped.read_bytes()), plain.read_bytes())
            self.assertEqual(zipped.read_bytes(), second.read_bytes())

    def test_seed_only_cli_builds_without_network(self):
        with tempfile.TemporaryDirectory() as tmp:
            output = Path(tmp) / "dictionary.tsv"
            subprocess.run([sys.executable, str(Path(builder.__file__)),
                            "--seed-only", "--output", str(output)], check=True,
                           capture_output=True, text=True)
            rows = output.read_text().splitlines()
            self.assertIn("ちゃっとじーぴーてぃー\tChatGPT", rows)
            self.assertEqual(rows, sorted(rows, key=lambda row: row.encode("utf-8")))


if __name__ == "__main__":
    unittest.main()
