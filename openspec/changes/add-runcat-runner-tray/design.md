## Context

动机见 proposal.md - Why，行为契约见 specs/runner-tray 与 specs/runner-settings。当前状态与约束：

- 托盘图标是 `App.xaml` L91-94 的 Hardcodet `TaskbarIcon` 资源（`IconSource="/Resources/icon.ico"`），`App.xaml.cs` 中 `TrayLeftMouseDown += TogglePopup` 已实现点击打开主面板；`TaskbarIcon.Icon`（`System.Drawing.Icon`）可运行时赋值，`System.Drawing.Common` 已随 Hardcodet 传递引入。
- 任务栏时钟覆盖窗口（`TaskbarClockWindow` 530 行 + `TaskbarClockFormatter` + 每秒 `_clockTimer`）待整体移除；弹窗定位 `WindowPositionHelper.PositionNearTaskbar` 自行取主显示器，与时钟窗口零耦合；天气刷新、午夜托盘提示刷新、命名管道、主题跟踪均不依赖时钟。
- RunCat365（Apache-2.0，WinForms/net9.0）的动画机制：预缓存 `List<Icon>` + 定时器 `AdvanceFrame()` 轮换 `NotifyIcon.Icon`；`ToIcon()` 手写 ICO 头把 PNG 字节直接封装（PNG-in-ICO，Vista+）；深色主题用 `Recolor()` 保留 alpha、替换 RGB；帧速 = `500 / max(1, load/5 × fpsRate)` ms，每秒 `PerformanceCounter` 采样、取近 5 次均值平滑。
- 跑者资源：40 个文件夹、331 帧 PNG、共约 424 KB。两种命名并存：内置 `{name}_{i}.png`（32×32），画廊 `{name}-frame-{i}.png`（高 36px、宽 25–100px 非正方形）。无 manifest，帧序即文件名末尾数字序；帧图为深色剪影 + alpha。
- 项目约束（AGENTS.md）：Win32 P/Invoke 集中在 Core/Helpers；设置改动需同步 LoadSettings/SaveSettings/OnResetDefaults；颜色字号用主题资源键；禁止 async void；IDisposable 用 using/try-finally；csproj 为 SDK 风格，松散资源已有 `None Update + CopyToOutputDirectory` 先例（HistoryToday JSON）。

## Goals / Non-Goals

**Goals:**

- 托盘图标替换为跑者逐帧动画，帧速随 CPU 负载动态变化，深浅主题均清晰可见。
- 40 个跑者全部随仓库分发，运行时可枚举、可在设置中预览和切换。
- 干净移除任务栏时钟覆盖功能（代码、设置、主题键、测试、文档）。

**Non-Goals:**

- 不做用户自定义跑者导入（RunCat365 的 CustomRunnerRepository/编辑器窗体不移植）。
- 不做 GPU/内存速度源、FPS 上限选项、托盘右键"跑者"子菜单、小游戏彩蛋。
- 不改弹窗定位、天气、日历、森日程等既有能力。

## Decisions

### D1. 托盘宿主：保留 Hardcodet TaskbarIcon，逐帧赋值 `Icon`

动画器每帧执行 `_trayIcon.Icon = icons[current]`。备选：照搬 RunCat365 换 `System.Windows.Forms.NotifyIcon`——被否决：需给 WPF 工程加 `UseWindowsForms`，且现有左键、右键菜单、ToolTip、午夜刷新逻辑全部要重写；Hardcodet 的 `Icon` 属性使动画只是属性赋值，改动面最小。`App.xaml` 的 `IconSource="/Resources/icon.ico"` 保留作启动首帧前的占位图标。

### D2. 资源分发：松散文件 CopyToOutputDirectory，运行时扫目录

331 帧 PNG 复制到 `src/CornerCalendar/Resources/Runners/<原文件夹名>/`，csproj 加 `None Update="Resources\Runners\**\*.png" CopyToOutputDirectory="PreserveNewest"`（沿用 HistoryToday JSON 模式）。枚举 = `Directory.GetDirectories` + 每目录 `*.png` 按文件名末尾整数升序（自然序，避免 frame-10 排在 frame-2 前）；两种命名模式天然兼容（都取末尾数字）。跑者标识 = 文件夹名（持久化值）；显示名 = 去掉 `-frames` 后缀、连字符转空格、首字母大写（如 `welsh-corgi-frames` → `Welsh Corgi`，`cat` → `Cat`）。备选：WPF `Resource` 嵌入 pack URI——被否决：枚举 `.g.resources` 需要 ResourceReader 且清单要另行维护，嵌入使程序集膨胀，松散文件与既有多文件制品分发方式一致，"缺帧跳过"也最直观。

### D3. 帧 → 托盘图标：移植 PNG-in-ICO 封装，重着色改用 WPF 管线

