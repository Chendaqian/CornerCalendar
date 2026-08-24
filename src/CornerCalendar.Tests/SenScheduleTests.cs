using CornerCalendar.Core.Models;
using CornerCalendar.Core.Services;
using CornerCalendar.ViewModels;
using Xunit;

namespace CornerCalendar.Tests;

public class SenScheduleTests
{
    private const string SampleMarkdown = """
    | 序号 | 活动 | 工作量(天) | 开始时间 | 结束时间 |
    |------|---------------|--------|------------|------------|
    | 1 | 需求立项评审(TR1) | - | 2026/7/2 | 2026/7/3 |
    | 2 | 需求概要设计评审(TR2) | - | 2026/7/14 | 2026/7/16 |
    | 3 | 技术架构设计评审(TR3) | - | 2026/7/31 | 2026/8/3 |
    | 4 | 需求详细设计评审(TR4) | - | 2026/8/10 | 2026/8/11 |
    | 5 | 需求宣讲 | 1 | 2026/8/19 | 2026/8/19 |
    | 6 | 需求分析与一页纸设计 | 3 | 2026/8/20 | 2026/8/24 |
    | 7 | 需求反讲 | 1 | 2026/8/25 | 2026/8/25 |
    | 8 | 需求反讲后一页纸调整 | 1 | 2026/8/26 | 2026/8/26 |
    | 9 | 一页纸设计评审(TR5) | 1 | 2026/8/27 | 2026/8/27 |
    | 10 | Coding&单元测试 | 17 | 2026/8/28 | 2026/9/20 |
    | 11 | 模块测试&集成测试 | 14 | 2026/9/21 | 2026/10/15 |
    | 12 | 上线 | 1 | 2026/10/16 | 2026/10/16 |
    | 13 | 上线问题处理 | 1 | 2026/10/19 | 2026/10/19 |
    | 14 | 总结&成员表现评价 | 1 | 2026/10/20 | 2026/10/20 |
    """;

    [Fact]
    public void 可以解析用户提供的十四行表格()
    {
        SenScheduleIteration iteration = SenScheduleParser.Parse("v2.24迭代", SampleMarkdown);

        Assert.Equal("v2.24迭代", iteration.Name);
        Assert.Equal(14, iteration.Activities.Count);
        Assert.Null(iteration.Activities[0].WorkloadDays);
        Assert.Equal(24, iteration.Activities[9].NaturalDays);
        Assert.Equal(25, iteration.Activities[10].NaturalDays);
        Assert.Equal(new DateTime(2026, 10, 20), iteration.EndDate);
    }

    [Fact]
    public void 无效行会报告行号且不返回半张表()
    {
        string invalid = SampleMarkdown.Replace("2026/9/20", "2026/8/20", StringComparison.Ordinal);

        FormatException exception = Assert.Throws<FormatException>(
            () => SenScheduleParser.Parse("v2.24迭代", invalid));

        Assert.Contains("第 12 行", exception.Message);
    }

    [Fact]
    public void 阶段映射和自然日汇总符合规则()
    {
        SenScheduleIteration iteration = SenScheduleParser.Parse("v2.24迭代", SampleMarkdown);
        SenScheduleService service = new(new[] { iteration });
        List<SenScheduleOccurrence> occurrences = service.GetOccurrences(
            new DateTime(2026, 8, 1),
            new DateTime(2026, 10, 1));

        SenScheduleOccurrence row5 = Assert.Single(occurrences, item => item.Sequence == 5);
        Assert.Null(row5.PhaseId);
        Assert.Equal(SenScheduleRules.Tr5CircleColorKey, row5.CircleColorKey);

        SenScheduleOccurrence row3 = Assert.Single(occurrences, item => item.Sequence == 3);
        Assert.Equal(4, row3.NaturalDays);
        Assert.Equal(2, row3.WorkDays);
        Assert.Equal(2, row3.PhaseWorkDays);

        SenScheduleOccurrence row7 = Assert.Single(occurrences, item => item.Sequence == 7);
        Assert.Equal("反", row7.BaseBadge);
        Assert.Equal(8, row7.PhaseTotalDays);

        SenScheduleOccurrence row10 = Assert.Single(occurrences, item => item.Sequence == 10);
        Assert.Equal(24, row10.NaturalDays);
        Assert.Equal(17, row10.WorkloadDays);

        (string? badge, string? name, _) = SenScheduleRules.GetBadge(row10, new DateTime(2026, 9, 20));
        Assert.Equal("提", badge);
        Assert.Equal("提测", name);
    }

