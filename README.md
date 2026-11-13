# 23×2 Kana — Android / iOS 日语假名键盘

23×2 Kana 使用两套各 23 个位置的假名板，在熟悉的 QWERTY 布局上直接输入日语假名。Android 版基于系统输入法服务，iOS 版提供系统键盘扩展；两端均可离线进行假名到汉字的转换。

当前版本为 **v0.2.0**。这是供安装体验和继续开发的键盘原型，转换质量仍需完善，尚未完成真机交互验收。

## 下载与安装

前往 [v0.2.0 发布页面](https://github.com/NamSaekYawGeek/Kana23x2IME/releases/tag/v0.2.0) 下载对应平台的文件。

| 产物 | 用途 |
| --- | --- |
| Android APK | 调试签名的安装包，适用于 Android 8.0 及以上 |
| iOS 模拟器 ZIP | 解压后得到 `.app`，供 Mac 上的 iOS 模拟器使用 |
| iOS 未签名真机 ZIP | 解压后得到 `.app`，需自行签名后才能安装到 iPhone / iPad |

### Android

1. 下载并安装 APK；根据系统提示允许当前下载来源安装应用。
2. 打开 **23×2 Kana**，按照引导启用输入法。
3. 在系统输入法选择器中切换到 **23×2 Kana**。

发布的 APK 使用调试签名。如果系统提示签名不一致而无法覆盖安装，需要先处理旧版安装；卸载会清除本地学习和剪贴板历史。

### iOS

支持 iOS 15 及以上。**未签名真机 `.app` 不能直接安装，也不是可直接安装的 IPA。** 真机测试需要在 Mac 的 Xcode 中打开工程，为宿主 App 和键盘扩展选择自己的签名团队。必要时修改两者的 Bundle ID，扩展 ID 应保留宿主 App ID 前缀。构建步骤见下文。

安装宿主 App 后，前往 **设置 → 通用 → 键盘 → 键盘 → 添加新键盘 → 23×2 Kana**。在输入框中长按 🌐 切换键盘，无需开启“允许完全访问”。

模拟器安装包解压后，可在已启动的模拟器中安装：

```bash
xcrun simctl install booted Kana23x2.app
```

如果模拟器不显示软键盘，请关闭其硬件键盘连接。

## 键位与操作

假名位置对应 QWERTY 字母键中除 Z、X、C 之外的 23 个位置：

| 字母位置 | 第一板 | 第二板 |
| --- | --- | --- |
| Q W E R T Y U I O P | あ い う え お か き く け こ | な に ぬ ね の は ひ ふ へ ほ |
| A S D F G H J K L | さ し す せ そ た ち つ て | ま み む め も ら り る れ |
| V B N M | と や ゆ よ | ろ わ を ん |

| 操作键 | 功能 |
| --- | --- |
| **Z** | 第一板 ↔ 第二板 |
| **X** | 切换浊音 |
| **C（第一板）** | 普通假名 ↔ 小假名 |
| **C（第二板）** | 切换半浊音 |
| **Shift / ⇧** | 平假名 ↔ 片假名 |
| **ー** | 在当前输入中加入长音 |

浊音、半浊音和小假名操作可以往返切换，例如：

- `は →（X）ば →（X）は`
- `は →（第二板 C）ぱ →（第二板 C）は`
- `ぱ →（X）ば`，`ば →（第二板 C）ぱ`

### iOS 输入方式

待转换的假名显示在键盘顶部。点按假名行可原样确定，点按候选词或空格可转换确定。输入中按回车会先确定假名，再按一次才发送换行。

移动光标、切换输入框或隐藏键盘会丢弃尚未确定的假名，请先确认需要保留的内容。iOS 在密码、部分电话号码输入框，以及禁止第三方键盘的 App 中会使用系统键盘。

## 功能与平台差异

两端均支持双假名板、平片假名切换、汉字候选、读音前缀预测、数字键盘、符号和表情面板。词典由 SKK 大词典、JIS 第三级／第四级罕见汉字补充、专有名词补充和仓库内的现代词列表生成。

Android 还包括：

- 本地候选学习与排序，参考使用频率和前一个已确认词；显示最近使用的词。
- 最多 30 项本地剪贴板历史，排除密码输入期间和标记为敏感的剪贴板内容。
- 3×4 数字键盘，以及日文、数学、箭头、图形、单位／货币、希腊字母、国际音标和假名扩展符号面板。
- 未申请 `INTERNET` 权限。

iOS 首版尚未实现个人学习、最近词和剪贴板历史，不请求完全访问，也不联网。完整词典使用内存映射和二分查找，以减少键盘扩展中的内存占用。

## v0.2.0 修复内容

- 兼容 Android 打包时自动解压并改名的词典资源，避免安装后无法载入汉字候选词典；构建时检查 APK 内实际词典。
- 删除最后一个待确认假名时，正确清除输入框中的组合文本。
- 删除已确认的 Emoji／罕见汉字时，避免破坏 UTF-16 代理对，并支持删除选区。
- 移动光标后结束旧组合，避免重新插入旧内容；重建输入视图时保留当前组合状态。
- 长音 `ー` 保留在当前词中，支持 `コーヒー` 等词的连续转换。
- 读音归一化保留分解形式的浊音和半浊音。
- 密码字段隐藏候选并停止学习，遵守禁止个性化学习标志。
- 修正旧版 Android 的敏感剪贴板判断，以及清空历史后立即重新收录当前剪贴板的问题。
- 修正 SKK 词典解析，避免把送假名限制块的括号等内容作为候选词。

## 转换能力与已知限制

转换引擎使用 SKK 读音精确匹配、常见活用规则、词组分段和前缀预测；Android 额外提供本地学习排序。罕见读音与人名予以保留，但长句、助词歧义及不规则活用的转换质量仍可能明显落后于成熟的日语输入法。

Android 删除操作能够保持补充平面字符完整，但尚不保证把家庭表情等多个码点组成的 Emoji 一次整组删除。iOS 待确认文本按完整字素删除。

自动化测试和构建已验证核心逻辑与编译结果；真机仍需检查键盘启用、候选选择、删除、光标移动、输入框切换、横竖屏和 🌐 切换等交互。

## 构建与测试

以下命令均在仓库根目录执行。首次构建完整词典需要网络，生成后随安装包离线使用。生成的词典文件不提交到 Git。

### GitHub Actions

- **Actions → Build APK → Run workflow**：生成 `Kana23x2-debug-apk`，其中包含 `app-debug.apk`。
- **Actions → Build iOS → Run workflow**：生成 `Kana23x2-iOS`，其中包含模拟器和未签名真机 ZIP。

请选择包含最新修复的分支运行。旧构建记录的 **Re-run** 会沿用旧提交，不会自动切换到最新代码。

### Android 本地构建

准备 Java 17、Gradle 8.10.2 和 Android SDK 35，并配置 SDK 路径：

```bash
python3 tools/build_dictionary.py
gradle :app:testDebugUnitTest :app:assembleDebug
```

APK 位于 `app/build/outputs/apk/debug/app-debug.apk`。

### iOS 本地构建

准备 Mac 和 Xcode 16 或更新版本：

```bash
brew install xcodegen
python3 tools/build_dictionary.py --output ios/Keyboard/Resources/skk_compact.tsv
swift test --package-path ios
xcodegen generate --spec ios/project.yml
open ios/Kana23x2.xcodeproj
```

在 Xcode 中选择 `Kana23x2` 构建方案及目标设备，完成签名配置后运行。生成的 Xcode 工程不提交到 Git，以 `ios/project.yml` 为配置来源。

### 词典与核心回归测试

```bash
python3 -m unittest discover -s tools -p 'test_*.py' -v
swift test --package-path ios
gradle :app:testDebugUnitTest
```

离线开发时，可给词典生成命令添加 `--seed-only`，仅使用仓库内的少量现代词。该模式用于开发检查，不代表完整词典的转换能力。现代词源文件为 `app/src/main/assets/modern_terms.tsv`。

## 许可与词典来源

项目采用 **GPL-2.0-or-later**。构建包含由 SKK Development Team 的词典生成的数据，相关来源与声明见 [第三方数据说明](THIRD_PARTY_NOTICES.md)。

SKK 词典上游：[skk-dev/dict](https://github.com/skk-dev/dict)。
