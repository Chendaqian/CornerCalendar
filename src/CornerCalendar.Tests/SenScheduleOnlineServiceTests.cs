using CornerCalendar.Core.Models;
using CornerCalendar.Core.Services;
using Xunit;

namespace CornerCalendar.Tests;

public class SenScheduleOnlineServiceTests
{
    [Fact]
    public void 清单解析出名称路径与启用状态()
    {
        const string yaml = """
            version: 1
            iterations:
              - name: "2.27"
                path: sen-schedule/2.27.md
                enabled: true
              - name: "2.26"
                path: sen-schedule/2.26.md
            """;

        SenScheduleOnlineService.Manifest manifest = SenScheduleOnlineService.ParseManifest(yaml);

        Assert.Equal(1, manifest.Version);
        Assert.Equal(2, manifest.Iterations.Count);
        Assert.Equal("2.27", manifest.Iterations[0].Name);
        Assert.Equal("sen-schedule/2.27.md", manifest.Iterations[0].FilePath);
        Assert.True(manifest.Iterations[0].Enabled);
        Assert.True(manifest.Iterations[1].Enabled);   // enabled 缺省为 true
    }

    [Fact]
    public void 清单为空或缺路径或格式错误被拒绝()
    {
        Assert.Throws<FormatException>(() => SenScheduleOnlineService.ParseManifest(""));
        Assert.Throws<FormatException>(() =>
            SenScheduleOnlineService.ParseManifest("iterations: 不是列表"));
        Assert.Throws<FormatException>(() => SenScheduleOnlineService.ParseManifest("""
            version: 1
            iterations:
              - name: "2.27"
            """));
    }

    [Fact]
    public void 根地址补齐尾斜杠并定位清单()
    {
        Uri manifest = SenScheduleOnlineService.BuildManifestUri(
            "https://raw.giteeusercontent.com/LuckBUBU/CornerCalendar/raw/master");
        Assert.Equal(
            "https://raw.giteeusercontent.com/LuckBUBU/CornerCalendar/raw/master/manifest.yaml",
            manifest.AbsoluteUri);

        Uri withSlash = SenScheduleOnlineService.BuildManifestUri("https://example.com/data/");
        Assert.Equal("https://example.com/data/manifest.yaml", withSlash.AbsoluteUri);

        Assert.Throws<FormatException>(() => SenScheduleOnlineService.BuildManifestUri("不是地址"));
        Assert.Throws<FormatException>(() => SenScheduleOnlineService.BuildManifestUri("  "));
    }

    [Fact]
    public void 迭代相对路径按清单所在目录解析且转义中文()
    {
        Uri manifest = new("https://example.com/raw/master/manifest.yaml");

        Uri iteration = SenScheduleOnlineService.ResolveIterationUrl(
            manifest, "sen-schedule/2.27.md");
        Assert.Equal(
            "https://example.com/raw/master/sen-schedule/2.27.md",
            iteration.AbsoluteUri);

        Uri chinese = SenScheduleOnlineService.ResolveIterationUrl(
            manifest, "sen-schedule/v2.27迭代.md");
        Assert.StartsWith(
            "https://example.com/raw/master/sen-schedule/",
            chinese.AbsoluteUri,
            StringComparison.Ordinal);
        Assert.DoesNotContain("迭代", chinese.AbsoluteUri);   // 中文段已百分号转义
    }

    [Fact]
    public void 合并保留本地Id与眼睛开关并覆盖同名活动()
    {
        SenScheduleIteration local = new()
        {
            Id = "local-id",
            Name = "2.27",
            IsEnabled = false,
            Activities =
            {
                new SenScheduleActivity
                {
                    Sequence = 1,
                    Title = "旧活动",
                    StartDate = new DateTime(2026, 1, 1),
                    EndDate = new DateTime(2026, 1, 2)
                }
            }
        };
        SenScheduleIteration localOnly = new() { Id = "keep", Name = "本地专属" };
        List<SenScheduleIteration> list = new() { local, localOnly };

        SenScheduleIteration online = SenScheduleParser.Parse("2.27", """
            | 序号 | 活动 | 工作量(天) | 开始时间 | 结束时间 |
            | --- | --- | --- | --- | --- |
            | 1 | 新活动 | 1 | 2026/2/1 | 2026/2/2 |
            """);
        SenScheduleIteration fresh = SenScheduleParser.Parse("2.28", """
            | 序号 | 活动 | 工作量(天) | 开始时间 | 结束时间 |
            | --- | --- | --- | --- | --- |
            | 1 | 上线 | 1 | 2026/3/1 | 2026/3/1 |
            """);

        SenScheduleOnlineService.MergeIterations(list, new[] { online, fresh });

        Assert.Equal(3, list.Count);
        Assert.Equal("2.28", list[0].Name);            // 最新在前：新迭代按活动日期排到最前且默认启用
        Assert.True(list[0].IsEnabled);
        Assert.Equal("2.27", list[1].Name);
        Assert.Equal("local-id", list[1].Id);          // 保留本地 Id
        Assert.False(list[1].IsEnabled);               // 保留本地眼睛开关
        Assert.Equal("新活动", list[1].Activities[0].Title);   // 活动被在线覆盖
        Assert.Equal("本地专属", list[2].Name);          // 无活动的本地迭代沉底
    }

    [Fact]
    public void 在线合并后迭代按最新在前排序()
    {
        SenScheduleIteration early = SenScheduleParser.Parse("2.26", """
            | 序号 | 活动 | 工作量(天) | 开始时间 | 结束时间 |
            | --- | --- | --- | --- | --- |
            | 1 | 需求 | 1 | 2026/2/26 | 2026/2/26 |
            """);
        SenScheduleIteration noActivity = new() { Name = "本地专属" };
        List<SenScheduleIteration> list = new() { early, noActivity };

        SenScheduleIteration late = SenScheduleParser.Parse("2.28", """
            | 序号 | 活动 | 工作量(天) | 开始时间 | 结束时间 |
            | --- | --- | --- | --- | --- |
            | 1 | 上线 | 1 | 2026/2/28 | 2026/2/28 |
            """);

        SenScheduleOnlineService.MergeIterations(list, new[] { late });

        Assert.Equal(
            new[] { "2.28", "2.26", "本地专属" },
            list.Select(item => item.Name));
    }

    [Fact]
    public void Markdown可选Owner列被读取()
    {
        SenScheduleIteration iteration = SenScheduleParser.Parse("2.26", """
            | 序号 | 活动 | 工作量(天) | 开始时间 | 结束时间 | Owner |
            | --- | --- | --- | --- | --- | --- |
            | 1 | 上线 | 1 | 2026/8/14 | 2026/8/14 | 测试主Leader |
            | 2 | 总结&成员表现评价 | - | 2026/8/18 | 2026/8/18 | 一条龙经理 |
            """);

        Assert.Equal(2, iteration.Activities.Count);
        Assert.Equal("测试主Leader", iteration.Activities[0].Owner);
        Assert.Null(iteration.Activities[1].WorkloadDays);
    }

    [Fact]
    public void 没有Owner列时保持为空()
    {
        SenScheduleIteration iteration = SenScheduleParser.Parse("2.27", """
            | 序号 | 活动 | 工作量(天) | 开始时间 | 结束时间 |
            | --- | --- | --- | --- | --- |
            | 1 | 上线 | 1 | 2026/8/14 | 2026/8/14 |
            """);

        Assert.Equal(string.Empty, iteration.Activities[0].Owner);
    }
}