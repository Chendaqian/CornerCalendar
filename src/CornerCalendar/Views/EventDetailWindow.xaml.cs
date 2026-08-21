using CornerCalendar.Core.Helpers;
using CornerCalendar.Core.Models;
using CornerCalendar.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Shapes;

namespace CornerCalendar.Views;

public partial class EventDetailWindow : Window
{
    public EventDetailWindow()
    {
        InitializeComponent();
    }

    public void ShowDay(CalendarDay day, Window mainPanel)
    {
        DayTitle.Text = day.Date.ToString("yyyy年M月d日 dddd");
        DaySubtitle.Text = day.IsToday ? "今天" : "日期详情";
        LunarText.Text = string.IsNullOrWhiteSpace(day.LunarDate)
            ? "农历信息暂无"
            : $"农历 {day.LunarDate}";

        List<string> festivalParts = new();
        if (!string.IsNullOrWhiteSpace(day.LunarFestival))
            festivalParts.Add(day.LunarFestival);
        if (!string.IsNullOrWhiteSpace(day.SolarTerm))
            festivalParts.Add($"节气 {day.SolarTerm}");
        FestivalText.Text = string.Join("  ·  ", festivalParts);
        FestivalText.Visibility = festivalParts.Count == 0
            ? Visibility.Collapsed
            : Visibility.Visible;

        List<string> holidayParts = new();
        if (!string.IsNullOrWhiteSpace(day.LegalHoliday)
            && !string.Equals(day.LegalHoliday, day.LunarFestival, StringComparison.Ordinal))
            holidayParts.Add(day.LegalHoliday);
        if (day.IsWorkday)
            holidayParts.Add("调休补班");
        else if (day.Date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            holidayParts.Add("周末休息");
        HolidayText.Text = string.Join("  ·  ", holidayParts);
        HolidayText.Visibility = holidayParts.Count == 0
            ? Visibility.Collapsed
            : Visibility.Visible;

        SuitableText.Text = string.IsNullOrWhiteSpace(day.SuitableActivities)
            ? "暂无数据"
            : day.SuitableActivities;
        AvoidText.Text = string.IsNullOrWhiteSpace(day.AvoidActivities)
            ? "暂无数据"
            : day.AvoidActivities;

        DayEventsPanel.Children.Clear();
        List<CalendarEvent> events = day.Events.OrderBy(evt => evt.StartTime).ToList();
        EventCountText.Text = $"{events.Count} 项";
        NoEventsText.Visibility = events.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        foreach (CalendarEvent calendarEvent in events)
            DayEventsPanel.Children.Add(CreateEventItem(calendarEvent));

        Show();
        UpdatePosition(mainPanel);
    }

    private static FrameworkElement CreateEventItem(CalendarEvent calendarEvent)
    {
        Border border = new()
        {
            BorderBrush = Application.Current.TryFindResource("SeparatorBrush") as System.Windows.Media.Brush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(0, 0, 0, 7)
        };

        Grid grid = new();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        Rectangle colorBar = new()
        {
            Fill = CreateBrush(calendarEvent.Color),
            RadiusX = 2,
            RadiusY = 2,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        Grid.SetColumn(colorBar, 0);
        grid.Children.Add(colorBar);

        StackPanel content = new();
        TextBlock title = new()
        {
            Text = calendarEvent.Title,
            FontSize = (double)(Application.Current.TryFindResource("FontSizeBody") ?? 12d),
            Foreground = Application.Current.TryFindResource("TextPrimaryBrush") as System.Windows.Media.Brush,
            TextWrapping = TextWrapping.Wrap
        };
        content.Children.Add(title);

        TextBlock time = new()
        {
            Text = EventListViewModel.FormatEventTime(calendarEvent),
            FontSize = (double)(Application.Current.TryFindResource("FontSizeSecondary") ?? 11d),
            Foreground = Application.Current.TryFindResource("TextSecondaryBrush") as System.Windows.Media.Brush,
            Margin = new Thickness(0, 3, 0, 0)
        };
        content.Children.Add(time);

        if (!string.IsNullOrWhiteSpace(calendarEvent.Location))
        {
            content.Children.Add(new TextBlock
            {
                Text = $"地点：{calendarEvent.Location}",
                FontSize = (double)(Application.Current.TryFindResource("FontSizeFootnote") ?? 10d),
                Foreground = Application.Current.TryFindResource("TextSecondaryBrush") as System.Windows.Media.Brush,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 3, 0, 0)
            });
        }

        Grid.SetColumn(content, 2);
        grid.Children.Add(content);
        border.Child = grid;
        return border;
    }

    private static System.Windows.Media.Brush CreateBrush(string color)
    {
        try
        {
            System.Windows.Media.Brush brush = (System.Windows.Media.Brush)
                new System.Windows.Media.BrushConverter().ConvertFromString(color)!;
            brush.Freeze();
            return brush;
        }
        catch
        {
            return Application.Current.TryFindResource("TodayAccentBrush") as System.Windows.Media.Brush
                ?? System.Windows.Media.Brushes.Gray;
        }
    }

    public void UpdatePosition(Window mainPanel)
    {
        if (!IsVisible)
            return;

        WindowPositionHelper.PositionBeside(this, mainPanel);
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        Hide();
    }
}