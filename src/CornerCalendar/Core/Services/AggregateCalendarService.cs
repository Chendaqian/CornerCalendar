using CornerCalendar.Core.Models;
using System.Diagnostics;

namespace CornerCalendar.Core.Services;

/// <summary>
/// 聚合日历服务 —— 合并多个数据源的事件
/// </summary>
public class AggregateCalendarService : ICalendarService, IChinaCalendarDataProvider, IDisposable
{
    private readonly List<ICalendarService> _services;

    public AggregateCalendarService(params ICalendarService[] services)
    {
        _services = services.ToList();
    }

    public async Task<List<CalendarEvent>> GetEventsAsync(DateTime start, DateTime end)
    {
        List<CalendarEvent> allEvents = new List<CalendarEvent>();
        int failedCount = 0;

        foreach (ICalendarService service in _services)
        {
            try
            {
                List<CalendarEvent> events = await service.GetEventsAsync(start, end);
                allEvents.AddRange(events);
            }
            catch (Exception ex)
            {
                failedCount++;
                Debug.WriteLine($"CornerCalendar: Service {service.GetType().Name} failed: {ex.Message}");
            }
        }

        // 全部数据源都失败时向上抛错，让 UI 能显示错误状态（部分失败仍返回可用数据）
        if (_services.Count > 0 && failedCount == _services.Count)
            throw new InvalidOperationException("所有日历数据源均加载失败");

        return allEvents.OrderBy(e => e.StartTime).ToList();
    }

    public async Task<IReadOnlyDictionary<DateTime, ChinaCalendarDayInfo>> GetDayInfoAsync(
        DateTime start,
        DateTime end)
    {
        IChinaCalendarDataProvider[] providers = _services
            .OfType<IChinaCalendarDataProvider>()
            .ToArray();

        if (providers.Length == 0)
            return new Dictionary<DateTime, ChinaCalendarDayInfo>();

        Task<IReadOnlyDictionary<DateTime, ChinaCalendarDayInfo>>[] tasks = providers
            .Select(provider => provider.GetDayInfoAsync(start, end))
            .ToArray();
        IReadOnlyDictionary<DateTime, ChinaCalendarDayInfo>[] results = await Task.WhenAll(tasks);

        Dictionary<DateTime, ChinaCalendarDayInfo> merged = new();
        foreach (IReadOnlyDictionary<DateTime, ChinaCalendarDayInfo> result in results)
        {
            foreach ((DateTime date, ChinaCalendarDayInfo info) in result)
            {
                if (!merged.TryGetValue(date, out ChinaCalendarDayInfo? existing))
                {
                    merged[date] = info;
                    continue;
                }

                merged[date] = new ChinaCalendarDayInfo(
                    string.IsNullOrWhiteSpace(existing.LunarDate) ? info.LunarDate : existing.LunarDate,
                    string.IsNullOrWhiteSpace(existing.LunarFestival) ? info.LunarFestival : existing.LunarFestival,
                    string.IsNullOrWhiteSpace(existing.SolarTerm) ? info.SolarTerm : existing.SolarTerm,
                    string.IsNullOrWhiteSpace(existing.LegalHoliday) ? info.LegalHoliday : existing.LegalHoliday,
                    existing.IsWorkday || info.IsWorkday,
                    existing.HolidayDayIndex == 0 ? info.HolidayDayIndex : existing.HolidayDayIndex,
                    string.IsNullOrWhiteSpace(existing.SuitableActivities)
                        ? info.SuitableActivities
                        : existing.SuitableActivities,
                    string.IsNullOrWhiteSpace(existing.AvoidActivities)
                        ? info.AvoidActivities
                        : existing.AvoidActivities);
            }
        }

        return merged;
    }

    public async Task ForceRefreshAsync()
    {
        foreach (ICalendarService service in _services)
        {
            try
            {
                await service.ForceRefreshAsync();
            }
            catch { }
        }
    }

    public void Dispose()
    {
        // 释放各数据源持有的资源（如 ICS 服务的 HttpClient）
        foreach (ICalendarService service in _services)
        {
            (service as IDisposable)?.Dispose();
        }
    }
}