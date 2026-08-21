using CornerCalendar.Core.Helpers;
using CornerCalendar.Core.Services;
using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;

namespace CornerCalendar.Views;

/// <summary>
/// ICS URL 列表项
/// </summary>
public class IcsUrlItem
{
    public int Index { get; set; }
    public string FullUrl { get; set; } = "";
    public string DisplayUrl { get; set; } = "";
    public string Color { get; set; } = "#FF6D00";
    public string Alias { get; set; } = "";
}

/// <summary>
/// 天气位置设置项。City 为空时表示使用当前公网 IP 自动定位。
/// </summary>
public class WeatherLocationItem
{
    public int Index { get; set; }
    public string City { get; set; } = "";
}

public partial class SettingsWindow : Window
{
    private const string GitHubLatestReleaseApi = "https://api.github.com/repos/Chendaqian/CornerCalendar/releases/latest";
    private const string GitHubReleasesUrl = "https://github.com/Chendaqian/CornerCalendar/releases/latest";
    private static readonly HttpClient UpdateHttpClient = CreateUpdateHttpClient();
    private readonly AppSettings _settings;
    private bool _initialized = false;
    private readonly List<IcsUrlItem> _icsUrls = new();
    private IcsUrlItem? _selectedIcsUrl;
    private readonly List<WeatherLocationItem> _weatherLocations = new();
    private WeatherLocationItem? _weatherDragItem;
    private Point _weatherDragStartPoint;

    private static readonly string[] FontSizeLabels = { "最小", "较小", "标准", "较大", "最大" };
    private static readonly int[] IcsRefreshValues = { 10, 30, 60, 120 };
    private static readonly int[] WeatherRefreshValues = { 30, 60, 120, 240 };

