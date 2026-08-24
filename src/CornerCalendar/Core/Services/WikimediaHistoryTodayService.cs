using CornerCalendar.Core.Models;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace CornerCalendar.Core.Services;

/// <summary>
/// Wikimedia 中文版“历史上的今天”服务。
/// 每个公历月日使用独立缓存文件；缓存文件存在时不检查过期时间。
/// </summary>
public sealed class WikimediaHistoryTodayService : IHistoryTodayService
{
    private const string ApiBaseUrl = "https://api.wikimedia.org/feed/v1/wikipedia/zh/onthisday";
    private static readonly string[] HistoryEndpoints = { "events", "births", "deaths" };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private sealed record HistoryCacheFile(
        int Version,
        IReadOnlyList<HistoryTodayItem> Items);

    private sealed class HistoryRateLimitException : Exception
    {
        public int RetryAfterSeconds { get; }

        public HistoryRateLimitException(int retryAfterSeconds)
            : base($"服务器限流，请等待约 {retryAfterSeconds} 秒")
        {
            RetryAfterSeconds = retryAfterSeconds;
        }
    }

    private readonly HttpClient _httpClient;

    private readonly string _resourceDirectory = Path.Combine(
        AppContext.BaseDirectory, "Resources", "HistoryToday");

    public WikimediaHistoryTodayService(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? CreateHttpClient();
    }

    public Task<IReadOnlyList<HistoryTodayItem>> GetAsync(
        DateOnly date,
        CancellationToken cancellationToken = default)
        => Task.FromResult(ReadLocalItems(date));

    public static IReadOnlyList<DateOnly> GetYearDates(int year)
        => Enumerable.Range(1, DateTime.IsLeapYear(year) ? 366 : 365)
            .Select(day => DateOnly.FromDateTime(new DateTime(year, 1, 1).AddDays(day - 1)))
            .ToList();

    public async Task CacheDatesAsync(
        IEnumerable<DateOnly> dates,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_resourceDirectory);
        int completed = 0;
        List<Exception> failures = new();
        object failureLock = new();

        IReadOnlyList<DateOnly> allDates = dates
            .Distinct()
            .ToList();
        IReadOnlyList<DateOnly> pendingDates = allDates
            .Where(date => !HasCurrentLocalItems(date))
            .ToList();
        completed = allDates.Count - pendingDates.Count;
        progress?.Report(completed);

