using CornerCalendar.Core.Models;

namespace CornerCalendar.Core.Services;

/// <summary>
/// 空日历服务（ICS URL 未配置时的占位服务）
/// </summary>
public class EmptyCalendarService : ICalendarService
{
    public Task<List<CalendarEvent>> GetEventsAsync(DateTime start, DateTime end)
        => Task.FromResult(new List<CalendarEvent>());

    public Task ForceRefreshAsync()
        => Task.CompletedTask;
}