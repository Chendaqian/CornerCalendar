using CornerCalendar.Core.Helpers;
using Xunit;

namespace CornerCalendar.Tests;

public class LunarCalendarHelperTests
{
    [Theory]
    [InlineData(2026, 2, 17, "春节")]
    [InlineData(2025, 1, 29, "春节")]
    [InlineData(2024, 2, 10, "春节")]
    [InlineData(2026, 3, 3, "元宵节")]   // 春节 + 14 天 = 正月十五
    [InlineData(2025, 10, 6, "中秋节")]
    [InlineData(2026, 6, 19, "端午节")]
    public void 传统节日显示节日名(int year, int month, int day, string expected)
    {
        Assert.Equal(expected, LunarCalendarHelper.GetLunarDateText(new DateTime(year, month, day)));
    }

    [Fact]
    public void 普通日期返回非空文本()
    {
        string text = LunarCalendarHelper.GetLunarDateText(new DateTime(2026, 8, 18));
        Assert.False(string.IsNullOrWhiteSpace(text));
    }

    [Fact]
    public void 超出支持范围返回空字符串()
    {
        Assert.Equal(string.Empty, LunarCalendarHelper.GetLunarDateText(new DateTime(1899, 1, 1)));
    }
}