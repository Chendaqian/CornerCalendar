using CornerCalendar.Core.Helpers;
using CornerCalendar.Core.Services;
using CornerCalendar.Views;
using Hardcodet.Wpf.TaskbarNotification;
using System.IO;
using System.IO.Pipes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Shell;
using System.Windows.Threading;

namespace CornerCalendar;

public partial class App : Application
{
    private const string SingleInstanceMutexName = "Local\\CornerCalendar.SingleInstance";
    private const string CommandPipeName = "CornerCalendar.Command";
    private Mutex? _instanceMutex;
    private TaskbarIcon? _trayIcon;
    private readonly List<TaskbarClockWindow> _taskbarClocks = new();
    private PopupWindow? _popup;
    private SettingsWindow? _settingsWindow;
    private DispatcherTimer? _midnightTimer;
    private DispatcherTimer? _clockTimer;
    private DispatcherTimer? _weatherTimer;
    private CancellationTokenSource? _weatherRefreshCts;
    private CancellationTokenSource? _commandServerCts;
    private bool _restartRequested;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        string? startupCommand = GetCommand(e.Args);
        bool createdNew;
        _instanceMutex = new Mutex(true, SingleInstanceMutexName, out createdNew);
        if (!createdNew)
        {
            if (startupCommand != null)
                SendCommandToExistingInstance(startupCommand);
            _instanceMutex.Dispose();
            _instanceMutex = null;
            Shutdown();
            return;
        }

        // 全局异常处理（ISSUES #6：写入日志文件，Release 下崩溃不再无声无息）
        DispatcherUnhandledException += (s, args) =>
        {
            System.Diagnostics.Debug.WriteLine($"CornerCalendar: Unhandled exception: {args.Exception}");
            ErrorLog.Write("DispatcherUnhandledException", args.Exception);
            args.Handled = true;
        };

        // 初始化托盘图标
        _trayIcon = (TaskbarIcon)FindResource("TrayIcon")!;

        // 左键点击弹出日历
        _trayIcon.TrayLeftMouseDown += (s, args) => TogglePopup();

        // 托盘图标固定为 Resources/icon.ico（App.xaml 的 IconSource），这里只设置提示文本
        _trayIcon.ToolTipText = $"CornerCalendar - {DateTime.Now:yyyy年M月d日 dddd}";

        // 应用保存的主题设置，并监听系统深浅色变化（ISSUES #2）
        AppSettings settings = AppSettings.Load();
        ThemeHelper.ApplyTheme(settings.ThemeMode);
        ThemeHelper.StartSystemThemeTracking();
        StartWeatherBackgroundRefresh();

        InitializeTaskbarClock();

        // 跨午夜刷新托盘图标日期（ISSUES #3）
        ScheduleMidnightTrayRefresh();

        // 托盘图标和任务栏时钟覆盖层共用同样的右键菜单
        _trayIcon.ContextMenu = CreateContextMenu();
        InitializeJumpList();
        StartCommandServer();

