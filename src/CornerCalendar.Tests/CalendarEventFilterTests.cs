using CornerCalendar.Core.Models;
using CornerCalendar.Core.Services;
using Xunit;

namespace CornerCalendar.Tests;

public class CalendarEventFilterTests
{
    [Fact]
    public void 只过滤内置中国日历事件()
    {
        DateTime date = new(2026, 2, 17);
        List<CalendarEvent> events =
        [
            new("春节", date, date.AddDays(1), true, "中国日历-节日纪念日", "#E74856"),
            new("春节", date, date.AddHours(1), false, "个人日历", "#0078D4"),
            new("会议", date, date.AddHours(1), false, "中国日历-节日纪念日", "#E74856")
        ];

        List<CalendarEvent> filtered =
            CalendarEventFilter.FilterBuiltInEvents(events, ["春节"]);

        Assert.Equal(2, filtered.Count);
        Assert.Contains(filtered, calendarEvent => calendarEvent.CalendarName == "个人日历");
        Assert.Contains(filtered, calendarEvent => calendarEvent.Title == "会议");
    }

    [Fact]
    public void 过滤节日详情和补班标记()
    {
        DateTime date = new(2026, 2, 17);
        Dictionary<DateTime, ChinaCalendarDayInfo> dayInfo = new()
        {
            [date] = new ChinaCalendarDayInfo(
                LunarFestival: "春节",
                SolarTerm: "立春",
                LegalHoliday: "春节补班",
                IsWorkday: true,
                HolidayDayIndex: 1)
        };

        IReadOnlyDictionary<DateTime, ChinaCalendarDayInfo> filtered =
            CalendarEventFilter.FilterDayInfo(dayInfo, ["春节", "立春"]);

        ChinaCalendarDayInfo result = filtered[date];
        Assert.Equal(string.Empty, result.LunarFestival);
        Assert.Equal(string.Empty, result.SolarTerm);
        Assert.Equal(string.Empty, result.LegalHoliday);
        Assert.False(result.IsWorkday);
        Assert.Equal(0, result.HolidayDayIndex);
    }

    [Fact]
    public void 节日名称会去掉假期和补班后缀()
    {
        Assert.Equal("春节", CalendarEventFilter.NormalizeName("「春节 假期」 第1天/共7天"));
        Assert.Equal("春节", CalendarEventFilter.NormalizeName("春节补班"));
    }
}