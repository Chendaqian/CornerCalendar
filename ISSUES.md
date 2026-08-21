# ISSUES.md — CornerCalendar 已知问题清单

> 生成时间：2026-08-18，基于对 `src/CornerCalendar` 全部源码的一次完整审查；同日完成批量修复。
> 状态标记：⬜ 未修复 ｜ ✅ 已修复（注明日期与方式）

---

## 🔴 功能性问题

### 2. 主题"跟随系统"实际不跟随 ✅（2026-08-18 修复）
- **位置**：`Core/Helpers/ThemeHelper.cs`（全文件）
- **描述**：`IsSystemDarkMode()` 只在 `ApplyTheme` 被调用时读取注册表 `AppsUseLightTheme`，全仓库没有任何 `SystemEvents.UserPreferenceChanged` 订阅。Windows 切换深浅色后应用不会跟随变化。早期进度文档宣称的"自动跟随系统主题（监听注册表）"并未实现（该文档已从仓库移除）。
- **修复方式**：`ThemeHelper` 新增 `StartSystemThemeTracking`/`StopSystemThemeTracking`，订阅 `SystemEvents.UserPreferenceChanged`；仅 `FollowSystem` 模式响应，经 Dispatcher 切回 UI 线程重新应用主题。`App` 启动时订阅、退出时退订。

### 3. 托盘图标日期跨午夜不更新 ✅（2026-08-18 修复）
- **位置**：`App.xaml.cs`
- **描述**：托盘图标的日期数字与提示文本仅在启动时生成一次。常驻进程跨过午夜后，托盘仍显示昨天的日期。
- **影响**：对日历应用是核心展示缺陷。
- **修复方式**：`App` 内一次性 `DispatcherTimer` 定时到午夜，触发后刷新托盘提示文本并重新调度（托盘图标后改为固定的 `Resources/icon.ico`，无需刷新）；休眠错过午夜时唤醒后仍会补刷新。

### 4. ICS 去重逻辑可能误删正常事件 ✅（2026-08-18 修复，含回归测试）
- **位置**：`Core/Services/IcsCalendarService.cs`
- **描述**：去重规则为"同一天 + 标题第一个括号前的前缀相同 → 只保留跨度最长的一条"。该规则为节假日订阅源设计（"端午节"/"端午节（休）"），但作用于**所有** ICS 订阅——同日的"评审会（设计）"与"评审会（开发）"这类正常事件会被吞掉一条。
- **修复方式**：新规则仅在「两条都是全天事件 + 同一天 + 一个标题完整包含另一个」时合并（定时事件永不合并）；跨度相同保留标题更短的一条。逻辑抽出为 `internal static DeduplicateAllDayEvents`，由 `CornerCalendar.Tests` 的 `IcsDeduplicationTests` 覆盖（含旧误删场景回归）。

### 5. ICS 后台刷新存在并发竞态 ✅（2026-08-18 修复）
- **位置**：`Core/Services/IcsCalendarService.cs`（原 `_isBackgroundRefreshing`）
- **描述**：`_isBackgroundRefreshing` 是无锁 bool。`ForceRefreshAsync` 先强制置 false 再刷新，可能与正在进行的后台刷新并发执行：两个下载同时写同一个磁盘缓存文件，可能写坏文件或抛异常。
- **修复方式**：改用 `SemaphoreSlim(1, 1)` 串行化所有网络刷新；拿锁后复查缓存新鲜度（非强制刷新命中则跳过）；`ForceRefreshAsync` 以 `force: true` 走同一入口，不再清标志位。

### 6. 全局异常被静默吞掉 ✅（2026-08-18 修复）
- **位置**：`App.xaml.cs`
- **描述**：`DispatcherUnhandledException` 仅 `Debug.WriteLine` + `Handled = true`。Release 下无日志、无提示，用户机器上的崩溃完全不可见；只有 `ShowSettings` 一处有文件日志。
- **修复方式**：新增 `Core/Helpers/ErrorLog.cs`（追加写 `%LOCALAPPDATA%\CornerCalendar\error.log`，超过 512KB 重写，日志失败静默不影响主流程）；全局异常处理器写入该日志。未引入日志框架（遵守项目约定）。

---

## 🟡 资源与性能

### 9. `HttpClient` 持有但无释放路径 ✅（2026-08-18 修复）
- **位置**：`Core/Services/IcsCalendarService.cs`
- **描述**：每个 ICS 订阅创建一个 `HttpClient`，但类未实现 `IDisposable`；切换数据源或退出应用时无释放路径。
- **修复方式**：`IcsCalendarService` 与 `AggregateCalendarService` 实现 `IDisposable`（释放 HttpClient / 信号量 / 子服务）；`CalendarViewModel` 实现 `IDisposable` 转发；`PopupWindow.OnClosed` 释放 VM。