    private static HttpClient CreateUpdateHttpClient()
    {
        HttpClient client = new() { Timeout = TimeSpan.FromSeconds(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("CornerCalendar/1.0");
        return client;
    }

    private static readonly string[] SubscriptionColors = {
        "#FF6D00", "#0078D4", "#E91E63", "#00897B", "#7B1FA2", "#C62828", "#2E7D32", "#F57F17"
    };

    public SettingsWindow()
    {
        _settings = AppSettings.Load();

        InitializeComponent();

        // 底部版本信息：版本号读取自程序集（构建时由 CornerCalendar.csproj 的 <Version> 注入）
        FooterVersionText.Text = $"v{AppVersion}";
        AboutVersionText.Text = $"版本：v{AppVersion}";

        LoadSettings();
        _initialized = true;
        UpdateCategoryVisibility();
    }

    /// <summary>
    /// 应用版本号（来自 csproj 的 &lt;Version&gt;，构建时写入 InformationalVersion；
    /// SDK 可能追加 "+提交哈希" 后缀，需截断）
    /// </summary>
    private static string AppVersion
    {
        get
        {
            string version = Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "1.0";
            int plus = version.IndexOf('+');
            return plus >= 0 ? version[..plus] : version;
        }
    }

    /// <summary>
    /// 将保存的设置值加载到 UI 控件
    /// </summary>
    private void LoadSettings()
    {
        // #1 颜色主题
        ThemeComboBox.SelectedIndex = (int)_settings.ThemeMode;

        // #2 字体大小
        FontSizeSlider.Value = _settings.FontSizeOffset;
        UpdateFontSizeLabel();

        // #3 开机自启动
        AutoStartupCheckBox.IsChecked = _settings.AutoStartup;

        // 加载 ICS URL 列表（含别名）；旧配置为空时展示一条默认数据地址。
        List<string> icsUrls = _settings.IcsUrls ?? new List<string>();
        List<string> icsAliases = _settings.IcsAliases ?? new List<string>();
        if (icsUrls.Count == 0)
        {
            AppSettings defaults = AppSettings.CreateDefaults();
            icsUrls = defaults.IcsUrls;
            icsAliases = defaults.IcsAliases;
        }
        SetIcsUrlItems(icsUrls, icsAliases);
        BuiltInIcsList.ItemsSource = ChinaCalendarService.BuiltInSources;

        int refreshIdx = Array.IndexOf(IcsRefreshValues, _settings.IcsRefreshMinutes);
        IcsRefreshCombo.SelectedIndex = refreshIdx >= 0 ? refreshIdx : 1;

        WeatherApiUrlTextBox.Text = string.IsNullOrWhiteSpace(_settings.WeatherApiUrl)
            ? WeatherService.DefaultWeatherApiUrl
            : _settings.WeatherApiUrl;
        int weatherRefreshIndex = Array.IndexOf(WeatherRefreshValues, _settings.WeatherRefreshMinutes);
        WeatherRefreshCombo.SelectedIndex = weatherRefreshIndex >= 0 ? weatherRefreshIndex : 2;

        _weatherLocations.Clear();
        List<string> savedLocations = _settings.WeatherLocations ?? new List<string>();
        if (savedLocations.Count == 0)
            savedLocations.Add("");

        for (int i = 0; i < savedLocations.Count; i++)
        {
            _weatherLocations.Add(new WeatherLocationItem
            {
                Index = i,
                City = savedLocations[i]
            });
        }
        RefreshWeatherLocationList();

        // #5 近期事件天数
        UpcomingDaysCombo.SelectedIndex = _settings.UpcomingDays switch
        {
            1 => 0,
            7 => 2,
            _ => 1  // 3 天默认
        };

        // #6 周起始日
        WeekStartSunday.IsChecked = _settings.WeekStartDay == WeekStartDay.Sunday;
        WeekStartMonday.IsChecked = _settings.WeekStartDay == WeekStartDay.Monday;
        ShowWeekNumbersCheckBox.IsChecked = _settings.ShowWeekNumbers;

        // #12 任务栏时间格式
        TaskbarTimeFormatTextBox.Text = string.IsNullOrWhiteSpace(_settings.TaskbarTimeFormat)
            ? TaskbarClockFormatter.DefaultFormat
            : _settings.TaskbarTimeFormat;
        UpdateTaskbarTimePreview();

        // 字体大小滑块事件
        FontSizeSlider.ValueChanged += (_, _) => UpdateFontSizeLabel();
    }

    private void SetIcsUrlItems(IEnumerable<string> urls, IReadOnlyList<string>? aliases = null)
    {
        _icsUrls.Clear();
        List<string> urlList = urls.ToList();
        for (int i = 0; i < urlList.Count; i++)
        {
            string alias = aliases != null && i < aliases.Count ? aliases[i] : "";
            _icsUrls.Add(new IcsUrlItem
            {
                Index = i,
                FullUrl = urlList[i],
                DisplayUrl = ShortenUrl(urlList[i]),
                Color = SubscriptionColors[i % SubscriptionColors.Length],
                Alias = alias
            });
        }

        _selectedIcsUrl = null;
        RefreshIcsUrlList();
    }

    private void OnTaskbarTimeFormatChanged(object sender, TextChangedEventArgs e)
    {
        if (_initialized)
            UpdateTaskbarTimePreview();
    }

    private void UpdateTaskbarTimePreview()
    {
        if (TaskbarTimePreview == null || TaskbarTimeFormatTextBox == null)
            return;

        TaskbarTimePreview.Text = TaskbarClockFormatter.Format(
            DateTime.Now,
            TaskbarTimeFormatTextBox.Text);
    }

    /// <summary>
    /// 截短 URL 显示（保留域名和路径首尾）
    /// </summary>
    private static string ShortenUrl(string url)
    {
        if (string.IsNullOrEmpty(url)) return "";
        if (url.Length <= 60) return url;

        try
        {
            Uri uri = new Uri(url);
            string path = uri.AbsolutePath;
            if (path.Length > 30)
                path = path.Substring(0, 15) + "..." + path.Substring(path.Length - 10);
            return $"{uri.Host}{path}";
        }
        catch
        {
            return url.Substring(0, 30) + "...";
        }
    }

    /// <summary>
    /// 刷新 ICS URL 列表控件
    /// </summary>
    private void RefreshIcsUrlList()
    {
        if (IcsUrlList == null) return;
        IcsUrlList.ItemsSource = null;
        IcsUrlList.ItemsSource = _icsUrls;
        _selectedIcsUrl ??= _icsUrls.FirstOrDefault();
        if (_selectedIcsUrl != null && !_icsUrls.Contains(_selectedIcsUrl))
            _selectedIcsUrl = _icsUrls.FirstOrDefault();
        UpdateSelectedIcsSource();
    }

    private void OnIcsUrlSelected(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is IcsUrlItem item)
        {
            _selectedIcsUrl = item;
            UpdateSelectedIcsSource();
        }
    }

    private void UpdateSelectedIcsSource()
    {
        if (IcsSelectedSourcePanel == null || IcsSelectedSourceLink == null
            || IcsSelectedSourceLinkText == null)
            return;

        bool hasSelection = _selectedIcsUrl != null && _icsUrls.Contains(_selectedIcsUrl);
        IcsSelectedSourcePanel.Visibility = hasSelection
            ? Visibility.Visible
            : Visibility.Collapsed;
        string url = hasSelection ? _selectedIcsUrl!.FullUrl : string.Empty;
        IcsSelectedSourceLinkText.Text = url;
        IcsSelectedSourceLink.NavigateUri = hasSelection && Uri.TryCreate(
            url, UriKind.Absolute, out Uri? uri) ? uri : null;
    }

    private void OnIcsSelectedSourceClick(object sender, RoutedEventArgs e)
    {
        if (sender is Hyperlink { NavigateUri: Uri uri })
        {
            try
            {
                Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
            }
            catch { }
        }

        e.Handled = true;
    }

    private void OnIcsUrlLinkClick(object sender, RoutedEventArgs e)
    {
        if (sender is Hyperlink { DataContext: IcsUrlItem item })
            OpenExternalUrl(item.FullUrl);

        e.Handled = true;
    }

    private void OnChinaCalendarLinkClick(object sender, RoutedEventArgs e)
    {
        OpenExternalUrl("https://github.com/YangH9/ChinaCalendar");
        e.Handled = true;
    }

    private void OnBuiltInIcsLinkClick(object sender, RoutedEventArgs e)
    {
        if (sender is Hyperlink { DataContext: BuiltInIcsSource source })
            OpenExternalUrl(source.Url);

        e.Handled = true;
    }

    private static void OpenExternalUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch { }
    }