`ToIcon`：读 PNG 原始字节 + 从 IHDR 解析宽高 → 手写 22 字节 ICO 头（reserved=0、type=1、count=1、数据偏移 22）→ `new Icon(stream)`。逐帧现场转换开销大，按 RunCat365 做法**预缓存整个 Icon 列表**（每个"跑者 × 主题"组合缓存一次），切换跑者或主题时重建并 Dispose 旧列表。反白（深色主题）：用 WPF `BitmapDecoder` → `WriteableBitmap`(Bgra32) 逐像素保留 alpha、RGB 置白 → `PngBitmapEncoder` 重新编码 → 封装 ICO；不移植 RunCat 的 unsafe `Bitmap.LockBits` 版本——被否决的理由：本工程是 WPF，`AllowUnsafeBlocks` 虽已开启但 WriteableBitmap 管线无需引入 `System.Drawing.Bitmap` 像素操作，代码更安全且效果等价。

### D4. 帧速：移植 RunCat365 的 CPU 动态间隔算法

纯函数 `interval = clamp((int)(500f / Math.Max(1f, load / 5f)), 25, 500)` ms（load 为 CPU 总占用 0–100，固定 40fps 上限即 rate=1，不提供设置项）。采样：`PerformanceCounter`（首选 `Processor Information / % Processor Utility`，失败回退 `Processor / % Processor Time`，同 RunCat CPURepository 的回退逻辑），近 5 次采样均值平滑；`PerformanceCounter` 位于 Microsoft.WindowsDesktop.App 共享框架（RunCat365 零 NuGet 引用即为证据），net8.0-windows 预期可直接使用，若编译不通过则补官方 `System.Diagnostics.PerformanceCounter` 包。线程模型：采样在后台（`System.Threading.Timer`，1s），回调仅计算间隔并 `Dispatcher.BeginInvoke` 调整 UI 线程 `DispatcherTimer`（Stop → 改 Interval → Start，同 RunCat）；换帧 tick 只做一次 Icon 赋值，开销可忽略。禁止 async void，全部走定时器回调或返回 Task 的方法。采样 tick 同时把托盘 `ToolTipText` 更新为 `CPU: {load:f1}%`（与 RunCat365 的 GetInfoDescription 一致）；原"午夜刷新日期提示"逻辑（`_midnightTimer`/`ScheduleMidnightTrayRefresh`/`OnMidnightTick`/`RefreshTrayIcon`）因提示不再含日期而整体移除。

### D5. 主题联动：跟随系统深浅色（非应用内主题）

托盘图标绘制在系统任务栏上，其对比度取决于**系统**深浅色而非应用内 `ThemeMode`（RunCat365 同样默认跟随系统）。动画器每秒采样 tick 顺带调用 `ThemeHelper.IsSystemDarkMode()`（注册表 `AppsUseLightTheme`）比对深浅态，翻转时重建反白/原色 Icon 缓存——不新增事件机制，1 秒内的切换延迟不可感知，实现最简。

### D6. 设置：`RunnerName` 字符串属性 + 既有三处对称模式

`AppSettings` 新增 `public string RunnerName { get; set; } = "cat";`，移除 `TaskbarTimeFormat`。System.Text.Json 反序列化默认忽略未知字段：旧 settings.json 里遗留的 `TaskbarTimeFormat` 加载即被丢弃，下次 Save 后自然消失，无需迁移代码。已保存跑者不存在时回退 `cat`。设置保存后生效路径照抄 `RefreshCalendarSettings`/`RefreshWeatherSettings` 模式：新增静态 `App.ApplyRunnerSettings()`，由 `SaveSettings` 调用，动画器热切换跑者（重建缓存、从首帧播放）。

### D7. 设置窗口"跑者"分类：WrapPanel 全量动画预览

`SettingsCategoryList` 在"显示"（6）后插入 `ListBoxItem "跑者"`（索引 7），"关于"顺移到 8；同步修改 `UpdateCategoryVisibility` 的硬编码索引映射。面板 = `ItemsControl`（`WrapPanel`）绑定跑者描述符列表（名称、显示名、帧 `BitmapImage[]` 缓存），每项为可点击卡片：`Image`（高约 36、`Stretch="Uniform"`）+ 显示名文本 + 选中态边框（用 `SelectedBrush`/`BorderBrush` 等既有主题键）。**一个共享 `DispatcherTimer`（固定 200ms 预览间隔，与托盘 CPU 帧速解耦，保证预览观感稳定）**每次 tick 推进所有卡片的帧索引；仅在"跑者"分类可见且窗口打开时运行（分类切换/窗口 Closed 即 Stop），满足"离开分类停止预览"。331 帧 BitmapImage 总量 <1 MB 解码内存，可全量缓存。备选：列表 + 单个大预览（RunCat CustomRunnerForm 式）——被否决：40 个跑者逐个点看效率低，全量同屏动画一眼可选。

