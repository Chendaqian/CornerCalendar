## Why

现有任务栏时间覆盖窗口依赖枚举原生窗口、隐藏系统时钟、屏幕取色和全屏检测等脆弱的 Win32 hack，跨 Windows 版本维护成本高；托盘图标则始终是静态的，缺乏表现力。参考开源项目 RunCat365（Apache-2.0）集成其招牌"跑者"托盘动画：托盘图标变为随 CPU 负载越跑越快的小动画，点击即可打开日历主面板；同时移除任务栏时间覆盖能力，把原生任务栏时钟还给系统。

## What Changes

- 新增托盘跑者动画：把 RunCat365 `resources\runners` 下全部 40 个跑者（3 个内置 cat/horse/parrot + 37 个 RunnerGallery 画廊跑者，共 331 帧 PNG、约 424 KB）复制进本仓库并随应用分发，托盘图标按当前选中跑者逐帧播放动画。
- 动画帧速随 CPU 负载动态变化（参考 RunCat365 的间隔计算：空闲约 500ms/帧，负载越高帧率越快，上限约 40fps），每秒采样一次 CPU 占用。
- 跑者帧图为深色剪影 + 透明通道；系统深色主题下自动重着色反白（参考 RunCat365 的 Recolor），保证在深色任务栏上可见。
- 左键单击托盘跑者打开主日历面板（沿用现有 `TrayLeftMouseDown → TogglePopup` 行为），托盘右键菜单（设置/重启/退出）保持不变。
- 新增设置项：当前选中跑者（`RunnerName`，默认 `cat`），持久化到 `settings.json`。
- 设置窗口新增"跑者"分类：以实时动画预览全部跑者，点击即可选中当前跑者，随"应用/保存"持久化。
- 设置窗口"跑者"分类末尾新增两个外部链接：RunCat365 项目 `https://github.com/runcat-dev/RunCat365` 与跑者资源来源 `https://runcat-dev.github.io/RunnerGallery/`。
- **BREAKING** 移除任务栏时间覆盖窗口功能：删除 `TaskbarClockWindow`、`TaskbarClockFormatter` 及其测试；移除 `TaskbarTimeFormat` 设置项、设置窗口"显示"分类中的"任务栏时间格式"输入与预览、相关主题资源键；所有显示器恢复系统原生任务栏时钟与通知中心。

## Capabilities

### New Capabilities

- `runner-tray`: 托盘跑者动画图标——跑者资源分发与枚举、逐帧动画播放、CPU 负载动态帧速、深色主题反白、左键点击打开主面板、退出清理，以及任务栏时间覆盖窗口的移除。
- `runner-settings`: 设置窗口跑者预览与选择——"跑者"分类的实时动画预览、点击选中与持久化、分类末尾的 RunCat365 与 RunnerGallery 外部链接、任务栏时间格式设置项的移除。

### Modified Capabilities

无（仓库尚无基线 specs，本变更全部为新增能力）。

## Impact

- `App.xaml` / `App.xaml.cs`：托盘图标接入跑者动画器；移除任务栏时钟的初始化、每秒刷新、退出清理；`TogglePopup` 不再需要显示器参数。
- `Core/Helpers`：新增跑者动画器（帧加载、PNG-in-ICO 图标缓存、动画定时器、CPU 采样与间隔计算、主题反白）。
- `Core/Services/AppSettings.cs`：新增 `RunnerName`，移除 `TaskbarTimeFormat`。
- `Views/SettingsWindow.xaml(.cs)`：新增"跑者"分类（后续分类索引后移），移除任务栏时间格式 UI 与逻辑，"关于"分类新增外链。
- `Views/Themes/Light.xaml`、`Dark.xaml`、`FontSizes.xaml`：删除 `TaskbarClockTextBrush`、`TaskbarClockFallbackBrush`、`FontSizeTaskbarClock` 等仅时钟使用的键。
- `CornerCalendar.csproj`：331 帧 PNG 以 `CopyToOutputDirectory` 松散文件分发（沿用 HistoryToday JSON 模式），制品体积增加约 424 KB。
- 测试：删除 `TaskbarClockFormatterTests.cs`；新增跑者资源枚举、帧序解析和 CPU 间隔计算的回归测试。
- 文档与流程：`AGENTS.md`、`README.md`、`README_zh.md`、`.github/workflows/release.yml` 中任务栏时钟相关描述同步更新。
- 许可与署名：RunCat365 代码为 Apache-2.0，移植代码保留版权声明；跑者帧图来源在设置"关于"页以链接署名。