### 10. 弹窗打开时双重刷新 ✅（2026-08-18 修复）
- **位置**：`Views/PopupWindow.xaml.cs`；`ViewModels/CalendarViewModel.cs`
- **描述**：VM 构造时先按默认 `WeekStartDay = 1`（周一）刷新一次，`ApplySettings` 再按设置值（默认周日）刷新一次——首帧闪烁，且浪费一轮数据加载。
- **修复方式**：VM 构造函数在首次刷新前即从设置加载周起始日与近期事件天数；`ApplySettings` 移除多余的 `RefreshDataAsync`（表头同步保留，幂等）。

---

## 🟠 规范违反与代码质量

### 11. `async void`（违反 AGENTS.md 异步规范） ✅（2026-08-18 修复）
- **位置**：`ViewModels/CalendarViewModel.cs`（原 `GenerateUpcomingEvents`）
- **描述**：普通 VM 方法使用 `async void`，异常无法被调用方捕获；且与 `UpdateUpcomingEventsFromDateAsync` 有约 30 行重复逻辑。
- **修复方式**：删除 `GenerateUpcomingEvents`，`RefreshDataAsync` 直接 `await UpdateUpcomingEventsFromDateAsync(SelectedDate)`，逻辑单一来源。

### 12. 静默失败泛滥、无错误状态 UI ✅（2026-08-18 修复）
- **位置**：`Core/Services/AggregateCalendarService.cs`、`ViewModels/CalendarViewModel.cs`、`Views/PopupWindow.xaml(.cs)`
- **描述**：事件拉取失败时显示空日历，用户无法区分"没有日程"和"拉取失败"。
- **修复方式**：`AggregateCalendarService.GetEventsAsync` 在**全部**数据源失败时抛错（部分失败仍返回可用数据）；VM 新增 `ErrorText` 属性，拉取失败置"日程加载失败，请检查网络或数据源设置"；面板事件区域显示该提示，成功后自动消失。

### 13. 持久化位置三处不一致 ✅（2026-08-18 修复）
- **位置**：`AppSettings.cs`（旧：`%LOCALAPPDATA%\miniCal\`）、`IcsCalendarService.cs`（`%LOCALAPPDATA%\CornerCalendar\cache\`）、`StartupHelper.cs`（注册表值名）
- **修复方式**：统一为 `%LOCALAPPDATA%\CornerCalendar\` 与注册表值名 `CornerCalendar`。迁移兼容逻辑随后按需求移除（2026-08-18）：经确认本机不存在旧版 `miniCal` 目录与旧注册表值，无数据丢失风险。

---

## ⚪ 小问题

### 14. 错误日志文件名不一致 ✅（2026-08-18 修复）
- **位置**：`App.xaml.cs`（`ShowSettings`）
- **描述**：实际写入 `CornerCalendar_error.log`，弹窗却提示 `minical_error.log`。已统一为 `CornerCalendar_error.log`。

### 15. `AppSettings` 读写非线程安全、非原子 ✅（2026-08-18 修复）
- **位置**：`Core/Services/AppSettings.cs`
- **描述**：多个窗口各自 `Load` 一份副本再 `Save`，可能互相覆盖；`Save` 直接 `File.WriteAllText`，写入中途崩溃会损坏文件。
- **修复方式**：新增 `AppSettings.Current` 全局单例（`Load()` 返回同一实例，调用方无需改动）；`Save` 加锁并先写 `.tmp` 再 `File.Move(overwrite: true)` 原子替换。

### 16. XAML 硬编码字号，字体设置被迫整体缩放 ✅（2026-08-18 修复）
- **位置**：各 `Views/*.xaml`；`Views/PopupWindow.xaml.cs`（`ApplyFontSizeOffset`）
- **描述**：XAML 中大量硬编码 FontSize，只能用 `ScaleTransform` 缩放整个面板；缩放后渲染模糊、布局失真。
- **修复方式**：新增 `Views/Themes/FontSizes.xaml`，51 处硬编码字号全部收敛为 10 个语义化资源键（DynamicResource）；`ApplyFontSizeOffset` 改为按档位（每级 6%）覆写应用级资源键，移除 `ScaleTransform`，已打开窗口即时生效。

### 17. 工程问题 ⬜（部分完成）
- ~~无单元测试项目~~ ✅（2026-08-18：新增 `src/CornerCalendar.Tests`（xunit），覆盖 ICS 去重回归与农历节日；`dotnet test src\CornerCalendar.sln` 运行）。
- Windows 系统日历数据源已移除，程序统一使用 ICS 订阅。

---

## 相关文档

- `AGENTS.md` — 项目结构与构建指南（面向代理/贡献者）
- 注：早期文档（README.md、PROGRESS.md、WinCal_产品与开发方案.md）已于 2026-08-18 从仓库移除
