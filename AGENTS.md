# CornerCalendar 项目代理指南

本文件描述 CornerCalendar 仓库的实际结构、构建流程和修改边界。所有回复使用中文，并在每次回复开头先说“喵”。更高优先级的系统或用户指令优先于本文件。

## 项目概览

CornerCalendar 是 Windows 10/11 上的任务栏日历工具：应用常驻系统托盘，覆盖任务栏时钟区域，点击后从底部弹出月历面板；面板提供农历、中国大陆法定节假日和调休、二十四节气、ICS 日程、天气摘要以及日期详情窗口。

## 技术栈

| 技术 | 用途 |
| --- | --- |
| C# 12 / .NET 8 | 应用运行时，目标框架为 `net8.0-windows` |
| WPF | 桌面 UI、动画和资源字典 |
| CommunityToolkit.Mvvm 8.3.0 | MVVM 基础设施 |
| Hardcodet.NotifyIcon.Wpf 2.0.1 | 系统托盘图标 |
| Ical.Net 4.3.1 | ICS 订阅解析 |
| Win32 / WPF interop | 定位主显示器任务栏、创建任务栏时间覆盖窗口 |
| Open-Meteo / 公网 IP 定位服务 | 天气与城市定位数据 |

## 仓库结构

```text
src/
├── CornerCalendar.sln
├── CornerCalendar.Tests/                 # xUnit 回归测试
└── CornerCalendar/
    ├── App.xaml(.cs)                     # 组合根、托盘、任务栏时钟和生命周期
    ├── Core/
    │   ├── Models/                       # CalendarDay、CalendarEvent、天气和中国日历模型
    │   ├── Services/                     # 设置、ICS、中国日历、天气和聚合服务
    │   └── Helpers/                      # Win32、主题、农历、自启动、日志和图标帮助类
    ├── ViewModels/                       # CalendarViewModel、EventListViewModel
    └── Views/
        ├── PopupWindow.xaml(.cs)         # 主月历面板、天气切换和动画
        ├── SettingsWindow.xaml(.cs)      # 分类设置窗口，显示在任务栏
        ├── EventDetailWindow.xaml(.cs)   # 点击日期打开的日详情和日程窗口
        ├── TaskbarClockWindow.xaml(.cs)  # 覆盖任务栏时间的窗口
        ├── Controls/                     # 月历、日期格、事件控件
        └── Themes/                       # Light、Dark、FontSizes 资源字典
```

## 常用命令

所有命令使用 PowerShell 7，脚本首部应设置 `$ErrorActionPreference = 'Stop'`。

```powershell
dotnet build src\CornerCalendar.sln
dotnet test src\CornerCalendar.sln
pwsh scripts\Publish-Artifacts.ps1
```

开发运行：

```powershell
.\src\CornerCalendar\bin\Debug\net8.0-windows\win-x64\CornerCalendar.exe
```

交付前至少确认：构建 0 错误 0 警告、测试通过；涉及窗口行为时还要在 Windows 上手工检查托盘、主显示器任务栏时钟、主面板、设置窗口，以及其他显示器的原生任务栏控制中心。

## 版本与发布

- `src/CornerCalendar/CornerCalendar.csproj` 的 `<Version>` 控制程序集版本；发布工作流构建 `CornerCalendar.dll` 后从 `AssemblyName.Version` 读取实际版本。
- `.github/workflows/release.yml` 是实际发布工作流，tag 只作触发器。
- tag 可以是 `v1.0.1` 或 `1.0.1`，去掉可选的 `v` 后必须与构建出的程序集版本完全一致。
- 发布工作流调用 `scripts/Publish-Artifacts.ps1`，输出 self-contained 和 framework 两种 win-x64 多文件制品。
- 修改版本的正确顺序：改 `<Version>` → 执行 `pwsh scripts\Publish-Release.ps1`。脚本会从实际构建出的 `CornerCalendar.dll` 读取程序集版本，提交并推送当前分支，自动创建并推送匹配 tag；`release.yml` 随后构建制品并创建 Release。可使用 `-WhatIf` 预览，使用 `-WaitForRelease` 等待 Release 完成。

## 关键行为约定

1. `App.xaml.cs` 是组合根，负责托盘图标、主显示器任务栏时钟覆盖层、天气后台刷新、弹窗/设置窗口生命周期和全局异常日志。
2. `AppSettings.Current` 是设置单例，文件位置为 `%LOCALAPPDATA%\CornerCalendar\settings.json`。保存使用临时文件和原子替换，不要在新代码中引入另一套设置实例或路径。
3. 中国日历由 `ChinaCalendarService` 读取 4 条远程 ICS，天气由 `WeatherService` 读取远程 API 并使用本地缓存和后台刷新；天气刷新频率可在设置中选择 30、60、120 或 240 分钟，默认 120 分钟；不要为了展示结果新增本地硬编码的节日或天气数据。
4. `SettingsWindow` 通过左侧分类导航组织设置，修改新设置时同步处理加载、保存和恢复默认值。
5. 点击日期格由 `MonthCalendar.DateClicked` 通知主面板，`EventDetailWindow` 显示该日期的日历信息和当天日程。不要重新引入基于鼠标悬浮自动弹出详情的行为。
6. 所有颜色和字号优先使用 `Themes/Light.xaml`、`Themes/Dark.xaml`、`Themes/FontSizes.xaml` 的资源键；不要在 XAML 中新增无必要的硬编码颜色或字号。
7. 任务栏时间覆盖窗口只允许创建在 Windows 标记的主显示器；其他显示器必须保留系统原生任务栏时间和通知中心。

## 数据源限制

程序只使用 ICS 作为日程数据源：内置中国日历和用户添加的日历均通过 ICS 加载。不要重新引入 Windows 系统日历数据源，也不要把系统数据伪装成模拟日程。

## 编码与异常约定

- 启用 nullable 和 implicit usings；新增代码遵循可空引用标注。
- 异步方法返回 `Task` 或 `Task<T>`，禁止新增 `async void`；WPF 事件处理器也应委托给返回 `Task` 的方法。
- 重新抛出异常使用 `throw`，禁止 `throw ex`。
- 实现 `IDisposable` 的对象使用 `using` 或 `try/finally` 释放。
- 不要在循环中执行数据库或网络 IO；远程数据请求应批量或单次完成。
- Win32 P/Invoke 集中放在 `Core/Helpers`，不在视图中散落 native 调用。
- 修改保持精确，避免顺手重构无关代码；不覆盖或回退用户已有的未提交修改。

## 已知注意事项

- `bin/`、`obj/` 和 `release/` 是生成目录，不应提交。
- `*.ics` 被 gitignore，测试不要把真实订阅样例提交进仓库。
- 日历缓存、天气缓存、设置和错误日志都位于 `%LOCALAPPDATA%\CornerCalendar\`；天气缓存文件为 `weather-cache.json`。
- 任务栏时间覆盖窗口只覆盖主显示器；退出时必须关闭覆盖窗口并保留其他显示器的系统任务栏功能。
- 许可证文件是 `LICENSE.txt`，文档为 `README.md` 和 `README_zh.md`。