        if (startupCommand != null)
        {
            Dispatcher.BeginInvoke(
                DispatcherPriority.ApplicationIdle,
                new Action(() => ExecuteCommand(startupCommand)));
        }
    }

    /// <summary>
    /// 显示设置窗口（独立顶层窗口，单例模式）
    /// 可从右键菜单或齿轮按钮调用
    /// </summary>
    public static void ShowSettings()
    {
        try
        {
            App app = (App)Current;
            if (app._settingsWindow != null && app._settingsWindow.IsVisible)
            {
                // 已打开则激活
                app._settingsWindow.Activate();
                return;
            }

            app._settingsWindow = new SettingsWindow();
            app._settingsWindow.Closed += (_, _) => app._settingsWindow = null;
            app._settingsWindow.Show();
            app._settingsWindow.Activate();
        }
        catch (Exception ex)
        {
            ErrorLog.Write("ShowSettings", ex);
            MessageBox.Show("错误已写入 %LOCALAPPDATA%\\CornerCalendar\\error.log", "CornerCalendar 错误",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OpenSettings()
    {
        // 使用 Dispatcher 延迟打开，避免与右键菜单的弹出窗口冲突
        Dispatcher.BeginInvoke(new Action(() =>
        {
            System.Diagnostics.Debug.WriteLine("CornerCalendar: OpenSettings dispatcher callback executing");
            ShowSettings();
        }));
    }

    private static string? GetCommand(IEnumerable<string> arguments)
        => arguments
            .FirstOrDefault(argument => argument.StartsWith("--command=", StringComparison.OrdinalIgnoreCase))
            ?.Split('=', 2)[1];

    private void InitializeJumpList()
    {
        string? applicationPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(applicationPath))
            return;

        JumpList jumpList = new();
        AddJumpTask(jumpList, applicationPath, "设置", "打开 CornerCalendar 设置", "settings");
        AddJumpTask(jumpList, applicationPath, "重启", "重启 CornerCalendar", "restart");
        AddJumpTask(jumpList, applicationPath, "退出", "退出 CornerCalendar", "exit");
        JumpList.SetJumpList(this, jumpList);
    }

    private static void AddJumpTask(
        JumpList jumpList,
        string applicationPath,
        string title,
        string description,
        string command)
    {
        jumpList.JumpItems.Add(new JumpTask
        {
            ApplicationPath = applicationPath,
            WorkingDirectory = AppContext.BaseDirectory,
            Arguments = $"--command={command}",
            IconResourcePath = applicationPath,
            IconResourceIndex = 0,
            Title = title,
            Description = description,
            CustomCategory = "CornerCalendar"
        });
    }

    private void StartCommandServer()
    {
        _commandServerCts = new CancellationTokenSource();
        _ = RunCommandServerAsync(_commandServerCts.Token);
    }

    private async Task RunCommandServerAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                using NamedPipeServerStream server = new(
                    CommandPipeName,
                    PipeDirection.In,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);
                await server.WaitForConnectionAsync(cancellationToken);
                using StreamReader reader = new(server);
                string? command = await reader.ReadLineAsync(cancellationToken);
                if (!string.IsNullOrWhiteSpace(command))
                    _ = Dispatcher.BeginInvoke(new Action(() => ExecuteCommand(command)));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"CornerCalendar: Command pipe failed: {ex.Message}");
            }
        }
    }

    private static void SendCommandToExistingInstance(string command)
    {
        try
        {
            using NamedPipeClientStream client = new(
                ".",
                CommandPipeName,
                PipeDirection.Out);
            client.Connect(1000);
            using StreamWriter writer = new(client) { AutoFlush = true };
            writer.WriteLine(command);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"CornerCalendar: Failed to send command to existing instance: {ex.Message}");
        }
    }

    private void ExecuteCommand(string command)
    {
        switch (command.ToLowerInvariant())
        {
            case "settings":
                OpenSettings();
                break;

            case "restart":
                RestartApplication();
                break;

            case "exit":
                Shutdown();
                break;
        }
    }

    private ContextMenu CreateContextMenu()
    {
        ContextMenu menu = new();

        MenuItem settingsItem = new()
        {
            Header = CreateMenuHeader("设置", SettingsIconGeometry)
        };
        settingsItem.Click += (s, args) => OpenSettings();
        menu.Items.Add(settingsItem);

        MenuItem restartItem = new()
        {
            Header = CreateMenuHeader("重启", RestartIconGeometry)
        };
        restartItem.Click += (s, args) => RestartApplication();
        menu.Items.Add(restartItem);

        menu.Items.Add(new Separator());

        MenuItem exitItem = new()
        {
            Header = CreateMenuHeader("退出", ExitIconGeometry)
        };
        exitItem.Click += (s, args) => Shutdown();
        menu.Items.Add(exitItem);

        return menu;
    }

    private void InitializeTaskbarClock()
    {
        try
        {
            // 只覆盖 Windows 标记的主显示器任务栏，副显示器保留系统原生时钟和控制中心。
            nint primaryTaskbar = TaskbarClockWindow.FindPrimaryTaskbarWindow();
            TaskbarClockWindow clock = new(primaryTaskbar)
            {
                ContextMenu = CreateContextMenu()
            };
            clock.ClockClicked += monitor => TogglePopup(monitor);
            clock.Show();
            _taskbarClocks.Add(clock);

            RefreshTaskbarClock();

            _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _clockTimer.Tick += OnClockTick;
            _clockTimer.Start();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"CornerCalendar: Taskbar clock overlay failed: {ex.Message}");
            foreach (TaskbarClockWindow clock in _taskbarClocks)
                clock.Close();
            _taskbarClocks.Clear();
        }
    }

    private void OnClockTick(object? sender, EventArgs e)
    {
        RefreshTaskbarClock();
    }

    private void StartWeatherBackgroundRefresh()
    {
        _weatherRefreshCts = new CancellationTokenSource();
        _ = RefreshWeatherCacheAsync(_weatherRefreshCts.Token);

        _weatherTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMinutes(GetWeatherRefreshMinutes())
        };
        _weatherTimer.Tick += OnWeatherTimerTick;
        _weatherTimer.Start();
    }

    private void RestartWeatherBackgroundRefresh()
    {
        StopWeatherBackgroundRefresh();
        StartWeatherBackgroundRefresh();
    }

    private void StopWeatherBackgroundRefresh()
    {
        if (_weatherTimer != null)
        {
            _weatherTimer.Stop();
            _weatherTimer.Tick -= OnWeatherTimerTick;
            _weatherTimer = null;
        }

        if (_weatherRefreshCts != null)
        {
            _weatherRefreshCts.Cancel();
            _weatherRefreshCts.Dispose();
            _weatherRefreshCts = null;
        }
    }

    private static int GetWeatherRefreshMinutes()
    {
        int value = AppSettings.Current.WeatherRefreshMinutes;
        return value is 30 or 60 or 120 or 240 ? value : 120;
    }

    private void OnWeatherTimerTick(object? sender, EventArgs e)
    {
        if (_weatherRefreshCts != null)
            _ = RefreshWeatherCacheAsync(_weatherRefreshCts.Token);
    }

    private async Task RefreshWeatherCacheAsync(CancellationToken cancellationToken)
    {
        try
        {
            AppSettings settings = AppSettings.Current;
            string[] locations = settings.WeatherLocations?.Count > 0
                ? settings.WeatherLocations.ToArray()
                : new[] { string.Empty };
            await WeatherService.RefreshAllAsync(
                locations,
                settings.WeatherApiUrl,
                GetWeatherRefreshMinutes(),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"CornerCalendar: Background weather refresh failed: {ex.Message}");
        }
    }

    public static void RefreshTaskbarClock(string? format = null)
    {
        if (Current is App app)
            app.RefreshTaskbarClockCore(format);
    }

    public static void RefreshCalendarSettings()
    {
        if (Current is App app)
            app._popup?.RefreshSettings();
    }

    public static void RefreshWeatherSettings()
    {
        if (Current is App app)
            app.RestartWeatherBackgroundRefresh();
    }

    private void RefreshTaskbarClockCore(string? format = null)
    {
        if (_taskbarClocks.Count == 0)
            return;

        string effectiveFormat = format ?? AppSettings.Current.TaskbarTimeFormat;
        string text = TaskbarClockFormatter.Format(DateTime.Now, effectiveFormat);
        foreach (TaskbarClockWindow clock in _taskbarClocks)
            clock.UpdateText(text);
    }

    // 菜单项矢量图标（24x24 视口的 Path 数据：齿轮 / 电源）
    private const string SettingsIconGeometry = "M12,15.5A3.5,3.5 0 0,1 8.5,12A3.5,3.5 0 0,1 12,8.5A3.5,3.5 0 0,1 15.5,12A3.5,3.5 0 0,1 12,15.5M19.43,12.97C19.47,12.65 19.5,12.33 19.5,12C19.5,11.67 19.47,11.34 19.43,11L21.54,9.37C21.73,9.22 21.78,8.95 21.66,8.73L19.66,5.27C19.54,5.05 19.27,4.96 19.05,5.05L16.56,6.05C16.04,5.66 15.5,5.32 14.87,5.07L14.5,2.42C14.46,2.18 14.25,2 14,2H10C9.75,2 9.54,2.18 9.5,2.42L9.13,5.07C8.5,5.32 7.96,5.66 7.44,6.05L4.95,5.05C4.73,4.96 4.46,5.05 4.34,5.27L2.34,8.73C2.21,8.95 2.27,9.22 2.46,9.37L4.57,11C4.53,11.34 4.5,11.67 4.5,12C4.5,12.33 4.53,12.65 4.57,13L2.46,14.63C2.27,14.78 2.21,15.05 2.34,15.27L4.34,18.73C4.46,18.95 4.73,19.04 4.95,18.95L7.44,17.95C7.96,18.34 8.5,18.68 9.13,18.93L9.5,21.58C9.54,21.82 9.75,22 10,22H14C14.25,22 14.46,21.82 14.5,21.58L14.87,18.93C15.5,18.68 16.04,18.34 16.56,17.95L19.05,18.95C19.27,19.04 19.54,18.95 19.66,18.73L21.66,15.27C21.78,15.05 21.73,14.78 21.54,14.63L19.43,12.97Z";

    private const string ExitIconGeometry = "M13,3H11V11H13V3M17.83,5.17L16.41,6.59C18.05,8.23 19,10.5 19,13A7,7 0 0,1 12,20A7,7 0 0,1 5,13C5,10.5 5.95,8.23 7.59,6.59L6.17,5.17C4.15,7.19 3,10 3,13A9,9 0 0,0 12,22A9,9 0 0,0 21,13C21,10 19.85,7.19 17.83,5.17Z";

    private const string RestartIconGeometry = "M17.65,6.35C16.2,4.9 14.21,4 12,4C7.58,4 4,7.58 4,12C4,16.42 7.58,20 12,20C15.73,20 18.84,17.45 19.73,14H17.65C16.83,16.33 14.61,18 12,18C8.69,18 6,15.31 6,12C6,8.69 8.69,6 12,6C13.66,6 15.14,6.69 16.22,7.78L13,11H20V4L17.65,6.35Z";

    /// <summary>
    /// 创建菜单项矢量图标，颜色绑定主题主文本色（深浅色主题自适应）。
    /// </summary>
    private static UIElement CreateMenuIcon(string geometryData)
    {
        Grid iconContainer = new()
        {
            Width = 18,
            Height = 18,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        System.Windows.Shapes.Path path = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse(geometryData),
            Stretch = Stretch.Uniform,
            Width = 16,
            Height = 16,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        path.SetResourceReference(Shape.FillProperty, "TextPrimaryBrush");
        iconContainer.Children.Add(path);
        return iconContainer;
    }

    private static UIElement CreateMenuHeader(string text, string geometryData)
    {
        Grid header = new();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        UIElement icon = CreateMenuIcon(geometryData);
        Grid.SetColumn(icon, 0);
        header.Children.Add(icon);

        TextBlock label = new()
        {
            Text = text,
            Margin = new Thickness(6, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        label.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
        Grid.SetColumn(label, 1);
        header.Children.Add(label);
        return header;
    }

    private void RestartApplication()
    {
        _restartRequested = true;
        Shutdown();
    }

    /// <summary>
    /// 调度下一次午夜托盘刷新（一次性定时器，触发后重新调度）。
    /// 休眠/待机导致错过午夜时，唤醒后会延迟触发，仍会刷新到正确日期。
    /// </summary>
    private void ScheduleMidnightTrayRefresh()
    {
        if (_midnightTimer != null)
        {
            _midnightTimer.Stop();
            _midnightTimer.Tick -= OnMidnightTick;
        }

        TimeSpan delay = DateTime.Now.Date.AddDays(1) - DateTime.Now;
        _midnightTimer = new DispatcherTimer { Interval = delay };
        _midnightTimer.Tick += OnMidnightTick;
        _midnightTimer.Start();
    }

    private void OnMidnightTick(object? sender, EventArgs e)
    {
        RefreshTrayIcon();
        ScheduleMidnightTrayRefresh();
    }

    private void RefreshTrayIcon()
    {
        if (_trayIcon == null) return;

        try
        {
            // 托盘图标固定为 icon.ico（XAML IconSource），午夜只需刷新带日期的提示文本
            _trayIcon.ToolTipText = $"CornerCalendar - {DateTime.Now:yyyy年M月d日 dddd}";
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"CornerCalendar: RefreshTrayIcon error: {ex.Message}");
        }
    }

    /// <summary>
    /// 拦截器回调：点击任务栏时钟在「显示面板 / 隐藏面板」之间切换。
    /// 面板未显示 → 显示；已显示 → 隐藏（再次点击时钟即收起）。
    /// </summary>
    private void ShowPopup(nint monitor = default)
    {
        try
        {
            if (_popup != null && _popup.IsVisible)
            {
                _popup.Activate();
                return;
            }

            _popup?.Close();
            PopupWindow popup = new();
            _popup = popup;
            popup.Closed += (_, _) =>
            {
                if (ReferenceEquals(_popup, popup))
                    _popup = null;
            };
            popup.Show();
            WindowPositionHelper.PositionNearTaskbar(popup, monitor);
            popup.Activate();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"CornerCalendar: ShowPopup error: {ex}");
            _popup = null;
        }
    }

    /// <summary>
    /// 切换日历面板显示/隐藏。用于托盘图标点击。
    /// </summary>
    private void TogglePopup(nint monitor = default)
    {
        try
        {
            if (_popup == null || !_popup.IsVisible)
            {
                _popup?.Close();
                PopupWindow popup = new();
                _popup = popup;
                popup.Closed += (_, _) =>
                {
                    if (ReferenceEquals(_popup, popup))
                        _popup = null;
                };
                popup.Show();
                WindowPositionHelper.PositionNearTaskbar(popup, monitor);
                popup.Activate();
            }
            else
            {
                _popup.Activate();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"CornerCalendar: TogglePopup error: {ex}");
            _popup = null;
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            if (_clockTimer != null)
            {
                _clockTimer.Stop();
                _clockTimer.Tick -= OnClockTick;
                _clockTimer = null;
            }
            foreach (TaskbarClockWindow clock in _taskbarClocks)
                clock.Close();
            _taskbarClocks.Clear();

            if (_midnightTimer != null)
            {
                _midnightTimer.Stop();
                _midnightTimer = null;
            }
            StopWeatherBackgroundRefresh();
            if (_commandServerCts != null)
            {
                _commandServerCts.Cancel();
                _commandServerCts.Dispose();
                _commandServerCts = null;
            }
            ThemeHelper.StopSystemThemeTracking();
            _trayIcon?.Dispose();
        }
        finally
        {
            if (_instanceMutex != null)
            {
                try
                {
                    _instanceMutex.ReleaseMutex();
                }
                catch (ApplicationException)
                {
                    // 互斥体未由当前进程持有时无需再次释放。
                }
                finally
                {
                    _instanceMutex.Dispose();
                    _instanceMutex = null;
                }
            }
        }

        if (_restartRequested && !string.IsNullOrWhiteSpace(Environment.ProcessPath))
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = Environment.ProcessPath,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"CornerCalendar: Restart failed: {ex.Message}");
            }
        }

        base.OnExit(e);
    }
}