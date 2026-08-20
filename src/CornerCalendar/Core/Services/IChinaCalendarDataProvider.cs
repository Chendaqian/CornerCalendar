using CornerCalendar.Core.Models;

namespace CornerCalendar.Core.Services;

/// <summary>
/// 提供远程中国日历数据的服务扩展接口。
/// </summary>
public interface IChinaCalendarDataProvider
{
    Task<IReadOnlyDictionary<DateTime, ChinaCalendarDayInfo>> GetDayInfoAsync(
        DateTime start,
        DateTime end);
}