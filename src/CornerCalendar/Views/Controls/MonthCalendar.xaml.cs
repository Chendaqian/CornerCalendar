using CornerCalendar.Core.Models;
using CornerCalendar.ViewModels;
using System.Collections.Specialized;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace CornerCalendar.Views.Controls;

public partial class MonthCalendar : UserControl
{
    private CalendarViewModel? _viewModel;

    public event Action<CalendarDay>? DateClicked;

    public static readonly DependencyProperty ShowWeekNumbersProperty =
        DependencyProperty.Register(
            nameof(ShowWeekNumbers),
            typeof(bool),
            typeof(MonthCalendar),
            new PropertyMetadata(false, OnShowWeekNumbersChanged));

    public bool ShowWeekNumbers
    {
        get => (bool)GetValue(ShowWeekNumbersProperty);
        set => SetValue(ShowWeekNumbersProperty, value);
    }

    public MonthCalendar()
    {
        InitializeComponent();

        PrevButton.Click += (s, e) =>
        {
            if (DataContext is CalendarViewModel vm)
                vm.NavigatePreviousMonth();
        };

        NextButton.Click += (s, e) =>
        {
            if (DataContext is CalendarViewModel vm)
                vm.NavigateNextMonth();
        };

        TodayButton.Click += (s, e) =>
        {
            if (DataContext is CalendarViewModel vm)
                vm.NavigateToToday();
        };

        MonthTitle.MouseLeftButtonUp += OnMonthTitleClick;

        Loaded += OnLoaded;
        DataContextChanged += OnDataContextChanged;

        // 使用 PreviewMouseLeftButtonUp 在 MonthCalendar 级别捕获点击
        // 这比在单个 DayCell 上绑定事件更可靠
        PreviewMouseLeftButtonUp += OnPreviewMouseLeftButtonUp;
    }

    private void OnMonthTitleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not CalendarViewModel vm)
            return;

        DatePickerWindow picker = new(vm.Year, vm.Month, vm.SelectedDate)
        {
            Owner = Window.GetWindow(this),
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        picker.DateSelected += vm.NavigateToDate;
        picker.ShowDialog();
        e.Handled = true;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is CalendarViewModel vm)
        {
            BindViewModel(vm);
            UpdateWeekHeaders(vm.WeekStartDay);
            vm.SelectDate(DateTime.Today);
        }
    }

    /// <summary>
    /// 更新星期标题行（支持周一起始或周日起始）
    /// </summary>
    public void UpdateWeekHeaders(int weekStartDay)
    {
        // 周一: 一二三四五六日, 周日: 日一二三四五六
        string[] monStart = { "一", "二", "三", "四", "五", "六", "日" };
        string[] sunStart = { "日", "一", "二", "三", "四", "五", "六" };
        string[] headers = weekStartDay == 1 ? monStart : sunStart;

        TextBlock[] textBlocks = new[] { H0, H1, H2, H3, H4, H5, H6 };
        for (int i = 0; i < 7; i++)
        {
            textBlocks[i].Text = headers[i];
            bool isWeekend = headers[i] is "六" or "日";
            textBlocks[i].SetResourceReference(
                TextBlock.ForegroundProperty,
                isWeekend ? "WeekendTextBrush" : "TextSecondaryBrush");
        }
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is CalendarViewModel vm)
            BindViewModel(vm);
    }

    private void BindViewModel(CalendarViewModel vm)
    {
        if (ReferenceEquals(_viewModel, vm))
            return;

        if (_viewModel != null)
        {
            _viewModel.CalendarDays.CollectionChanged -= OnCalendarDaysChanged;
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _viewModel = vm;
        CalendarGrid.ItemsSource = vm.CalendarDays;
        vm.CalendarDays.CollectionChanged += OnCalendarDaysChanged;
        vm.PropertyChanged += OnViewModelPropertyChanged;
        UpdateWeekNumbers(vm.CalendarDays);
        ApplyWeekNumberVisibility();
        ScheduleSelectionUpdate();
    }

    private void OnCalendarDaysChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_viewModel != null)
        {
            UpdateWeekNumbers(_viewModel.CalendarDays);
            ScheduleSelectionUpdate();
        }
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(CalendarViewModel.SelectedDate)
            or nameof(CalendarViewModel.SelectedDayIndex))
        {
            ScheduleSelectionUpdate();
        }
    }

    private void ScheduleSelectionUpdate()
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(ApplySelection));
    }

    private void ApplySelection()
    {
        if (_viewModel == null)
            return;

        CalendarGrid.UpdateLayout();
        ClearAllSelections();

        foreach (DayCell dayCell in FindDescendants<DayCell>(CalendarGrid))
        {
            if (dayCell.DayData?.Date.Date == _viewModel.SelectedDate.Date)
            {
                dayCell.IsSelected = true;
                break;
            }
        }
    }

    private static IEnumerable<T> FindDescendants<T>(DependencyObject parent)
        where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match)
                yield return match;

            foreach (T descendant in FindDescendants<T>(child))
                yield return descendant;
        }
    }

    private void UpdateWeekNumbers(IReadOnlyList<CalendarDay> days)
    {
        List<WeekNumberInfo> weekNumbers = new();
        for (int row = 0; row < 6; row++)
        {
            int index = row * 7;
            if (index >= days.Count)
                break;

            weekNumbers.Add(new WeekNumberInfo(ISOWeek.GetWeekOfYear(days[index].Date)));
        }

        WeekNumberGrid.ItemsSource = weekNumbers;
    }

    private static void OnShowWeekNumbersChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((MonthCalendar)d).ApplyWeekNumberVisibility();
    }

    private void ApplyWeekNumberVisibility()
    {
        bool visible = ShowWeekNumbers;
        GridLength width = visible ? new GridLength(30) : new GridLength(0);
        WeekNumberHeaderColumn.Width = width;
        WeekNumberGridColumn.Width = width;
        WeekNumberGrid.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    }

    private sealed record WeekNumberInfo(int Number)
    {
        public string ToolTip => $"第{Number}周";
    }

    private void OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        // 从点击位置向上查找 DayCell
        HitTestResult hitResult = VisualTreeHelper.HitTest(this, e.GetPosition(this));
        if (hitResult == null) return;

        DayCell? dayCell = FindAncestor<DayCell>(hitResult.VisualHit);
        if (dayCell?.DayData is { } dayData && DataContext is CalendarViewModel vm)
        {
            // 清除所有选中状态
            ClearAllSelections();

            // 选中当前
            dayCell.IsSelected = true;

            // 更新 ViewModel
            vm.SelectDate(dayData.Date);
            DateClicked?.Invoke(dayData);
        }
    }

    private static T? FindAncestor<T>(DependencyObject current) where T : DependencyObject
    {
        while (current != null)
        {
            if (current is T ancestor)
                return ancestor;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    /// <summary>
    /// 清除所有 DayCell 的选中状态
    /// </summary>
    private void ClearAllSelections()
    {
        ClearAllSelectionsRecursive(CalendarGrid);
    }

    private void ClearAllSelectionsRecursive(DependencyObject parent)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, i);
            if (child is DayCell dayCell)
            {
                dayCell.IsSelected = false;
            }
            else
            {
                ClearAllSelectionsRecursive(child);
            }
        }
    }
}