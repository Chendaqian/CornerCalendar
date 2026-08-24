using CornerCalendar.Core.Helpers;
using CornerCalendar.Core.Models;
using CornerCalendar.Core.Services;
using CornerCalendar.ViewModels;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace CornerCalendar.Views;

public partial class EventDetailWindow : Window
{
    private readonly IHistoryTodayService _historyTodayService;
    private Window? _mainPanel;
    private int _historyRequestVersion;

    public EventDetailWindow(IHistoryTodayService historyTodayService)
    {
        _historyTodayService = historyTodayService;
        InitializeComponent();
    }

    public void ShowDay(CalendarDay day, Window mainPanel)
    {
        _mainPanel = mainPanel;
        AppSettings settings = AppSettings.Load();
        bool showHistory = settings.ShowHistoryToday;
        DayTitle.Text = day.Date.ToString("yyyy年M月d日 dddd");
        int relativeDay = (day.Date.Date - DateTime.Today).Days;
        RelativeDayText.Text = relativeDay < 0
            ? $"{-relativeDay} 天前"
            : $"{relativeDay} 天后";
        RelativeDayText.Visibility = relativeDay == 0
            ? Visibility.Collapsed
            : Visibility.Visible;
        SetLunarText(day.LunarDate);

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

        HistorySectionTitle.Visibility = showHistory ? Visibility.Visible : Visibility.Collapsed;
        HistoryCountText.Visibility = showHistory ? Visibility.Visible : Visibility.Collapsed;
        HistoryScrollViewer.Visibility = showHistory ? Visibility.Visible : Visibility.Collapsed;
        HistoryLoadingText.Visibility = showHistory ? Visibility.Visible : Visibility.Collapsed;
        NoHistoryText.Visibility = Visibility.Collapsed;
        HistoryErrorText.Visibility = Visibility.Collapsed;
        HistorySeparator.Visibility = showHistory ? Visibility.Visible : Visibility.Collapsed;

        double mainHeight = mainPanel.ActualHeight > 0
            ? mainPanel.ActualHeight
            : mainPanel.Height;
        if (mainHeight > 0)
            Height = mainHeight;

        int historyRequestVersion = ++_historyRequestVersion;
        HistorySectionTitle.Text = day.IsToday ? "历史上的今天" : "这一天的历史";
        HistoryCountText.Text = string.Empty;
        HistoryScrollViewer.ScrollToTop();
        HistoryItemsPanel.Children.Clear();
        HistoryLoadingText.Visibility = showHistory
            ? Visibility.Visible
            : Visibility.Collapsed;
        NoHistoryText.Visibility = Visibility.Collapsed;
        HistoryErrorText.Visibility = Visibility.Collapsed;

        Show();
        UpdatePosition(mainPanel);
        if (showHistory)
            _ = LoadHistoryAsync(day.Date, historyRequestVersion, settings);
    }

