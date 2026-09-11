using CornerCalendar.Core.Services;
using System.Text.Json;
using System.Text.Json.Serialization;
using Xunit;

namespace CornerCalendar.Tests;

public class AppSettingsTests
{
    // 与 AppSettings 内部序列化配置一致（枚举序列化为字符串）
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() }
    };

    [Fact]
    public void 新建设置默认值来自内置默认配置文件()
    {
        AppSettings settings = AppSettings.CreateDefaults();

        Assert.Equal("rubber-duck-frames", settings.RunnerName);
        Assert.True(settings.SenScheduleEnabled);   // 属性初始化器为 false，只有内置 default-settings.json 能提供 true
        Assert.Equal(0, settings.HistoryMaxItems);
        Assert.Empty(settings.SenSchedules);        // 迭代不内置，首次启动从 SenOnlineUrl 下载
    }

    [Fact]
    public void 新建设置默认显示森日程阶段圆圈()
    {
        AppSettings settings = AppSettings.CreateDefaults();

        Assert.True(settings.ShowSenPhaseCircles);
    }

    [Fact]
    public void 新建设置默认森日程在线地址指向Gitee仓库()
    {
        AppSettings settings = AppSettings.CreateDefaults();

        Assert.Equal(
            "https://raw.giteeusercontent.com/LuckBUBU/CornerCalendar/raw/master",
            settings.SenOnlineUrl);
    }

    [Fact]
    public void 旧配置中的任务栏时间格式字段被忽略()
    {
        // 旧版 settings.json 含 TaskbarTimeFormat 字段：加载时应静默忽略、其余字段正常读取
        const string legacyJson = """
        {
            "ThemeMode": "Dark",
            "TaskbarTimeFormat": "HH:mm:ss\\nyyyy/MM/dd",
            "RunnerName": "frog-frames"
        }
        """;

        AppSettings? settings = JsonSerializer.Deserialize<AppSettings>(legacyJson, JsonOptions);

        Assert.NotNull(settings);
        Assert.Equal(ThemeMode.Dark, settings!.ThemeMode);
        Assert.Equal("frog-frames", settings.RunnerName);
        Assert.DoesNotContain(
            "TaskbarTimeFormat",
            typeof(AppSettings).GetProperties().Select(property => property.Name));
    }

    [Fact]
    public void 缺少跑者字段的旧配置回退默认值()
    {
        const string legacyJson = """
        {
            "ThemeMode": "Light"
        }
        """;

        AppSettings? settings = JsonSerializer.Deserialize<AppSettings>(legacyJson, JsonOptions);

        Assert.NotNull(settings);
        Assert.Equal("cat", settings!.RunnerName);
    }
}