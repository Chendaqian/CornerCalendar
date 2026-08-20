using CornerCalendar.Core.Models;

namespace CornerCalendar.ViewModels;

/// <summary>
/// 事件列表文本格式化帮助类。
/// </summary>
public static class EventListViewModel
{
    /// <summary>
    /// 格式化事件时间。
    /// </summary>
    public static string FormatEventTime(CalendarEvent evt)
    {
        if (evt.IsAllDay)
            return "全天";

        DateTime start = evt.StartTime;
        DateTime end = evt.EndTime;

        if (start.Date == end.Date)
            return $"{start:HH:mm} - {end:HH:mm}";

        return $"{start:M月d日 HH:mm} - {end:M月d日 HH:mm}";
    }

    /// <summary>
    /// 格式化事件在近期列表中的时间摘要。
    /// </summary>
    public static string FormatEventSummary(CalendarEvent evt)
    {
        return evt.IsAllDay ? "全天" : evt.StartTime.ToString("HH:mm");
    }
}