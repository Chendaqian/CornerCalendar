namespace CornerCalendar.Core.Models;

/// <summary>
/// 天气图标类型，对应 Open-Meteo 的 weather_code 分类。
/// </summary>
public enum WeatherIconKind
{
    Clear,
    PartlyCloudy,
    Cloudy,
    Fog,
    Rain,
    Snow,
    Thunder,
    Unknown
}

/// <summary>
/// 当前天气摘要。
/// </summary>
public sealed record WeatherInfo(
    string City,
    double Temperature,
    string Description,
    WeatherIconKind IconKind);