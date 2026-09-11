## 1. 跑者资源迁移

- [x] 1.1 用 PowerShell 脚本（`$ErrorActionPreference = 'Stop'`）把 `D:\Source\Repos\RunCat365\RunCat365\resources\runners` 下全部 40 个跑者文件夹原样复制到 `src/CornerCalendar/Resources/Runners/`，保留原文件夹名与文件名；验证：目标目录文件夹数 = 40、PNG 总数 = 331，`git status` 仅显示新增 PNG
- [x] 1.2 在 `CornerCalendar.csproj` 增加 `<None Update="Resources\Runners\**\*.png" CopyToOutputDirectory="PreserveNewest" />`（沿用 HistoryToday JSON 模式）；验证：`dotnet build src\CornerCalendar.sln` 后 `bin\Debug\net8.0-windows\win-x64\Resources\Runners` 下出现全部 40 个文件夹

## 2. 跑者核心库（枚举、图标、CPU 帧速）

- [x] 2.1 新增 `Core/Models/RunnerInfo.cs`（跑者描述符：文件夹名、显示名、帧文件路径列表）；验证：构建通过
- [x] 2.2 新增 `Core/Helpers/RunnerLibrary.cs`：目录枚举全部跑者、帧按文件名末尾整数自然序排序（兼容 `{name}_{i}.png` 与 `{name}-frame-{i}.png`）、文件夹名转显示名（去 `-frames`、连字符转空格、首字母大写）、损坏帧跳过、WPF WriteableBitmap 反白（保留 alpha、RGB 置白）、PNG-in-ICO 封装 `ToIcon`（手写 22 字节 ICO 头 + IHDR 宽高，移植处文件头保留 RunCat365 的 Apache-2.0 版权声明）；在 `CornerCalendar.Tests` 新增中文命名 xUnit 测试覆盖：枚举数量与帧数、frame-2 先于 frame-10、缺帧/坏帧跳过、显示名转换、反白后像素 alpha 不变 RGB 为白、ToIcon 产出可读取的 Icon；验证：`dotnet test src\CornerCalendar.sln` 全绿
- [x] 2.3 实现 CPU 采样与间隔计算：纯函数 `CalculateInterval(load)` = `clamp((int)(500f / max(1f, load / 5f)), 25, 500)`，`PerformanceCounter` 优先 `Processor Information / % Processor Utility`、回退 `Processor / % Processor Time`、双失败回退固定 500ms（近 5 次采样均值平滑；若共享框架不含 PerformanceCounter 则补官方 NuGet 包）；新增单元测试覆盖 load=0→500ms、load=100→25ms、中间值单调递减、越界钳制；验证：`dotnet test` 全绿

## 3. 移除任务栏时钟功能

- [x] 3.1 删除 `Views/TaskbarClockWindow.xaml`、`Views/TaskbarClockWindow.xaml.cs`、`Core/Helpers/TaskbarClockFormatter.cs`、`CornerCalendar.Tests/TaskbarClockFormatterTests.cs`；验证：构建通过（此时 App.xaml.cs 等引用处报错属预期，由 3.2 消除）
- [x] 3.2 清理 `App.xaml.cs`：删除 `_taskbarClocks`/`_clockTimer` 字段、`InitializeTaskbarClock`/`OnClockTick`/`RefreshTaskbarClock`/`RefreshTaskbarClockCore` 方法、OnStartup 中的调用与 OnExit 中的时钟清理段；`TogglePopup(nint monitor = default)` 简化为无参 `TogglePopup()`（既有无调用者的 `ShowPopup` 属预先存在死代码，保留不动）；验证：`dotnet build` 0 错误 0 警告，全仓 grep `TaskbarClock` 在 src 下无代码残留
- [x] 3.3 移除 `AppSettings.TaskbarTimeFormat` 属性，并删除 `SettingsWindow.xaml` "显示"分类中的任务栏时间格式标题/输入框/说明/预览 Border 与 `SettingsWindow.xaml.cs` 中对应的加载、`OnTaskbarTimeFormatChanged`、`UpdateTaskbarTimePreview`、保存、`App.RefreshTaskbarClock` 调用和重置默认值行；新增单元测试：新建 `AppSettings` 的 `RunnerName` 默认为 `cat` 且不含时间格式属性、反序列化带 `TaskbarTimeFormat` 字段的旧 JSON 被静默忽略；验证：`dotnet test` 全绿
- [x] 3.4 删除主题资源键：`Light.xaml`/`Dark.xaml` 的 `TaskbarClockTextColor`、`TaskbarClockFallbackColor`、`TaskbarClockTextBrush`、`TaskbarClockFallbackBrush` 与 `FontSizes.xaml` 的 `FontSizeTaskbarClock`；验证：构建通过且全仓 grep 上述键名无残留引用
- [x] 3.5 更新文档与流程文案：`AGENTS.md`（概览、仓库结构、关键行为约定第 7 条、已知注意事项）、`README.md`、`README_zh.md`、`.github/workflows/release.yml` 中所有任务栏时钟/时间格式描述，改为托盘跑者动画表述；验证：grep "任务栏时钟|TaskbarClock|时间格式" 在文档中无过期描述

