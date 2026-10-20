# 23×2 Kana — Android IME test build

A real Android `InputMethodService` prototype for the 23×2 direct-kana QWERTY layout.

## Core layout

Board 1 (23 base positions):

`あいうえお / かきくけこ / さしすせそ / たちつてと / やゆよ`

Board 2:

`なにぬねの / はひふへほ / まみむめも / らりるれろ / わをん`

The positions are QWERTY letters excluding Z/X/C:

- Q W E R T Y U I O P
- A S D F G H J K L
- V B N M

Operators:

- **Z**: board 1 ↔ board 2
- **X**: dakuten transformation
- **C on board 1**: full-size ↔ small kana
- **C on board 2**: handakuten transformation
- **Shift**: hiragana ↔ katakana

Dakuten / handakuten / small-kana operations are reversible. Example: `は →(X) ば →(X) は`; `は →(C on board 2) ぱ →(C) は`; `ぱ →(X) ば`; `ば →(C) ぱ`.

## Included test features

- System-wide Android IME, not an in-app fake keyboard.
- Kana → Kanji candidates and prefix prediction.
- Personal candidate ranking and recent-word / next-word suggestions stored locally.
- SKK large dictionary + JIS level 3/4 rare-kanji supplement + proper-noun supplement.
- A small editable modern-term seed list.
- Emoji panel.
- Local clipboard history (30 items; password fields and clips marked sensitive are excluded).
- 3×4 numeric keypad.
- Japanese, mathematical, arrows, shapes, units/currency, Greek and IPA/kana-extension symbol panels.
- No `INTERNET` permission.

## Candidate-engine scope in 0.1

This is deliberately a **testable first conversion engine**, not a Mozc/Gboard-equivalent morphological or neural IME. It uses exact SKK readings, generated common conjugation forms, longest-match phrase segmentation, prefix prediction and local user learning. Rare readings and names are intentionally retained. Long sentences, ambiguous particles and irregular inflections can still rank worse than a production Japanese IME.

## Build

The repository contains a GitHub Actions workflow. On GitHub, open **Actions → Build APK → Run workflow**; the artifact `Kana23x2-debug-apk` contains `app-debug.apk`.

Local build with Android SDK/Java 17:

```bash
gradle :app:assembleDebug
```

Then install the APK, open the launcher helper, enable **23×2 Kana**, and select it as the current input method.

## License

GPL-2.0-or-later, because the test build bundles GPL-2.0-or-later SKK dictionary data. See `THIRD_PARTY_NOTICES.md`.
