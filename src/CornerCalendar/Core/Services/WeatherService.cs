using CornerCalendar.Core.Models;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text.Json;

namespace CornerCalendar.Core.Services;

/// <summary>
/// 远程天气服务：使用 IP 定位或 Open-Meteo 地理编码，再读取当前天气。
/// 不在本地内置天气数据；网络失败时由界面显示失败状态。
/// </summary>
public static class WeatherService
{
    public const string DefaultWeatherApiUrl = "https://api.open-meteo.com/v1/forecast";

    private static readonly HttpClient Http = CreateHttpClient();

    private static HttpClient CreateHttpClient()
    {
        HttpClient client = new() { Timeout = TimeSpan.FromSeconds(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("CornerCalendar/1.0");
        return client;
    }

    public static async Task<WeatherInfo?> GetWeatherAsync(
        string? cityName,
        CancellationToken cancellationToken = default,
        string? weatherApiUrl = null)
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

        string baseUrl = NormalizeWeatherApiUrl(weatherApiUrl);
        string separator = baseUrl.Contains('?', StringComparison.Ordinal) ? "&" : "?";
        string url =
            $"{baseUrl}{separator}latitude={latitude.ToString(CultureInfo.InvariantCulture)}" +
            $"&longitude={longitude.ToString(CultureInfo.InvariantCulture)}" +
            "&current=temperature_2m,weather_code";

        using JsonDocument document = await GetJsonAsync(url, cancellationToken);
        JsonElement current = document.RootElement.GetProperty("current");
        double temperature = current.GetProperty("temperature_2m").GetDouble();
        int weatherCode = current.GetProperty("weather_code").GetInt32();
        (string description, WeatherIconKind iconKind) = MapWeatherCode(weatherCode);

        return new WeatherInfo(city, temperature, description, iconKind);
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

    private readonly record struct IpLocation(double Latitude, double Longitude, string City);
}