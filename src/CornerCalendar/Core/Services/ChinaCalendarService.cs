using CornerCalendar.Core.Models;

namespace CornerCalendar.Core.Services;

/// <summary>
/// YangH9/ChinaCalendar 远程 ICS 数据源。
/// 数据地址和类型以其 README 中公开的订阅文件为准，不在程序中内置年度节假日数据。
/// </summary>
public sealed class ChinaCalendarService : ICalendarService, IChinaCalendarDataProvider, IDisposable
{
    public const string HolidayUrl = "https://yangh9.github.io/ChinaCalendar/cal_holiday.ics";
    public const string FestivalUrl = "https://yangh9.github.io/ChinaCalendar/cal_festival.ics";
    public const string SolarTermUrl = "https://yangh9.github.io/ChinaCalendar/cal_solarTerm.ics";
    public const string LunarUrl = "https://yangh9.github.io/ChinaCalendar/cal_lunar.ics";

    private const string HolidayCalendarName = "中国日历-法定节假日";
    private const string FestivalCalendarName = "中国日历-节日纪念日";
    private const string SolarTermCalendarName = "中国日历-二十四节气";
    private const string LunarCalendarName = "中国日历-农历";

    private readonly IcsCalendarService _holiday;
    private readonly IcsCalendarService _festival;
    private readonly IcsCalendarService _solarTerm;
    private readonly IcsCalendarService _lunar;

    public ChinaCalendarService(int refreshMinutes = 30)
    {
        _holiday = new IcsCalendarService(HolidayUrl, refreshMinutes, HolidayCalendarName, "#D13438");
        _festival = new IcsCalendarService(FestivalUrl, refreshMinutes, FestivalCalendarName, "#E74856");
        _solarTerm = new IcsCalendarService(SolarTermUrl, refreshMinutes, SolarTermCalendarName, "#107C10");
        _lunar = new IcsCalendarService(LunarUrl, refreshMinutes, LunarCalendarName, "#8764B8");
    }

    public async Task<List<CalendarEvent>> GetEventsAsync(DateTime start, DateTime end)
    {
        Task<List<CalendarEvent>> holidayTask = GetEventsSafelyAsync(_holiday, start, end);
        Task<List<CalendarEvent>> festivalTask = GetEventsSafelyAsync(_festival, start, end);
        Task<List<CalendarEvent>> solarTermTask = GetEventsSafelyAsync(_solarTerm, start, end);

        await Task.WhenAll(holidayTask, festivalTask, solarTermTask);

        return holidayTask.Result
            .Concat(festivalTask.Result)
            .Concat(solarTermTask.Result)
            .Select(CleanEventTitle)
            .OrderBy(e => e.StartTime)
            .ToList();
    }

