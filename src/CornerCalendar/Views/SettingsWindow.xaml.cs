using CornerCalendar.Core.Helpers;
using CornerCalendar.Core.Models;
using CornerCalendar.Core.Services;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Threading;

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

public enum HolidayFilterCategory
{
    Gregorian,
    Lunar,
    Dynamic
}

public sealed class HolidayFilterOption : INotifyPropertyChanged
{
    private bool _isEnabled = true;

    public string Name { get; init; } = "";
    public string TimeText { get; init; } = "";
    public string DetailText { get; init; } = "";
    public DateTime SortDate { get; init; }
    public HolidayFilterCategory Category { get; init; }

    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (_isEnabled == value)
                return;

            _isEnabled = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsEnabled)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
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
    private readonly List<SenScheduleIteration> _senSchedules = new();
    private SenScheduleIteration? _senDragItem;
    private Point _senDragStartPoint;
    private readonly List<WeatherLocationItem> _weatherLocations = new();
    private readonly ObservableCollection<HolidayFilterOption> _holidayFilterOptions = new();
    private WeatherLocationItem? _weatherDragItem;
    private Point _weatherDragStartPoint;
    private bool _holidayFiltersLoaded;
    private bool _holidayDetailMode = true;

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
        _ = LoadHolidayFiltersAsync();
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
        List<int> userSubscriptionIndices = icsUrls
            .Select((url, index) => (url, index))
            .Where(item => !ChinaCalendarService.BuiltInSources.Any(source =>
                string.Equals(source.Url, item.url, StringComparison.OrdinalIgnoreCase)))
            .Select(item => item.index)
            .ToList();
        SetIcsUrlItems(
            userSubscriptionIndices.Select(index => icsUrls[index]),
            userSubscriptionIndices
                .Select(index => index < icsAliases.Count ? icsAliases[index] : string.Empty)
                .ToList());
        BuiltInIcsList.ItemsSource = ChinaCalendarService.BuiltInSources;

        int refreshIdx = Array.IndexOf(IcsRefreshValues, _settings.IcsRefreshMinutes);
        IcsRefreshCombo.SelectedIndex = refreshIdx >= 0 ? refreshIdx : 1;

        SenScheduleEnabledCheckBox.IsChecked = _settings.SenScheduleEnabled;
        SetSenSchedules(_settings.SenSchedules ?? new List<SenScheduleIteration>());

        WeatherApiUrlTextBox.Text = string.IsNullOrWhiteSpace(_settings.WeatherApiUrl)
            ? WeatherService.DefaultWeatherApiUrl
            : _settings.WeatherApiUrl;
        int weatherRefreshIndex = Array.IndexOf(WeatherRefreshValues, _settings.WeatherRefreshMinutes);
        WeatherRefreshCombo.SelectedIndex = weatherRefreshIndex >= 0 ? weatherRefreshIndex : 2;

        ShowHistoryTodayCheckBox.IsChecked = _settings.ShowHistoryToday;
        HashSet<string> historyCategories = (_settings.HistoryCategories ?? new List<string>())
            .ToHashSet(StringComparer.Ordinal);
        HistoryEventsCheckBox.IsChecked = historyCategories.Contains("事件");
        HistoryBirthsCheckBox.IsChecked = historyCategories.Contains("出生");
        HistoryDeathsCheckBox.IsChecked = historyCategories.Contains("逝世");
        HistoryMaxItemsCombo.SelectedIndex = _settings.HistoryMaxItems switch
        {
            5 => 0,
            20 => 2,
            0 => 3,
            _ => 1
        };
        HistoryMinYearCombo.SelectedIndex = _settings.HistoryMinYear switch
        {
            0 => 0,
            1800 => 2,
            _ => 1
        };

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

    private void SetSenSchedules(IEnumerable<SenScheduleIteration> schedules)
    {
        _senSchedules.Clear();
        _senSchedules.AddRange(schedules);
        RefreshSenScheduleList();
    }

    private void RefreshSenScheduleList()
    {
        SenIterationList.ItemsSource = null;
        SenIterationList.ItemsSource = _senSchedules;
        SenIterationEmptyText.Visibility = _senSchedules.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void PersistSenSchedules()
    {
        _settings.SenSchedules = _senSchedules.ToList();
        _settings.Save();
        App.RefreshCalendarSettings();
    }

    private void OnImportSenScheduleExcel(object sender, RoutedEventArgs e)
    {
        SenImportErrorText.Visibility = Visibility.Collapsed;

        OpenFileDialog dialog = new()
        {
            Title = "选择森日程 Excel 工作簿",
            Filter = "Excel 工作簿 (*.xlsx;*.xlsm)|*.xlsx;*.xlsm",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            IReadOnlyList<SenScheduleIteration> parsed = SenScheduleExcelParser.Parse(dialog.FileName);
            List<SenScheduleIteration> existing = parsed
                .Where(item => _senSchedules.Any(iteration =>
                    string.Equals(iteration.Name, item.Name, StringComparison.Ordinal)))
                .ToList();
            if (existing.Count > 0)
            {
                string names = string.Join("、", existing.Select(item => item.Name));
                MessageBoxResult result = MessageBox.Show(
                    $"以下迭代已存在：{names}\n是否用工作簿中的数据更新？",
                    "更新森日程",
                    MessageBoxButton.OKCancel,
                    MessageBoxImage.Question);
                if (result != MessageBoxResult.OK)
                    return;
            }

            foreach (SenScheduleIteration item in parsed)
            {
                SenScheduleIteration? previous = _senSchedules.FirstOrDefault(iteration =>
                    string.Equals(iteration.Name, item.Name, StringComparison.Ordinal));
                if (previous is null)
                {
                    _senSchedules.Add(item);
                    continue;
                }

                int index = _senSchedules.IndexOf(previous);
                item.Id = previous.Id;
                _senSchedules[index] = item;
            }

            PersistSenSchedules();
            RefreshSenScheduleList();
            SenImportErrorText.Text = $"已导入 {parsed.Count} 个迭代";
            SenImportErrorText.SetResourceReference(TextBlock.ForegroundProperty, "TodayAccentBrush");
            SenImportErrorText.Visibility = Visibility.Visible;
        }
        catch (Exception ex) when (ex is FormatException or IOException or UnauthorizedAccessException)
        {
            SenImportErrorText.Text = ex.Message;
            SenImportErrorText.SetResourceReference(TextBlock.ForegroundProperty, "WorkdayBadgeTextBrush");
            SenImportErrorText.Visibility = Visibility.Visible;
        }
    }

    private void OnPreviewSenSchedule(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.OriginalSource is Button
            || sender is not FrameworkElement element
            || element.DataContext is not SenScheduleIteration iteration)
        {
            return;
        }

        e.Handled = true;
        SenSchedulePreviewWindow preview = new(iteration)
        {
            Owner = this
        };
        preview.ShowDialog();
    }

    private void OnSenDragHandleMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement handle
            || handle.DataContext is not SenScheduleIteration iteration)
        {
            return;
        }

        _senDragItem = iteration;
        _senDragStartPoint = e.GetPosition(null);
        handle.CaptureMouse();
        e.Handled = true;
    }

    private void OnSenDragHandleMouseMove(object sender, MouseEventArgs e)
    {
        if (_senDragItem is null || e.LeftButton != MouseButtonState.Pressed)
            return;

        Point currentPoint = e.GetPosition(null);
        if (Math.Abs(currentPoint.X - _senDragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(currentPoint.Y - _senDragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        if (sender is not UIElement handle)
            return;

        SenScheduleIteration iteration = _senDragItem;
        _senDragItem = null;
        if (handle.IsMouseCaptured)
            handle.ReleaseMouseCapture();

        DataObject dragData = new(typeof(SenScheduleIteration), iteration);
        DragDrop.DoDragDrop(handle, dragData, DragDropEffects.Move);
        e.Handled = true;
    }

    private void OnSenScheduleDragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(typeof(SenScheduleIteration)))
        {
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
    }

    private void OnSenScheduleDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(SenScheduleIteration)) is not SenScheduleIteration source
            || sender is not FrameworkElement targetElement
            || targetElement.DataContext is not SenScheduleIteration target
            || ReferenceEquals(source, target))
        {
            return;
        }

        int sourceIndex = _senSchedules.IndexOf(source);
        int targetIndex = _senSchedules.IndexOf(target);
        if (sourceIndex < 0 || targetIndex < 0)
            return;

        _senSchedules.RemoveAt(sourceIndex);
        if (sourceIndex < targetIndex)
            targetIndex--;

        _senSchedules.Insert(targetIndex, source);
        PersistSenSchedules();
        RefreshSenScheduleList();
        e.Handled = true;
    }

    private void OnToggleSenScheduleVisibility(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string id)
            return;

        SenScheduleIteration? iteration = _senSchedules.FirstOrDefault(item => item.Id == id);
        if (iteration is null)
            return;

        iteration.IsEnabled = !iteration.IsEnabled;
        PersistSenSchedules();
        RefreshSenScheduleList();
        e.Handled = true;
    }

    private void OnRemoveSenSchedule(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string id)
            return;

        SenScheduleIteration? iteration = _senSchedules.FirstOrDefault(item => item.Id == id);
        if (iteration is null)
            return;

        MessageBoxResult result = MessageBox.Show(
            $"确定删除迭代“{iteration.Name}”？",
            "删除森日程",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Question);
        if (result != MessageBoxResult.OK)
            return;

        _senSchedules.Remove(iteration);
        PersistSenSchedules();
        RefreshSenScheduleList();
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
                DisplayUrl = ShortenSource(urlList[i]),
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

    private static string ShortenSource(string source)
    {
        if (IsLocalIcsSource(source))
            return $"本地：{Path.GetFileName(source)}";

        return ShortenUrl(source);
    }

    private static bool IsLocalIcsSource(string source)
        => Path.IsPathRooted(source)
            || source.StartsWith("file://", StringComparison.OrdinalIgnoreCase);

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
        string source = hasSelection ? _selectedIcsUrl!.FullUrl : string.Empty;
        IcsSelectedSourceLinkText.Text = hasSelection
            ? ShortenSource(source)
            : string.Empty;
        IcsSelectedSourceLink.NavigateUri = hasSelection
            && !IsLocalIcsSource(source)
            && Uri.TryCreate(source, UriKind.Absolute, out Uri? uri)
                ? uri
                : null;
    }

    private void OnIcsSelectedSourceClick(object sender, RoutedEventArgs e)
    {
        if (_selectedIcsUrl != null && IsLocalIcsSource(_selectedIcsUrl.FullUrl))
        {
            OpenExternalUrl(_selectedIcsUrl.FullUrl);
            e.Handled = true;
            return;
        }

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
    /// 添加本地 ICS 文件
    /// </summary>
    private void OnAddIcsUrl(object sender, RoutedEventArgs e)
    {
        OpenFileDialog dialog = new()
        {
            Title = "选择 ICS 日历文件",
            Filter = "ICS 日历文件 (*.ics)|*.ics|所有文件 (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog(this) != true)
            return;

        string path = Path.GetFullPath(dialog.FileName);
        if (_icsUrls.Any(item => string.Equals(item.FullUrl, path, StringComparison.OrdinalIgnoreCase)))
            return;

        int index = _icsUrls.Count;
        _icsUrls.Add(new IcsUrlItem
        {
            Index = index,
            FullUrl = path,
            DisplayUrl = ShortenSource(path),
            Color = SubscriptionColors[index % SubscriptionColors.Length]
        });
        _selectedIcsUrl = _icsUrls[^1];
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

    private void OnSettingsCategoryMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (SettingsCategoryList.Items.Count == 0)
            return;

        int direction = e.Delta < 0 ? 1 : -1;
        int nextIndex = Math.Clamp(
            SettingsCategoryList.SelectedIndex + direction,
            0,
            SettingsCategoryList.Items.Count - 1);
        if (nextIndex != SettingsCategoryList.SelectedIndex)
            SettingsCategoryList.SelectedIndex = nextIndex;

        e.Handled = true;
    }

    private void UpdateCategoryVisibility()
    {
        int selectedIndex = SettingsCategoryList?.SelectedIndex ?? 0;
        GeneralPanel.Visibility = selectedIndex == 0 ? Visibility.Visible : Visibility.Collapsed;
        CalendarPanel.Visibility = selectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
        SenPanel.Visibility = selectedIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
        HolidayPanel.Visibility = selectedIndex == 3 ? Visibility.Visible : Visibility.Collapsed;
        WeatherPanel.Visibility = selectedIndex == 4 ? Visibility.Visible : Visibility.Collapsed;
        HistoryPanel.Visibility = selectedIndex == 5 ? Visibility.Visible : Visibility.Collapsed;
        DisplayPanel.Visibility = selectedIndex == 6 ? Visibility.Visible : Visibility.Collapsed;
        AboutPanel.Visibility = selectedIndex == 7 ? Visibility.Visible : Visibility.Collapsed;
    }

    private async Task LoadHolidayFiltersAsync()
    {
        try
        {
            using ChinaCalendarService service = new(_settings.IcsRefreshMinutes);
            DateTime start = new(DateTime.Today.Year, 1, 1);
            List<CalendarEvent> events = await service.GetEventsAsync(start, start.AddYears(2));
            IReadOnlyList<HolidayFilterOption> options = BuildHolidayFilterOptions(events);
            HashSet<string> hiddenNames = (_settings.HiddenHolidayNames ?? new List<string>())
                .Select(CalendarEventFilter.NormalizeName)
                .ToHashSet(StringComparer.Ordinal);

            _holidayFilterOptions.Clear();
            foreach (HolidayFilterOption option in options)
            {
                option.IsEnabled = !hiddenNames.Contains(option.Name);
                _holidayFilterOptions.Add(option);
            }

            GregorianHolidayList.ItemsSource = _holidayFilterOptions
                .Where(option => option.Category == HolidayFilterCategory.Gregorian)
                .ToList();
            LunarHolidayList.ItemsSource = _holidayFilterOptions
                .Where(option => option.Category == HolidayFilterCategory.Lunar)
                .ToList();
            DynamicHolidayList.ItemsSource = _holidayFilterOptions
                .Where(option => option.Category == HolidayFilterCategory.Dynamic)
                .ToList();

            _holidayFiltersLoaded = true;
            HolidayFilterLoadingText.Visibility = Visibility.Collapsed;
            bool isEmpty = options.Count == 0;
            HolidayFilterTabs.Visibility = isEmpty ? Visibility.Collapsed : Visibility.Visible;
            RefreshHolidayFilterLists();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"CornerCalendar: Failed to load holiday filter options: {ex.Message}");
            HolidayFilterLoadingText.Text = "节日列表加载失败";
            HolidayFilterEmptyText.Visibility = Visibility.Visible;
        }
    }

    private static IReadOnlyList<HolidayFilterOption> BuildHolidayFilterOptions(
        IEnumerable<CalendarEvent> events)
    {
        Dictionary<string, List<CalendarEvent>> groups = new(StringComparer.Ordinal);
        foreach (CalendarEvent calendarEvent in events)
        {
            string name = CalendarEventFilter.NormalizeName(calendarEvent.Title);
            if (name.Length == 0)
                continue;

            if (!groups.TryGetValue(name, out List<CalendarEvent>? group))
            {
                group = new List<CalendarEvent>();
                groups[name] = group;
            }

            group.Add(calendarEvent);
        }

        return groups
            .Select(pair => CreateHolidayFilterOption(pair.Key, pair.Value))
            .OrderBy(option => option.SortDate)
            .ThenBy(option => option.Name, StringComparer.Ordinal)
            .ToList();
    }

    private static HolidayFilterOption CreateHolidayFilterOption(
        string name,
        IReadOnlyList<CalendarEvent> events)
    {
        HolidayFilterCategory category = ClassifyHoliday(events);
        CalendarEvent firstEvent = events.OrderBy(calendarEvent => calendarEvent.StartTime).First();
        string timeText = BuildTimeText(category, events, firstEvent);
        string detailText = events
            .Select(calendarEvent => CleanRule(calendarEvent.Description))
            .FirstOrDefault(value => value.Length > 0)
            ?? string.Empty;

        return new HolidayFilterOption
        {
            Name = name,
            TimeText = timeText,
            DetailText = detailText,
            SortDate = firstEvent.StartTime.Date,
            Category = category
        };
    }

    private static string BuildTimeText(
        HolidayFilterCategory category,
        IReadOnlyList<CalendarEvent> events,
        CalendarEvent firstEvent)
    {
        if (category == HolidayFilterCategory.Gregorian)
            return $"{firstEvent.StartTime.Month}月{firstEvent.StartTime.Day}日";

        string timeText = events
            .Select(calendarEvent => CleanRule(calendarEvent.Description))
            .FirstOrDefault(value => value.Length > 0)
            ?? (category == HolidayFilterCategory.Lunar ? "农历日期" : "动态日期");

        if (timeText.StartsWith("每年", StringComparison.Ordinal))
            timeText = timeText[2..].TrimStart();

        int separatorIndex = timeText.IndexOfAny(['（', '(', '，', ',', '。', '；', ';']);
        if (separatorIndex > 0)
            timeText = timeText[..separatorIndex].TrimEnd();

        if (category == HolidayFilterCategory.Lunar
            && !timeText.StartsWith("农历", StringComparison.Ordinal))
        {
            timeText = "农历" + timeText;
        }

        return timeText.Length <= 18 ? timeText : $"{timeText[..18]}...";
    }

    private static HolidayFilterCategory ClassifyHoliday(
        IReadOnlyList<CalendarEvent> events)
    {
        if (events.Any(calendarEvent =>
                string.Equals(calendarEvent.CalendarName, "中国日历-二十四节气", StringComparison.Ordinal)))
        {
            return HolidayFilterCategory.Dynamic;
        }

        if (events.Any(calendarEvent => IsLunarDescription(calendarEvent.Description)))
            return HolidayFilterCategory.Lunar;

        if (events.Any(calendarEvent => IsDynamicDescription(calendarEvent.Description))
            || events.Select(calendarEvent => (calendarEvent.StartTime.Month, calendarEvent.StartTime.Day))
                .Distinct()
                .Count() > 1)
        {
            return HolidayFilterCategory.Dynamic;
        }

        return HolidayFilterCategory.Gregorian;
    }

    private static bool IsLunarDescription(string? description)
        => description is not null
            && (description.Contains("农历", StringComparison.Ordinal)
                || description.Contains("正月", StringComparison.Ordinal)
                || description.Contains("腊月", StringComparison.Ordinal));

    private static bool IsDynamicDescription(string? description)
        => description is not null
            && (description.Contains("星期", StringComparison.Ordinal)
                || description.Contains("节气", StringComparison.Ordinal)
                || description.Contains("第", StringComparison.Ordinal));

    private static string CleanRule(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
            return string.Empty;

        return description
            .Replace("\\r", " ", StringComparison.OrdinalIgnoreCase)
            .Replace("\\n", " ", StringComparison.OrdinalIgnoreCase)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Trim();
    }

    private void OnHolidaySearchTextChanged(object sender, TextChangedEventArgs e)
    {
        if (HolidaySearchPlaceholder != null)
        {
            HolidaySearchPlaceholder.Visibility = string.IsNullOrEmpty(HolidaySearchTextBox.Text)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        if (_holidayFiltersLoaded)
            RefreshHolidayFilterLists();
    }

    private void OnHolidayFilterTabChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_holidayFiltersLoaded)
        {
            double settingsScrollOffset = SettingsContentScrollViewer.VerticalOffset;
            RefreshHolidayFilterLists();
            ResetHolidayScrollViewers();
            _ = ResetHolidayScrollViewersAsync();
            _ = RestoreSettingsScrollAsync(settingsScrollOffset);
        }
    }

    private void OnHolidayDetailModeChanged(object sender, RoutedEventArgs e)
    {
        _holidayDetailMode = HolidayDetailToggle.IsChecked == true;
        if (_holidayFiltersLoaded)
        {
            RefreshHolidayFilterLists();
            ResetHolidayScrollViewers();
            _ = ResetHolidayScrollViewersAsync();
        }
    }

    private void OnHolidayScrollViewerLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is ScrollViewer scrollViewer)
            ResetHolidayScrollViewer(scrollViewer);
    }

    private void OnHolidayScrollViewerIsVisibleChanged(
        object sender,
        DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true && sender is ScrollViewer scrollViewer)
        {
            ResetHolidayScrollViewer(scrollViewer);
            _ = ResetHolidayScrollViewerAsync(scrollViewer);
        }
    }

    private void ResetHolidayScrollViewers()
    {
        ResetHolidayScrollViewer(GregorianHolidayScrollViewer);
        ResetHolidayScrollViewer(LunarHolidayScrollViewer);
        ResetHolidayScrollViewer(DynamicHolidayScrollViewer);
    }

    private async Task ResetHolidayScrollViewersAsync()
    {
        await Dispatcher.InvokeAsync(
            ResetHolidayScrollViewers,
            DispatcherPriority.Render);
        await Dispatcher.InvokeAsync(
            ResetHolidayScrollViewers,
            DispatcherPriority.ContextIdle);
    }

    private async Task RestoreSettingsScrollAsync(double offset)
    {
        await Dispatcher.InvokeAsync(
            () => SettingsContentScrollViewer.ScrollToVerticalOffset(offset),
            DispatcherPriority.Render);
        await Dispatcher.InvokeAsync(
            () => SettingsContentScrollViewer.ScrollToVerticalOffset(offset),
            DispatcherPriority.ContextIdle);
    }

    private async Task ResetHolidayScrollViewerAsync(ScrollViewer scrollViewer)
    {
        await Dispatcher.InvokeAsync(
            () => ResetHolidayScrollViewer(scrollViewer),
            DispatcherPriority.ContextIdle);
    }

    private static void ResetHolidayScrollViewer(ScrollViewer scrollViewer)
    {
        scrollViewer.ScrollToTop();
        scrollViewer.ScrollToVerticalOffset(0);
    }

    private void RefreshHolidayFilterLists()
    {
        HolidayFilterCategory category = HolidayFilterTabs.SelectedIndex switch
        {
            1 => HolidayFilterCategory.Lunar,
            2 => HolidayFilterCategory.Dynamic,
            _ => HolidayFilterCategory.Gregorian
        };
        string query = HolidaySearchTextBox.Text.Trim();
        List<HolidayFilterOption> visibleOptions = _holidayFilterOptions
            .Where(option => option.Category == category)
            .Where(option => query.Length == 0
                || option.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                || option.TimeText.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderBy(option => option.SortDate)
            .ThenBy(option => option.Name, StringComparer.Ordinal)
            .ToList();
        DataTemplate itemTemplate = (DataTemplate)FindResource(
            _holidayDetailMode
                ? "HolidayFilterDetailItemTemplate"
                : "HolidayFilterBriefItemTemplate");

        switch (category)
        {
            case HolidayFilterCategory.Gregorian:
                GregorianHolidayList.ItemTemplate = itemTemplate;
                GregorianHolidayList.ItemsSource = visibleOptions;
                break;

            case HolidayFilterCategory.Lunar:
                LunarHolidayList.ItemTemplate = itemTemplate;
                LunarHolidayList.ItemsSource = visibleOptions;
                break;

            case HolidayFilterCategory.Dynamic:
                DynamicHolidayList.ItemTemplate = itemTemplate;
                DynamicHolidayList.ItemsSource = visibleOptions;
                break;
        }

        HolidayFilterEmptyText.Text = query.Length == 0
            ? "暂无可配置的节日"
            : "没有找到相关节日";
        HolidayFilterEmptyText.Visibility = visibleOptions.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
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
        _settings.SenScheduleEnabled = SenScheduleEnabledCheckBox.IsChecked == true;
        _settings.SenSchedules = _senSchedules.ToList();
        if (_holidayFiltersLoaded)
        {
            _settings.HiddenHolidayNames = _holidayFilterOptions
                .Where(option => !option.IsEnabled)
                .Select(option => option.Name)
                .ToList();
        }
        _settings.WeatherApiUrl = weatherApiUrl;
        _settings.WeatherRefreshMinutes = WeatherRefreshValues[WeatherRefreshCombo.SelectedIndex];
        _settings.WeatherLocations = _weatherLocations
            .Select(location => location.City.Trim())
            .ToList();

        _settings.ShowHistoryToday = ShowHistoryTodayCheckBox.IsChecked == true;
        _settings.HistoryCategories = new[]
        {
            (Enabled: HistoryEventsCheckBox.IsChecked == true, Category: "事件"),
            (Enabled: HistoryBirthsCheckBox.IsChecked == true, Category: "出生"),
            (Enabled: HistoryDeathsCheckBox.IsChecked == true, Category: "逝世")
        }.Where(item => item.Enabled).Select(item => item.Category).ToList();
        _settings.HistoryMaxItems = HistoryMaxItemsCombo.SelectedIndex switch
        {
            0 => 5,
            2 => 20,
            3 => 0,
            _ => 10
        };
        _settings.HistoryMinYear = HistoryMinYearCombo.SelectedIndex switch
        {
            0 => 0,
            2 => 1800,
            _ => 1900
        };

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
        IcsRefreshCombo.SelectedIndex = Array.IndexOf(IcsRefreshValues, defaults.IcsRefreshMinutes);
        SenScheduleEnabledCheckBox.IsChecked = defaults.SenScheduleEnabled;
        _senSchedules.Clear();
        SenImportErrorText.Visibility = Visibility.Collapsed;
        RefreshSenScheduleList();
        foreach (HolidayFilterOption option in _holidayFilterOptions)
            option.IsEnabled = true;
        HolidayFilterTabs.SelectedIndex = 0;
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
        ShowHistoryTodayCheckBox.IsChecked = defaults.ShowHistoryToday;
        HistoryEventsCheckBox.IsChecked = true;
        HistoryBirthsCheckBox.IsChecked = false;
        HistoryDeathsCheckBox.IsChecked = false;
        HistoryMaxItemsCombo.SelectedIndex = 1;
        HistoryMinYearCombo.SelectedIndex = 1;
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