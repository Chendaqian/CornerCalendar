# AGENTS.md — CornerCalendar（WinCal / miniCal）

> 本文件面向 AI 编码代理与新贡献者，描述本仓库的项目结构、构建方式、架构要点与已知坑。
> 全局编码规范（Shell、异常、异步、行为准则）见用户级 AGENTS.md，本文件只写项目专属内容。

## 项目简介

Windows 11 任务栏日历小工具（产品名 WinCal / WinminiCal，托盘文案用 miniCal，仓库与程序集名为 CornerCalendar）。
常驻系统托盘，点击任务栏时钟区域弹出月历面板，支持农历、事件列表、事件详情、ICS 订阅，并可拦截替换 Windows 原生日历弹窗。灵感来自 macOS 的 Itsycal。

## 技术栈

| 技术 | 用途 |
|------|------|
| C# 12 + .NET 8（net8.0-windows） | 语言与运行时 |
| WPF | UI 框架 |
| CommunityToolkit.Mvvm 8.3.0 | MVVM 基础设施 |
| Hardcodet.NotifyIcon.Wpf 2.0.1 | 系统托盘图标 |
| Ical.Net 4.3.1 | ICS 日历解析 |
| SetWinEventHook（Win32 P/Invoke） | 系统日历窗口拦截 |

## 构建与验证

```powershell
# 开发构建（已验证可用）
dotnet build src\CornerCalendar.sln

# 运行调试产物
.\src\CornerCalendar\bin\Debug\net8.0-windows\win-x64\CornerCalendar.exe

# 发布单文件（Release / win-x64 / self-contained，csproj 已内置发布参数）
dotnet publish src\CornerCalendar\CornerCalendar.csproj -c Release -o dist
```

- 前置：.NET 8 SDK；目标平台仅 Windows（WPF + win-x64）。
- 仓库无单元测试项目；验证方式以「构建 0 错误 0 警告 + 手工运行验证」为准。
- ⚠️ 根目录 `build.bat` / `build_nopause.bat` / `publish.bat` 是重命名前的遗留脚本：
  `build.bat` 在仓库根目录执行 `dotnet build`（根目录无工程文件，会失败），
  `publish.bat` 仍引用旧工程名 `WinCal.csproj`（已不存在）。**请一律使用上面的 dotnet CLI 命令**，不要修复 bat 脚本除非用户明确要求。`scripts/` 目前是空目录。

## 项目结构与代码定位

```
src/
├── CornerCalendar.sln                    # 解决方案（仅一个 WPF 工程）
└── CornerCalendar/
    ├── App.xaml(.cs)                     # 组合根：托盘图标、弹窗/设置窗口生命周期、拦截器启动、全局异常
    ├── Core/
    │   ├── Models/                       # CalendarEvent / CalendarDay / CalendarAccountInfo
    │   ├── Services/                     # 日历数据服务（见下）+ AppSettings 设置持久化
    │   └── Helpers/                      # Win32 与工具类（见下）
    ├── ViewModels/
    │   ├── CalendarViewModel.cs          # 月历主逻辑（月份切换、日期格子、事件点标记）~485 行
    │   └── EventListViewModel.cs         # 近期事件列表逻辑
    └── Views/
        ├── PopupWindow.xaml(.cs)         # 弹出日历主窗口（圆角、失焦关闭）
        ├── SettingsWindow.xaml(.cs)      # 设置窗口（独立顶层、单例）
        ├── EventDetailWindow.xaml(.cs)   # 事件详情浮层
        ├── Controls/                     # MonthCalendar / DayCell / EventItem / EventDetailPopup
        └── Themes/                       # Light.xaml / Dark.xaml 资源字典
```

### Core/Services（日历数据源）

| 文件 | 职责 |
|------|------|
| `ICalendarService.cs` | 日历服务统一接口 |
| `MockCalendarService.cs` | 模拟数据（调试用） |
| `IcsCalendarService.cs` | 远程 .ics 订阅（Ical.Net 解析，定时刷新） |
| `WindowsCalendarService.cs` | 系统邮箱日历（**已 Compile Remove，未参与编译**，见下） |
| `EmptyCalendarService.cs` | 无数据源时的默认空实现 |
| `AggregateCalendarService.cs` | 多数据源聚合 |
| `AppSettings.cs` | 设置持久化（System.Text.Json） |

