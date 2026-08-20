namespace CornerCalendar.Core.Models;

/// <summary>
/// ChinaCalendar 远程 ICS 为单个日期提供的附加信息。
/// </summary>
public record ChinaCalendarDayInfo(
    string LunarDate = "",
    string LunarFestival = "",
    string SolarTerm = "",
    string LegalHoliday = "",
    bool IsWorkday = false,
    int HolidayDayIndex = 0,
    string SuitableActivities = "",
    string AvoidActivities = ""
);