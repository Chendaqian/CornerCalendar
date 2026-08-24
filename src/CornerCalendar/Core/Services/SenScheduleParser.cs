using CornerCalendar.Core.Models;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace CornerCalendar.Core.Services;

/// <summary>
/// 解析设置页粘贴的五列表格 Markdown。
/// </summary>
public static class SenScheduleParser
{
    private static readonly string[] DateFormats =
    {
        "yyyy/M/d", "yyyy/M/dd", "yyyy/MM/d", "yyyy/MM/dd", "yyyy-MM-dd"
    };

    public static SenScheduleIteration Parse(
        string iterationName,
        string markdown,
        string? existingId = null)
    {
        if (string.IsNullOrWhiteSpace(iterationName))
            throw new FormatException("请输入迭代名称");

        if (string.IsNullOrWhiteSpace(markdown))
            throw new FormatException("请输入 Markdown 表格");

        string[] lines = markdown.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');

        int headerLineIndex = -1;
        Dictionary<string, int>? columns = null;
        for (int i = 0; i < lines.Length; i++)
        {
            List<string> cells = SplitRow(lines[i]);
            Dictionary<string, int> candidate = cells
                .Select((cell, index) => (Cell: cell.Trim(), Index: index))
                .Where(item => item.Cell.Length > 0)
                .GroupBy(item => item.Cell, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First().Index, StringComparer.Ordinal);

            if (candidate.ContainsKey("序号")
                && candidate.ContainsKey("活动")
                && candidate.ContainsKey("工作量(天)")
                && candidate.ContainsKey("开始时间")
                && candidate.ContainsKey("结束时间"))
            {
                headerLineIndex = i;
                columns = candidate;
                break;
            }
        }

        if (headerLineIndex < 0 || columns is null)
            throw new FormatException("未找到包含序号、活动、工作量(天)、开始时间、结束时间的 Markdown 表头");

        if (headerLineIndex + 1 >= lines.Length || !IsSeparatorRow(SplitRow(lines[headerLineIndex + 1])))
            throw new FormatException($"第 {headerLineIndex + 2} 行不是 Markdown 表格分隔线");

        List<SenScheduleActivity> activities = new();
        HashSet<int> sequences = new();
        for (int i = headerLineIndex + 2; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i]))
                continue;

            List<string> cells = SplitRow(lines[i]);
            if (cells.Count == 0 || cells.All(string.IsNullOrWhiteSpace))
                continue;
            if (cells.Count <= columns.Values.Max())
                throw new FormatException($"第 {i + 1} 行列数不足");

            string sequenceText = GetCell(cells, columns, "序号");
            if (!int.TryParse(sequenceText, NumberStyles.None, CultureInfo.InvariantCulture, out int sequence)
                || sequence <= 0)
            {
                throw new FormatException($"第 {i + 1} 行序号无效：{sequenceText}");
            }

            if (!sequences.Add(sequence))
                throw new FormatException($"第 {i + 1} 行序号重复：{sequence}");

            string title = GetCell(cells, columns, "活动");
            if (title.Length == 0)
                throw new FormatException($"第 {i + 1} 行活动名称为空");

            string workloadText = GetCell(cells, columns, "工作量(天)");
            int? workloadDays = null;
            if (workloadText != "-")
            {
                if (!int.TryParse(workloadText, NumberStyles.None, CultureInfo.InvariantCulture, out int parsedWorkload)
                    || parsedWorkload <= 0)
                {
                    throw new FormatException($"第 {i + 1} 行工作量无效：{workloadText}");
                }

                workloadDays = parsedWorkload;
            }

            DateTime startDate = ParseDate(GetCell(cells, columns, "开始时间"), i + 1);
            DateTime endDate = ParseDate(GetCell(cells, columns, "结束时间"), i + 1);
            if (endDate < startDate)
                throw new FormatException($"第 {i + 1} 行结束时间早于开始时间");

            activities.Add(new SenScheduleActivity
            {
                Sequence = sequence,
                Title = title,
                WorkloadDays = workloadDays,
                StartDate = startDate,
                EndDate = endDate
            });
        }

        if (activities.Count == 0)
            throw new FormatException("Markdown 表格没有有效活动行");

        return new SenScheduleIteration
        {
            Id = string.IsNullOrWhiteSpace(existingId)
                ? Guid.NewGuid().ToString("N")
                : existingId,
            Name = iterationName.Trim(),
            Activities = activities.OrderBy(activity => activity.Sequence).ToList()
        };
    }

    private static DateTime ParseDate(string value, int lineNumber)
    {
        if (DateTime.TryParseExact(
                value,
                DateFormats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out DateTime date))
        {
            return date.Date;
        }

        throw new FormatException($"第 {lineNumber} 行日期无效：{value}");
    }

    private static string GetCell(
        IReadOnlyList<string> cells,
        IReadOnlyDictionary<string, int> columns,
        string name)
        => cells[columns[name]].Trim();

    private static bool IsSeparatorRow(IReadOnlyList<string> cells)
        => cells.Count > 0
            && cells.All(cell => Regex.IsMatch(cell.Trim(), @"^:?-+:?$"));

    private static List<string> SplitRow(string line)
    {
        string value = line.Trim();
        if (value.StartsWith('|'))
            value = value[1..];
        if (value.EndsWith('|') && !value.EndsWith("\\|", StringComparison.Ordinal))
            value = value[..^1];

        List<string> cells = new();
        StringBuilder current = new();
        bool escaped = false;
        foreach (char character in value)
        {
            if (escaped)
            {
                current.Append(character);
                escaped = false;
                continue;
            }

            if (character == '\\')
            {
                escaped = true;
                continue;
            }

            if (character == '|')
            {
                cells.Add(current.ToString());
                current.Clear();
                continue;
            }

            current.Append(character);
        }

        if (escaped)
            current.Append('\\');
        cells.Add(current.ToString());
        return cells;
    }
}