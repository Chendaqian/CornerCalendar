using CornerCalendar.Core.Models;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace CornerCalendar.Views.Controls;

public partial class DayCell : UserControl
{
    public DayCell()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 绑定的日历日数据
    /// </summary>
    public static readonly DependencyProperty DayDataProperty =
        DependencyProperty.Register(
            nameof(DayData), typeof(CalendarDay), typeof(DayCell),
            new PropertyMetadata(null, OnDayDataChanged));

    public CalendarDay? DayData
    {
        get => (CalendarDay?)GetValue(DayDataProperty);
        set => SetValue(DayDataProperty, value);
    }

    /// <summary>
    /// 是否被选中
    /// </summary>
    public static readonly DependencyProperty IsSelectedProperty =
        DependencyProperty.Register(
            nameof(IsSelected), typeof(bool), typeof(DayCell),
            new PropertyMetadata(false, OnIsSelectedChanged));

    public bool IsSelected
    {
        get => (bool)GetValue(IsSelectedProperty);
        set => SetValue(IsSelectedProperty, value);
    }

    /// <summary>
    /// 日期被点击时触发的事件
    /// </summary>
    public event RoutedEventHandler? DayClicked;

    private static void OnDayDataChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        DayCell cell = (DayCell)d;
        if (e.NewValue is not CalendarDay day) return;

        // 日期数字
        cell.DayText.Text = day.Date.Day.ToString();

        // 今日高亮
        cell.TodayCircle.Visibility = day.IsToday ? Visibility.Visible : Visibility.Collapsed;
        SetDayTextBrush(cell, day, isSelected: false);

        cell.DayText.FontWeight = day.IsToday ? FontWeights.Bold : FontWeights.Normal;

        cell.HolidayBadge.Visibility = Visibility.Collapsed;
        if (day.IsWorkday)
        {
            cell.HolidayBadgeText.Text = "班";
            cell.HolidayBadge.SetResourceReference(
                Border.BackgroundProperty, "WorkdayBadgeBrush");
            cell.HolidayBadgeText.SetResourceReference(
                TextBlock.ForegroundProperty, "WorkdayBadgeTextBrush");
            cell.HolidayBadge.Visibility = Visibility.Visible;
        }
        else if (!string.IsNullOrEmpty(day.LegalHoliday))
        {
            cell.HolidayBadgeText.Text = "休";
            cell.HolidayBadge.SetResourceReference(
                Border.BackgroundProperty, "RestDayBadgeBrush");
            cell.HolidayBadgeText.SetResourceReference(
                TextBlock.ForegroundProperty, "RestDayBadgeTextBrush");
            cell.HolidayBadge.Visibility = Visibility.Visible;
        }

        // 每天只显示一个附加标签：法定节假日/补班 > 传统节日 > 节气 > 农历日。
        // 没有节日时只显示“初几”等农历日名，避免农历月和节日叠加挤在同一格。
        string infoText = GetCalendarInfoText(day);

        cell.CalendarInfoText.Text = infoText;
        cell.CalendarInfoText.Visibility = string.IsNullOrEmpty(infoText)
            ? Visibility.Collapsed
            : Visibility.Visible;
        cell.CalendarInfoText.SetResourceReference(
            TextBlock.ForegroundProperty, "TodayAccentBrush");

        List<string> tooltipLines = new() { day.Date.ToString("yyyy年M月d日") };
        if (!string.IsNullOrEmpty(day.LunarDate))
            tooltipLines.Add($"农历 {day.LunarDate}");
        if (!string.IsNullOrEmpty(day.SolarTerm))
            tooltipLines.Add($"节气 {day.SolarTerm}");
        if (!string.IsNullOrEmpty(day.LunarFestival))
            tooltipLines.Add($"节日 {day.LunarFestival}");
        if (!string.IsNullOrEmpty(day.LegalHoliday))
            tooltipLines.Add($"法定节假日 {day.LegalHoliday}");
        if (day.IsWorkday)
            tooltipLines.Add("调休补班");
        cell.ToolTip = string.Join(Environment.NewLine, tooltipLines);

        // 事件圆点 - 最多3个，按事件显示；同一数据源的多个事件也分别显示。
        if (day.HasEvents && day.Events.Count > 0)
        {
            List<string> eventColors = day.Events
                .Take(3)
                .Select(e => e.Color)
                .ToList();

            cell.EventDots.Visibility = Visibility.Visible;
            System.Windows.Shapes.Ellipse[] dots = new[] { cell.Dot1, cell.Dot2, cell.Dot3 };
            for (int i = 0; i < dots.Length; i++)
            {
                if (i < eventColors.Count)
                {
                    dots[i].Visibility = Visibility.Visible;
                    try
                    {
                        SolidColorBrush brush = new SolidColorBrush(
                            (Color)ColorConverter.ConvertFromString(eventColors[i]));
                        brush.Freeze();
                        dots[i].Fill = brush;
                    }
                    catch
                    {
                        dots[i].SetResourceReference(
                            System.Windows.Shapes.Shape.FillProperty, "EventDotBrush");
                    }
                }
                else
                {
                    dots[i].Visibility = Visibility.Collapsed;
                }
            }
        }
        else
        {
            cell.EventDots.Visibility = Visibility.Collapsed;
            cell.Dot1.Visibility = Visibility.Collapsed;
            cell.Dot2.Visibility = Visibility.Collapsed;
            cell.Dot3.Visibility = Visibility.Collapsed;
        }
    }

    private static void OnIsSelectedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        DayCell cell = (DayCell)d;
        bool isSelected = (bool)e.NewValue;

        // 选中状态：显示蓝色边框圆圈（仅非今日时显示，今日有自己的填充圆）
        if (cell.DayData is { IsToday: false })
        {
            cell.SelectedCircle.Visibility = isSelected ? Visibility.Visible : Visibility.Collapsed;
            SetDayTextBrush(cell, cell.DayData, isSelected);
        }
    }

    private void OnDayClick(object sender, MouseButtonEventArgs e)
    {
        DayClicked?.Invoke(this, new RoutedEventArgs { Source = this });
    }

    private static void SetDayTextBrush(DayCell cell, CalendarDay day, bool isSelected)
    {
        string resourceKey = day.IsToday
            ? "TextOnAccentBrush"
            : isSelected
                ? "TodayAccentBrush"
                : day.IsCurrentMonth
                    ? day.Date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday
                        ? "WeekendTextBrush"
                        : "TextPrimaryBrush"
                    : "TextDisabledBrush";

        // 必须使用动态资源，否则切换主题后代码设置的旧画刷会继续覆盖 XAML 主题。
        cell.DayText.SetResourceReference(TextBlock.ForegroundProperty, resourceKey);
    }

    private static string GetCalendarInfoText(CalendarDay day)
    {
        // 调休补班只通过右上角“班”角标表达，主标签仍显示当天的节日或农历日。
        if (!day.IsWorkday && !string.IsNullOrWhiteSpace(day.LegalHoliday))
            return day.LegalHoliday;

        if (!string.IsNullOrWhiteSpace(day.LunarFestival))
            return day.LunarFestival;

        if (!string.IsNullOrWhiteSpace(day.SolarTerm))
            return day.SolarTerm;

        if (string.IsNullOrWhiteSpace(day.LunarDate))
            return string.Empty;

        string[] lunarParts = day.LunarDate.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return lunarParts.Length > 0 ? lunarParts[^1] : day.LunarDate;
    }
}