    [Fact]
    public void 未知序号保留为普通森活动()
    {
        const string markdown = """
        | 序号 | 活动 | 工作量(天) | 开始时间 | 结束时间 |
        | --- | --- | --- | --- | --- |
        | 99 | 自定义活动 | - | 2026/8/1 | 2026/8/2 |
        """;

        SenScheduleIteration iteration = SenScheduleParser.Parse("自定义迭代", markdown);
        SenScheduleOccurrence occurrence = Assert.Single(new SenScheduleService(new[] { iteration })
            .GetOccurrences(new DateTime(2026, 8, 1), new DateTime(2026, 8, 3)));

        Assert.Null(occurrence.BaseBadge);
        Assert.Null(occurrence.CircleColorKey);
        Assert.Equal(2, occurrence.NaturalDays);
    }

    [Fact]
    public void 多个迭代同日合并并按规则选择主活动()
    {
        SenScheduleIteration first = SenScheduleParser.Parse("v2.24迭代", SampleMarkdown);
        SenScheduleIteration second = SenScheduleParser.Parse("v2.25迭代", SampleMarkdown);
        SenScheduleService service = new(new[] { first, second });

        List<SenScheduleOccurrence> occurrences = service.GetOccurrences(
            new DateTime(2026, 8, 25),
            new DateTime(2026, 8, 26));
        SenScheduleOccurrence? primary = SenScheduleService.SelectPrimary(
            occurrences,
            new DateTime(2026, 8, 25));

        Assert.Equal(2, occurrences.Count);
        Assert.NotNull(primary);
        Assert.Equal(7, primary!.Sequence);
        Assert.Equal("v2.24迭代", primary.IterationName);
    }

    [Fact]
    public void 删除迭代后查询结果不再包含该迭代()
    {
        SenScheduleIteration first = SenScheduleParser.Parse("v2.24迭代", SampleMarkdown);
        SenScheduleIteration second = SenScheduleParser.Parse("v2.25迭代", SampleMarkdown);
        List<SenScheduleIteration> schedules = new() { first, second };

        schedules.Remove(first);
        SenScheduleService service = new(schedules);
        List<SenScheduleOccurrence> occurrences = service.GetOccurrences(
            new DateTime(2026, 8, 25),
            new DateTime(2026, 8, 26));

        Assert.All(occurrences, occurrence => Assert.Equal("v2.25迭代", occurrence.IterationName));
    }

    [Fact]
    public void 隐藏迭代不产生日历活动()
    {
        SenScheduleIteration iteration = SenScheduleParser.Parse("v2.24迭代", SampleMarkdown);
        iteration.IsEnabled = false;

        SenScheduleService service = new(new[] { iteration });

        Assert.Empty(service.GetOccurrences(
            new DateTime(2026, 8, 25),
            new DateTime(2026, 8, 26)));
    }

    [Fact]
    public void 新设置默认关闭森日程且没有导入迭代()
    {
        AppSettings settings = AppSettings.CreateDefaults();

        Assert.False(settings.SenScheduleEnabled);
        Assert.Empty(settings.SenSchedules);
    }

    [Fact]
    public async Task ViewModel关闭森开关时不合并森事件开启后合并()
    {
        AppSettings settings = AppSettings.Current;
        bool previousEnabled = settings.SenScheduleEnabled;
        List<SenScheduleIteration> previousSchedules = settings.SenSchedules;
        SenScheduleIteration iteration = SenScheduleParser.Parse("测试迭代", SampleMarkdown);
        try
        {
            settings.SenSchedules = new List<SenScheduleIteration> { iteration };
            settings.SenScheduleEnabled = false;
            using CalendarViewModel viewModel = new(new EmptyCalendarService(), new SenScheduleService(settings.SenSchedules));

            await viewModel.RefreshDataAsync();
            Assert.Empty(viewModel.CalendarDays
                .Where(day => day.Date.Date >= new DateTime(2026, 8, 19)
                    && day.Date.Date <= new DateTime(2026, 8, 27))
                .SelectMany(day => day.SenEvents ?? Array.Empty<SenScheduleOccurrence>()));

            settings.SenScheduleEnabled = true;
            await viewModel.RefreshDataAsync();
            viewModel.NavigateToDate(new DateTime(2026, 8, 25));
            await viewModel.RefreshDataAsync();
            Assert.NotEmpty(viewModel.CalendarDays
                .Where(day => day.Date.Date == new DateTime(2026, 8, 25))
                .SelectMany(day => day.SenEvents ?? Array.Empty<SenScheduleOccurrence>()));
        }
        finally
        {
            settings.SenScheduleEnabled = previousEnabled;
            settings.SenSchedules = previousSchedules;
        }
    }
}