## 4. 托盘跑者动画集成

- [x] 4.1 新增 `Core/Helpers/TrayRunnerAnimator.cs`（`IDisposable`）：预缓存当前"跑者 × 主题"的 Icon 列表，UI 线程 `DispatcherTimer` 按 `CalculateInterval` 换帧赋值 `_trayIcon.Icon`，后台 `System.Threading.Timer` 每秒采样 CPU 并 `Dispatcher.BeginInvoke` 调整间隔，每秒顺带比对有效深浅主题（`ThemeMode` 设置 + `ThemeHelper` 系统主题）翻转时重建缓存并 Dispose 旧 Icon；`App.OnStartup` 在设置加载后创建并启动动画器，`App.OnExit` 调用 `Dispose()`；验证：运行 exe 后托盘显示 cat 动画，空闲慢速、跑压测（如 `dotnet build` 循环）时明显加速，系统切深色主题后约 1 秒内反白
- [x] 4.2 `App` 新增静态入口 `ApplyRunnerSettings()`（照 `RefreshCalendarSettings` 模式）：按 `AppSettings.Current.RunnerName` 热切换动画器跑者并从首帧播放，跑者不存在时回退 `cat`；在 `SettingsWindow.SaveSettings` 中调用；验证：设置中改跑者保存后托盘立即切换，无需重启
- [x] 4.3 托盘悬停提示改为与 RunCat365 一致：每秒随采样刷新为 `CPU: 保留一位小数%`（如 `CPU: 23.5%`），移除午夜日期提示刷新逻辑（`_midnightTimer`/`ScheduleMidnightTrayRefresh`/`OnMidnightTick`/`RefreshTrayIcon`）；验证：悬停托盘显示 CPU 占用且每秒变化，`dotnet test` 全绿

## 5. 设置窗口"跑者"分类

- [x] 5.1 `SettingsWindow.xaml` 在"显示"后插入 `ListBoxItem` "跑者"（索引 7，"关于"顺移为 8），新增 `RunnerPanel` StackPanel（标题 + WrapPanel 布局的 `ItemsControl`，卡片 = 预览 Image（高约 36、Uniform）+ 显示名 + 选中态边框，颜色仅用既有主题资源键）；同步修改 `UpdateCategoryVisibility` 索引映射与滚轮切换；验证：设置窗口各分类切换正常，"跑者"分类列出全部 40 个跑者及可读名称
- [x] 5.2 实现预览动画：帧 `BitmapImage` 全量缓存，单个共享 `DispatcherTimer`（固定 200ms）每 tick 推进所有卡片帧索引；仅在"跑者"分类可见且窗口打开时运行，分类切走或窗口 Closed 即停止；验证：停留在分类时 40 个预览全部循环动画，切到其他分类后定时器停止（可加日志/断点确认），托盘动画不受影响
- [x] 5.3 实现选中与持久化：点击卡片更新选中态视觉，`LoadSettings` 按 `RunnerName` 回显选中（缺失回退 `cat`），`SaveSettings` 收集选中值写入 `_settings.RunnerName`（保存即触发 4.2 的热切换），`OnResetDefaults` 重置为 `cat`；验证：选中 Shiba Inu 保存后托盘立即切换且重启后保持，"恢复默认" + 保存回到 cat

## 6. 跑者分类署名外链

- [x] 6.1 `SettingsWindow.xaml` 的 `RunnerPanel` 末尾追加"跑者动画"小节，含两个 Hyperlink：RunCat365 项目 `https://github.com/runcat-dev/RunCat365` 与跑者资源来源 `https://runcat-dev.github.io/RunnerGallery/`，点击处理器复用既有 `OpenExternalUrl`，样式对齐"关于"分类现有"项目地址"链接（按用户反馈从关于页移至跑者 tab 末尾）；验证：点击两个链接分别用默认浏览器打开对应网址

## 7. 交付验证

- [x] 7.1 `dotnet build src\CornerCalendar.sln` 0 错误 0 警告，`dotnet test src\CornerCalendar.sln` 全部通过（65 个测试全绿；另经用户批准删除了历史遗留的 3 个与现设计不符的 HistoryTodayServiceTests 过期测试以解除编译阻塞）
- [x] 7.2 按 AGENTS.md 交付清单在 Windows 上手工检查：托盘跑者动画播放与 CPU 加速、深浅主题反白、左键点击弹出主面板（面板已开时仅激活）、右键菜单设置/重启/退出可用、主显示器与其他显示器任务栏均为系统原生日钟且无覆盖窗口、设置"跑者"分类预览/选中/保存/恢复默认、"关于"外链、退出后进程无残留（用户已确认通过；"午夜托盘提示"检查项因 4.3 的 TIP 需求变更而移除）
