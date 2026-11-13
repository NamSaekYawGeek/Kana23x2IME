# 23×2 Kana — Android & iOS keyboard prototypes

A real Android `InputMethodService` and iOS keyboard extension for the 23×2 direct-kana QWERTY layout. Both platforms use offline SKK dictionary data. These are test builds, not production Japanese conversion engines.

## iOS 测试版

`ios/` 包含安装说明 App、系统键盘扩展和可独立测试的 Swift 转换核心，支持 iOS 15 及以上。保留与 Android 相同的 23×2 键位、Z/X/C 操作、平片假名切换、长音、离线汉字候选、数字、符号与表情。

- 待转换的假名显示在键盘顶部；点假名行原样确定，点候选或空格转换确定。回车先确定假名，再按一次换行。
- 移动光标、切换输入框或隐藏键盘会丢弃尚未确定的假名，请先确认需要保留的输入。
- 不需要“允许完全访问”，不联网。iOS 首版尚未实现 Android 的个人学习、最近词和剪贴板历史。
- iOS 的密码、部分电话号码输入框，以及禁用第三方键盘的 App 会使用系统键盘。

GitHub **Actions → Build iOS** 构建模拟器 App 和未签名真机 App，产物名为 `Kana23x2-iOS`。分支推送和 PR 也会触发检查。未签名 `.app` **不能直接安装到 iPhone，也不是可直接安装的 IPA**；真机测试需在 Mac 的 Xcode 中为 App 和扩展选择自己的签名 Team，必要时修改两者的 bundle ID（扩展 ID 保持 App ID 前缀）。

Mac / Xcode 16+ 本地构建：

```bash
brew install xcodegen
python3 tools/build_dictionary.py --output ios/Keyboard/Resources/skk_compact.tsv
swift test --package-path ios
xcodegen generate --spec ios/project.yml
open ios/Kana23x2.xcodeproj
```

在 Xcode 选择 `Kana23x2` scheme 和设备后运行。随后到 **设置 → 通用 → 键盘 → 键盘 → 添加新键盘 → 23×2 Kana**，在输入框长按 🌐 切换。模拟器产物解压后可用 `xcrun simctl install booted Kana23x2.app` 安装；若软键盘不显示，关闭模拟器的硬件键盘连接。

仅离线开发可在生成词典命令中添加 `--seed-only`；它只包含少量现代词，不代表完整词典转换能力。完整词典使用内存映射及按字节二分查找，避免在 iOS 扩展内创建庞大的字典对象树。

## Android 输入修复

本次修复包括：最后一个组合假名删不掉、补充平面字符（Emoji / 罕见汉字）删除破坏代理对、移动光标后复用旧组合、重建输入视图丢失组合状态、长音 `ー` 过早提交、分解形式的浊音/半浊音被归一化抹掉，以及密码/禁用个性化学习字段仍记录候选。敏感剪贴板标记和清空历史行为也已修正。

回归检查：

```bash
python3 -m unittest discover -s tools -p 'test_*.py' -v
swift test --package-path ios
gradle :app:testDebugUnitTest :app:assembleDebug
```

真机验收建议：测试 `か → が → か`、`は → ぱ → ば`、小假名往返、`コーヒー` 连续转换、删除最后一个假名/Emoji、移动光标后继续输入、切换输入框、横竖屏和 🌐 切换键盘。iOS 字符组合删除与 Android 的单码点删除范围不同；Android 复杂家庭 Emoji 等多码点序列尚不保证整组删除。

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

## Android included test features

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
