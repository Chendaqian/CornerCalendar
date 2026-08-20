using CornerCalendar.Core.Models;
using CornerCalendar.Core.Services;
using Xunit;

namespace CornerCalendar.Tests;

public class ChinaCalendarServiceTests
{
    [Fact]
    public void 解析放假和补班事件标题()
    {
        Dictionary<DateTime, ChinaCalendarService.ChinaCalendarDayInfoBuilder> builders = new();
        DateTime holidayDate = new(2026, 10, 1);
        DateTime workdayDate = new(2026, 9, 27);

        ChinaCalendarService.AddHolidayInfo(builders, new List<CalendarEvent>
        {
            new("「国庆节 假期」 第1天/共7天", holidayDate, holidayDate.AddDays(1), true, "test", "#000000"),
            new("「中秋节 补班」 第1天/共1天", workdayDate, workdayDate.AddHours(9), false, "test", "#000000")
        });

        Assert.Equal("国庆节", builders[holidayDate].Build().LegalHoliday);
        Assert.Equal("中秋节补班", builders[workdayDate].Build().LegalHoliday);
        Assert.True(builders[workdayDate].Build().IsWorkday);
    }

    [Fact]
    public void 节假日名称去掉地域和类型冗余字样()
    {
        Dictionary<DateTime, ChinaCalendarService.ChinaCalendarDayInfoBuilder> builders = new();
        DateTime date = new(2026, 10, 1);

        ChinaCalendarService.AddHolidayInfo(builders, new List<CalendarEvent>
        {
            new("「中华人民共和国国庆节 法定节假日」 第1天/共1天", date, date.AddDays(1), true, "test", "#000000")
        });

        Assert.Equal("国庆节", builders[date].Build().LegalHoliday);
    }

    [Fact]
    public void 解析农历节日和节气标题()
    {
        Dictionary<DateTime, ChinaCalendarService.ChinaCalendarDayInfoBuilder> builders = new();
        DateTime date = new(2026, 8, 19);

        ChinaCalendarService.AddFestivalInfo(builders, new List<CalendarEvent>
        {
            new("『七夕』", date, date.AddDays(1), true, "test", "#000000")
        });
        ChinaCalendarService.AddSolarTermInfo(builders, new List<CalendarEvent>
        {
            new("『立秋』", date, date.AddDays(1), true, "test", "#000000")
        });
        ChinaCalendarService.AddLunarInfo(builders, new List<CalendarEvent>
        {
            new("『七月 初七 2026年』", date, date.AddDays(1), true, "test", "#000000")
        });

        ChinaCalendarDayInfo info = builders[date].Build();
        Assert.Equal("七夕", info.LunarFestival);
        Assert.Equal("立秋", info.SolarTerm);
        Assert.Equal("七月 初七", info.LunarDate);
    }

    [Fact]
    public void 解析农历订阅描述中的宜忌()
    {
        (string suitable, string avoid) = ChinaCalendarService.ParseSuitableAvoid(
            "宜：破屋 坏垣 治病 余事勿取\n忌：祈福 纳采 订盟 嫁娶 入宅 安葬");

        Assert.Equal("破屋、坏垣、治病、余事勿取", suitable);
        Assert.Equal("祈福、纳采、订盟、嫁娶、入宅、安葬", avoid);
    }

    [Fact]
    public void 解析ICS转义换行和馀字()
    {
        (string suitable, string avoid) = ChinaCalendarService.ParseSuitableAvoid(
            "宜：治病 馀事勿取\\n忌：安葬");

        Assert.Equal("治病、余事勿取", suitable);
        Assert.Equal("安葬", avoid);
    }

    [Fact]
    public void 日程标题去掉地域和范围前缀()
    {
        DateTime date = new(2026, 5, 12);
        CalendarEvent calendarEvent = new(
            "「中国全国国际世界护士节」",
            date,
            date.AddDays(1),
            true,
            "中国日历-节日纪念日",
            "#000000");

        CalendarEvent cleaned = ChinaCalendarService.CleanEventTitle(calendarEvent);

        Assert.Equal("「护士节」", cleaned.Title);
    }
}