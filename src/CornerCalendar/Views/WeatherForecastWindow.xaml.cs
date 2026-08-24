using CornerCalendar.Core.Helpers;
using CornerCalendar.Core.Models;
using System.Windows;

namespace CornerCalendar.Views;

public partial class WeatherForecastWindow : Window
{
    public WeatherForecastWindow()
    {
        InitializeComponent();
    }

    public void ShowForecast(WeatherInfo weather, Window mainPanel)
    {
        CityText.Text = weather.City;
        ForecastItems.ItemsSource = weather.Forecast
            .Where(day => day.Date.Date > DateTime.Today)
            .Take(7)
            .Select(day => new ForecastDisplayItem(day, WeatherIconFactory.Create(day.IconKind)))
            .ToList();

        Show();
        UpdatePosition(mainPanel);
    }

    private void UpdatePosition(Window mainPanel)
    {
        WindowPositionHelper.PositionLeftAligned(this, mainPanel);
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        Hide();
    }

    private sealed class ForecastDisplayItem
    {
        public string DateLabel { get; }
        public FrameworkElement Icon { get; }
        public string Description { get; }
        public string TemperatureRange { get; }
        public string ToolTip { get; }

        public ForecastDisplayItem(WeatherForecastDay day, FrameworkElement icon)
        {
            DateLabel = $"{day.Date.Day}日 {GetWeekday(day.Date)}";
            Icon = icon;
            Description = day.Description;
            TemperatureRange = $"{day.MinTemperature:F0}° / {day.MaxTemperature:F0}°";
            ToolTip =
                $"{DateLabel}\n" +
                $"天气：{Description}\n" +
                $"温度：最低 {day.MinTemperature:F0}°，最高 {day.MaxTemperature:F0}°\n" +
                $"体感：最低 {day.FeelsLikeMinTemperature:F0}°，最高 {day.FeelsLikeMaxTemperature:F0}°\n" +
                $"湿度：{day.RelativeHumidityMin:F0}% - {day.RelativeHumidityMax:F0}%\n" +
                $"云量：最高 {day.CloudCover:F0}%\n" +
                $"风速：最大 {day.WindSpeed:F0} km/h\n" +
                $"降水：概率 {day.PrecipitationProbability:F0}%，累计 {day.PrecipitationSum:F1} mm\n" +
                $"紫外线：{day.UvIndexMax:F1}\n" +
                $"能见度：{day.Visibility / 1000:F1} km\n" +
                $"日出日落：{day.Sunrise} / {day.Sunset}";
        }

        private static string GetWeekday(DateTime date) => date.DayOfWeek switch
        {
            DayOfWeek.Sunday => "周日",
            DayOfWeek.Monday => "周一",
            DayOfWeek.Tuesday => "周二",
            DayOfWeek.Wednesday => "周三",
            DayOfWeek.Thursday => "周四",
            DayOfWeek.Friday => "周五",
            _ => "周六"
        };
    }
}