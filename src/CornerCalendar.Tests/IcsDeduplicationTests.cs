using CornerCalendar.Core.Models;
using CornerCalendar.Core.Services;
using Xunit;

namespace CornerCalendar.Tests;

/// <summary>
/// ISSUES #4 回归测试：去重只允许「全天 + 同日 + 标题包含关系」的合并，
/// 不能再误删"评审会（设计）"/"评审会（开发）"这类正常事件。
/// </summary>
public class IcsDeduplicationTests
{
    private static CalendarEvent AllDay(string title, DateTime day, int spanDays = 1) => new(
        Title: title,
        StartTime: day,
        EndTime: day.AddDays(spanDays),
        IsAllDay: true,
        CalendarName: "test",
        Color: "#000000");

    private static CalendarEvent Timed(string title, DateTime start, DateTime end) => new(
        Title: title,
        StartTime: start,
        EndTime: end,
        IsAllDay: false,
        CalendarName: "test",
        Color: "#000000");

    [Fact]
    public void 全天节假日变体合并_保留跨度更长的一条()
    {
        DateTime day = new DateTime(2026, 6, 19);
        List<CalendarEvent> events = new()
        {
            AllDay("端午节", day),
            AllDay("端午节（休）", day, 3),
        };

        List<CalendarEvent> result = IcsCalendarService.DeduplicateAllDayEvents(events);

        Assert.Single(result);
        Assert.Equal("端午节（休）", result[0].Title);
        Assert.Equal(day.AddDays(3), result[0].EndTime);
    }

    [Fact]
    public void 跨度相同的包含标题_保留更短的标题()
    {
        DateTime day = new DateTime(2026, 6, 19);
        List<CalendarEvent> events = new()
        {
            AllDay("端午节（休）", day),
            AllDay("端午节", day),
        };

        List<CalendarEvent> result = IcsCalendarService.DeduplicateAllDayEvents(events);

        Assert.Single(result);
        Assert.Equal("端午节", result[0].Title);
    }

    [Fact]
    public void 定时的同前缀事件不会被误删()
    {
        // 旧实现的误删场景：同日前缀相同的两条定时事件
        DateTime day = new DateTime(2026, 8, 20);
        List<CalendarEvent> events = new()
        {
            Timed("评审会（设计）", day.AddHours(10), day.AddHours(11)),
            Timed("评审会（开发）", day.AddHours(14), day.AddHours(15)),
        };

        List<CalendarEvent> result = IcsCalendarService.DeduplicateAllDayEvents(events);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void 全天但标题无包含关系的事件全部保留()
    {
        DateTime day = new DateTime(2026, 10, 1);
        List<CalendarEvent> events = new()
        {
            AllDay("发布会（上午场）", day),
            AllDay("发布会（下午场）", day),
        };

        List<CalendarEvent> result = IcsCalendarService.DeduplicateAllDayEvents(events);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void 不同日期的同名事件不会合并()
    {
        List<CalendarEvent> events = new()
        {
            AllDay("例会", new DateTime(2026, 8, 17)),
            AllDay("例会", new DateTime(2026, 8, 18)),
        };

        List<CalendarEvent> result = IcsCalendarService.DeduplicateAllDayEvents(events);

        Assert.Equal(2, result.Count);
    }
}