### D8. "跑者"分类末尾外链与署名

`RunnerPanel` 末尾追加"跑者动画"小节：两行 Hyperlink（RunCat365 项目、RunnerGallery 资源来源），点击处理器复用既有 `OpenExternalUrl(string)`（`ProcessStartInfo UseShellExecute=true`）；样式对齐"关于"分类现有"项目地址"链接（用户反馈：署名跟随跑者功能放在跑者 tab，而非关于页）。移植自 RunCat365 的代码文件（ToIcon、间隔计算、反白思路）按 Apache-2.0 要求在文件头保留原版权声明（Copyright Takuto Nakamura）并注明出处。

### D9. 时钟移除与新增文件布局

删除：`Views/TaskbarClockWindow.xaml(.cs)`、`Core/Helpers/TaskbarClockFormatter.cs`、`Tests/TaskbarClockFormatterTests.cs`；`App.xaml.cs` 中 `_taskbarClocks`/`_clockTimer` 字段、`InitializeTaskbarClock`/`OnClockTick`/`RefreshTaskbarClock(Core)` 方法及 OnExit 清理段；`AppSettings.TaskbarTimeFormat`；SettingsWindow 的时间格式 UI 与加载/保存/重置/预览逻辑；Light/Dark.xaml 的 `TaskbarClockText(Color)/Brush`、`TaskbarClockFallback(Color)/Brush` 与 FontSizes.xaml 的 `FontSizeTaskbarClock`；AGENTS.md/README/README_zh/release.yml 文案同步。`TogglePopup(nint monitor)` 简化为无参（弹窗定位本就强制主显示器）。既有死代码 `ShowPopup`（当前已无调用者）按"不删预先存在死代码"约定保留不动。新增：`Core/Models/RunnerInfo.cs`（跑者描述符）、`Core/Helpers/RunnerLibrary.cs`（枚举/加载/显示名/反白/ToIcon，纯静态可测）、`Core/Helpers/CpuLoadMonitor.cs`（IDisposable：PerformanceCounter 双回退采样 + 近 5 次均值平滑 + CalculateInterval 纯函数）、`Core/Helpers/TrayRunnerAnimator.cs`（IDisposable：帧定时器 + 采样定时器组合 + Icon 缓存生命周期）。新文件无需改 csproj（SDK 风格自动包含），仅 PNG 资源需要。

## Risks / Trade-offs

- [非正方形宽帧（最宽 100×36）在通知区域的渲染表现不确定] → PNG-in-ICO 保留真实宽高，与 RunCat365 对同一批画廊资源的处理完全一致（画廊即为其设计）；交付前按 AGENTS.md 在 Windows 11 主显示器任务栏手工核验，若出现拉伸/裁切，退路是加载时把帧等比放到透明正方形画布再封装。
- [`PerformanceCounter` 计数器类别在个别系统被禁用/缺失] → 移植 RunCat 的双计数器回退；两者都失败时退回固定 500ms 间隔（动画仍播放，只是不随负载加速），并记录一次错误日志。
- [每秒采样 + DispatcherTimer 常驻的后台开销] → 采样为单计数器读取，换帧为一次属性赋值，均为微秒级；预览定时器仅设置窗口"跑者"分类可见时运行。
- [331 个 PNG 进 git 使仓库体积 +约 424 KB] → 一次性成本，可接受；不引入 LFS。
- [移除时钟后用户预期变化（BREAKING）] → README/Release notes 明示；原生任务栏时钟恢复为系统行为，无需清理动作（原 `EnsureSystemClockState` 的恢复逻辑在 OnExit 路径删除后，需确认最后一次运行退出时已调用 `RestoreSystemClock`——实施顺序上先保留退出清理一次再删代码，或在升级首启时不做任何处理，由 Windows 自行重建通知区域；采用后者，因原生时钟子窗口隐藏仅在覆盖窗口存活期间生效，进程退出即恢复）。
- [画廊跑者不在 RunCat365 git 历史中，许可状态依赖 RunnerGallery 站点条款] → 用户明确指定复制并在"关于"署名来源链接；不移植、不再分发之外的使用限制随 Apache-2.0/站点条款，署名照办。

## Migration Plan

纯客户端更新，无数据迁移：旧 settings.json 的 `TaskbarTimeFormat` 被静默忽略，新增 `RunnerName` 缺省为 `cat`。发布走既有流程（改 `<Version>` → `pwsh scripts\Publish-Release.ps1`）。回滚 = 回退版本重新发布；回滚后旧版重新写入 `TaskbarTimeFormat` 默认值即可，无状态残留。
