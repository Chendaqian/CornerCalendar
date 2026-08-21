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
    WeatherIconKind IconKind,
    double FeelsLikeTemperature,
    double RelativeHumidity,
    double WindSpeed,
    double Precipitation,
    double CloudCover,
    double UvIndex,
    double DewPoint,
    double Visibility,
    IReadOnlyList<WeatherForecastDay> Forecast);

/// <summary>
/// 一天的天气预报。
/// </summary>
public sealed record WeatherForecastDay(
    DateTime Date,
    double MaxTemperature,
    double MinTemperature,
    double FeelsLikeMaxTemperature,
    double FeelsLikeMinTemperature,
    double RelativeHumidityMax,
    double RelativeHumidityMin,
    double CloudCover,
    double Visibility,
    string Description,
    WeatherIconKind IconKind,
    double PrecipitationProbability,
    double WindSpeed,
    double PrecipitationSum,
    double UvIndexMax,
    string Sunrise,
    string Sunset);