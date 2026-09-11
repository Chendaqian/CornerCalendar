using CornerCalendar.Core.Models;
using CornerCalendar.Core.Services;
using Xunit;

namespace CornerCalendar.Tests;

public class HistoryTodayServiceTests
{
    [Fact]
    public void 响应按分类解析历史条目()
    {
        const string json = """
        {
          "events": [
            {
              "year": 1969,
              "text": "阿波罗 11 号发射。",
              "pages": [
                {
                  "title": "阿波罗 11 号",
                  "content_urls": {
                    "desktop": {
                      "page": "https://zh.wikipedia.org/wiki/阿波罗11号"
                    }
                  }
                }
              ]
            }
          ],
          "births": [
            {
              "year": 1920,
              "text": "某人出生。",
              "pages": [
                {
                  "title": "某人"
                }
              ]
            }
          ]
        }
        """;

        IReadOnlyList<HistoryTodayItem> items =
            WikimediaHistoryTodayService.ParseResponse(json);

        Assert.Equal(2, items.Count);
        Assert.Equal("事件", items[0].Category);
        Assert.Equal(1969, items[0].Year);
        Assert.Equal("阿波罗 11 号", items[0].Title);
        Assert.Equal("https://zh.wikipedia.org/wiki/阿波罗11号", items[0].SourceUrl);
        Assert.Equal("出生", items[1].Category);
    }
}