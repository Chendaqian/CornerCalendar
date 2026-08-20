using CornerCalendar.Core.Helpers;
using Xunit;

namespace CornerCalendar.Tests;

public class ChinaCalendarHelperTests
{
    [Theory]
    [InlineData(2024, 3, 20, "春分")]
    [InlineData(2024, 4, 4, "清明")]
    [InlineData(2025, 7, 7, "小暑")]
    [InlineData(2026, 2, 4, "立春")]
    [InlineData(2026, 6, 21, "夏至")]
    public void 返回对应的二十四节气(int year, int month, int day, string expected)
    {
        Assert.Equal(expected, ChinaCalendarHelper.GetSolarTerm(new DateTime(year, month, day)));
    }

    [Theory]
    [InlineData(2026, 1, 1, "元旦")]
    [InlineData(2026, 2, 17, "春节")]
    [InlineData(2025, 5, 31, "端午节")]
    [InlineData(2025, 10, 1, "国庆节")]
    public void 返回中国大陆法定节假日(int year, int month, int day, string expected)
    {
        Assert.Equal(expected, ChinaCalendarHelper.GetLegalHoliday(new DateTime(year, month, day)));
    }

    [Fact]
    public void 清明节不会重复标记节气和法定节日()
    {
        DateTime date = new(2026, 4, 5);

        Assert.Equal("清明", ChinaCalendarHelper.GetSolarTerm(date));
        Assert.Equal("清明节", ChinaCalendarHelper.GetLegalHoliday(date));
    }

    [Fact]
    public void 黄历宜忌使用公开规则库计算()
    {
        (string suitable, string avoid) = HuangLiHelper.GetDayYiJi(new DateTime(2026, 8, 29));

        Assert.Contains("祭祀", suitable);
        Assert.Contains("平治道涂", suitable);
        Assert.Contains("嫁娶", avoid);
        Assert.Contains("安葬", avoid);
    }
}