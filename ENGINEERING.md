# LongShot 工程介绍（交接用）

## 一、这是什么

Windows 滚动长截图工具。启动后弹出小窗，选「自动」或「手动」，把当前前台窗口逐屏截图拼成一张长图，保存到 `C:\Users\LXY\Pictures\Screenshots`，然后进程退出。

单文件 C# WinForms，用系统自带的 `csc.exe` 编译成单个 exe，零第三方依赖。仓库：https://github.com/alxy1018-ctrl/longshot

## 二、当前状态

- 双模式已完成：自动（工具自己滚）、手动（用户自己滚，按 Esc 结束）。
- 自动模式有端到端验证记录：7000px 测试内容 → 20 步滚动 → 2560×8750 长图，总高与内容一致。
- 手动模式的代码和界面都完成，但真实窗口下的滚动记录没有跑过完整验证——开发收尾时前台被其他窗口占用，测试没做成。接手后建议先补这一项。
- 最近一次自动模式回归也因同样原因失败（截到了前台的游戏窗口，输出单屏 2560×1440），不是代码回归。

## 三、目录与文件

```
longshot/
├── LongShot.cs        主程序，所有逻辑都在这一个文件里
├── build.bat          编译脚本：csc 编译 + 复制到桌面
├── README.md          面向使用者的说明
├── ENGINEERING.md     本文
├── ui_preview.png     界面截图
├── demo_2560x8750.png 端到端测试产物
├── demo_small.png     上面那张的缩略图
├── .gitignore         排除 *.exe / *.log / trace.log
└── test/
    ├── TestTarget.cs  可滚动的测试窗口：7000px 色带内容，标题栏显示实时滚动位置
    ├── MatchTest.cs   位移检测的离线测试
    ├── longpage.html  长网页（150 个色块），浏览器场景测试用
    └── longtext.txt   250 行文本，记事本场景测试用
```

`LongShot.exe`、`MatchTest.exe`、`TestTarget.exe` 都是编译产物，不进仓库。

## 四、构建与部署

- 编译器：`C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe`（.NET Framework 4.x 自带）。
- 直接运行 `build.bat`。产物是 `LongShot.exe`，脚本会同时复制到 `%USERPROFILE%\Desktop\LongShot.exe`。
- 编译参数里 `/unsafe+` 和 `/codepage:65001` 不能省：Matcher 用 unsafe 读像素，源码是 UTF-8 中文。
- **运行入口是桌面副本**，原因见「已知的坑」第一条。

## 五、运行流程

1. 启动 → 小窗显示：标题「滚动长截图」、两行说明、左「自动」右「手动」、保存路径。整体按内容块居中，窗口尺寸或 DPI 变化时重算（`LayoutCentered`）。
2. 点按钮 → 窗口隐藏 → 后台线程：
   - `Sleep(700)` 等窗口消失、焦点回到目标窗口；
   - `TargetPicker.Pick()`：取前台窗口客户区；前台是桌面/任务栏或尺寸异常时回退主屏工作区；
   - 按模式调 `Stitcher.RunAuto` 或 `Stitcher.RunManual`；
   - 保存 PNG；成功后用资源管理器打开并选中文件，然后退出；失败弹错误框后退出。
3. Esc 的语义分模式：**自动模式是中止（保存已截部分），手动模式是完成记录并拼接**。这两条不要合并。

## 六、代码结构（LongShot.cs 内）

| 区块 | 职责 |
|---|---|
| `Native` | user32 P/Invoke：DPI 感知、前台窗口、客户区、SendInput 滚轮、Esc 状态、SetForegroundWindow |
| `Paths` | 输出目录；不重名文件名（重复时加 `_1`、`_2`） |
| `TargetPicker` | `Pick()`：前台窗口客户区，失败回退主屏工作区 |
| `ScreenCapture` | `CopyFromScreen` 截客户区 |
| `Matcher` | `FindShift(prev, next)`：4x 降采样粗搜 + 全分辨率 1px 精修，返回垂直位移 |
| `Stitcher` | `RunAuto`、`RunManual`、`SaveResult` |
| `MainForm` | 小窗 UI、托盘提示（手动模式记录中）、后台线程、错误框 |
| `Program` | 入口；解析 `--auto` / `--manual` 测试参数 |