        await Parallel.ForEachAsync(
            pendingDates,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = 1,
                CancellationToken = cancellationToken
            },
            async (date, token) =>
            {
                try
                {
                    await CacheDateWithRetryAsync(date, token);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    lock (failureLock)
                        failures.Add(new InvalidOperationException(
                            $"{date:MM-dd}：{ex.Message}", ex));
                }
                finally
                {
                    progress?.Report(Interlocked.Increment(ref completed));
                }
            });

        if (failures.Count > 0)
        {
            string details = string.Join("；", failures.Take(3).Select(failure => failure.Message));
            throw new InvalidOperationException(
                $"有 {failures.Count} 天缓存失败。{details}");
        }
    }

    private async Task CacheDateWithRetryAsync(
        DateOnly date,
        CancellationToken cancellationToken)
    {
        Exception? lastError = null;
        for (int attempt = 1; attempt <= 5; attempt++)
        {
            try
            {
                IReadOnlyList<HistoryTodayItem> items = await LoadRemoteAsync(date, cancellationToken);
                await SaveLocalItemsAsync(date, items, cancellationToken);
                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                lastError = ex;
                if (attempt < 5)
                {
                    int delaySeconds = ex is HistoryRateLimitException rateLimit
                        ? rateLimit.RetryAfterSeconds
                        : Math.Min(60, attempt * 5);
                    await Task.Delay(TimeSpan.FromSeconds(delaySeconds), cancellationToken);
                }
            }
        }

        throw lastError ?? new InvalidOperationException("未知网络错误");
    }

    internal static string BuildRequestUrl(DateOnly date)
        => $"embedded://history-today/{date.ToString("MM/dd", CultureInfo.InvariantCulture)}";

    internal static IReadOnlyList<HistoryTodayItem> ParseResponse(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new JsonException("Wikimedia response root must be an object.");

        List<HistoryTodayItem> items = new();
        foreach (JsonProperty property in document.RootElement.EnumerateObject())
        {
            string category = MapCategory(property.Name);
            if (category.Length == 0 || property.Value.ValueKind != JsonValueKind.Array)
                continue;

            foreach (JsonElement entry in property.Value.EnumerateArray())
            {
                HistoryTodayItem? item = ParseItem(entry, category);
                if (item != null)
                    items.Add(item);
            }
        }

        return OrderHistoryItems(items);
    }

    private IReadOnlyList<HistoryTodayItem> ReadLocalItems(DateOnly date)
    {
        string fileName = date.ToString("MM-dd", CultureInfo.InvariantCulture) + ".json";
        string path = Path.Combine(_resourceDirectory, fileName);
        if (File.Exists(path))
        {
            try
            {
                string json = File.ReadAllText(path);
                return ParseLocalItems(json);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"CornerCalendar: Failed to load local history: {ex.Message}");
            }
        }

        return Array.Empty<HistoryTodayItem>();
    }

    private static IReadOnlyList<HistoryTodayItem> ParseLocalItems(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        List<HistoryTodayItem>? items = document.RootElement.ValueKind == JsonValueKind.Array
            ? JsonSerializer.Deserialize<List<HistoryTodayItem>>(json, JsonOptions)
            : JsonSerializer.Deserialize<HistoryCacheFile>(json, JsonOptions)?.Items.ToList();
        return OrderHistoryItems(items?.Where(IsDisplayedHistoryItem)
            ?? Enumerable.Empty<HistoryTodayItem>());
    }

    private async Task<IReadOnlyList<HistoryTodayItem>> LoadRemoteAsync(
        DateOnly date,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<HistoryTodayItem>[] results = await Task.WhenAll(
            HistoryEndpoints.Select(endpoint => LoadEndpointAsync(date, endpoint, cancellationToken)));
        return OrderHistoryItems(results.SelectMany(items => items)
            .Where(IsDisplayedHistoryItem));
    }

    private async Task<IReadOnlyList<HistoryTodayItem>> LoadEndpointAsync(
        DateOnly date,
        string endpoint,
        CancellationToken cancellationToken)
    {
        string url = $"{ApiBaseUrl}/{endpoint}/{date.ToString("MM/dd", CultureInfo.InvariantCulture)}";
        using HttpResponseMessage response = await _httpClient.GetAsync(url, cancellationToken);
        if ((int)response.StatusCode == 429)
        {
            int retryAfter = response.Headers.RetryAfter?.Delta is TimeSpan delta
                ? Math.Max(10, (int)Math.Ceiling(delta.TotalSeconds))
                : 60;
            throw new HistoryRateLimitException(retryAfter);
        }
        response.EnsureSuccessStatusCode();
        return ParseResponse(await response.Content.ReadAsStringAsync(cancellationToken));
    }

    private async Task SaveLocalItemsAsync(
        DateOnly date,
        IReadOnlyList<HistoryTodayItem> items,
        CancellationToken cancellationToken)
    {
        string path = Path.Combine(
            _resourceDirectory,
            date.ToString("MM-dd", CultureInfo.InvariantCulture) + ".json");
        string tempPath = path + ".tmp";
        string json = JsonSerializer.Serialize(new HistoryCacheFile(2, items), JsonOptions);
        await File.WriteAllTextAsync(tempPath, json, Encoding.UTF8, cancellationToken);
        File.Move(tempPath, path, overwrite: true);
    }

    private bool HasCurrentLocalItems(DateOnly date)
    {
        string path = Path.Combine(
            _resourceDirectory,
            date.ToString("MM-dd", CultureInfo.InvariantCulture) + ".json");
        if (!File.Exists(path))
            return false;

        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("Version", out JsonElement version)
                && version.GetInt32() == 2;
        }
        catch
        {
            return false;
        }
    }

    private static HttpClient CreateHttpClient()
    {
        HttpClient client = new() { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("CornerCalendar/1.0");
        return client;
    }

    private static bool IsDisplayedHistoryItem(HistoryTodayItem item)
        => item.Category is "事件" or "出生" or "逝世";

    private static HistoryTodayItem? ParseItem(JsonElement entry, string category)
    {
        if (entry.ValueKind != JsonValueKind.Object)
            return null;

        int? year = null;
        if (entry.TryGetProperty("year", out JsonElement yearElement)
            && yearElement.TryGetInt32(out int parsedYear))
        {
            year = parsedYear;
        }

        string description = GetString(entry, "text");
        string title = string.Empty;
        string? sourceTitle = null;
        string? sourceUrl = null;

        if (entry.TryGetProperty("pages", out JsonElement pages)
            && pages.ValueKind == JsonValueKind.Array
            && pages.GetArrayLength() > 0)
        {
            JsonElement page = pages[0];
            sourceTitle = GetString(page, "title");
            title = sourceTitle ?? string.Empty;
            sourceUrl = GetPageUrl(page);
        }

        if (title.Length == 0)
            title = description;
        if (title.Length == 0 && description.Length == 0)
            return null;

        return new HistoryTodayItem(
            year,
            title,
            description,
            category,
            sourceTitle,
            sourceUrl);
    }

    private static string? GetPageUrl(JsonElement page)
    {
        if (!page.TryGetProperty("content_urls", out JsonElement contentUrls)
            || contentUrls.ValueKind != JsonValueKind.Object)
            return null;

        foreach (string platform in new[] { "desktop", "mobile" })
        {
            if (contentUrls.TryGetProperty(platform, out JsonElement platformUrls)
                && platformUrls.TryGetProperty("page", out JsonElement pageUrl))
            {
                string? value = pageUrl.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                    return value;
            }
        }

        return null;
    }

    private static string GetString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement property)
            || property.ValueKind != JsonValueKind.String)
            return string.Empty;

        return property.GetString()?.Trim() ?? string.Empty;
    }

    private static string MapCategory(string propertyName)
        => propertyName.ToLowerInvariant() switch
        {
            "events" => "事件",
            "births" => "出生",
            "deaths" => "逝世",
            "holidays" => "节日",
            _ => string.Empty
        };

    private static int CategoryOrder(string category)
        => category switch
        {
            "事件" => 0,
            "出生" => 1,
            "逝世" => 2,
            _ => 3
        };

    private static IReadOnlyList<HistoryTodayItem> OrderHistoryItems(
        IEnumerable<HistoryTodayItem> items)
        => items
            .GroupBy(item => $"{item.Year}|{item.Title}|{item.Description}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderByDescending(item => item.Year ?? int.MinValue)
            .ThenBy(item => CategoryOrder(item.Category))
            .ToList();
}