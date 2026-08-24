using CornerCalendar.Core.Models;

namespace CornerCalendar.Core.Services;

/// <summary>
/// 应用内置中国日历的节日过滤设置。
/// 自定义 ICS 事件不会被此过滤器处理。
/// </summary>
public static class CalendarEventFilter
{
    public static List<CalendarEvent> FilterBuiltInEvents(
        IEnumerable<CalendarEvent> events,
        IEnumerable<string>? hiddenNames)
    {
        HashSet<string> hidden = CreateHiddenNameSet(hiddenNames);
        if (hidden.Count == 0)
            return events.ToList();

        return events
            .Where(calendarEvent =>
                !IsBuiltInCalendar(calendarEvent)
                || !hidden.Contains(NormalizeName(calendarEvent.Title)))
            .ToList();
    }

    public static IReadOnlyDictionary<DateTime, ChinaCalendarDayInfo> FilterDayInfo(
        IReadOnlyDictionary<DateTime, ChinaCalendarDayInfo> dayInfo,
        IEnumerable<string>? hiddenNames)
    {
        HashSet<string> hidden = CreateHiddenNameSet(hiddenNames);
        if (hidden.Count == 0)
            return dayInfo;

        Dictionary<DateTime, ChinaCalendarDayInfo> filtered = new();
        foreach ((DateTime date, ChinaCalendarDayInfo info) in dayInfo)
        {
            bool hideFestival = IsHidden(info.LunarFestival, hidden);
            bool hideSolarTerm = IsHidden(info.SolarTerm, hidden);
            bool hideHoliday = IsHidden(info.LegalHoliday, hidden);

            filtered[date] = new ChinaCalendarDayInfo(
                LunarDate: info.LunarDate,
                LunarFestival: hideFestival ? string.Empty : info.LunarFestival,
                SolarTerm: hideSolarTerm ? string.Empty : info.SolarTerm,
                LegalHoliday: hideHoliday ? string.Empty : info.LegalHoliday,
                IsWorkday: hideHoliday ? false : info.IsWorkday,
                HolidayDayIndex: hideHoliday ? 0 : info.HolidayDayIndex,
                SuitableActivities: info.SuitableActivities,
                AvoidActivities: info.AvoidActivities);
        }

        return filtered;
    }

    public static string NormalizeName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        string normalized = value
            .Replace("「", string.Empty, StringComparison.Ordinal)
            .Replace("」", string.Empty, StringComparison.Ordinal)
            .Replace("『", string.Empty, StringComparison.Ordinal)
            .Replace("』", string.Empty, StringComparison.Ordinal)
            .Replace("【", string.Empty, StringComparison.Ordinal)
            .Replace("】", string.Empty, StringComparison.Ordinal)
            .Replace("[", string.Empty, StringComparison.Ordinal)
            .Replace("]", string.Empty, StringComparison.Ordinal)
            .Trim();
        int detailIndex = normalized.IndexOf(" 第", StringComparison.Ordinal);
        if (detailIndex >= 0)
            normalized = normalized[..detailIndex];

        foreach (string suffix in new[] { " 假期", "假期", " 补班", "补班", "（休）", "(休)", " 休" })
        {
            if (normalized.EndsWith(suffix, StringComparison.Ordinal))
            {
                normalized = normalized[..^suffix.Length];
                break;
            }
        }

        return normalized
            .Trim(' ', '\t', '、', '，', ',', ':', '：', '-', '_', '「', '」', '(', ')', '（', '）');
    }

    private static HashSet<string> CreateHiddenNameSet(IEnumerable<string>? hiddenNames)
        => (hiddenNames ?? Enumerable.Empty<string>())
            .Select(NormalizeName)
            .Where(name => name.Length > 0)
            .ToHashSet(StringComparer.Ordinal);

    private static bool IsHidden(string? value, HashSet<string> hidden)
        => value != null && hidden.Contains(NormalizeName(value));

    private static bool IsBuiltInCalendar(CalendarEvent calendarEvent)
        => calendarEvent.CalendarName.StartsWith("中国日历-", StringComparison.Ordinal);
}