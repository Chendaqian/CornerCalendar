using CornerCalendar.Core.Models;
using CornerCalendar.Core.Services;
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

        UpdateSenVisuals(cell, day);

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

        AppendSenTooltips(tooltipLines, day);
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

        if (cell.DayData is not null)
            UpdateSenVisuals(cell, cell.DayData);
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

    private static void UpdateSenVisuals(DayCell cell, CalendarDay day)
    {
        SenScheduleOccurrence? primary = day.SenEvents is { Count: > 0 }
            ? SenScheduleService.SelectPrimary(day.SenEvents, day.Date)
            : null;

        cell.SenBadge.Visibility = Visibility.Collapsed;
        cell.SenCircle.Visibility = Visibility.Collapsed;
        if (primary is null)
            return;

        (string? badge, _, string? badgeColorKey) = SenScheduleRules.GetBadge(primary, day.Date);
        if (!string.IsNullOrWhiteSpace(badge) && !string.IsNullOrWhiteSpace(badgeColorKey))
        {
            cell.SenBadgeText.Text = badge;
            cell.SenBadgeText.SetResourceReference(
                TextBlock.ForegroundProperty,
                badgeColorKey);
            cell.SenBadge.Visibility = Visibility.Visible;
        }

        // 阶段圆圈表示森日程活动范围，即使当天是周末或调休上班日也应显示。
        // 选中和今日状态仍保持现有蓝色/填充圆圈。
        if (!day.IsToday
            && !cell.IsSelected
            && !string.IsNullOrWhiteSpace(primary.CircleColorKey))
        {
            cell.SenCircle.SetResourceReference(
                System.Windows.Shapes.Shape.StrokeProperty,
                primary.CircleColorKey);
            cell.SenCircle.Visibility = Visibility.Visible;
        }
    }

    private static void AppendSenTooltips(List<string> tooltipLines, CalendarDay day)
    {
        if (day.SenEvents is not { Count: > 0 })
            return;

        foreach (IGrouping<string, SenScheduleOccurrence> group in day.SenEvents
                     .OrderBy(occurrence => occurrence.IterationName, StringComparer.Ordinal)
                     .GroupBy(occurrence => occurrence.IterationName, StringComparer.Ordinal))
        {
            tooltipLines.Add($"森日程 · {group.Key}");
            foreach (SenScheduleOccurrence occurrence in group.OrderBy(item => item.Sequence))
            {
                (string? badge, string? badgeName, _) = SenScheduleRules.GetBadge(occurrence, day.Date);
                string phaseText = occurrence.PhaseId is null
                    ? $"{occurrence.Title} 自然 {occurrence.NaturalDays} 天，工作 {occurrence.WorkDays} 天"
                    : $"{occurrence.PhaseId}（{occurrence.PhaseName}）自然 {occurrence.PhaseTotalDays} 天，工作 {occurrence.PhaseWorkDays} 天";
                tooltipLines.Add(phaseText);

                if (occurrence.PhaseId is not null
                    && !IsPhaseTitle(occurrence))
                {
                    string badgeText = badge is null
                        ? string.Empty
                        : $"，角标 {badge}{(badgeName is null ? string.Empty : $"（{badgeName}）")}";
                    tooltipLines.Add($"当前活动：{occurrence.Title}{badgeText}");
                }
            }
        }
    }

    private static bool IsPhaseTitle(SenScheduleOccurrence occurrence)
    {
        if (occurrence.PhaseId is null || occurrence.PhaseName is null)
            return false;

        return string.Equals(occurrence.Title, occurrence.PhaseName, StringComparison.Ordinal)
            || string.Equals(
                occurrence.Title,
                $"{occurrence.PhaseName}({occurrence.PhaseId})",
                StringComparison.Ordinal);
    }
}
