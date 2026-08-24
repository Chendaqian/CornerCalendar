using CornerCalendar.Core.Services;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace CornerCalendar.Tests;

public class IcsCalendarServiceTests
{
    [Fact]
    public async Task 可以直接读取本地ICS文件()
    {
        string filePath = Path.Combine(
            Path.GetTempPath(),
            $"corner-calendar-{Guid.NewGuid():N}.ics");
        string cachePath = GetCachePath(filePath);
        const string ics = """
        BEGIN:VCALENDAR
        VERSION:2.0
        PRODID:-//CornerCalendar Tests//EN
        BEGIN:VEVENT
        UID:local-test-event@cornercalendar
        DTSTART;VALUE=DATE:20260822
        DTEND;VALUE=DATE:20260823
        SUMMARY:本地测试日程
        END:VEVENT
        END:VCALENDAR
        """;

        await File.WriteAllTextAsync(filePath, ics, Encoding.UTF8);
        IcsCalendarService service = new(filePath, 120, "本地测试", "#0078D4");
        try
        {
            List<CornerCalendar.Core.Models.CalendarEvent> events =
                await service.GetEventsAsync(
                    new DateTime(2026, 8, 22),
                    new DateTime(2026, 8, 23));

            Assert.Single(events);
            Assert.Equal("本地测试日程", events[0].Title);
        }
        finally
        {
            service.Dispose();
            File.Delete(filePath);
            File.Delete(cachePath);
        }
    }

    private static string GetCachePath(string filePath)
    {
        string hash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(filePath)))[..16];
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CornerCalendar",
            "cache",
            $"{hash}.ics");
    }
}