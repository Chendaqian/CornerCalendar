using CornerCalendar.Core.Models;

namespace CornerCalendar.Core.Services;

/// <summary>
/// 查询已导入的本地森迭代，并计算阶段汇总。
/// </summary>
public sealed class SenScheduleService
{
    private readonly IReadOnlyList<SenScheduleIteration> _iterations;

    public SenScheduleService(IEnumerable<SenScheduleIteration>? iterations)
    {
        _iterations = (iterations ?? Enumerable.Empty<SenScheduleIteration>())
            .Where(iteration => iteration is not null
                && iteration.IsEnabled
                && iteration.Activities.Count > 0)
            .ToList();
    }

    public IReadOnlyList<SenScheduleIteration> Iterations => _iterations;

    public List<SenScheduleOccurrence> GetOccurrences(DateTime start, DateTime end)
    {
        DateTime startDate = start.Date;
        DateTime endExclusive = end.Date;
        List<SenScheduleOccurrence> occurrences = new();

        foreach (SenScheduleIteration iteration in _iterations)
        {
            Dictionary<string, int> phaseTotals = CalculatePhaseTotals(iteration);
            Dictionary<string, int> phaseWorkDays = CalculatePhaseWorkDays(iteration);
            foreach (SenScheduleActivity activity in iteration.Activities)
            {
                if (activity.EndDate.Date < startDate || activity.StartDate.Date >= endExclusive)
                    continue;

                occurrences.Add(SenScheduleRules.CreateOccurrence(
                    iteration,
                    activity,
                    phaseTotals,
                    phaseWorkDays));
            }
        }

        return occurrences
            .OrderBy(occurrence => occurrence.StartDate)
            .ThenBy(occurrence => occurrence.IterationName, StringComparer.Ordinal)
            .ThenBy(occurrence => occurrence.Sequence)
            .ToList();
    }

    public static Dictionary<string, int> CalculatePhaseTotals(SenScheduleIteration iteration)
    {
        Dictionary<string, HashSet<DateTime>> datesByPhase = new(StringComparer.Ordinal);
        foreach (SenScheduleActivity activity in iteration.Activities)
        {
            string? phaseId = activity.Sequence switch
            {
                1 => "TR1",
                2 => "TR2",
                3 => "TR3",
                4 => "TR4",
                6 or 7 or 8 or 9 => "TR5",
                _ => null
            };
            if (phaseId is null)
                continue;

            if (!datesByPhase.TryGetValue(phaseId, out HashSet<DateTime>? dates))
            {
                dates = new HashSet<DateTime>();
                datesByPhase[phaseId] = dates;
            }

            for (DateTime date = activity.StartDate.Date; date <= activity.EndDate.Date; date = date.AddDays(1))
                dates.Add(date);
        }

        return datesByPhase.ToDictionary(pair => pair.Key, pair => pair.Value.Count, StringComparer.Ordinal);
    }

    public static SenScheduleOccurrence? SelectPrimary(
        IEnumerable<SenScheduleOccurrence> occurrences,
        DateTime date)
    {
        return occurrences
            .Where(occurrence => SenScheduleRules.IsInRange(occurrence, date))
            .OrderByDescending(occurrence => occurrence.EndBadge is not null && occurrence.EndDate.Date == date.Date)
            .ThenBy(occurrence => occurrence.Sequence)
            .ThenBy(occurrence => occurrence.IterationName, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    public static void ApplyChinaWorkdays(
        IEnumerable<SenScheduleOccurrence> occurrences,
        IReadOnlyDictionary<DateTime, ChinaCalendarDayInfo> chinaCalendarInfo)
    {
        List<SenScheduleOccurrence> items = occurrences.ToList();
        foreach (SenScheduleOccurrence occurrence in items)
        {
            occurrence.WorkDays = CountWorkdays(
                occurrence.StartDate,
                occurrence.EndDate,
                chinaCalendarInfo);
        }

        foreach (IGrouping<(string IterationId, string PhaseId), SenScheduleOccurrence> group in items
                     .Where(occurrence => occurrence.PhaseId is not null)
                     .GroupBy(occurrence => (occurrence.IterationId, occurrence.PhaseId!),
                         EqualityComparer<(string, string)>.Default))
        {
            HashSet<DateTime> dates = new();
            foreach (SenScheduleOccurrence occurrence in group)
            {
                for (DateTime date = occurrence.StartDate.Date;
                     date <= occurrence.EndDate.Date;
                     date = date.AddDays(1))
                {
                    dates.Add(date);
                }
            }

            int phaseWorkDays = dates.Count(date => IsWorkday(date, chinaCalendarInfo));
            foreach (SenScheduleOccurrence occurrence in group)
                occurrence.PhaseWorkDays = phaseWorkDays;
        }
    }

    private static Dictionary<string, int> CalculatePhaseWorkDays(SenScheduleIteration iteration)
    {
        Dictionary<string, HashSet<DateTime>> datesByPhase = new(StringComparer.Ordinal);
        foreach (SenScheduleActivity activity in iteration.Activities)
        {
            string? phaseId = activity.Sequence switch
            {
                1 => "TR1",
                2 => "TR2",
                3 => "TR3",
                4 => "TR4",
                6 or 7 or 8 or 9 => "TR5",
                _ => null
            };
            if (phaseId is null)
                continue;

            if (!datesByPhase.TryGetValue(phaseId, out HashSet<DateTime>? dates))
            {
                dates = new HashSet<DateTime>();
                datesByPhase[phaseId] = dates;
            }

            for (DateTime date = activity.StartDate.Date; date <= activity.EndDate.Date; date = date.AddDays(1))
            {
                if (date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
                    dates.Add(date);
            }
        }

        return datesByPhase.ToDictionary(pair => pair.Key, pair => pair.Value.Count, StringComparer.Ordinal);
    }

    private static int CountWorkdays(
        DateTime startDate,
        DateTime endDate,
        IReadOnlyDictionary<DateTime, ChinaCalendarDayInfo> chinaCalendarInfo)
    {
        int count = 0;
        for (DateTime date = startDate.Date; date <= endDate.Date; date = date.AddDays(1))
        {
            if (IsWorkday(date, chinaCalendarInfo))
                count++;
        }

        return count;
    }

    private static bool IsWorkday(
        DateTime date,
        IReadOnlyDictionary<DateTime, ChinaCalendarDayInfo> chinaCalendarInfo)
    {
        if (chinaCalendarInfo.TryGetValue(date.Date, out ChinaCalendarDayInfo? info))
        {
            if (info.IsWorkday)
                return true;
            if (!string.IsNullOrWhiteSpace(info.LegalHoliday))
                return false;
        }

        return date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday);
    }
}