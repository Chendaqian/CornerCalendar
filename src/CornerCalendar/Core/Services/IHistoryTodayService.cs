using CornerCalendar.Core.Models;

namespace CornerCalendar.Core.Services;

public interface IHistoryTodayService
{
    Task<IReadOnlyList<HistoryTodayItem>> GetAsync(
        DateOnly date,
        CancellationToken cancellationToken = default);
}