### Core/Helpers

| 文件 | 职责 |
|------|------|
| `SystemCalendarInterceptor.cs` | SetWinEventHook 监听 ShellExperienceHost，拦截替换系统日历弹窗 |
| `WindowPositionHelper.cs` | 面板贴近任务栏右下角定位、多显示器工作区计算 |
| `ThemeHelper.cs` | 应用主题 + 监听注册表 `AppsUseLightTheme` 跟随系统 |
| `TrayIconGenerator.cs` | 动态绘制带当日日期数字的托盘图标 |
| `LunarCalendarHelper.cs` | 农历转换 |
| `StartupHelper.cs` | 开机自启动（注册表） |

## 关键架构机制

1. **组合根在 `App.xaml.cs`**：OnStartup 中创建托盘图标（左键 `TogglePopup`、右键菜单「设置/退出」）、启动 `SystemCalendarInterceptor`（回调 `ShowPopup`）、加载 `AppSettings` 并应用主题。设置窗口经 `App.ShowSettings()` 单例打开。
2. **拦截器行为**：检测到系统日历/通知中心弹出 → 隐藏原窗口并弹出本应用面板；500ms 防抖冷却；退出时恢复被隐藏的系统窗口；面板失焦自动关闭。
3. **设置持久化路径以代码为准**：实际写入 `%LOCALAPPDATA%\miniCal\settings.json`（`AppSettings.SettingsDir`）。README/PROGRESS 中写的 WinCal、代码注释写的 CornerCalendar 均为过时描述，修改时不要随手「统一」。
4. **主题**：所有颜色走 `Themes/Light.xaml`、`Dark.xaml` 资源字典，XAML 中只引用 DynamicResource 键名，不写死色值。
5. **MVVM**：ViewModel 用 CommunityToolkit.Mvvm（ObservableObject / RelayCommand）；跨线程更新 UI 必须经 Dispatcher。

## 编码约定（本项目）

- `Nullable` 与 `ImplicitUsings` 均启用；新代码遵循可空引用类型标注。
- WPF 窗口/控件代码：UI 交互逻辑放 code-behind，业务状态放 ViewModel，不要互相穿透。
- Win32 P/Invoke 集中在 `Core/Helpers`，不要散落到 Views。
- 异步规则（继承全局）：禁止 `async void`；`throw` 不 `throw ex`；`IDisposable` 用 try/finally 或 using；禁止在循环中做网络 IO（ICS 拉取必须批量/单次完成）。
- 错误处理现状：`App` 内有全局 DispatcherUnhandledException 兜底；`ShowSettings` 异常时写桌面日志文件。新增代码保持同等克制，不要引入日志框架。

## 已知坑与注意事项

1. **`WindowsCalendarService.cs` 被 `<Compile Remove>` 排除**：需要 Windows 10 SDK + CsWinRT 才能编译。csproj 中相关行（UseWinRT、CsWinRT 包引用）均为注释状态。不要取消注释或把该文件加回编译，除非用户明确要求接入系统日历 API。
2. **命名混乱是历史遗留**：仓库名 CornerCalendar、产品名 WinCal/WinminiCal、托盘与目录用 miniCal、文档混用。遇到不一致保持现状，不做批量重命名。
3. **`*.ics` 在 .gitignore 中**（测试数据），不要把样例 ics 文件提交进仓库。
4. **`bin/`、`obj/` 位于 `src/CornerCalendar/` 下**且已 gitignore；`dist/`、`publish/` 为发布产物目录，同样勿提交。
5. 构建必须通过 `src/CornerCalendar.sln`（或 csproj 全路径）；仓库根目录没有工程文件。

## 相关文档

- `README.md` — 功能特性与用户向说明（部分路径描述已过时，以代码为准）
- `PROGRESS.md` — 开发进度记录（MVP + 设置界面已完成）
- `WinCal_产品与开发方案.md` — 产品定位、模块设计、UI 规范、阶段规划（750 行，改动前先查阅对应章节）