    public async Task<IReadOnlyDictionary<DateTime, ChinaCalendarDayInfo>> GetDayInfoAsync(
        DateTime start,
        DateTime end)
    {
        Task<List<CalendarEvent>> holidayTask = GetEventsSafelyAsync(_holiday, start, end);
        Task<List<CalendarEvent>> festivalTask = GetEventsSafelyAsync(_festival, start, end);
        Task<List<CalendarEvent>> solarTermTask = GetEventsSafelyAsync(_solarTerm, start, end);
        Task<List<CalendarEvent>> lunarTask = GetEventsSafelyAsync(_lunar, start, end);

        await Task.WhenAll(holidayTask, festivalTask, solarTermTask, lunarTask);

        Dictionary<DateTime, ChinaCalendarDayInfoBuilder> builders = new();
        AddHolidayInfo(builders, holidayTask.Result);
        AddFestivalInfo(builders, festivalTask.Result);
        AddSolarTermInfo(builders, solarTermTask.Result);
        AddLunarInfo(builders, lunarTask.Result);

        return builders.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.Build());
    }

    public async Task ForceRefreshAsync()
    {
        Task holidayTask = _holiday.ForceRefreshAsync();
        Task festivalTask = _festival.ForceRefreshAsync();
        Task solarTermTask = _solarTerm.ForceRefreshAsync();
        Task lunarTask = _lunar.ForceRefreshAsync();
        await Task.WhenAll(holidayTask, festivalTask, solarTermTask, lunarTask);
    }

    public void Dispose()
    {
        _holiday.Dispose();
        _festival.Dispose();
        _solarTerm.Dispose();
        _lunar.Dispose();
    }

    internal static void AddHolidayInfo(
        Dictionary<DateTime, ChinaCalendarDayInfoBuilder> builders,
        IEnumerable<CalendarEvent> events)
    {
        foreach (CalendarEvent calendarEvent in events)
        {
            int holidayDayIndex = ParseHolidayDayIndex(calendarEvent.Title);
            string title = RemoveBrackets(calendarEvent.Title);
            int detailIndex = title.IndexOf(" 第", StringComparison.Ordinal);
            if (detailIndex >= 0)
                title = title[..detailIndex];

            int suffixIndex = title.IndexOf(" 补班", StringComparison.Ordinal);
            bool isWorkday = suffixIndex >= 0;
            string name = isWorkday
                ? title[..suffixIndex]
                : RemoveSuffix(title, " 假期");
            name = CleanHolidayName(name);

            ChinaCalendarDayInfoBuilder builder = GetBuilder(builders, calendarEvent.StartTime.Date);
            if (isWorkday)
            {
                builder.IsWorkday = true;
                builder.LegalHoliday = string.IsNullOrWhiteSpace(name) ? "补班" : name + "补班";
            }
            else if (!string.IsNullOrWhiteSpace(name))
            {
                builder.LegalHoliday = name;
                if (holidayDayIndex > 0
                    && (builder.HolidayDayIndex == 0 || holidayDayIndex < builder.HolidayDayIndex))
                {
                    builder.HolidayDayIndex = holidayDayIndex;
                }
            }
        }
    }

    internal static void AddFestivalInfo(
        Dictionary<DateTime, ChinaCalendarDayInfoBuilder> builders,
        IEnumerable<CalendarEvent> events)
    {
        foreach (CalendarEvent calendarEvent in events)
        {
            string title = CleanHolidayName(RemoveBrackets(calendarEvent.Title));
            if (!string.IsNullOrWhiteSpace(title))
                GetBuilder(builders, calendarEvent.StartTime.Date).LunarFestival = title;
        }
    }

    internal static void AddSolarTermInfo(
        Dictionary<DateTime, ChinaCalendarDayInfoBuilder> builders,
        IEnumerable<CalendarEvent> events)
    {
        foreach (CalendarEvent calendarEvent in events)
        {
            string title = RemoveBrackets(calendarEvent.Title);
            if (!string.IsNullOrWhiteSpace(title))
                GetBuilder(builders, calendarEvent.StartTime.Date).SolarTerm = title;
        }
    }

    internal static void AddLunarInfo(
        Dictionary<DateTime, ChinaCalendarDayInfoBuilder> builders,
        IEnumerable<CalendarEvent> events)
    {
        foreach (CalendarEvent calendarEvent in events)
        {
            string title = RemoveBrackets(calendarEvent.Title);
            string[] parts = title.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
            {
                string lunarDate = string.Join(' ', parts.Take(2));
                ChinaCalendarDayInfoBuilder builder = GetBuilder(builders, calendarEvent.StartTime.Date);
                builder.LunarDate = lunarDate;

                (string suitableActivities, string avoidActivities) =
                    ParseSuitableAvoid(calendarEvent.Description);
                if (!string.IsNullOrWhiteSpace(suitableActivities))
                    builder.SuitableActivities = suitableActivities;
                if (!string.IsNullOrWhiteSpace(avoidActivities))
                    builder.AvoidActivities = avoidActivities;
            }
        }
    }

    /// <summary>
    /// 解析订阅源 DESCRIPTION 中的宜忌信息；没有宜忌字段时由 ViewModel 使用公开规则库补齐。
    /// </summary>
    internal static (string SuitableActivities, string AvoidActivities) ParseSuitableAvoid(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
            return (string.Empty, string.Empty);

        string text = description
            .Replace("\\r", " ", StringComparison.OrdinalIgnoreCase)
            .Replace("\\n", " ", StringComparison.OrdinalIgnoreCase)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal);
        return (
            ExtractAlmanacPart(text, "宜", "忌"),
            ExtractAlmanacPart(text, "忌", "宜"));
    }

    private static string ExtractAlmanacPart(string text, string label, string otherLabel)
    {
        int labelIndex = text.IndexOf(label, StringComparison.Ordinal);
        if (labelIndex < 0)
            return string.Empty;

        int valueStart = labelIndex + label.Length;
        while (valueStart < text.Length &&
               (char.IsWhiteSpace(text[valueStart]) || text[valueStart] is ':' or '：'))
        {
            valueStart++;
        }

        int valueEnd = text.IndexOf(otherLabel, valueStart, StringComparison.Ordinal);
        if (valueEnd < 0)
            valueEnd = text.Length;

        return NormalizeActivityText(text[valueStart..valueEnd]);
    }

    private static string NormalizeActivityText(string value)
    {
        string[] activities = value
            .Replace("馀", "余", StringComparison.Ordinal)
            .Split(new[] { ' ', '\t', ',', '，', '、', ';', '；' },
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(activity => !string.Equals(activity, "无", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return string.Join("、", activities);
    }

    private static ChinaCalendarDayInfoBuilder GetBuilder(
        Dictionary<DateTime, ChinaCalendarDayInfoBuilder> builders,
        DateTime date)
    {
        if (!builders.TryGetValue(date, out ChinaCalendarDayInfoBuilder? builder))
        {
            builder = new ChinaCalendarDayInfoBuilder();
            builders[date] = builder;
        }

        return builder;
    }

    private static string RemoveBrackets(string value)
    {
        string trimmed = value.Trim();
        if ((trimmed.StartsWith('「') && trimmed.Contains('」'))
            || (trimmed.StartsWith('『') && trimmed.Contains('』')))
        {
            int closingIndex = trimmed.IndexOfAny(new[] { '」', '』' });
            return trimmed[1..closingIndex];
        }

        return trimmed.Trim('「', '」', '『', '』');
    }

    private static string RemoveSuffix(string value, string suffix)
    {
        return value.EndsWith(suffix, StringComparison.Ordinal)
            ? value[..^suffix.Length]
            : value;
    }

    private static string CleanHolidayName(string value)
    {
        string cleaned = value;
        string[] redundantWords =
        {
            "中华人民共和国",
            "法定节假日",
            "节假日",
            "中华",
            "全国",
            "中国",
            "国际",
            "世界"
        };

        foreach (string word in redundantWords)
            cleaned = cleaned.Replace(word, string.Empty, StringComparison.Ordinal);

        return cleaned.Trim(' ', '\t', '、', '，', ',', ':', '：', '-', '_');
    }

    internal static CalendarEvent CleanEventTitle(CalendarEvent calendarEvent)
    {
        string title = CleanHolidayName(calendarEvent.Title);
        return string.IsNullOrWhiteSpace(title)
            ? calendarEvent
            : calendarEvent with { Title = title };
    }

    private static int ParseHolidayDayIndex(string value)
    {
        int start = value.IndexOf('第');
        if (start < 0)
            return 0;

        int end = value.IndexOf('天', start + 1);
        if (end <= start + 1)
            return 0;

        return int.TryParse(value[(start + 1)..end], out int index) ? index : 0;
    }

    private static async Task<List<CalendarEvent>> GetEventsSafelyAsync(
        ICalendarService service,
        DateTime start,
        DateTime end)
    {
        try
        {
            return await service.GetEventsAsync(start, end);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"CornerCalendar: ChinaCalendar feed failed ({service.GetType().Name}): {ex.Message}");
            return new List<CalendarEvent>();
        }
    }

    internal sealed class ChinaCalendarDayInfoBuilder
    {
        public string LunarDate { get; set; } = "";
        public string LunarFestival { get; set; } = "";
        public string SolarTerm { get; set; } = "";
        public string LegalHoliday { get; set; } = "";
        public bool IsWorkday { get; set; }
        public int HolidayDayIndex { get; set; }
        public string SuitableActivities { get; set; } = "";
        public string AvoidActivities { get; set; } = "";

        public ChinaCalendarDayInfo Build() => new(
            LunarDate,
            LunarFestival,
            SolarTerm,
            LegalHoliday,
            IsWorkday,
            HolidayDayIndex,
            SuitableActivities,
            AvoidActivities);
    }
}