    private async Task LoadHistoryAsync(DateTime date, int requestVersion, AppSettings settings)
    {
        try
        {
            IReadOnlyList<HistoryTodayItem> items = await _historyTodayService.GetAsync(
                DateOnly.FromDateTime(date));
            HashSet<string> categories = (settings.HistoryCategories ?? new List<string>())
                .ToHashSet(StringComparer.Ordinal);
            items = items
                .Where(item => categories.Contains(item.Category)
                    && (!item.Year.HasValue || settings.HistoryMinYear <= 0 || item.Year.Value >= settings.HistoryMinYear))
                .Take(settings.HistoryMaxItems > 0 ? settings.HistoryMaxItems : int.MaxValue)
                .ToList();
            if (requestVersion != _historyRequestVersion)
                return;

            HistoryLoadingText.Visibility = Visibility.Collapsed;
            HistoryCountText.Text = items.Count == 0 ? string.Empty : $"{items.Count} 项";
            NoHistoryText.Visibility = items.Count == 0
                ? Visibility.Visible
                : Visibility.Collapsed;

            foreach (HistoryTodayItem item in items)
                HistoryItemsPanel.Children.Add(CreateHistoryItem(item));

            HistoryScrollViewer.ScrollToTop();
            await Dispatcher.InvokeAsync(
                HistoryScrollViewer.ScrollToTop,
                DispatcherPriority.Loaded);

            if (_mainPanel != null)
                UpdatePosition(_mainPanel);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"CornerCalendar: History today load failed: {ex.Message}");
            if (requestVersion != _historyRequestVersion)
                return;

            HistoryLoadingText.Visibility = Visibility.Collapsed;
            HistoryErrorText.Visibility = Visibility.Visible;
        }
    }

    private void SetLunarText(string lunarDate)
    {
        if (string.IsNullOrWhiteSpace(lunarDate))
        {
            LunarLabelRun.Text = "农历信息暂无";
            LunarLabelRun.Foreground = Application.Current.TryFindResource("TextSecondaryBrush")
                as System.Windows.Media.Brush;
            LunarMonthRun.Text = string.Empty;
            LunarDayRun.Text = string.Empty;
            return;
        }

        string[] parts = lunarDate
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length > 0
            && string.Equals(parts[0], "农历", StringComparison.Ordinal))
        {
            parts = parts[1..];
        }

        LunarLabelRun.Text = "农历 ";
        LunarLabelRun.Foreground = Application.Current.TryFindResource("LunarLabelBrush")
            as System.Windows.Media.Brush;
        LunarMonthRun.Text = parts.ElementAtOrDefault(0) ?? string.Empty;
        LunarDayRun.Text = parts.Length > 1
            ? string.Join(' ', parts.Skip(1))
            : string.Empty;
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

    private static FrameworkElement CreateHistoryItem(HistoryTodayItem item)
    {
        Border border = new()
        {
            BorderBrush = Application.Current.TryFindResource("SeparatorBrush") as System.Windows.Media.Brush,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(0, 7, 0, 7)
        };

        Grid grid = new();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(54) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(62) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        Border categoryBadge = new()
        {
            Background = GetHistoryCategoryBrush(item.Category),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(5, 2, 5, 2),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Child = new TextBlock
            {
                Text = item.Category,
                FontSize = (double)(Application.Current.TryFindResource("FontSizeFootnote") ?? 10d),
                Foreground = Application.Current.TryFindResource("TextOnAccentBrush") as System.Windows.Media.Brush,
                TextAlignment = TextAlignment.Center
            }
        };
        Grid.SetColumn(categoryBadge, 0);
        grid.Children.Add(categoryBadge);

        TextBlock year = new()
        {
            FontSize = (double)(Application.Current.TryFindResource("FontSizeSecondary") ?? 11d),
            FontWeight = FontWeights.SemiBold,
            Foreground = Application.Current.TryFindResource("TodayAccentBrush") as System.Windows.Media.Brush,
            TextWrapping = TextWrapping.Wrap
        };
        string yearText = item.Year.HasValue ? $"{item.Year}年" : item.Category;
        if (Uri.TryCreate(item.SourceUrl, UriKind.Absolute, out Uri? sourceUri))
        {
            Hyperlink link = new(new Run(yearText))
            {
                NavigateUri = sourceUri,
                Foreground = Application.Current.TryFindResource("TodayAccentBrush") as System.Windows.Media.Brush
            };
            link.RequestNavigate += OnHistorySourceNavigate;
            year.Inlines.Add(link);
        }
        else
        {
            year.Text = yearText;
        }

        Grid.SetColumn(year, 1);
        grid.Children.Add(year);

        TextBlock title = new()
        {
            Text = GetHistoryText(item),
            FontSize = (double)(Application.Current.TryFindResource("FontSizeBody") ?? 12d),
            Foreground = Application.Current.TryFindResource("TextPrimaryBrush") as System.Windows.Media.Brush,
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxHeight = 36
        };
        Grid.SetColumn(title, 3);
        grid.Children.Add(title);
        border.Child = grid;
        border.ToolTip = CreateHistoryToolTip(item);
        ToolTipService.SetShowDuration(border, 30000);
        return border;
    }

    private static System.Windows.Media.Brush GetHistoryCategoryBrush(string category)
        => category switch
        {
            "出生" => Application.Current.TryFindResource("HistoryBirthBrush")
                as System.Windows.Media.Brush,
            "逝世" => Application.Current.TryFindResource("HistoryDeathBrush")
                as System.Windows.Media.Brush,
            _ => Application.Current.TryFindResource("HistoryEventBrush")
                as System.Windows.Media.Brush
        } ?? System.Windows.Media.Brushes.Gray;

    private static string GetHistoryText(HistoryTodayItem item)
    {
        string text = string.IsNullOrWhiteSpace(item.Description)
            ? item.Title
            : item.Description.Trim();
        if (item.Year.HasValue)
        {
            string yearPrefix = $"{item.Year}年";
            if (text.StartsWith(yearPrefix, StringComparison.Ordinal))
                text = text[yearPrefix.Length..].TrimStart(' ', ':', '：', '-', '－');
        }

        return string.IsNullOrWhiteSpace(text) ? "历史事件" : text;
    }

    private static ToolTip CreateHistoryToolTip(HistoryTodayItem item)
    {
        StackPanel content = new()
        {
            MaxWidth = 360
        };
        string year = item.Year.HasValue ? $"{item.Year}年" : item.Category;
        content.Children.Add(new TextBlock
        {
            Text = year,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap
        });

        string historyText = GetHistoryText(item);
        if (!string.IsNullOrWhiteSpace(historyText))
        {
            content.Children.Add(new TextBlock
            {
                Text = historyText,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 0)
            });
        }

        if (!string.IsNullOrWhiteSpace(item.SourceTitle))
        {
            content.Children.Add(new TextBlock
            {
                Text = $"来源：{item.SourceTitle}",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 0)
            });
        }

        return new ToolTip { Content = content };
    }

    private static void OnHistorySourceNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri)
            {
                UseShellExecute = true
            });
            e.Handled = true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"CornerCalendar: Failed to open history source: {ex.Message}");
        }
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

        double mainHeight = mainPanel.ActualHeight > 0
            ? mainPanel.ActualHeight
            : mainPanel.Height;
        if (mainHeight > 0 && Math.Abs(Height - mainHeight) > 0.5)
            Height = mainHeight;

        WindowPositionHelper.PositionBeside(this, mainPanel);
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        Hide();
    }
}