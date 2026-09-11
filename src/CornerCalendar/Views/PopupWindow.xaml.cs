using CornerCalendar.Core.Helpers;
using CornerCalendar.Core.Models;
using CornerCalendar.Core.Services;
using CornerCalendar.ViewModels;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace CornerCalendar.Views;

public partial class PopupWindow : Window
{
    private readonly CalendarViewModel _calendarViewModel;
    private readonly AppSettings _settings;
    private readonly IHistoryTodayService _historyTodayService;
    private EventDetailWindow? _detailWindow;
    private DateTime? _detailDate;
    private WeatherForecastWindow? _weatherForecastWindow;
    private WeatherInfo? _currentWeather;
    private readonly List<string> _weatherLocations;
    private CancellationTokenSource? _weatherLoadCts;
    private int _weatherIndex;
    private bool _pendingForecastOpen;    // 天气加载中用户点击了天气区：数据到达后立即打开七天天气窗口

    public PopupWindow()
    {
        // 加载设置
        _settings = AppSettings.Load();
        _weatherLocations = _settings.WeatherLocations?.Count > 0
            ? _settings.WeatherLocations
            : new List<string> { "" };

        // 初始化 ViewModel
        _calendarViewModel = new CalendarViewModel();
        _historyTodayService = new WikimediaHistoryTodayService();

        InitializeComponent();

        // 设置 DataContext
        DataContext = _calendarViewModel;

        // 绑定事件列表
        EventListControl.ItemsSource = _calendarViewModel.UpcomingEvents;

        // 订阅事件列表变化以更新空状态提示
        _calendarViewModel.UpcomingEvents.CollectionChanged += (_, _) => UpdateNoEventsVisibility();

        // 订阅加载错误状态（ISSUES #12）
        _calendarViewModel.PropertyChanged += OnViewModelPropertyChanged;

        Loaded += OnLoaded;

        // 窗口尺寸变化时重新定位（异步加载事件后窗口变高）
        SizeChanged += OnSizeChanged;

        // 点击日期格显示当天详情，详情中同时展示节日信息和历史资料
        Calendar.DateClicked += OnDateClicked;

        // 主面板激活后支持使用左右方向键切换月份
        PreviewKeyDown += OnPreviewKeyDown;

        // 构造期即预载天气：配合 App 启动空闲预建窗口，首次点击托盘也能秒开
        StartWeatherLoad();
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Left)
        {
            _calendarViewModel.NavigatePreviousMonth();
            e.Handled = true;
        }
        else if (e.Key == Key.Right)
        {
            _calendarViewModel.NavigateNextMonth();
            e.Handled = true;
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ApplySettings();
        UpdateNoEventsVisibility();
        UpdateErrorVisibility();
        UpdateWeatherPage();
    }

