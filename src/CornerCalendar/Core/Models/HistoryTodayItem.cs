namespace CornerCalendar.Core.Models;

/// <summary>
/// 历史上的今天条目。
/// </summary>
public sealed record HistoryTodayItem(
    int? Year,
    string Title,
    string Description,
    string Category,
    string? SourceTitle,
    string? SourceUrl);