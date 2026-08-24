using CornerCalendar.Core.Models;
using CornerCalendar.Core.Services;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Xunit;

namespace CornerCalendar.Tests;

public class HistoryTodayServiceTests
{
    [Fact]
    public void 请求地址使用公历月日()
    {
        string url = WikimediaHistoryTodayService.BuildRequestUrl(new DateOnly(2026, 8, 21));

        Assert.Equal(
            "https://api.wikimedia.org/feed/v1/wikipedia/zh/onthisday/events/08/21",
            url);
    }

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

    [Fact]
    public async Task 已有本地缓存时不请求网络()
    {
        string cacheDirectory = CreateCacheDirectory();
        try
        {
            List<HistoryTodayItem> cachedItems =
            [
                new(1900, "本地资料", "本地描述", "事件", "本地资料", "https://example.com/local")
            ];
            string cachePath = Path.Combine(cacheDirectory, "08-21.json");
            await File.WriteAllTextAsync(
                cachePath,
                JsonSerializer.Serialize(cachedItems),
                Encoding.UTF8);

            int requestCount = 0;
            using HttpClient client = new(new CountingHandler(() =>
            {
                requestCount++;
                throw new HttpRequestException("不应请求网络");
            }));
            WikimediaHistoryTodayService service = new(client, cacheDirectory);

            IReadOnlyList<HistoryTodayItem> items =
                await service.GetAsync(new DateOnly(2026, 8, 21));

            Assert.Single(items);
            Assert.Equal("本地资料", items[0].Title);
            Assert.Equal(0, requestCount);
        }
        finally
        {
            Directory.Delete(cacheDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task 没有本地缓存时请求网络并保存结果()
    {
        string cacheDirectory = CreateCacheDirectory();
        try
        {
            const string json = """
            {
              "events": [
                {
                  "year": 2020,
                  "text": "远程资料。",
                  "pages": [
                    {
                      "title": "远程资料"
                    }
                  ]
                }
              ]
            }
            """;
            int requestCount = 0;
            using HttpClient client = new(new CountingHandler(() =>
            {
                requestCount++;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                };
            }));
            WikimediaHistoryTodayService service = new(client, cacheDirectory);

            IReadOnlyList<HistoryTodayItem> items =
                await service.GetAsync(new DateOnly(2026, 8, 21));

            Assert.Single(items);
            Assert.Equal("远程资料", items[0].Title);
            Assert.Equal(1, requestCount);
            Assert.True(File.Exists(Path.Combine(cacheDirectory, "08-21.json")));
        }
        finally
        {
            Directory.Delete(cacheDirectory, recursive: true);
        }
    }

    private static string CreateCacheDirectory()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            "CornerCalendarTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class CountingHandler : HttpMessageHandler
    {
        private readonly Func<HttpResponseMessage> _responseFactory;

        public CountingHandler(Func<HttpResponseMessage> responseFactory)
        {
            _responseFactory = responseFactory;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => Task.FromResult(_responseFactory());
    }
}