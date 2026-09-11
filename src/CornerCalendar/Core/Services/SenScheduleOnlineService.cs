using CornerCalendar.Core.Models;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using YamlDotNet.Serialization;

namespace CornerCalendar.Core.Services;

/// <summary>
/// 森日程在线数据源：从数据根地址拉取 manifest.yaml 清单与各迭代 Markdown 文件并合并到本地。
/// </summary>
/// <remark>
/// 清单为 YAML（YamlDotNet 解析），条目 path 相对 manifest.yaml 所在目录解析（支持文件夹前缀）；
/// 数据文件复用 <see cref="SenScheduleParser" /> 的五列 Markdown 格式。
/// 启动时自动拉取一次，设置窗口森日程分类提供手动刷新；
/// 拉取成功写入本地缓存，失败时回退上次缓存（同 ICS/天气模式）。
/// 数据仓库：https://gitee.com/LuckBUBU/CornerCalendar 。
/// </remark>
public static class SenScheduleOnlineService
{
    private const int MaxIterations = 100;
    private const int MaxFileChars = 1024 * 1024;

    private static readonly string CacheDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CornerCalendar", "cache");

    private static readonly string CachePath = Path.Combine(CacheDirectory, "sen-online.json");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>
    /// 拉取结果：Iterations 为可用迭代（可能来自缓存），FromCache 为 true 表示在线失败回退缓存，Error 为失败原因。
    /// </summary>
    public sealed record FetchResult(
        IReadOnlyList<SenScheduleIteration> Iterations,
        bool FromCache,
        string? Error);

    /// <summary>
    /// manifest.yaml 的单个迭代条目。
    /// </summary>
    public sealed class ManifestEntry
    {
        [YamlMember(Alias = "name")]
        public string Name { get; set; } = string.Empty;

        [YamlMember(Alias = "path")]
        public string FilePath { get; set; } = string.Empty;

        [YamlMember(Alias = "enabled")]
        public bool Enabled { get; set; } = true;
    }

    /// <summary>
    /// manifest.yaml 文件结构。
    /// </summary>
    public sealed class Manifest
    {
        [YamlMember(Alias = "version")]
        public int Version { get; set; } = 1;

        [YamlMember(Alias = "iterations")]
        public List<ManifestEntry> Iterations { get; set; } = new();
    }

    /// <summary>
    /// 由数据根地址构造 manifest.yaml 的完整地址（根地址不带尾斜杠时自动补齐）。
    /// </summary>
    public static Uri BuildManifestUri(string rootUrl)
    {
        if (string.IsNullOrWhiteSpace(rootUrl)
            || !Uri.TryCreate(rootUrl.Trim(), UriKind.Absolute, out Uri? root)
            || (root.Scheme != Uri.UriSchemeHttps && root.Scheme != Uri.UriSchemeHttp))
            throw new FormatException("在线数据地址无效，需为 HTTP/HTTPS 完整地址");

        string normalized = root.AbsoluteUri.EndsWith('/')
            ? root.AbsoluteUri
            : root.AbsoluteUri + "/";
        return new Uri(new Uri(normalized), "manifest.yaml");
    }

    /// <summary>
    /// 把清单条目的相对路径解析为完整下载地址（相对 manifest.yaml 所在目录，逐段转义）。
    /// </summary>
    public static Uri ResolveIterationUrl(Uri manifestUri, string path)
    {
        string encoded = string.Join('/', path.Split('/', '\\')
            .Select(segment => Uri.EscapeDataString(segment)));
        return new Uri(manifestUri, encoded);
    }

    /// <summary>
    /// 解析 manifest.yaml 内容并做基础校验。
    /// </summary>
    public static Manifest ParseManifest(string yaml)
    {
        if (string.IsNullOrWhiteSpace(yaml))
            throw new FormatException("清单内容为空");

        Manifest? manifest;
        try
        {
            manifest = new DeserializerBuilder()
                .IgnoreUnmatchedProperties()
                .Build()
                .Deserialize<Manifest>(yaml);
        }
        catch (Exception ex)
        {
            throw new FormatException($"清单 YAML 格式错误：{ex.Message}", ex);
        }

        if (manifest?.Iterations is not { Count: > 0 })
            throw new FormatException("清单中没有 iterations 条目");
        if (manifest.Iterations.Count > MaxIterations)
            throw new FormatException($"清单迭代数量超过上限 {MaxIterations}");

        foreach (ManifestEntry entry in manifest.Iterations)
        {
            if (string.IsNullOrWhiteSpace(entry.Name))
                throw new FormatException("清单中存在缺少 name 的迭代条目");
            if (string.IsNullOrWhiteSpace(entry.FilePath))
                throw new FormatException($"迭代 {entry.Name} 缺少 path");
        }

        return manifest;
    }

