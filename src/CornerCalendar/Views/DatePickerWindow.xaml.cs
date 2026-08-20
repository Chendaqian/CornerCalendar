using System.Windows;
using System.Windows.Controls;

namespace CornerCalendar.Views;

public sealed class DatePickerDay
{
    public DateTime? Date { get; }
    public string Text { get; }
    public bool IsCurrentMonth { get; }
    public bool IsSelected { get; }

    public DatePickerDay(DateTime? date, bool isCurrentMonth, bool isSelected)
    {
        Date = date;
        Text = date?.Day.ToString() ?? string.Empty;
        IsCurrentMonth = isCurrentMonth;
        IsSelected = isSelected;
    }
}

public sealed class DatePickerMonth
{
    public int Year { get; }
    public int Month { get; }
    public string MonthLabel => $"{Month}月";
    public IReadOnlyList<DatePickerDay> Days { get; }

    public DatePickerMonth(int year, int month, DateTime selectedDate)
    {
        Year = year;
        Month = month;

        DateTime firstDay = new(year, month, 1);
        int offset = ((int)firstDay.DayOfWeek + 6) % 7;
        List<DatePickerDay> days = new(42);
        for (int i = 0; i < 42; i++)
        {
            DateTime date = firstDay.AddDays(i - offset);
            bool isCurrentMonth = date.Month == month;
            days.Add(new DatePickerDay(
                isCurrentMonth ? date : null,
                isCurrentMonth,
                isCurrentMonth && date.Date == selectedDate.Date));
        }

        Days = days;
    }
}

public sealed class DatePickerYear
{
    public int Value { get; }
    public bool IsSelected { get; }

    public DatePickerYear(int value, bool isSelected)
    {
        Value = value;
        IsSelected = isSelected;
    }
}

public partial class DatePickerWindow : Window
{
    private int _selectedYear;
    private int _selectedMonth;
    private DateTime _selectedDate;

    public event Action<DateTime>? DateSelected;

    public DatePickerWindow(int year, int month, DateTime selectedDate)
    {
        _selectedYear = year;
        _selectedMonth = month;
        _selectedDate = selectedDate;

        InitializeComponent();
        ShowMonthView();
    }

    private void ShowMonthView()
    {
        YearButton.Content = $"{_selectedYear}年";
        MonthGrid.ItemsSource = Enumerable.Range(1, 12)
            .Select(month => new DatePickerMonth(_selectedYear, month, _selectedDate))
            .ToList();
        MonthGrid.Visibility = Visibility.Visible;
        YearGrid.Visibility = Visibility.Collapsed;
    }

    private void ShowYearView()
    {
        YearButton.Content = $"{_selectedYear}年";
        YearGrid.ItemsSource = Enumerable.Range(_selectedYear - 10, 20)
            .Select(year => new DatePickerYear(year, year == _selectedYear))
            .ToList();
        MonthGrid.Visibility = Visibility.Collapsed;
        YearGrid.Visibility = Visibility.Visible;
    }

    private void OnYearButtonClick(object sender, RoutedEventArgs e)
    {
        ShowYearView();
    }

    private void OnYearClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: DatePickerYear year })
        {
            SetSelectedYear(year.Value);
            ShowMonthView();
        }
    }

    private void OnPreviousYearClick(object sender, RoutedEventArgs e)
    {
        SetSelectedYear(_selectedYear - 1);
        ShowMonthView();
    }

    private void OnNextYearClick(object sender, RoutedEventArgs e)
    {
        SetSelectedYear(_selectedYear + 1);
        ShowMonthView();
    }

    private void SetSelectedYear(int year)
    {
        _selectedYear = year;
        int day = Math.Min(_selectedDate.Day, DateTime.DaysInMonth(year, _selectedMonth));
        _selectedDate = new DateTime(year, _selectedMonth, day);
    }

    private void OnMonthClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: DatePickerMonth month })
            SelectMonth(month.Year, month.Month);
    }

    private void OnDayClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: DatePickerDay { Date: DateTime date } })
            SelectDate(date);
    }

    private void SelectMonth(int year, int month)
    {
        int day = Math.Min(_selectedDate.Day, DateTime.DaysInMonth(year, month));
        SelectDate(new DateTime(year, month, day));
    }

    private void SelectDate(DateTime date)
    {
        _selectedDate = date.Date;
        _selectedYear = _selectedDate.Year;
        _selectedMonth = _selectedDate.Month;
        DateSelected?.Invoke(_selectedDate);
        Close();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        Close();
    }
}