    /// <summary>
    /// 添加 ICS URL
    /// </summary>
    private void OnAddIcsUrl(object sender, RoutedEventArgs e)
    {
        string url = NewIcsUrlTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(url)) return;

        if (!url.StartsWith("http://") && !url.StartsWith("https://"))
        {
            MessageBox.Show("请输入有效的 HTTP/HTTPS 链接", "提示",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        int idx = _icsUrls.Count;
        _icsUrls.Add(new IcsUrlItem
        {
            Index = idx,
            FullUrl = url,
            DisplayUrl = ShortenUrl(url),
            Color = SubscriptionColors[idx % SubscriptionColors.Length]
        });

        _selectedIcsUrl = _icsUrls[^1];
        NewIcsUrlTextBox.Text = "";
        RefreshIcsUrlList();
    }

    /// <summary>
    /// 删除 ICS URL
    /// </summary>
    private void OnRemoveIcsUrl(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is int idx)
        {
            IcsUrlItem? item = _icsUrls.FirstOrDefault(u => u.Index == idx);
            if (item != null)
            {
                if (ReferenceEquals(_selectedIcsUrl, item))
                    _selectedIcsUrl = null;
                _icsUrls.Remove(item);
                // 重新编号和分配颜色
                for (int i = 0; i < _icsUrls.Count; i++)
                {
                    _icsUrls[i].Index = i;
                    _icsUrls[i].Color = SubscriptionColors[i % SubscriptionColors.Length];
                }
                RefreshIcsUrlList();
            }
        }
    }

