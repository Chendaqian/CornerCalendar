namespace CornerCalendar.Core.Models;

/// <summary>
/// 单天数据模型（日期 + 事件 + 中国日历信息）
/// </summary>
public record CalendarDay(
    DateTime Date,
    bool IsCurrentMonth,
    bool IsToday,
    bool HasEvents,
    string LunarDate,
    string LunarFestival,
    string SolarTerm,
    string LegalHoliday,
    bool IsWorkday,
    int HolidayDayIndex,
    List<CalendarEvent> Events,
    string SuitableActivities = "",
    string AvoidActivities = "",
    IReadOnlyList<SenScheduleOccurrence>? SenEvents = null
);