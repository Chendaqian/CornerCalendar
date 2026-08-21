using Ical.Net;
using Ical.Net.DataTypes;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using CornerCalendarEvent = CornerCalendar.Core.Models.CalendarEvent;
using IcalEvent = Ical.Net.CalendarComponents.CalendarEvent;

namespace CornerCalendar.Core.Services;

/// <summary>
/// 远程 ICS 日历订阅服务 —— 从指定 URL 下载 .ics 文件并解析事件
/// </summary>
public class IcsCalendarService : ICalendarService, IDisposable
{
    private readonly string _icsUrl;
    private readonly int _refreshMinutes;
    private readonly string _calendarName;
    private readonly string _color;

    // 缓存原始 Calendar 对象（包含 RRULE 定义），不缓存展开后的事件
    private Calendar? _cachedCalendar;

    private DateTime _lastRefreshTime = DateTime.MinValue;
    private readonly HttpClient _httpClient;
    private readonly string _diskCachePath; // 磁盘缓存文件路径
    private readonly SemaphoreSlim _refreshLock = new(1, 1); // 保证同一时刻只有一个网络刷新在途

    public IcsCalendarService(string icsUrl, int refreshMinutes = 30, string calendarName = "ICS 订阅", string color = "#0078D4")
    {
        _icsUrl = icsUrl ?? throw new ArgumentNullException(nameof(icsUrl));
        _refreshMinutes = refreshMinutes;
        _calendarName = calendarName;
        _color = color;

        HttpClientHandler handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        };
        _httpClient = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(15)
        };
        // 模拟浏览器 User-Agent，避免某些服务器拒绝请求
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "CornerCalendar/1.0 (Calendar Client)");

        // 磁盘缓存路径：%LOCALAPPDATA%/CornerCalendar/cache/{url_hash}.ics
        string cacheDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CornerCalendar", "cache");
        Directory.CreateDirectory(cacheDir);
        string urlHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(_icsUrl)))[..16];
        _diskCachePath = Path.Combine(cacheDir, $"{urlHash}.ics");
    }

    public async Task<List<CornerCalendarEvent>> GetEventsAsync(DateTime start, DateTime end)
    {
        await EnsureCacheAsync();

        if (_cachedCalendar == null)
            return new List<CornerCalendarEvent>();

        // 使用 GetOccurrences 展开重复事件（RRULE）到指定日期范围
        HashSet<Occurrence> occurrences = _cachedCalendar.GetOccurrences(start, end);

        List<CornerCalendarEvent> events = new List<CornerCalendarEvent>();
        foreach (Occurrence? occurrence in occurrences)
        {
            try
            {
                CornerCalendarEvent? calEvent = ConvertOccurrence(occurrence);
                if (calEvent != null)
                    events.Add(calEvent);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"CornerCalendar: Failed to convert ICS occurrence: {ex.Message}");
            }
        }

        // 保守去重：仅合并「全天 + 同日 + 标题包含关系」的事件，详见 DeduplicateAllDayEvents 注释
        return DeduplicateAllDayEvents(events)
            .OrderBy(e => e.StartTime)
            .ToList();
    }

    public async Task ForceRefreshAsync()
    {
        // 强制刷新：跳过所有缓存，直接从网络下载
        // （信号量保证与在途后台刷新串行执行，避免并发写同一缓存文件）
        await RefreshFromNetworkAsync(force: true);
    }

    /// <summary>
    /// 确保缓存有效。优先级：内存缓存 → 磁盘缓存 → 网络下载。
    /// 磁盘缓存命中后，如已过期则在后台异步刷新（不阻塞调用方）。
    /// </summary>
    private async Task EnsureCacheAsync()
    {
        // 1. 内存缓存仍然有效 → 直接返回（最快路径）
        if (_cachedCalendar != null && (DateTime.Now - _lastRefreshTime).TotalMinutes < _refreshMinutes)
            return;

        // 2. 内存缓存为空 → 尝试从磁盘加载（快速路径）
        if (_cachedCalendar == null)
        {
            await LoadFromDiskCacheAsync();
        }

        // 3. 已有数据（内存或磁盘），检查是否需要后台刷新
        // （_refreshLock 保证同一时刻只有一个刷新在途；并发调用进入锁后会命中新鲜度复查而立即跳过）
        if (_cachedCalendar != null)
        {
            if ((DateTime.Now - _lastRefreshTime).TotalMinutes >= _refreshMinutes)
            {
                _ = RefreshFromNetworkAsync(); // 后台刷新，不阻塞
            }
            return;
        }

        // 4. 无任何缓存 → 必须同步网络下载（仅首次启动会发生）
        await RefreshFromNetworkAsync();
    }

    /// <summary>
    /// 从本地磁盘缓存文件加载 ICS 数据（毫秒级）
    /// </summary>
    private async Task LoadFromDiskCacheAsync()
    {
        try
        {
            if (!File.Exists(_diskCachePath))
                return;

            string icsContent = await File.ReadAllTextAsync(_diskCachePath);
            if (string.IsNullOrWhiteSpace(icsContent))
                return;

            _cachedCalendar = ParseIcsContent(icsContent);
            if (_cachedCalendar != null)
            {
                // 使用文件最后写入时间作为缓存时间
                _lastRefreshTime = File.GetLastWriteTime(_diskCachePath);
                Debug.WriteLine($"CornerCalendar: Loaded ICS from disk cache ({_cachedCalendar.Events.Count} events, cached at {_lastRefreshTime:HH:mm:ss})");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"CornerCalendar: Failed to load disk cache: {ex.Message}");
        }
    }

    /// <summary>
    /// 从网络下载并更新缓存（内存 + 磁盘）。
    /// 信号量保证同一时刻只有一个刷新在途，避免并发下载写坏同一磁盘缓存文件。
    /// </summary>
    private async Task RefreshFromNetworkAsync(bool force = false)
    {
        await _refreshLock.WaitAsync();
        try
        {
            // 拿到锁后复查：若刚有别的刷新完成则直接跳过（强制刷新除外）
            if (!force && _cachedCalendar != null &&
                (DateTime.Now - _lastRefreshTime).TotalMinutes < _refreshMinutes)
                return;

            string icsContent = await DownloadIcsAsync();
            Calendar? calendar = ParseIcsContent(icsContent);

            if (calendar != null)
            {
                _cachedCalendar = calendar;
                _lastRefreshTime = DateTime.Now;

                // 保存到磁盘缓存
                try
                {
                    await File.WriteAllTextAsync(_diskCachePath, icsContent);
                    Debug.WriteLine($"CornerCalendar: ICS disk cache updated ({calendar.Events.Count} events)");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"CornerCalendar: Failed to write disk cache: {ex.Message}");
                }

                Debug.WriteLine($"CornerCalendar: ICS refreshed from network ({calendar.Events.Count} events)");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"CornerCalendar: Network refresh failed: {ex.Message}");
            // 保留旧缓存（如果有）
            if (_cachedCalendar == null)
                throw;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    /// <summary>
    /// 下载远程 .ics 文件内容
    /// </summary>
    private async Task<string> DownloadIcsAsync()
    {
        HttpResponseMessage response = await _httpClient.GetAsync(_icsUrl);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    /// <summary>
    /// 解析 ICS 文本为 Calendar 对象（保留 RRULE 等重复规则，不展开）
    /// </summary>
    private Calendar? ParseIcsContent(string icsContent)
    {
        try
        {
            Calendar calendar = Calendar.Load(icsContent);
            Debug.WriteLine($"CornerCalendar: Parsed ICS calendar with {calendar.Events.Count} event definitions");
            return calendar;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"CornerCalendar: Failed to parse ICS calendar: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 将一个 Occurrence（可能是重复事件的某一次出现）转换为 CornerCalendar CalendarEvent
    /// </summary>
    private CornerCalendarEvent? ConvertOccurrence(Occurrence occurrence)
    {
        if (occurrence.Source == null)
            return null;

        IcalEvent? icalEvent = occurrence.Source as IcalEvent;
        if (icalEvent == null)
            return null;

        Period period = occurrence.Period;
        if (period?.StartTime == null)
            return null;

        DateTime startTime = period.StartTime.AsSystemLocal;
        bool isAllDay = icalEvent.IsAllDay;

        DateTime endTime;
        if (period.EndTime != null)
        {
            endTime = period.EndTime.AsSystemLocal;
        }
        else if (period.Duration != default)
        {
            endTime = startTime + period.Duration;
        }
        else
        {
            endTime = isAllDay ? startTime.AddDays(1) : startTime.AddHours(1);
        }

        // 全天事件：确保 EndTime > StartTime（单日全天事件 DTEND==DTSTART 时修正为次日）
        if (isAllDay && endTime.Date <= startTime.Date)
            endTime = startTime.AddDays(1);

        string title = !string.IsNullOrWhiteSpace(icalEvent.Summary)
            ? icalEvent.Summary
            : "(无标题)";

        string location = icalEvent.Location ?? "";
        string description = icalEvent.Description ?? "";

        return new CornerCalendarEvent(
            Title: title,
            StartTime: startTime,
            EndTime: endTime,
            IsAllDay: isAllDay,
            CalendarName: _calendarName,
            Color: _color,
            Location: location,
            Description: description
        );
    }

    /// <summary>
    /// 节假日类全天事件的保守去重。
    /// 旧实现按「标题第一个括号前的前缀」分组合并，会误吞"评审会（设计）"/"评审会（开发）"这类正常事件。
    /// 新规则仅在同时满足以下条件时合并：
    ///   1. 两条都是全天事件（为"端午节"/"端午节（休）"这类节假日订阅源设计，定时事件永不合并）；
    ///   2. 同一天且一个标题完整包含另一个标题。
    /// 合并时保留跨度更长的一条；跨度相同保留标题更短的（显示更干净）。
    /// internal static 以便单元测试。
    /// </summary>
    internal static List<CornerCalendarEvent> DeduplicateAllDayEvents(List<CornerCalendarEvent> events)
    {
        List<CornerCalendarEvent> result = new List<CornerCalendarEvent>();

        foreach (IGrouping<DateTime, CornerCalendarEvent> dayGroup in events.GroupBy(e => e.StartTime.Date))
        {
            List<CornerCalendarEvent> kept = new List<CornerCalendarEvent>();

            foreach (CornerCalendarEvent evt in dayGroup)
            {
                // 定时事件永不参与合并
                if (!evt.IsAllDay)
                {
                    kept.Add(evt);
                    continue;
                }

                CornerCalendarEvent? duplicate = kept.FirstOrDefault(k =>
                    k.IsAllDay && TitlesIndicateSameEvent(k.Title, evt.Title));

                if (duplicate == null)
                {
                    kept.Add(evt);
                    continue;
                }

                // 保留跨度更长的；跨度相同保留标题更短的
                bool replace = evt.EndTime > duplicate.EndTime ||
                    (evt.EndTime == duplicate.EndTime && evt.Title.Length < duplicate.Title.Length);
                if (replace)
                {
                    kept.Remove(duplicate);
                    kept.Add(evt);
                }
            }

            result.AddRange(kept);
        }

        return result;
    }

    private static bool TitlesIndicateSameEvent(string a, string b)
    {
        // 过短的标题包含关系匹配面过大（如单字），不视为同一事件
        string comparableA = NormalizeComparableTitle(a);
        string comparableB = NormalizeComparableTitle(b);
        if (Math.Min(comparableA.Length, comparableB.Length) < 2)
            return false;

        return comparableA.Contains(comparableB, StringComparison.Ordinal)
            || comparableB.Contains(comparableA, StringComparison.Ordinal);
    }

    private static string NormalizeComparableTitle(string title)
    {
        string trimmed = title.Trim();
        if (trimmed.StartsWith('「') && trimmed.Contains('」'))
            return trimmed[1..trimmed.IndexOf('」')];
        if (trimmed.StartsWith('『') && trimmed.Contains('』'))
            return trimmed[1..trimmed.IndexOf('』')];
        return trimmed;
    }

    public void Dispose()
    {
        _httpClient.Dispose();
        _refreshLock.Dispose();
    }
}