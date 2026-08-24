namespace CornerCalendar.Core.Models;

/// <summary>
/// 一行森日程活动。
/// </summary>
public sealed class SenScheduleActivity
{
    public int Sequence { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Owner { get; set; } = string.Empty;

    public int? WorkloadDays { get; set; }

    public DateTime StartDate { get; set; }

    public DateTime EndDate { get; set; }

    public int NaturalDays => (EndDate.Date - StartDate.Date).Days + 1;
}

/// <summary>
/// 一个工作表对应的项目迭代。
/// </summary>
public sealed class SenScheduleIteration
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = string.Empty;

    public bool IsEnabled { get; set; } = true;

    public List<SenScheduleActivity> Activities { get; set; } = new();

    public DateTime StartDate => Activities.Count == 0
        ? DateTime.MinValue
        : Activities.Min(activity => activity.StartDate).Date;

    public DateTime EndDate => Activities.Count == 0
        ? DateTime.MinValue
        : Activities.Max(activity => activity.EndDate).Date;
}

/// <summary>
/// 森日程活动在某个日期范围内的渲染数据。
/// </summary>
public sealed class SenScheduleOccurrence
{
    public string IterationId { get; init; } = string.Empty;

    public string IterationName { get; init; } = string.Empty;

    public int Sequence { get; init; }

    public string Title { get; init; } = string.Empty;

    public int? WorkloadDays { get; init; }

    public DateTime StartDate { get; init; }

    public DateTime EndDate { get; init; }

    public string? PhaseId { get; init; }

    public string? PhaseName { get; init; }

    public int? PhaseTotalDays { get; init; }

    public int? PhaseWorkDays { get; set; }

    public string? CircleColorKey { get; init; }

    public string? BaseBadge { get; init; }

    public string? BaseBadgeName { get; init; }

    public string? BaseBadgeColorKey { get; init; }

    public string? EndBadge { get; init; }

    public string? EndBadgeName { get; init; }

    public string? EndBadgeColorKey { get; init; }

    public int NaturalDays => (EndDate.Date - StartDate.Date).Days + 1;

    public int WorkDays { get; set; }
}