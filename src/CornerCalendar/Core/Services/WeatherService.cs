using CornerCalendar.Core.Models;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text.Json;

namespace CornerCalendar.Core.Services;

/// <summary>
/// 远程天气服务：使用 IP 定位或 Open-Meteo 地理编码，再读取当前天气和七天预报。
/// 不在本地内置天气数据；网络失败时由界面显示失败状态。
/// </summary>
public static class WeatherService
{
    public const string DefaultWeatherApiUrl = "https://api.open-meteo.com/v1/forecast";
    public const int DefaultWeatherRefreshMinutes = 120;

    private static readonly HttpClient Http = CreateHttpClient();
    private static readonly object CacheSync = new();
    private static readonly ConcurrentDictionary<string, Task<WeatherInfo?>> RefreshTasks = new();
    private static readonly Dictionary<string, CachedWeather> Cache = new(StringComparer.OrdinalIgnoreCase);

    private static readonly string CachePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CornerCalendar",
        "weather-cache.json");

    private static bool _cacheLoaded;

    private static HttpClient CreateHttpClient()
    {
        HttpClient client = new() { Timeout = TimeSpan.FromSeconds(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("CornerCalendar/1.0");
        return client;
    }

    public static async Task<WeatherInfo?> GetWeatherAsync(
        string? cityName,
        CancellationToken cancellationToken = default,
        string? weatherApiUrl = null,
        int refreshMinutes = DefaultWeatherRefreshMinutes)
    {
        string baseUrl = NormalizeWeatherApiUrl(weatherApiUrl);
        string cacheKey = BuildCacheKey(cityName, baseUrl);
        TimeSpan cacheLifetime = GetCacheLifetime(refreshMinutes);
        CachedWeather? cached = GetCachedWeather(cacheKey);
        if (cached?.Weather != null)
        {
            if (DateTime.UtcNow - cached.UpdatedAtUtc >= cacheLifetime)
            {
                _ = RefreshWeatherAsync(
                    cityName,
                    baseUrl,
                    cacheKey,
                    refreshMinutes,
                    CancellationToken.None,
                    force: true);
            }

            return cached.Weather;
        }

        return await RefreshWeatherAsync(
            cityName,
            baseUrl,
            cacheKey,
            refreshMinutes,
            cancellationToken,
            force: true);
    }

    /// <summary>
    /// 后台刷新多个位置。请求并行执行，避免逐个位置等待网络响应。
    /// </summary>
    public static async Task RefreshAllAsync(
        IEnumerable<string> locations,
        string? weatherApiUrl = null,
        int refreshMinutes = DefaultWeatherRefreshMinutes,
        CancellationToken cancellationToken = default)
    {
        string baseUrl = NormalizeWeatherApiUrl(weatherApiUrl);
        string[] uniqueLocations = locations
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Task<WeatherInfo?>[] refreshTasks = uniqueLocations
            .Select(location =>
            {
                string cacheKey = BuildCacheKey(location, baseUrl);
                return RefreshWeatherAsync(
                    location,
                    baseUrl,
                    cacheKey,
                    refreshMinutes,
                    cancellationToken,
                    force: true);
            })
            .ToArray();

        await Task.WhenAll(refreshTasks);
    }

    private static Task<WeatherInfo?> RefreshWeatherAsync(
        string? cityName,
        string baseUrl,
        string cacheKey,
        int refreshMinutes,
        CancellationToken cancellationToken,
        bool force)
    {
        return RefreshTasks.GetOrAdd(
            cacheKey,
            _ => RefreshWeatherCoreAsync(
                cityName,
                baseUrl,
                cacheKey,
                refreshMinutes,
                cancellationToken,
                force));
    }

    private static async Task<WeatherInfo?> RefreshWeatherCoreAsync(
        string? cityName,
        string baseUrl,
        string cacheKey,
        int refreshMinutes,
        CancellationToken cancellationToken,
        bool force)
    {
        try
        {
            CachedWeather? cached = GetCachedWeather(cacheKey);
            if (!force
                && cached?.Weather != null
                && DateTime.UtcNow - cached.UpdatedAtUtc < GetCacheLifetime(refreshMinutes))
                return cached.Weather;

            WeatherInfo? weather = await DownloadWeatherAsync(cityName, baseUrl, cancellationToken);
            if (weather != null)
                SaveCachedWeather(cacheKey, weather);

            return weather;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"CornerCalendar: Weather refresh failed: {ex.Message}");
            return GetCachedWeather(cacheKey)?.Weather;
        }
        finally
        {
            RefreshTasks.TryRemove(cacheKey, out _);
        }
    }

    private static async Task<WeatherInfo?> DownloadWeatherAsync(
        string? cityName,
        string baseUrl,
        CancellationToken cancellationToken)
    {
        double latitude;
        double longitude;
        string city;

        if (string.IsNullOrWhiteSpace(cityName))
        {
            (latitude, longitude, city) = await LocateByIpAsync(cancellationToken);
            if (double.IsNaN(latitude) || double.IsNaN(longitude))
                return null;

            city = string.IsNullOrWhiteSpace(city) ? "当前位置" : city;
        }
        else
        {
            city = cityName.Trim();
            (latitude, longitude) = await GeocodeAsync(city, cancellationToken);
            if (double.IsNaN(latitude) || double.IsNaN(longitude))
                return null;
        }

        string separator = baseUrl.Contains('?', StringComparison.Ordinal) ? "&" : "?";
        string url =
            $"{baseUrl}{separator}latitude={latitude.ToString(CultureInfo.InvariantCulture)}" +
            $"&longitude={longitude.ToString(CultureInfo.InvariantCulture)}" +
            "&current=temperature_2m,weather_code,apparent_temperature,relative_humidity_2m,wind_speed_10m,precipitation,cloud_cover,uv_index,dew_point_2m,visibility" +
            "&daily=weather_code,temperature_2m_max,temperature_2m_min,apparent_temperature_max,apparent_temperature_min," +
            "relative_humidity_2m_max,relative_humidity_2m_min,cloud_cover_max,visibility_mean," +
            "precipitation_probability_max,wind_speed_10m_max,precipitation_sum,uv_index_max,sunrise,sunset" +
            "&forecast_days=8&timezone=auto";

        using JsonDocument document = await GetJsonAsync(url, cancellationToken);
        JsonElement current = document.RootElement.GetProperty("current");
        double temperature = current.GetProperty("temperature_2m").GetDouble();
        int weatherCode = current.GetProperty("weather_code").GetInt32();
        double feelsLikeTemperature = current.GetProperty("apparent_temperature").GetDouble();
        double relativeHumidity = current.GetProperty("relative_humidity_2m").GetDouble();
        double windSpeed = current.GetProperty("wind_speed_10m").GetDouble();
        double precipitation = current.GetProperty("precipitation").GetDouble();
        double cloudCover = current.GetProperty("cloud_cover").GetDouble();
        double uvIndex = current.GetProperty("uv_index").GetDouble();
        double dewPoint = current.GetProperty("dew_point_2m").GetDouble();
        double visibility = current.GetProperty("visibility").GetDouble();
        (string description, WeatherIconKind iconKind) = MapWeatherCode(weatherCode);
        List<WeatherForecastDay> forecast = ParseForecast(document.RootElement);

        return new WeatherInfo(
            city,
            temperature,
            description,
            iconKind,
            feelsLikeTemperature,
            relativeHumidity,
            windSpeed,
            precipitation,
            cloudCover,
            uvIndex,
            dewPoint,
            visibility,
            forecast);
    }

    private static string BuildCacheKey(string? cityName, string baseUrl)
        => $"{baseUrl}\n{(cityName ?? string.Empty).Trim()}";

    private static TimeSpan GetCacheLifetime(int refreshMinutes)
        => TimeSpan.FromMinutes(refreshMinutes is 30 or 60 or 120 or 240
            ? refreshMinutes
            : DefaultWeatherRefreshMinutes);

    private static CachedWeather? GetCachedWeather(string cacheKey)
    {
        EnsureCacheLoaded();
        lock (CacheSync)
            return Cache.TryGetValue(cacheKey, out CachedWeather? cached) ? cached : null;
    }

    private static void SaveCachedWeather(string cacheKey, WeatherInfo weather)
    {
        EnsureCacheLoaded();
        lock (CacheSync)
        {
            Cache[cacheKey] = new CachedWeather
            {
                UpdatedAtUtc = DateTime.UtcNow,
                Weather = weather
            };

            try
            {
                string? directory = Path.GetDirectoryName(CachePath);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);

                string json = JsonSerializer.Serialize(Cache, new JsonSerializerOptions { WriteIndented = true });
                string tempPath = CachePath + ".tmp";
                File.WriteAllText(tempPath, json);
                File.Move(tempPath, CachePath, overwrite: true);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"CornerCalendar: Failed to save weather cache: {ex.Message}");
            }
        }
    }

    private static void EnsureCacheLoaded()
    {
        lock (CacheSync)
        {
            if (_cacheLoaded)
                return;

            _cacheLoaded = true;
            try
            {
                if (!File.Exists(CachePath))
                    return;

                string json = File.ReadAllText(CachePath);
                Dictionary<string, CachedWeather>? saved = JsonSerializer.Deserialize<Dictionary<string, CachedWeather>>(json);
                if (saved == null)
                    return;

                foreach ((string key, CachedWeather value) in saved)
                {
                    if (value.Weather != null)
                        Cache[key] = value;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"CornerCalendar: Failed to load weather cache: {ex.Message}");
            }
        }
    }

    private static List<WeatherForecastDay> ParseForecast(JsonElement root)
    {
        JsonElement daily = root.GetProperty("daily");
        JsonElement dates = daily.GetProperty("time");
        JsonElement codes = daily.GetProperty("weather_code");
        JsonElement maxTemperatures = daily.GetProperty("temperature_2m_max");
        JsonElement minTemperatures = daily.GetProperty("temperature_2m_min");
        JsonElement feelsLikeMaxTemperatures = daily.GetProperty("apparent_temperature_max");
        JsonElement feelsLikeMinTemperatures = daily.GetProperty("apparent_temperature_min");
        JsonElement humidityMax = daily.GetProperty("relative_humidity_2m_max");
        JsonElement humidityMin = daily.GetProperty("relative_humidity_2m_min");
        JsonElement cloudCovers = daily.GetProperty("cloud_cover_max");
        JsonElement visibilities = daily.GetProperty("visibility_mean");
        JsonElement precipitationProbabilities = daily.GetProperty("precipitation_probability_max");
        JsonElement windSpeeds = daily.GetProperty("wind_speed_10m_max");
        JsonElement precipitationSums = daily.GetProperty("precipitation_sum");
        JsonElement uvIndexes = daily.GetProperty("uv_index_max");
        JsonElement sunrises = daily.GetProperty("sunrise");
        JsonElement sunsets = daily.GetProperty("sunset");
        int count = Math.Min(8, dates.GetArrayLength());
        List<WeatherForecastDay> forecast = new(count);

        for (int index = 0; index < count; index++)
        {
            DateTime date = DateTime.Parse(
                dates[index].GetString()!,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None);
            int code = codes[index].GetInt32();
            (string description, WeatherIconKind iconKind) = MapWeatherCode(code);
            forecast.Add(new WeatherForecastDay(
                date,
                maxTemperatures[index].GetDouble(),
                minTemperatures[index].GetDouble(),
                feelsLikeMaxTemperatures[index].GetDouble(),
                feelsLikeMinTemperatures[index].GetDouble(),
                humidityMax[index].GetDouble(),
                humidityMin[index].GetDouble(),
                cloudCovers[index].GetDouble(),
                visibilities[index].GetDouble(),
                description,
                iconKind,
                precipitationProbabilities[index].GetDouble(),
                windSpeeds[index].GetDouble(),
                precipitationSums[index].GetDouble(),
                uvIndexes[index].GetDouble(),
                FormatTime(sunrises[index].GetString()),
                FormatTime(sunsets[index].GetString())));
        }

        return forecast;
    }

    private static string FormatTime(string? value)
    {
        return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime time)
            ? time.ToString("HH:mm")
            : "--:--";
    }

    private static string NormalizeWeatherApiUrl(string? weatherApiUrl)
    {
        if (!Uri.TryCreate(weatherApiUrl, UriKind.Absolute, out Uri? uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return DefaultWeatherApiUrl;
        }

        return weatherApiUrl!.TrimEnd('&', '?');
    }

    private static async Task<(double latitude, double longitude, string city)> LocateByIpAsync(
        CancellationToken cancellationToken)
    {
        // 定位接口可能被限流或在不同网络环境下不可达。并行请求多个来源，
        // 只要有一个返回有效坐标即可，避免单个接口故障让自动定位整体失败。
        Task<IpLocation?>[] locationTasks =
        {
            TryLocateByIpAsync(
                "https://ipwho.is/",
                ParseIpWhoLocation,
                cancellationToken),
            TryLocateByIpAsync(
                "https://ipapi.co/json/",
                ParseIpApiCoLocation,
                cancellationToken),
            TryLocateByIpAsync(
                "http://ip-api.com/json/?fields=status,lat,lon,city",
                ParseIpApiLocation,
                cancellationToken)
        };

        IpLocation?[] locations = await Task.WhenAll(locationTasks);
        IpLocation? location = locations.FirstOrDefault(value => value.HasValue);
        return location is IpLocation valid
            ? (valid.Latitude, valid.Longitude, valid.City)
            : (double.NaN, double.NaN, string.Empty);
    }

    private static async Task<IpLocation?> TryLocateByIpAsync(
        string url,
        Func<JsonElement, IpLocation?> parser,
        CancellationToken cancellationToken)
    {
        try
        {
            using JsonDocument document = await GetJsonAsync(url, cancellationToken);
            return parser(document.RootElement);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    private static IpLocation? ParseIpWhoLocation(JsonElement root)
    {
        if (!TryGetBoolean(root, "success", out bool success) || !success)
            return null;

        return CreateIpLocation(root, "latitude", "longitude", "city");
    }

    private static IpLocation? ParseIpApiCoLocation(JsonElement root)
    {
        if (TryGetBoolean(root, "error", out bool error) && error)
            return null;

        return CreateIpLocation(root, "latitude", "longitude", "city");
    }

    private static IpLocation? ParseIpApiLocation(JsonElement root)
    {
        if (!TryGetString(root, "status", out string? status)
            || !string.Equals(status, "success", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return CreateIpLocation(root, "lat", "lon", "city");
    }

    private static IpLocation? CreateIpLocation(
        JsonElement root,
        string latitudeName,
        string longitudeName,
        string cityName)
    {
        if (!TryGetDouble(root, latitudeName, out double latitude)
            || !TryGetDouble(root, longitudeName, out double longitude)
            || !double.IsFinite(latitude)
            || !double.IsFinite(longitude)
            || latitude is < -90 or > 90
            || longitude is < -180 or > 180)
        {
            return null;
        }

        string city = TryGetString(root, cityName, out string? value) ? value ?? string.Empty : string.Empty;
        return new IpLocation(latitude, longitude, city);
    }

    private static bool TryGetDouble(JsonElement root, string name, out double value)
    {
        value = double.NaN;
        if (!root.TryGetProperty(name, out JsonElement property))
            return false;

        if (property.ValueKind == JsonValueKind.Number)
            return property.TryGetDouble(out value);

        return property.ValueKind == JsonValueKind.String
            && double.TryParse(
                property.GetString(),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out value);
    }

    private static bool TryGetString(JsonElement root, string name, out string? value)
    {
        value = null;
        return root.TryGetProperty(name, out JsonElement property)
            && property.ValueKind == JsonValueKind.String
            && (value = property.GetString()) != null;
    }

    private static bool TryGetBoolean(JsonElement root, string name, out bool value)
    {
        value = false;
        if (!root.TryGetProperty(name, out JsonElement property)
            || (property.ValueKind != JsonValueKind.True
                && property.ValueKind != JsonValueKind.False))
        {
            return false;
        }

        value = property.GetBoolean();
        return true;
    }

    private static async Task<(double latitude, double longitude)> GeocodeAsync(
        string cityName,
        CancellationToken cancellationToken)
    {
        string url =
            "https://geocoding-api.open-meteo.com/v1/search" +
            $"?name={Uri.EscapeDataString(cityName)}&count=1&language=zh&format=json";

        using JsonDocument document = await GetJsonAsync(url, cancellationToken);
        if (!document.RootElement.TryGetProperty("results", out JsonElement results)
            || results.GetArrayLength() == 0)
        {
            return (double.NaN, double.NaN);
        }

        JsonElement first = results[0];
        return (
            first.GetProperty("latitude").GetDouble(),
            first.GetProperty("longitude").GetDouble());
    }

    private static async Task<JsonDocument> GetJsonAsync(
        string url,
        CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await Http.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }

    private static (string description, WeatherIconKind kind) MapWeatherCode(int code) => code switch
    {
        0 => ("晴", WeatherIconKind.Clear),
        1 => ("大致晴朗", WeatherIconKind.PartlyCloudy),
        2 => ("多云", WeatherIconKind.PartlyCloudy),
        3 => ("阴", WeatherIconKind.Cloudy),
        45 or 48 => ("雾", WeatherIconKind.Fog),
        51 or 53 or 55 or 56 or 57 => ("毛毛雨", WeatherIconKind.Rain),
        61 or 63 or 65 or 66 or 67 => ("雨", WeatherIconKind.Rain),
        71 or 73 or 75 or 77 or 85 or 86 => ("雪", WeatherIconKind.Snow),
        80 or 81 or 82 => ("阵雨", WeatherIconKind.Rain),
        95 or 96 or 99 => ("雷暴", WeatherIconKind.Thunder),
        _ => ("未知", WeatherIconKind.Unknown)
    };

    private sealed class CachedWeather
    {
        public DateTime UpdatedAtUtc { get; set; }
        public WeatherInfo? Weather { get; set; }
    }

    private readonly record struct IpLocation(double Latitude, double Longitude, string City);
}