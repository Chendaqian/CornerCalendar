using CornerCalendar.Core.Models;

namespace CornerCalendar.Core.Services;

/// <summary>
/// 日历服务接口
/// </summary>
public interface ICalendarService
{
    /// <summary>
    /// 获取指定时间范围内的事件列表
    /// </summary>
    Task<List<CalendarEvent>> GetEventsAsync(DateTime start, DateTime end);

    /// <summary>
    /// 强制刷新缓存（忽略缓存，重新从系统拉取）
    /// </summary>
    Task ForceRefreshAsync();
}