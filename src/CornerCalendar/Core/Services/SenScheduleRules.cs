using CornerCalendar.Core.Models;

namespace CornerCalendar.Core.Services;

/// <summary>
/// 标准森迭代的序号映射。规则只按表格序号工作，不从活动标题猜测。
/// </summary>
public static class SenScheduleRules
{
    public const string Tr1CircleColorKey = "SenCircleTr1Brush";
    public const string Tr2CircleColorKey = "SenCircleTr2Brush";
    public const string Tr3CircleColorKey = "SenCircleTr3Brush";
    public const string Tr4CircleColorKey = "SenCircleTr4Brush";
    public const string Tr5CircleColorKey = "SenCircleTr5Brush";
    public const string CodingCircleColorKey = "SenCircleCodingBrush";
    public const string TestingCircleColorKey = "SenCircleTestingBrush";
    public const string ReleaseCircleColorKey = "SenCircleReleaseBrush";

    private sealed record Rule(
        string? PhaseId,
        string? PhaseName,
        string? CircleColorKey,
        string? BaseBadge,
        string? BaseBadgeName,
        string? BaseBadgeColorKey,
        string? EndBadge,
        string? EndBadgeName,
        string? EndBadgeColorKey);

    public static SenScheduleOccurrence CreateOccurrence(
        SenScheduleIteration iteration,
        SenScheduleActivity activity,
        IReadOnlyDictionary<string, int> phaseTotals,
        IReadOnlyDictionary<string, int> phaseWorkDays)
    {
        Rule rule = GetRule(activity.Sequence);
        int? phaseTotal = rule.PhaseId is not null
            && phaseTotals.TryGetValue(rule.PhaseId, out int total)
                ? total
                : null;
        int phaseWorkdayTotal = rule.PhaseId is not null
            && phaseWorkDays.TryGetValue(rule.PhaseId, out int workdayTotal)
                ? workdayTotal
                : 0;

        return new SenScheduleOccurrence
        {
            IterationId = iteration.Id,
            IterationName = iteration.Name,
            Sequence = activity.Sequence,
            Title = activity.Title,
            WorkloadDays = activity.WorkloadDays,
            StartDate = activity.StartDate.Date,
            EndDate = activity.EndDate.Date,
            PhaseId = rule.PhaseId,
            PhaseName = rule.PhaseName,
            PhaseTotalDays = phaseTotal,
            PhaseWorkDays = rule.PhaseId is null ? null : phaseWorkdayTotal,
            CircleColorKey = rule.CircleColorKey,
            BaseBadge = rule.BaseBadge,
            BaseBadgeName = rule.BaseBadgeName,
            BaseBadgeColorKey = rule.BaseBadgeColorKey,
            EndBadge = rule.EndBadge,
            EndBadgeName = rule.EndBadgeName,
            EndBadgeColorKey = rule.EndBadgeColorKey,
            WorkDays = CalculateWeekdays(activity.StartDate, activity.EndDate)
        };
    }

    public static (string? Badge, string? Name, string? ColorKey) GetBadge(
        SenScheduleOccurrence occurrence,
        DateTime date)
    {
        if (occurrence.EndBadge is not null && date.Date == occurrence.EndDate.Date)
        {
            return (occurrence.EndBadge, occurrence.EndBadgeName, occurrence.EndBadgeColorKey);
        }

        return (occurrence.BaseBadge, occurrence.BaseBadgeName, occurrence.BaseBadgeColorKey);
    }

    public static bool IsInRange(SenScheduleOccurrence occurrence, DateTime date)
        => date.Date >= occurrence.StartDate.Date && date.Date <= occurrence.EndDate.Date;

    private static Rule GetRule(int sequence)
        => sequence switch
        {
            1 => new("TR1", "需求立项评审", Tr1CircleColorKey, "R1", "需求立项评审", Tr1CircleColorKey, null, null, null),
            2 => new("TR2", "需求概要设计评审", Tr2CircleColorKey, "R2", "需求概要设计评审", Tr2CircleColorKey, null, null, null),
            3 => new("TR3", "技术架构设计评审", Tr3CircleColorKey, "R3", "技术架构设计评审", Tr3CircleColorKey, null, null, null),
            4 => new("TR4", "需求详细设计评审", Tr4CircleColorKey, "R4", "需求详细设计评审", Tr4CircleColorKey, null, null, null),
            5 => new(null, null, Tr5CircleColorKey, "宣", "宣讲", "SenBadgeBriefingBrush", null, null, null),
            6 => new("TR5", "一页纸设计评审", Tr5CircleColorKey, "R5", "一页纸设计评审", Tr5CircleColorKey, null, null, null),
            7 => new("TR5", "一页纸设计评审", Tr5CircleColorKey, "反", "反讲", "SenBadgeReverseBrush", null, null, null),
            8 => new("TR5", "一页纸设计评审", Tr5CircleColorKey, "R5", "一页纸设计评审", Tr5CircleColorKey, null, null, null),
            9 => new("TR5", "一页纸设计评审", Tr5CircleColorKey, "R5", "一页纸设计评审", Tr5CircleColorKey, null, null, null),
            10 => new(null, null, CodingCircleColorKey, "编", "编码", CodingCircleColorKey, "提", "提测", "SenBadgeSubmitBrush"),
            11 => new(null, null, TestingCircleColorKey, "测", "测试", TestingCircleColorKey, null, null, null),
            12 => new(null, null, ReleaseCircleColorKey, "上", "上线", ReleaseCircleColorKey, null, null, null),
            13 => new(null, null, ReleaseCircleColorKey, "巡", "巡检", "SenBadgePatrolBrush", null, null, null),
            14 => new(null, null, ReleaseCircleColorKey, "总", "总结", "SenBadgeSummaryBrush", null, null, null),
            _ => new(null, null, null, null, null, null, null, null, null)
        };

    public static string? GetCircleColorKey(int sequence)
        => GetRule(sequence).CircleColorKey;

    private static int CalculateWeekdays(DateTime startDate, DateTime endDate)
    {
        int count = 0;
        for (DateTime date = startDate.Date; date <= endDate.Date; date = date.AddDays(1))
        {
            if (date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
                count++;
        }

        return count;
    }
}