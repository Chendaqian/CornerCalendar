using CornerCalendar.Core.Models;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace CornerCalendar.Core.Helpers;

/// <summary>
/// 用 WPF 矢量图形绘制天气图标，避免依赖字体中的 emoji 样式。
/// </summary>
public static class WeatherIconFactory
{
    public static FrameworkElement Create(WeatherIconKind kind)
    {
        Canvas canvas = new() { Width = 28, Height = 28 };
        Brush cloud = Application.Current.TryFindResource("TextSecondaryBrush") as Brush
            ?? Brushes.LightGray;
        Brush sun = new SolidColorBrush(Color.FromRgb(0xFF, 0xC1, 0x07));
        Brush rain = new SolidColorBrush(Color.FromRgb(0x42, 0xA5, 0xF5));
        Brush snow = Application.Current.TryFindResource("TextPrimaryBrush") as Brush
            ?? Brushes.White;

        switch (kind)
        {
            case WeatherIconKind.Clear:
                DrawSun(canvas, sun, 14, 14, 5);
                break;

            case WeatherIconKind.PartlyCloudy:
                DrawSun(canvas, sun, 9, 9, 4);
                DrawCloud(canvas, cloud);
                break;

            case WeatherIconKind.Cloudy:
                DrawCloud(canvas, cloud);
                break;

            case WeatherIconKind.Fog:
                DrawCloud(canvas, cloud);
                DrawFogLines(canvas, cloud);
                break;

            case WeatherIconKind.Rain:
                DrawCloud(canvas, cloud);
                DrawRainDrops(canvas, rain);
                break;

            case WeatherIconKind.Snow:
                DrawCloud(canvas, cloud);
                DrawSnowflakes(canvas, snow);
                break;

            case WeatherIconKind.Thunder:
                DrawCloud(canvas, cloud);
                DrawLightning(canvas, sun);
                break;

            default:
                Add(canvas, new Ellipse { Width = 18, Height = 18, Fill = cloud }, 5, 5);
                break;
        }

        return canvas;
    }

    private static void Add(Canvas canvas, UIElement element, double left, double top)
    {
        Canvas.SetLeft(element, left);
        Canvas.SetTop(element, top);
        canvas.Children.Add(element);
    }

    private static void DrawSun(Canvas canvas, Brush brush, double cx, double cy, double radius)
    {
        Add(canvas, new Ellipse { Width = radius * 2, Height = radius * 2, Fill = brush },
            cx - radius, cy - radius);
        for (int i = 0; i < 8; i++)
        {
            double angle = i * Math.PI / 4;
            double cos = Math.Cos(angle);
            double sin = Math.Sin(angle);
            canvas.Children.Add(new Line
            {
                X1 = cx + cos * (radius + 1),
                Y1 = cy + sin * (radius + 1),
                X2 = cx + cos * (radius + 4),
                Y2 = cy + sin * (radius + 4),
                Stroke = brush,
                StrokeThickness = 1.5
            });
        }
    }

    private static void DrawCloud(Canvas canvas, Brush brush)
    {
        Add(canvas, new Ellipse { Width = 19, Height = 10, Fill = brush }, 4, 12);
        Add(canvas, new Ellipse { Width = 10, Height = 9, Fill = brush }, 7, 8);
        Add(canvas, new Ellipse { Width = 9, Height = 9, Fill = brush }, 13, 7);
    }

    private static void DrawFogLines(Canvas canvas, Brush brush)
    {
        canvas.Children.Add(new Line { X1 = 6, Y1 = 23, X2 = 22, Y2 = 23, Stroke = brush, StrokeThickness = 1.5 });
        canvas.Children.Add(new Line { X1 = 8, Y1 = 27, X2 = 20, Y2 = 27, Stroke = brush, StrokeThickness = 1.5 });
    }

    private static void DrawRainDrops(Canvas canvas, Brush brush)
    {
        Add(canvas, new Ellipse { Width = 3, Height = 5, Fill = brush }, 8, 22);
        Add(canvas, new Ellipse { Width = 3, Height = 5, Fill = brush }, 13, 20);
        Add(canvas, new Ellipse { Width = 3, Height = 5, Fill = brush }, 18, 22);
    }

    private static void DrawSnowflakes(Canvas canvas, Brush brush)
    {
        DrawSnowflake(canvas, brush, 10, 24);
        DrawSnowflake(canvas, brush, 18, 24);
    }

    private static void DrawSnowflake(Canvas canvas, Brush brush, double cx, double cy)
    {
        for (int i = 0; i < 3; i++)
        {
            double angle = i * Math.PI / 3;
            double dx = Math.Cos(angle) * 3;
            double dy = Math.Sin(angle) * 3;
            canvas.Children.Add(new Line
            {
                X1 = cx - dx,
                Y1 = cy - dy,
                X2 = cx + dx,
                Y2 = cy + dy,
                Stroke = brush,
                StrokeThickness = 1.1
            });
        }
    }

    private static void DrawLightning(Canvas canvas, Brush brush)
    {
        canvas.Children.Add(new Polygon
        {
            Points = new PointCollection
            {
                new(15, 17), new(10, 24), new(14, 24), new(12, 29)
            },
            Fill = brush
        });
    }
}