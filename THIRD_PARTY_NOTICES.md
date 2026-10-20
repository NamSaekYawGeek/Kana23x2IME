# Third-party data

This test build creates a compact derivative of dictionaries from the SKK Development Team's `skk-dev/dict` project during CI:

- SKK-JISYO.L
- SKK-JISYO.JIS3_4
- SKK-JISYO.propernoun

The original dictionary headers state GNU General Public License version 2 or (at your option) any later version. The compact asset preserves reading/candidate data while removing SKK annotations and limiting duplicate candidates for a mobile test build.

Upstream: https://github.com/skk-dev/dict

The GitHub Actions build downloads the upstream dictionaries and runs `tools/build_dictionary.py` before assembling the APK.
