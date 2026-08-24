using CornerCalendar.Core.Models;
using System.Windows;

namespace CornerCalendar.Views;

public partial class SenSchedulePreviewWindow : Window
{
    public SenSchedulePreviewWindow(SenScheduleIteration iteration)
    {
        InitializeComponent();
        Title = $"森日程预览 - {iteration.Name}";
        TitleText.Text = iteration.Name;
        SummaryText.Text = $"{iteration.Activities.Count} 项活动  ·  {iteration.StartDate:yyyy/M/d} - {iteration.EndDate:yyyy/M/d}";
        ActivityGrid.ItemsSource = iteration.Activities;
    }
}