    /// <summary>
    /// 显示面板：立即弹出并定位到任务栏旁，无入场动画。
    /// </summary>
    /// <remark>
    /// 设置与日历数据在隐藏期间已由 App.RefreshCalendarSettings 同步；
    /// 窗口实例保活复用，打开无重建成本。
    /// </remark>
    public void ShowPopup()
    {
        StartWeatherLoad();
        Show();
        WindowPositionHelper.PositionNearTaskbar(this);
        Activate();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CalendarViewModel.ErrorText))
            UpdateErrorVisibility();
    }

    /// <summary>
    /// 加载失败时显示错误提示（ISSUES #12）
    /// </summary>
    private void UpdateErrorVisibility()
    {
        LoadErrorText.Text = _calendarViewModel.ErrorText ?? "";
        LoadErrorText.Visibility = string.IsNullOrEmpty(_calendarViewModel.ErrorText)
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private void UpdateWeatherPage()
    {
        int count = _weatherLocations.Count;
        WeatherPageText.Text = $"{_weatherIndex + 1}/{count}";
        bool canSwitch = count > 1;
        PreviousWeatherButton.Visibility = canSwitch ? Visibility.Visible : Visibility.Collapsed;
        NextWeatherButton.Visibility = canSwitch ? Visibility.Visible : Visibility.Collapsed;
    }

    private void StartWeatherLoad()
    {
        if (_weatherLoadCts != null)
        {
            try
            {
                _weatherLoadCts.Cancel();
            }
            finally
            {
                _weatherLoadCts.Dispose();
                _weatherLoadCts = null;
            }
        }

        _weatherLoadCts = new CancellationTokenSource();
        _ = LoadWeatherAsync(_weatherIndex, _weatherLoadCts.Token);
    }

    private async Task LoadWeatherAsync(int index, CancellationToken cancellationToken)
    {
        string location = _weatherLocations[index];
        WeatherIconHost.Content = null;
        WeatherCityText.Text = string.IsNullOrWhiteSpace(location) ? "自动定位" : location;
        WeatherSummaryText.Text = "加载天气中...";
        SetWeatherSummary(null, null);

        try
        {
            WeatherInfo? weather = await WeatherService.GetWeatherAsync(
                location,
                cancellationToken,
                _settings.WeatherApiUrl,
                _settings.WeatherRefreshMinutes);
            if (cancellationToken.IsCancellationRequested || index != _weatherIndex)
                return;

            if (weather == null)
            {
                _currentWeather = null;
                _pendingForecastOpen = false;
                WeatherSummaryText.Text = "天气获取失败";
                WeatherSection.ToolTip = "天气获取失败";
                return;
            }

            _currentWeather = weather;
            WeatherIconHost.Content = WeatherIconFactory.Create(weather.IconKind);
            WeatherCityText.Text = weather.City;
            WeatherSummaryText.Text = weather.Description;
            WeatherForecastDay? today = weather.Forecast
                .FirstOrDefault(day => day.Date.Date == DateTime.Today);
            SetWeatherSummary(weather, today);
            WeatherSection.ToolTip =
                $"{weather.City}\n" +
                $"天气：{weather.Description}\n" +
                $"温度：当前 {weather.Temperature:F0}°，体感 {weather.FeelsLikeTemperature:F0}°\n" +
                $"湿度：{weather.RelativeHumidity:F0}%\n" +
                $"云量：{weather.CloudCover:F0}%\n" +
                $"风速：{weather.WindSpeed:F0} km/h\n" +
                $"降水：当前 {weather.Precipitation:F1} mm，概率 {today?.PrecipitationProbability ?? 0:F0}%\n" +
                $"紫外线：{weather.UvIndex:F1}\n" +
                $"能见度：{weather.Visibility / 1000:F1} km\n" +
                $"日出日落：{today?.Sunrise ?? "--:--"} / {today?.Sunset ?? "--:--"}\n" +
                "点击查看未来七天天气";
            SyncWeatherForecastWindow(weather);
        }
        catch (OperationCanceledException)
        {
            // 切换天气位置时取消旧请求，不显示错误。
        }
        catch
        {
            if (!cancellationToken.IsCancellationRequested && index == _weatherIndex)
            {
                _pendingForecastOpen = false;
                WeatherSummaryText.Text = "天气获取失败";
            }
        }
    }

    /// <summary>
    /// 天气数据到达后同步七天天气窗口：已打开则刷新为当前城市；有待打开请求则立即打开。
    /// </summary>
    private void SyncWeatherForecastWindow(WeatherInfo weather)
    {
        if (_weatherForecastWindow?.IsVisible == true)
        {
            _weatherForecastWindow.UpdateForecast(weather);
            return;
        }

        if (_pendingForecastOpen)
        {
            _pendingForecastOpen = false;
            _weatherForecastWindow ??= new WeatherForecastWindow();
            _weatherForecastWindow.ShowForecast(weather, this);
        }
    }

    private void OnPreviousWeatherClick(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        ChangeWeather(-1);
    }

    private void OnNextWeatherClick(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        ChangeWeather(1);
    }

    private void OnWeatherSectionPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (IsInsideButton(e.OriginalSource as DependencyObject))
            return;

        if (_weatherForecastWindow?.IsVisible == true)
        {
            // 已打开：点击天气区收起
            _weatherForecastWindow.Hide();
            _pendingForecastOpen = false;
        }
        else if (_currentWeather?.Forecast.Count > 0)
        {
            _weatherForecastWindow ??= new WeatherForecastWindow();
            _weatherForecastWindow.ShowForecast(_currentWeather, this);
        }
        else
        {
            // 天气仍在加载：数据到达后立即打开，点击不再有"死区"
            _pendingForecastOpen = true;
        }

        e.Handled = true;
    }

    private static bool IsInsideButton(DependencyObject? element)
    {
        while (element != null)
        {
            if (element is Button)
                return true;
            // 点击彩色内联文本时 OriginalSource 是 Run（ContentElement 而非 Visual），
            // VisualTreeHelper 会抛异常，需改走逻辑树（Run → TextBlock → …）
            element = element is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D
                ? System.Windows.Media.VisualTreeHelper.GetParent(element)
                : LogicalTreeHelper.GetParent(element);
        }

        return false;
    }

    private void ChangeWeather(int direction)
    {
        if (_weatherLocations.Count < 2)
            return;

        _weatherIndex = (_weatherIndex + direction + _weatherLocations.Count)
            % _weatherLocations.Count;
        UpdateWeatherPage();
        StartWeatherLoad();
    }

    /// <summary>
    /// 窗口尺寸变化后重新定位，确保不超出屏幕底部
    /// </summary>
    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        WindowPositionHelper.PositionNearTaskbar(this);
    }

    /// <summary>
    /// 应用用户设置到界面
    /// </summary>
    private void ApplySettings()
    {
        // #1 主题
        ThemeHelper.ApplyTheme(_settings.ThemeMode);

        // #2 字体大小偏移
        ApplyFontSizeOffset();

        // #6 周起始日（网格数据已在 VM 构造时按设置完成首次加载，这里只同步表头，不再重复刷新 —— ISSUES #10）
        _calendarViewModel.WeekStartDay = _settings.WeekStartDay == WeekStartDay.Monday ? 1 : 0;
        Calendar.UpdateWeekHeaders(_calendarViewModel.WeekStartDay);
        Calendar.ShowWeekNumbers = _settings.ShowWeekNumbers;
        UpdateSenScheduleButton();
    }

    private void UpdateSenScheduleButton()
    {
        bool enabled = _settings.SenScheduleEnabled;
        // 设置里关闭森日程总开关时，主窗口不展示森按钮
        SenScheduleButton.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        SenScheduleButton.Background = enabled
            ? (Brush)FindResource("SelectedBrush")
            : Brushes.Transparent;
        SenScheduleButtonText.SetResourceReference(
            TextBlock.ForegroundProperty,
            enabled ? "TodayAccentBrush" : "TextSecondaryBrush");
        SenScheduleButton.ToolTip = enabled ? "关闭森日程" : "开启森日程";
    }

    public void RefreshSettings()
    {
        ApplySettings();
        _ = _calendarViewModel.ReloadSettingsAsync();
    }

    /// <summary>
    /// 字号基准表，与 Views/Themes/FontSizes.xaml 一一对应。
    /// ISSUES #16：字号已收敛为资源键，这里按档位覆写应用级资源，
    /// 替代旧的 ScaleTransform 整体缩放（整体缩放会导致布局失真与渲染模糊）。
    /// </summary>
    private static readonly (string Key, double Base)[] FontSizeResources =
    {
        ("FontSizeHeading", 18),
        ("FontSizePreviewLarge", 17),
        ("FontSizeWindowTitle", 15),
        ("FontSizeSubtitle", 14),
        ("FontSizeItemTitle", 13),
        ("FontSizeBody", 12),
        ("FontSizeSecondary", 11),
        ("FontSizeFootnote", 10),
        ("FontSizeCaption", 9),
        ("FontSizeLunar", 8),
        ("FontSizeCalendarInfo", 7.5),
    };

    private const double MainWindowScale = 1.3;

    /// <summary>
    /// 应用字体大小偏移：offset 范围 -2~+2，每级缩放 6%（与旧版手感一致），
    /// 对每个字号资源键按档位覆写。DynamicResource 使已打开的窗口即时生效。
    /// </summary>
    private void ApplyFontSizeOffset()
    {
        double scale = 1.0 + _settings.FontSizeOffset * 0.06;
        foreach ((string key, double baseSize) in FontSizeResources)
        {
            Application.Current.Resources[key] = baseSize * scale;
            Resources[key] = baseSize * MainWindowScale * scale;
        }
    }

    /// <summary>
    /// 隐藏面板：子窗口一并收起后立即隐藏，无退场动画。
    /// 窗口实例保留复用；真正释放发生在应用退出关闭时。
    /// </summary>
    public void HidePopup()
    {
        if (!IsVisible)
            return;

        CloseWeatherForecastWindow();
        CloseDetailWindow();
        Hide();
    }

    /// <summary>
    /// 点击日期后显示当天详情窗口，不再通过鼠标悬浮触发。
    /// </summary>
    private void OnDateClicked(CalendarDay day)
    {
        if (_detailWindow?.IsVisible == true
            && _detailDate?.Date == day.Date.Date)
        {
            CloseDetailWindow();
            return;
        }

        if (_detailWindow == null)
        {
            _detailWindow = new EventDetailWindow(_historyTodayService);
            _detailWindow.ShowActivated = false;
        }

        _detailDate = day.Date.Date;
        _detailWindow.ShowDay(day, this);
    }

    /// <summary>
    /// 关闭详情窗口
    /// </summary>
    private void CloseDetailWindow()
    {
        if (_detailWindow != null)
        {
            _detailWindow.Hide();
        }
        _detailDate = null;
    }

    private void SetWeatherSummary(WeatherInfo? weather, WeatherForecastDay? today)
    {
        if (weather == null)
        {
            CurrentTemperatureRun.Text = "--°";
            WeatherPrecipitationRun.Text = "--%";
            WeatherWindRun.Text = "-- km/h";
            WeatherFeelsLikeRun.Text = "--°";
            WeatherHumidityRun.Text = "--%";
            return;
        }

        CurrentTemperatureRun.Text = $"{weather.Temperature:F0}°";
        CurrentTemperatureRun.Foreground = GetTemperatureBrush(weather.Temperature);
        WeatherPrecipitationRun.Text = today == null
            ? "--%"
            : $"{today.PrecipitationProbability:F0}%";
        WeatherWindRun.Text = today == null
            ? "-- km/h"
            : $"{today.WindSpeed:F0} km/h";
        WeatherFeelsLikeRun.Text = $"{weather.FeelsLikeTemperature:F0}°";
        WeatherHumidityRun.Text = $"{weather.RelativeHumidity:F0}%";
        WeatherFeelsLikeRun.Foreground = GetTemperatureBrush(weather.FeelsLikeTemperature);
    }

    private static System.Windows.Media.Brush? GetTemperatureBrush(double temperature)
    {
        string resourceKey = temperature < 15
            ? "WeatherColdBrush"
            : temperature > 25
                ? "WeatherWarmBrush"
                : "TextPrimaryBrush";
        return Application.Current.TryFindResource(resourceKey) as System.Windows.Media.Brush;
    }

    private void CloseWeatherForecastWindow()
    {
        if (_weatherForecastWindow != null)
        {
            _weatherForecastWindow.Hide();
        }
    }

    private void DisposeChildWindows()
    {
        if (_detailWindow != null)
        {
            _detailWindow.Close();
            _detailWindow = null;
        }

        if (_weatherForecastWindow != null)
        {
            _weatherForecastWindow.Close();
            _weatherForecastWindow = null;
        }

        _detailDate = null;
    }

    /// <summary>
    /// 隐藏时同时关闭详情
    /// </summary>
    protected override void OnClosed(EventArgs e)
    {
        DisposeChildWindows();
        if (_weatherLoadCts != null)
        {
            try
            {
                _weatherLoadCts.Cancel();
            }
            finally
            {
                _weatherLoadCts.Dispose();
                _weatherLoadCts = null;
            }
        }
        _calendarViewModel.Dispose(); // 释放数据源服务持有的资源（ICS HttpClient 等）
        base.OnClosed(e);
    }

    /// <summary>
    /// 更新事件列表区域的可见性（有事件显示列表，无事件显示占位文案）
    /// </summary>
    private void UpdateNoEventsVisibility()
    {
        bool hasEvents = _calendarViewModel.UpcomingEvents.Count > 0;
        EventScrollViewer.Visibility = hasEvents ? Visibility.Visible : Visibility.Collapsed;
        NoEventsText.Visibility = hasEvents ? Visibility.Collapsed : Visibility.Visible;
        EndMarker.Visibility = hasEvents ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// 点击刷新按钮强制刷新日历数据
    /// </summary>
    private void OnRefreshClick(object sender, RoutedEventArgs e)
    {
        _ = RefreshCalendarAsync();
    }

    private async Task RefreshCalendarAsync()
    {
        RefreshButton.IsEnabled = false;
        try
        {
            await _calendarViewModel.ForceRefreshAsync();
            UpdateNoEventsVisibility();
        }
        finally
        {
            RefreshButton.IsEnabled = true;
        }
    }

    /// <summary>
    /// 点击齿轮图标打开设置窗口（模态）
    /// </summary>
    private void OnSettingsClick(object sender, RoutedEventArgs e)
    {
        CloseDetailWindow();
        App.ShowSettings();
    }

    private void OnSenScheduleClick(object sender, RoutedEventArgs e)
    {
        _ = ToggleSenScheduleAsync();
    }

    private async Task ToggleSenScheduleAsync()
    {
        SenScheduleButton.IsEnabled = false;
        try
        {
            await _calendarViewModel.SetSenScheduleEnabledAsync(!_settings.SenScheduleEnabled);
            UpdateSenScheduleButton();
            UpdateNoEventsVisibility();
        }
        finally
        {
            SenScheduleButton.IsEnabled = true;
        }
    }

    private void OnClosePopupClick(object sender, RoutedEventArgs e)
    {
        HidePopup();
    }
}