    private void UpdateFontSizeLabel()
    {
        int idx = (int)FontSizeSlider.Value + 2; // -2..2 → 0..4
        FontSizeLabel.Text = FontSizeLabels[idx];
    }

    private void OnCategoryChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initialized)
            return;

        UpdateCategoryVisibility();
    }

    private void UpdateCategoryVisibility()
    {
        int selectedIndex = SettingsCategoryList?.SelectedIndex ?? 0;
        GeneralPanel.Visibility = selectedIndex == 0 ? Visibility.Visible : Visibility.Collapsed;
        CalendarPanel.Visibility = selectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
        WeatherPanel.Visibility = selectedIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
        DisplayPanel.Visibility = selectedIndex == 3 ? Visibility.Visible : Visibility.Collapsed;
        UpdatePanel.Visibility = selectedIndex == 4 ? Visibility.Visible : Visibility.Collapsed;
        AboutPanel.Visibility = selectedIndex == 5 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnCheckUpdateClick(object sender, RoutedEventArgs e)
    {
        CheckUpdateButton.IsEnabled = false;
        UpdateStatusText.Text = "正在检查最新版本...";
        _ = CheckForUpdatesAsync();
    }

    private async Task CheckForUpdatesAsync()
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(
                await UpdateHttpClient.GetStringAsync(GitHubLatestReleaseApi));
            string latestTag = document.RootElement.GetProperty("tag_name").GetString() ?? "";
            string releaseUrl = document.RootElement.TryGetProperty("html_url", out JsonElement url)
                ? url.GetString() ?? GitHubReleasesUrl
                : GitHubReleasesUrl;
            string latestVersionText = latestTag.TrimStart('v', 'V');

            if (!Version.TryParse(AppVersion, out Version? currentVersion)
                || !Version.TryParse(latestVersionText, out Version? latestVersion))
            {
                UpdateStatusText.Text = $"当前版本 v{AppVersion}，最新版本 {latestTag}。";
                ShowReleasePrompt(latestTag, releaseUrl);
                return;
            }

            if (latestVersion <= currentVersion)
            {
                UpdateStatusText.Text = $"当前版本 v{AppVersion} 已是最新版本。";
                MessageBox.Show(UpdateStatusText.Text, "检查更新", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                UpdateStatusText.Text = $"当前版本 v{AppVersion}，最新版本 {latestTag}。";
                ShowReleasePrompt(latestTag, releaseUrl);
            }
        }
        catch (Exception ex)
        {
            UpdateStatusText.Text = "检查更新失败，请检查网络连接。";
            MessageBox.Show($"检查更新失败：{ex.Message}", "检查更新", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            CheckUpdateButton.IsEnabled = true;
        }
    }

    private static void ShowReleasePrompt(string latestTag, string releaseUrl)
    {
        MessageBoxResult result = MessageBox.Show(
            $"发现新版本 {latestTag}。\n\nRelease 页面：\n{releaseUrl}\n\n是否打开 Release 页面？",
            "发现新版本",
            MessageBoxButton.YesNo,
            MessageBoxImage.Information);
        if (result == MessageBoxResult.Yes)
        {
            try
            {
                Process.Start(new ProcessStartInfo(releaseUrl) { UseShellExecute = true });
            }
            catch { }
        }
    }

    private void RefreshWeatherLocationList()
    {
        if (WeatherLocationList == null)
            return;

        WeatherLocationList.ItemsSource = null;
        WeatherLocationList.ItemsSource = _weatherLocations;
    }

    private void OnAddWeatherLocation(object sender, RoutedEventArgs e)
    {
        string city = NewWeatherCityTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(city))
        {
            MessageBox.Show("请输入城市名称", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _weatherLocations.Add(new WeatherLocationItem
        {
            Index = _weatherLocations.Count,
            City = city
        });
        NewWeatherCityTextBox.Text = "";
        RefreshWeatherLocationList();
    }

    private void OnRemoveWeatherLocation(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not int index)
            return;

        WeatherLocationItem? item = _weatherLocations.FirstOrDefault(location => location.Index == index);
        if (item == null)
            return;

        _weatherLocations.Remove(item);
        if (_weatherLocations.Count == 0)
        {
            _weatherLocations.Add(new WeatherLocationItem { Index = 0, City = "" });
        }

        NormalizeWeatherLocationIndices();
        RefreshWeatherLocationList();
    }

    private void OnWeatherDragHandleMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement handle
            || handle.DataContext is not WeatherLocationItem item)
        {
            return;
        }

        _weatherDragItem = item;
        _weatherDragStartPoint = e.GetPosition(null);
        handle.CaptureMouse();
        e.Handled = true;
    }

    private void OnWeatherDragHandleMouseMove(object sender, MouseEventArgs e)
    {
        if (_weatherDragItem == null || e.LeftButton != MouseButtonState.Pressed)
            return;

        Point currentPoint = e.GetPosition(null);
        if (Math.Abs(currentPoint.X - _weatherDragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(currentPoint.Y - _weatherDragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        if (sender is not UIElement handle)
            return;

        WeatherLocationItem item = _weatherDragItem;
        _weatherDragItem = null;
        if (handle.IsMouseCaptured)
            handle.ReleaseMouseCapture();

        DataObject dragData = new(typeof(WeatherLocationItem), item);
        DragDrop.DoDragDrop(handle, dragData, DragDropEffects.Move);
        e.Handled = true;
    }

    private void OnWeatherLocationDragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(typeof(WeatherLocationItem)))
        {
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
    }

    private void OnWeatherLocationDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(WeatherLocationItem)) is not WeatherLocationItem source
            || sender is not FrameworkElement targetElement
            || targetElement.DataContext is not WeatherLocationItem target
            || ReferenceEquals(source, target))
        {
            return;
        }

        int sourceIndex = _weatherLocations.IndexOf(source);
        int targetIndex = _weatherLocations.IndexOf(target);
        if (sourceIndex < 0 || targetIndex < 0)
            return;

        _weatherLocations.RemoveAt(sourceIndex);
        if (sourceIndex < targetIndex)
            targetIndex--;

        _weatherLocations.Insert(targetIndex, source);
        NormalizeWeatherLocationIndices();
        RefreshWeatherLocationList();
        e.Handled = true;
    }

    private void NormalizeWeatherLocationIndices()
    {
        for (int i = 0; i < _weatherLocations.Count; i++)
            _weatherLocations[i].Index = i;
    }

    private void OnApply(object sender, RoutedEventArgs e)
    {
        SaveSettings(closeWindow: false);
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        SaveSettings(closeWindow: true);
    }

    /// <summary>
    /// 从 UI 控件收集值并保存。
    /// </summary>
    private void SaveSettings(bool closeWindow)
    {
        string weatherApiUrl = WeatherApiUrlTextBox.Text.Trim();
        if (!Uri.TryCreate(weatherApiUrl, UriKind.Absolute, out Uri? weatherUri)
            || (weatherUri.Scheme != Uri.UriSchemeHttp
                && weatherUri.Scheme != Uri.UriSchemeHttps))
        {
            MessageBox.Show("请输入有效的 HTTP/HTTPS 天气服务地址", "提示",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // #1 颜色主题
        _settings.ThemeMode = (ThemeMode)ThemeComboBox.SelectedIndex;

        // 立即应用主题切换
        ThemeHelper.ApplyTheme(_settings.ThemeMode);

        // #2 字体大小
        _settings.FontSizeOffset = (int)FontSizeSlider.Value;

        // #3 开机自启动
        _settings.AutoStartup = AutoStartupCheckBox.IsChecked == true;
        ApplyAutoStartup(_settings.AutoStartup);

        _settings.IcsUrls = _icsUrls.Select(u => u.FullUrl).ToList();
        _settings.IcsAliases = _icsUrls.Select(u => u.Alias ?? "").ToList();
        _settings.IcsRefreshMinutes = IcsRefreshValues[IcsRefreshCombo.SelectedIndex];
        _settings.WeatherApiUrl = weatherApiUrl;
        _settings.WeatherRefreshMinutes = WeatherRefreshValues[WeatherRefreshCombo.SelectedIndex];
        _settings.WeatherLocations = _weatherLocations
            .Select(location => location.City.Trim())
            .ToList();

        // #5 近期事件天数
        _settings.UpcomingDays = UpcomingDaysCombo.SelectedIndex switch
        {
            0 => 1,
            2 => 7,
            _ => 3
        };

        // #6 周起始日
        _settings.WeekStartDay = WeekStartMonday.IsChecked == true
            ? WeekStartDay.Monday
            : WeekStartDay.Sunday;
        _settings.ShowWeekNumbers = ShowWeekNumbersCheckBox.IsChecked == true;

        // #12 任务栏时间格式
        _settings.TaskbarTimeFormat = string.IsNullOrWhiteSpace(TaskbarTimeFormatTextBox.Text)
            ? TaskbarClockFormatter.DefaultFormat
            : TaskbarTimeFormatTextBox.Text.Trim();

        // 持久化
        _settings.Save();
        App.RefreshTaskbarClock(_settings.TaskbarTimeFormat);
        App.RefreshCalendarSettings();
        App.RefreshWeatherSettings();

        if (closeWindow)
            Close();
    }

    /// <summary>
    /// 恢复默认值
    /// </summary>
    private void OnResetDefaults(object sender, RoutedEventArgs e)
    {
        MessageBoxResult result = MessageBox.Show(
            "确定恢复所有设置为默认值？", "恢复默认",
            MessageBoxButton.OKCancel, MessageBoxImage.Question);

        if (result != MessageBoxResult.OK) return;

        AppSettings defaults = AppSettings.CreateDefaults();

        ThemeComboBox.SelectedIndex = (int)defaults.ThemeMode;
        FontSizeSlider.Value = defaults.FontSizeOffset;
        AutoStartupCheckBox.IsChecked = defaults.AutoStartup;
        SetIcsUrlItems(defaults.IcsUrls, defaults.IcsAliases);
        NewIcsUrlTextBox.Text = "";
        IcsRefreshCombo.SelectedIndex = Array.IndexOf(IcsRefreshValues, defaults.IcsRefreshMinutes);
        _weatherLocations.Clear();
        for (int i = 0; i < defaults.WeatherLocations.Count; i++)
        {
            _weatherLocations.Add(new WeatherLocationItem
            {
                Index = i,
                City = defaults.WeatherLocations[i]
            });
        }
        RefreshWeatherLocationList();
        NewWeatherCityTextBox.Text = "";
        WeatherApiUrlTextBox.Text = defaults.WeatherApiUrl;
        WeatherRefreshCombo.SelectedIndex = Array.IndexOf(WeatherRefreshValues, defaults.WeatherRefreshMinutes);
        UpcomingDaysCombo.SelectedIndex = defaults.UpcomingDays switch
        {
            1 => 0,
            7 => 2,
            _ => 1
        };
        WeekStartSunday.IsChecked = defaults.WeekStartDay == WeekStartDay.Sunday;
        WeekStartMonday.IsChecked = defaults.WeekStartDay == WeekStartDay.Monday;
        ShowWeekNumbersCheckBox.IsChecked = defaults.ShowWeekNumbers;
        TaskbarTimeFormatTextBox.Text = defaults.TaskbarTimeFormat;
    }

    private void ApplyAutoStartup(bool enable)
    {
        try
        {
            if (enable)
                StartupHelper.Enable();
            else
                StartupHelper.Disable();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"设置开机自启动失败：{ex.Message}", "错误",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnFooterLinkClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("https://chendaqian.github.io") { UseShellExecute = true });
        }
        catch { }
    }

    private void OnProjectLinkClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("https://github.com/Chendaqian/CornerCalendar")
            {
                UseShellExecute = true
            });
        }
        catch { }
    }

    private void OnReleaseLinkClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(GitHubReleasesUrl)
            {
                UseShellExecute = true
            });
        }
        catch { }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    /// <summary>
    /// 允许拖动无边框窗口
    /// </summary>
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        DragMove();
    }
}