## 七、关键实现细节

- **位移检测**：先用 4x 降采样灰度图在垂直方向搜索最佳对齐位置（排除右侧约 32px 的滚动条区域），再在全分辨率上精修到 1px。两帧完全相同（Score < 1.8）直接判定无变化。有效位移小于 8px 视为无变化。
- **拼接**：下一帧底部 d 行是新进入画面的内容，追加到总图末尾。顶部固定栏（浏览器地址栏之类）只出现一次。
- **自动模式**：每次滚 3 格，滚动后等 600ms 截图；匹配失败再等 350ms 重拍一次；连续 5 次无有效位移停止。格数会根据实测位移自适应调整（1–8 格）。总高上限 18000px。
- **手动模式**：不滚轮、不移动鼠标，每 220ms 截一帧；位移 ≥ 4px 才记录并增量拼接；按 Esc 结束。没有「连续失败停止」——用户可能停在某处思考。记录期间任务栏托盘显示图标。
- **高 DPI**：`Program.Main` 里先 `SetProcessDPIAware()`，125% 缩放下按物理像素 2560×1440 截图，不会糊。
- **保存**：PNG 先编码到 `MemoryStream` 再 `File.WriteAllBytes`，避免 GDI+ 直接落盘在某些环境下的失败。

## 八、已知的坑

1. **`D:\Code` 是低完整性目录**。这台机器上 DSH 的工作区被标记为低完整性，从 `D:\Code` 启动的 exe 进程是 Low IL：写不了 `C:\Users\LXY\Pictures`，滚轮注入也会被 UIPI 拦截。所以必须用桌面副本运行（`Desktop` 是正常完整性）。测试时用 WMI 或计划任务从 `D:\Code` 启动同样会中招，验证脚本要把测试程序复制到 `%TEMP%` 或桌面再跑。
2. **Esc 语义分模式**，见第五节。改停止逻辑时别把自动和手动揉成一个。
3. **保存路径是硬编码的** `C:\Users\LXY\Pictures\Screenshots`，这是需求指定的。除非用户明确要求，不要改成动态路径。
4. **浏览器平滑滚动**：Edge 实测时在第 15 步左右提前停止，输出缺了约三分之一页面。已针对平滑滚动加了等待、重拍和更宽的停止阈值，但根因没有确认，也没有复测到底。以后碰浏览器相关逻辑，先复现这个场景。
5. **手动模式未完整验证**，见第二节。它和自动模式共用 `Matcher` 与 `SaveResult`，但记录循环本身没有在真实窗口跑过。
6. **小窗布局**：`LayoutCentered` 在 Load/Resize 时按内容块整体居中，不要改回固定坐标；`AutoScaleMode.Dpi` 下硬坐标在 125% 缩放会偏。

## 九、测试现状

- `MatchTest.exe`：6 个滚动量（60–700px）误差在 1px 内；相同帧、到底重复帧报 0；无关帧报无效。
- `TestTarget.exe` + `--auto`：20 步有效位移，每步 375px（TestTarget 逻辑滚 300px × 1.25 DPI），触底后连续无变化停止，输出 2560×8750。
- GUI 按钮链路：用 `BM_CLICK` 触发「开始」，隐藏 → 截图 → 保存 → 退出全链路通过。
- 手动模式：未跑。
- 输出图程序化检查：69 个色带标签，间距 125±1px，只有触底段有约 40px 的部分帧偏差。

## 十、交接建议

1. 先跑 `MatchTest.exe` 和一次 `TestTarget` 自动模式，确认当前环境能复现 8750 的结果。
2. 补手动模式的真实窗口验证：起一个可滚动窗口，点「手动」，自己滚一段，按 Esc，看输出长图是否连续。
3. 不要动「复制到桌面运行」的部署方式和低完整性规避逻辑。
4. 改 UI 前先看 `README.md` 里的用户约定（Esc 语义、保存路径、两个按钮的文案）。
