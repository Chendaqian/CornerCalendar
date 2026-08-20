using System.Diagnostics;
using Windows.ApplicationModel.Appointments;
using CornerCalendar.Core.Models;

namespace CornerCalendar.Core.Services;

/// <summary>
/// Windows 系统日历服务实现（带 10 分钟缓存）
/// </summary>
public class WindowsCalendarService : ICalendarService
{
    private List<CalendarEvent>? _cachedEvents;
    private DateTime _cacheTimestamp;
    private readonly TimeSpan _cacheDuration = TimeSpan.FromMinutes(10);
    private (DateTime start, DateTime end) _cacheRange;

    public async Task<List<CalendarEvent>> GetEventsAsync(DateTime start, DateTime end)
    {
        // 检查缓存是否有效（相同范围 + 未过期）
        if (_cachedEvents != null &&
            _cacheRange.start == start && _cacheRange.end == end &&
            DateTime.Now - _cacheTimestamp < _cacheDuration)
        {
            return _cachedEvents;
        }

        try
        {
            var store = await AppointmentManager.RequestStoreAsync(
                AppointmentStoreAccessType.AllCalendarsReadOnly);

            if (store == null)
            {
                Debug.WriteLine("CornerCalendar: Calendar store is null (permission denied or unsupported).");
                return new List<CalendarEvent>();
            }

            // 先获取所有日历，建立 ID→颜色映射
            var calendarColors = new Dictionary<string, string>();
            try
            {
                var calendars = await store.FindAppointmentCalendarsAsync(
                    FindAppointmentCalendarsOptions.IncludeHidden);
                foreach (var cal in calendars)
                {
                    var color = cal.DisplayName.Contains("Outlook", StringComparison.OrdinalIgnoreCase)
                        ? "#0078D4"
                        : cal.DisplayName.Contains("Google", StringComparison.OrdinalIgnoreCase)
                            ? "#4285F4"
                            : cal.DisplayName.Contains("iCloud", StringComparison.OrdinalIgnoreCase)
                                ? "#FC3D39"
                                : "#0078D4";
                    calendarColors[cal.Id] = color;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"CornerCalendar: Failed to get calendar colors: {ex.Message}");
            }

            var options = new FindAppointmentsOptions
            {
                MaxCount = 200
            };

            var appointments = await store.FindAppointmentsAsync(start, end - start, options);

            var events = appointments.Select(a =>
            {
                // 尝试匹配日历颜色
                var color = calendarColors.TryGetValue(a.CalendarId ?? "", out var c) ? c : "#0078D4";
                return new CalendarEvent(
                    Title: a.Subject,
                    StartTime: a.StartTime.LocalDateTime,
                    EndTime: a.StartTime.LocalDateTime + a.Duration,
                    IsAllDay: a.AllDay,
                    CalendarName: a.CalendarId ?? string.Empty,
                    Color: color,
                    Location: a.Location ?? string.Empty,
                    Description: a.Details ?? string.Empty
                );
            }).ToList();

            // 更新缓存
            _cachedEvents = events;
            _cacheRange = (start, end);
            _cacheTimestamp = DateTime.Now;

            return events;
        }
        catch (UnauthorizedAccessException)
        {
            Debug.WriteLine("CornerCalendar: Calendar permission denied.");
            return new List<CalendarEvent>();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"CornerCalendar: Calendar API error: {ex.Message}");
            return new List<CalendarEvent>();
        }
    }

    public Task ForceRefreshAsync()
    {
        _cachedEvents = null;
        _cacheTimestamp = DateTime.MinValue;
        return Task.CompletedTask;
    }

}