    /// <summary>
    /// 拉取全部启用的在线迭代：清单 + 并行下载各 Markdown 文件；失败回退本地缓存。
    /// </summary>
    public static async Task<FetchResult> FetchIterationsAsync(
        string rootUrl,
        CancellationToken cancellationToken = default)
    {
        try
        {
            Uri manifestUri = BuildManifestUri(rootUrl);
            using HttpClient client = CreateHttpClient();
            Manifest manifest = ParseManifest(
                await GetLimitedAsync(client, manifestUri, cancellationToken));

            List<ManifestEntry> entries = manifest.Iterations
                .Where(entry => entry.Enabled)
                .ToList();
            SenScheduleIteration[] parsed = await Task.WhenAll(entries
                .Select(async entry => SenScheduleParser.Parse(
                    entry.Name,
                    await GetLimitedAsync(
                        client,
                        ResolveIterationUrl(manifestUri, entry.FilePath),
                        cancellationToken))));

            List<SenScheduleIteration> iterations = parsed.ToList();
            SaveCache(iterations);
            return new FetchResult(iterations, false, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new FetchResult(Array.Empty<SenScheduleIteration>(), false, null);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"CornerCalendar: 森日程在线拉取失败：{ex.Message}");
            IReadOnlyList<SenScheduleIteration>? cached = LoadCache();
            if (cached != null)
                return new FetchResult(cached, true, ex.Message);

            return new FetchResult(Array.Empty<SenScheduleIteration>(), false, ex.Message);
        }
    }

    /// <summary>
    /// 把在线迭代合并进本地列表：同名（Ordinal）以在线数据覆盖活动并保留本地 Id 与眼睛开关，合并后整体按「最新在前」重排。
    /// </summary>
    /// <remark>
    /// 排序键为迭代最早活动开始日期（<see cref="SenScheduleIteration.StartDate" />）降序，
    /// 无活动的迭代沉底，同日期按名称（Ordinal）降序兜底；
    /// 设置窗口仍可拖拽手动调序，下次在线合并会按本规则重排。
    /// </remark>
    public static void MergeIterations(
        List<SenScheduleIteration> local,
        IReadOnlyList<SenScheduleIteration> online)
    {
        foreach (SenScheduleIteration item in online)
        {
            SenScheduleIteration? previous = local.FirstOrDefault(iteration =>
                string.Equals(iteration.Name, item.Name, StringComparison.Ordinal));
            if (previous is null)
            {
                local.Add(item);
                continue;
            }

            int index = local.IndexOf(previous);
            item.Id = previous.Id;
            item.IsEnabled = previous.IsEnabled;
            local[index] = item;
        }

        local.Sort((left, right) =>
        {
            int byStartDate = right.StartDate.CompareTo(left.StartDate);    // 活动日期新的在前
            return byStartDate != 0
                ? byStartDate
                : string.Compare(right.Name, left.Name, StringComparison.Ordinal);
        });
    }

    private static async Task<string> GetLimitedAsync(
        HttpClient client,
        Uri uri,
        CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await client.GetAsync(uri, cancellationToken);
        response.EnsureSuccessStatusCode();
        string content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (content.Length > MaxFileChars)
            throw new FormatException($"在线文件超过大小上限（{MaxFileChars} 字符）：{uri}");

        return content;
    }

    private static HttpClient CreateHttpClient()
    {
        HttpClient client = new() { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("CornerCalendar/1.0");
        return client;
    }

    private static void SaveCache(IReadOnlyList<SenScheduleIteration> iterations)
    {
        try
        {
            Directory.CreateDirectory(CacheDirectory);
            string json = JsonSerializer.Serialize(iterations, JsonOptions);
            string tempPath = CachePath + ".tmp";
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, CachePath, overwrite: true);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"CornerCalendar: 森日程在线缓存写入失败：{ex.Message}");
        }
    }

    internal static IReadOnlyList<SenScheduleIteration>? LoadCache()
    {
        try
        {
            if (!File.Exists(CachePath))
                return null;

            return JsonSerializer.Deserialize<List<SenScheduleIteration>>(
                File.ReadAllText(CachePath));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"CornerCalendar: 森日程在线缓存读取失败：{ex.Message}");
            return null;
        }
    }
}