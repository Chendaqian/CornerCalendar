using CornerCalendar.Core.Helpers;
using Xunit;

namespace CornerCalendar.Tests;

public class TaskbarClockFormatterTests
{
    [Fact]
    public void 支持DateTime格式并将字面量换行转换为实际换行()
    {
        DateTime dateTime = new(2026, 8, 18, 13, 5, 0);

        string result = TaskbarClockFormatter.Format(dateTime, "HH:mm\\nyyyy/MM/dd");

        Assert.Equal($"13:05{Environment.NewLine}2026/08/18", result);
    }

    [Fact]
    public void 空格式使用默认格式()
    {
        DateTime dateTime = new(2026, 8, 18, 13, 5, 0);

        string result = TaskbarClockFormatter.Format(dateTime, " ");

        Assert.Equal($"13:05:00{Environment.NewLine}2026/08/18", result);
    }
}