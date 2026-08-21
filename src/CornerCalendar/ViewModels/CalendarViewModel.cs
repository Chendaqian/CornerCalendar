using CornerCalendar.Core.Helpers;
using CornerCalendar.Core.Models;
using CornerCalendar.Core.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace CornerCalendar.ViewModels;

/// <summary>
/// 日历主 ViewModel：月份导航、日期网格生成、事件聚合
/// </summary>
public class CalendarViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly ICalendarService _calendarService;

    private int _year;
    private int _month;
    private string _monthDisplay = string.Empty;
    private DateTime _selectedDate = DateTime.Today;
    private bool _isLoading;
    private string? _errorText;

    private IReadOnlyDictionary<DateTime, ChinaCalendarDayInfo> _chinaCalendarInfo =
        new Dictionary<DateTime, ChinaCalendarDayInfo>();

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// 当前显示的年份
    /// </summary>
    public int Year
    {
        get => _year;
        set { _year = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// 当前显示的月份（1~12）
    /// </summary>
    public int Month
    {
        get => _month;
        set { _month = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// 月份显示文本，如 "2025年5月"
    /// </summary>
    public string MonthDisplay
    {
        get => _monthDisplay;
        set { _monthDisplay = value; OnPropertyChanged(); }
    }

    private int _selectedDayIndex = -1;

    /// <summary>
    /// 当前选中日期
    /// </summary>
    public DateTime SelectedDate
    {
        get => _selectedDate;
        set { _selectedDate = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// 选中日期在 CalendarDays 中的索引（-1 表示无选中）
    /// </summary>
    public int SelectedDayIndex
    {
        get => _selectedDayIndex;
        set { _selectedDayIndex = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// 是否正在加载
    /// </summary>
    public bool IsLoading
    {
        get => _isLoading;
        set { _isLoading = value; OnPropertyChanged(); }
    }

    private const string LoadErrorMessage = "日程加载失败，请检查网络或数据源设置";

    /// <summary>
    /// 数据加载失败的错误提示（加载成功时为 null）
    /// </summary>
    public string? ErrorText
    {
        get => _errorText;
        set { _errorText = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// 日历网格数据（6行7列 = 42天，含上下月补位）
    /// </summary>
    public ObservableCollection<CalendarDay> CalendarDays { get; } = new();

    /// <summary>
    /// 近期事件列表
    /// </summary>
    public ObservableCollection<CalendarEvent> UpcomingEvents { get; } = new();

    /// <summary>
    /// 周起始日（0=周日, 1=周一）
    /// </summary>
    public int WeekStartDay { get; set; } = 1; // 默认周一开始

    /// <summary>
    /// 近期事件天数范围
    /// </summary>
    public int UpcomingDays { get; set; } = 7;

    public CalendarViewModel() : this(CreateDefaultService())
    {
    }

    public CalendarViewModel(ICalendarService calendarService)
    {
        _calendarService = calendarService;

        // 首次刷新前加载全部影响渲染的设置（周起始日 / 近期事件天数），
        // 保证第一次生成网格就用正确的周起始日，避免首屏双重刷新
        AppSettings settings = AppSettings.Load();
        UpcomingDays = settings.UpcomingDays;
        WeekStartDay = settings.WeekStartDay == Core.Services.WeekStartDay.Monday ? 1 : 0;

        NavigateToToday();
    }

    /// <summary>
    /// 根据用户设置创建日历服务
    /// </summary>
    private static ICalendarService CreateDefaultService()
    {
        AppSettings settings = AppSettings.Load();

        List<ICalendarService> services = new()
        {
            new ChinaCalendarService(settings.IcsRefreshMinutes)
        };

        // 日程统一来自 ICS：中国日历本身也是 ICS，用户还可以添加自己的订阅。
        services.Add(CreateIcsService(settings));

        return new AggregateCalendarService(services.ToArray());
    }

    private static readonly string[] SubscriptionColors = {
        "#FF6D00", "#0078D4", "#E91E63", "#00897B", "#7B1FA2", "#C62828", "#2E7D32", "#F57F17"
    };

    private static ICalendarService CreateIcsService(AppSettings settings)
    {
        List<(string Url, string Alias)> subscriptions = (settings.IcsUrls ?? new List<string>())
            .Select((url, index) => (
                Url: url,
                Alias: settings.IcsAliases != null && index < settings.IcsAliases.Count
                    ? settings.IcsAliases[index]
                    : string.Empty))
            .Where(subscription => !string.IsNullOrWhiteSpace(subscription.Url)
                && !ChinaCalendarService.BuiltInSources.Any(source =>
                    string.Equals(source.Url, subscription.Url, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        if (subscriptions.Count == 0)
            return new EmptyCalendarService();

        if (subscriptions.Count == 1)
        {
            string name = GetAlias(subscriptions[0].Alias, 0, subscriptions[0].Url);
            return new IcsCalendarService(
                subscriptions[0].Url,
                settings.IcsRefreshMinutes,
                name,
                SubscriptionColors[0]);
        }

        // 多个 URL：创建多个服务并用 AggregateCalendarService 聚合
        List<ICalendarService> services = new List<ICalendarService>();
        for (int i = 0; i < subscriptions.Count; i++)
        {
            string name = GetAlias(subscriptions[i].Alias, i, subscriptions[i].Url);
            services.Add(new IcsCalendarService(
                subscriptions[i].Url,
                settings.IcsRefreshMinutes,
                name,
                SubscriptionColors[i % SubscriptionColors.Length]));
        }

        return new AggregateCalendarService(services.ToArray());
    }

    /// <summary>
    /// 获取订阅别名：优先使用用户设置的别名，否则从 URL 域名推断
    /// </summary>
    private static string GetAlias(string alias, int index, string url)
    {
        if (!string.IsNullOrWhiteSpace(alias))
            return alias;

        // 从 URL 推断默认名称
        try
        {
            string host = new Uri(url).Host;
            // 移除常见前缀
            if (host.StartsWith("calendar.")) host = host["calendar.".Length..];
            if (host.StartsWith("www.")) host = host["www.".Length..];
            if (host.StartsWith("cal.")) host = host["cal.".Length..];

            return host;
        }
        catch
        {
            return $"订阅 {index + 1}";
        }
    }

    /// <summary>
    /// 导航到今天
    /// </summary>
    public void NavigateToToday()
    {
        Year = DateTime.Today.Year;
        Month = DateTime.Today.Month;
        SelectedDate = DateTime.Today;
        UpdateMonthDisplay();
        _ = RefreshDataAsync();
    }

    public void NavigateToMonth(int year, int month)
    {
        if (month is < 1 or > 12)
            throw new ArgumentOutOfRangeException(nameof(month));

        int day = Math.Min(SelectedDate.Day, DateTime.DaysInMonth(year, month));
        NavigateToDate(new DateTime(year, month, day));
    }

    /// <summary>
    /// 导航到指定日期，并将该日期设为选中日期。
    /// </summary>
    public void NavigateToDate(DateTime date)
    {
        date = date.Date;
        Year = date.Year;
        Month = date.Month;
        SelectedDate = date;
        UpdateMonthDisplay();
        _ = RefreshDataAsync();
    }

    /// <summary>
    /// 上一月（不改变 SelectedDate，事件列表保持不变）
    /// </summary>
    public void NavigatePreviousMonth()
    {
        Month--;
        if (Month < 1)
        {
            Month = 12;
            Year--;
        }
        UpdateMonthDisplay();
        _ = RefreshDataAsync();
    }

    /// <summary>
    /// 下一月（不改变 SelectedDate，事件列表保持不变）
    /// </summary>
    public void NavigateNextMonth()
    {
        Month++;
        if (Month > 12)
        {
            Month = 1;
            Year++;
        }
        UpdateMonthDisplay();
        _ = RefreshDataAsync();
    }

    /// <summary>
    /// 选中某个日期
    /// </summary>
    public void SelectDate(DateTime date)
    {
        SelectedDate = date;

        // 更新选中索引
        SelectedDayIndex = -1;
        for (int i = 0; i < CalendarDays.Count; i++)
        {
            if (CalendarDays[i].Date.Date == date.Date)
            {
                SelectedDayIndex = i;
                break;
            }
        }

        // 刷新事件列表，显示选中日期起的事件
        _ = UpdateUpcomingEventsFromDateAsync(date);
    }

    /// <summary>
    /// 从指定日期开始更新近期事件列表（直接从服务获取，不受 CalendarDays 范围限制）
    /// </summary>
    private async Task UpdateUpcomingEventsFromDateAsync(DateTime fromDate)
    {
        UpcomingEvents.Clear();

        DateTime endDate = fromDate.Date.AddDays(UpcomingDays);

        List<CalendarEvent> events;
        try
        {
            events = await _calendarService.GetEventsAsync(fromDate.Date, endDate);
            ErrorText = null;
        }
        catch
        {
            ErrorText = LoadErrorMessage;
            return;
        }

        // 按时间排序，取前 100 条
        IEnumerable<CalendarEvent> sorted = events
            .OrderBy(e => e.StartTime)
            .Take(100);

        foreach (CalendarEvent? evt in sorted)
        {
            UpcomingEvents.Add(evt);
        }
    }

    /// <summary>
    /// 强制刷新（忽略缓存，重新拉取）。
    /// 刷新失败不向调用方抛错：设置 ErrorText 由界面提示（单个 ICS 服务无缓存断网时，
    /// 服务层会抛异常，若不管在这里会变成事件处理器里的未处理异常）。
    /// </summary>
    public async Task ForceRefreshAsync()
    {
        try
        {
            await _calendarService.ForceRefreshAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"CornerCalendar: ForceRefreshAsync failed: {ex.Message}");
            ErrorText = LoadErrorMessage;
        }

        await RefreshDataAsync();
    }

    /// <summary>
    /// 刷新所有数据：日历网格立即渲染，事件异步加载
    /// </summary>
    public async Task RefreshDataAsync()
    {
        // 第一阶段：立即生成日历网格（不依赖事件数据，纯日期计算）
        GenerateCalendarGrid(new List<CalendarEvent>());

        // 第二阶段：异步加载事件数据，再更新网格和事件列表
        IsLoading = true;
        try
        {
            DateTime rangeStart = new DateTime(Year, Month, 1).AddDays(-7);
            DateTime rangeEnd = new DateTime(Year, Month, 1).AddMonths(1).AddDays(7);

            List<CalendarEvent> events;
            try
            {
                events = await _calendarService.GetEventsAsync(rangeStart, rangeEnd);
                ErrorText = null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"CornerCalendar: RefreshDataAsync failed: {ex.Message}");
                events = new List<CalendarEvent>();
                ErrorText = LoadErrorMessage;
            }

            if (_calendarService is IChinaCalendarDataProvider chinaCalendarProvider)
            {
                try
                {
                    _chinaCalendarInfo = await chinaCalendarProvider.GetDayInfoAsync(rangeStart, rangeEnd);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"CornerCalendar: ChinaCalendar info refresh failed: {ex.Message}");
                    _chinaCalendarInfo = new Dictionary<DateTime, ChinaCalendarDayInfo>();
                }
            }
            else
            {
                _chinaCalendarInfo = new Dictionary<DateTime, ChinaCalendarDayInfo>();
            }

            // 用事件数据重新生成网格（更新事件点标记）
            GenerateCalendarGrid(events);

            // 生成近期事件列表（await 统一的 Task 方法，替代原 async void 实现）
            await UpdateUpcomingEventsFromDateAsync(SelectedDate);
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// 生成 6×7 日历网格
    /// </summary>
    private void GenerateCalendarGrid(List<CalendarEvent> events)
    {
        CalendarDays.Clear();

        DateTime firstDayOfMonth = new DateTime(Year, Month, 1);
        int daysInMonth = DateTime.DaysInMonth(Year, Month);

        // 计算第一天在网格中的偏移（考虑周起始日）
        int firstDayOfWeek = (int)firstDayOfMonth.DayOfWeek;
        int offset = (firstDayOfWeek - WeekStartDay + 7) % 7;

        // 网格起始日期（上月补位）
        DateTime gridStartDate = firstDayOfMonth.AddDays(-offset);

        // 填充 42 个格子（6行 × 7列）
        for (int i = 0; i < 42; i++)
        {
            DateTime date = gridStartDate.AddDays(i);
            bool isCurrentMonth = date.Month == Month && date.Year == Year;
            bool isToday = date.Date == DateTime.Today;

            // 查找当天的事件
            List<CalendarEvent> dayEvents = events.Where(e =>
                e.IsAllDay
                    ? date.Date >= e.StartTime.Date &&
                      date.Date < (e.EndTime.Date > e.StartTime.Date ? e.EndTime.Date : e.StartTime.Date.AddDays(1))
                    : date.Date == e.StartTime.Date
            ).ToList();

            _chinaCalendarInfo.TryGetValue(date.Date, out ChinaCalendarDayInfo? chinaInfo);
            (string calculatedSuitable, string calculatedAvoid) = HuangLiHelper.GetDayYiJi(date);

            CalendarDays.Add(new CalendarDay(
                Date: date,
                IsCurrentMonth: isCurrentMonth,
                IsToday: isToday,
                HasEvents: dayEvents.Count > 0,
                LunarDate: chinaInfo?.LunarDate ?? string.Empty,
                LunarFestival: chinaInfo?.LunarFestival ?? string.Empty,
                SolarTerm: chinaInfo?.SolarTerm ?? string.Empty,
                LegalHoliday: chinaInfo?.LegalHoliday ?? string.Empty,
                IsWorkday: chinaInfo?.IsWorkday ?? false,
                HolidayDayIndex: chinaInfo?.HolidayDayIndex ?? 0,
                Events: dayEvents,
                SuitableActivities: string.IsNullOrWhiteSpace(chinaInfo?.SuitableActivities)
                    ? calculatedSuitable
                    : chinaInfo.SuitableActivities,
                AvoidActivities: string.IsNullOrWhiteSpace(chinaInfo?.AvoidActivities)
                    ? calculatedAvoid
                    : chinaInfo.AvoidActivities
            ));
        }

        // 网格生成后重新计算选中索引
        UpdateSelectedDayIndex();
    }

    /// <summary>
    /// 根据 SelectedDate 在当前日历网格中的位置更新 SelectedDayIndex。
    /// 如果 SelectedDate 不在当前显示月份中，则不高亮任何日期（-1）。
    /// </summary>
    private void UpdateSelectedDayIndex()
    {
        SelectedDayIndex = -1;
        for (int i = 0; i < CalendarDays.Count; i++)
        {
            if (CalendarDays[i].Date.Date == SelectedDate.Date)
            {
                SelectedDayIndex = i;
                break;
            }
        }
    }

    private void UpdateMonthDisplay()
    {
        MonthDisplay = $"{Year}年{Month}月";
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    /// <summary>
    /// 释放持有的日历服务（如 ICS 服务的 HttpClient / 信号量）
    /// </summary>
    public void Dispose()
    {
        (_calendarService as IDisposable)?.Dispose();
    }
}