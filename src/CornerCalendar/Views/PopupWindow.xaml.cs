using CornerCalendar.Core.Helpers;
using CornerCalendar.Core.Models;
using CornerCalendar.Core.Services;
using CornerCalendar.ViewModels;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

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

        // 每次变为可见（首次弹出 / 从隐藏恢复）播放入场动画，避免生硬出现
        IsVisibleChanged += OnVisibilityChanged;
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

    private void OnVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true)
        {
            // 窗口先保持透明，等定位和布局完成后再设置起始位移，避免先绘制最终位置造成闪烁。
            RootBorder.Opacity = 0;
            RootTranslate.BeginAnimation(TranslateTransform.YProperty, null);
            Dispatcher.BeginInvoke(new Action(PlayShowAnimation), DispatcherPriority.Render);
        }
    }

    /// <summary>
    /// 入场动画：整个面板从窗口底边（任务栏方向）滑出直到最终位置。
    /// 窗口本身贴近任务栏定位，起始偏移量 = 面板高度，偏移部分被窗口边界裁剪，
    /// 视觉上即"从任务栏里弹出"。
    /// </summary>
    private void PlayShowAnimation()
    {
        UpdateLayout();

        // 多留一小段距离，让面板完全从任务栏方向外进入，视觉行程与放大后的面板高度匹配。
        double distance = Math.Max(RootBorder.ActualHeight, ActualHeight) + 16;
        if (distance <= 0)
        {
            RootBorder.Opacity = 1;
            return;
        }

        RootTranslate.Y = distance;
        RootBorder.Opacity = 1;

        CubicEase ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        DoubleAnimation slide = new DoubleAnimation(distance, 0, TimeSpan.FromMilliseconds(360))
        {
            EasingFunction = ease
        };
        RootTranslate.BeginAnimation(TranslateTransform.YProperty, slide);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ApplySettings();
        UpdateNoEventsVisibility();
        UpdateErrorVisibility();
        UpdateWeatherPage();
        StartWeatherLoad();
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
        }
        catch (OperationCanceledException)
        {
            // 切换天气位置时取消旧请求，不显示错误。
        }
        catch
        {
            if (!cancellationToken.IsCancellationRequested && index == _weatherIndex)
                WeatherSummaryText.Text = "天气获取失败";
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

        if (_currentWeather?.Forecast.Count > 0)
        {
            if (_weatherForecastWindow?.IsVisible == true)
            {
                _weatherForecastWindow.Hide();
                e.Handled = true;
                return;
            }

            _weatherForecastWindow ??= new WeatherForecastWindow();
            _weatherForecastWindow.ShowForecast(_currentWeather, this);
        }

        e.Handled = true;
    }

    private static bool IsInsideButton(DependencyObject? element)
    {
        while (element != null)
        {
            if (element is Button)
                return true;
            element = System.Windows.Media.VisualTreeHelper.GetParent(element);
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

    private bool _isHidingAnimated;

    /// <summary>
    /// 带动画关闭：面板向任务栏方向下滑后关闭（与入场动画对称）。
    /// </summary>
    public void HideAnimated()
    {
        if (!IsVisible || _isHidingAnimated)
            return;

        CloseWeatherForecastWindow();
        _isHidingAnimated = true;

        double distance = Math.Max(RootBorder.ActualHeight, 1);
        CubicEase ease = new CubicEase { EasingMode = EasingMode.EaseIn };
        DoubleAnimation slide = new DoubleAnimation(0, distance, TimeSpan.FromMilliseconds(200))
        {
            EasingFunction = ease
        };
        slide.Completed += (s, e) =>
        {
            _isHidingAnimated = false;
            RootTranslate.Y = 0;
            Close();
        };
        RootTranslate.BeginAnimation(TranslateTransform.YProperty, slide);
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
        CloseDetailWindow();
        HideAnimated();
    }
}
