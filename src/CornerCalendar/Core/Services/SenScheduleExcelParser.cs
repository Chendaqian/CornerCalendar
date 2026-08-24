using CornerCalendar.Core.Models;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;

namespace CornerCalendar.Core.Services;

/// <summary>
/// 读取森日程 Excel 工作簿。每个工作表对应一个迭代，工作表名称作为迭代名称。
/// </summary>
public static class SenScheduleExcelParser
{
    private const string SpreadsheetNamespace = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string RelationshipNamespace = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string PackageRelationshipNamespace = "http://schemas.openxmlformats.org/package/2006/relationships";

    private static readonly string[] DateFormats =
    {
        "yyyy/M/d", "yyyy/M/dd", "yyyy/MM/d", "yyyy/MM/dd",
        "yyyy-MM-d", "yyyy-MM-dd", "yyyy/M/d H:mm", "yyyy-MM-dd HH:mm:ss"
    };

    public static IReadOnlyList<SenScheduleIteration> Parse(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new FormatException("请选择 Excel 文件");

        if (!File.Exists(filePath))
            throw new FormatException("Excel 文件不存在");

        try
        {
            using FileStream stream = File.OpenRead(filePath);
            using ZipArchive archive = new(stream, ZipArchiveMode.Read, leaveOpen: false);
            XDocument workbook = ReadXml(archive, "xl/workbook.xml");
            XDocument relationships = ReadXml(archive, "xl/_rels/workbook.xml.rels");
            List<string> sharedStrings = ReadSharedStrings(archive);
            Dictionary<string, string> sheetTargets = ReadSheetTargets(relationships);

            List<SenScheduleIteration> iterations = new();
            XNamespace main = SpreadsheetNamespace;
            XNamespace relationship = RelationshipNamespace;
            XElement? sheets = workbook.Root?.Element(main + "sheets");
            if (sheets is null)
                throw new FormatException("Excel 工作簿中没有工作表");

            foreach (XElement sheet in sheets.Elements(main + "sheet"))
            {
                string name = ((string?)sheet.Attribute("name"))?.Trim() ?? string.Empty;
                string relationshipId = (string?)sheet.Attribute(relationship + "id") ?? string.Empty;
                if (name.Length == 0 || !sheetTargets.TryGetValue(relationshipId, out string? target))
                    continue;

                iterations.Add(ParseSheet(
                    name,
                    ReadXml(archive, ResolveWorksheetEntry(target)),
                    sharedStrings));
            }

            if (iterations.Count == 0)
                throw new FormatException("Excel 工作簿中没有可读取的工作表");

            return iterations;
        }
        catch (FormatException)
        {
            throw;
        }
        catch (InvalidDataException ex)
        {
            throw new FormatException("Excel 文件格式无效或已损坏", ex);
        }
        catch (XmlException ex)
        {
            throw new FormatException("Excel 文件内容无效", ex);
        }
    }

    private static SenScheduleIteration ParseSheet(
        string sheetName,
        XDocument document,
        IReadOnlyList<string> sharedStrings)
    {
        List<ExcelRow> rows = ReadRows(document, sharedStrings);
        Dictionary<string, int>? columns = null;
        List<SenScheduleActivity> activities = new();

        foreach (ExcelRow row in rows)
        {
            Dictionary<string, int>? header = FindColumns(row.Values);
            if (header is not null)
            {
                columns = header;
                continue;
            }

            if (columns is null || row.Values.Count == 0)
                continue;

            if (row.Values.Count == 1)
                continue;

            string sequenceText = GetCell(row, columns, "序号");
            if (!int.TryParse(sequenceText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int sourceSequence)
                || sourceSequence <= 0)
            {
                throw new FormatException($"工作表“{sheetName}”第 {row.Number} 行序号无效：{sequenceText}");
            }

            string title = GetCell(row, columns, "活动");
            if (title.Length == 0)
                throw new FormatException($"工作表“{sheetName}”第 {row.Number} 行活动名称为空");

            string workloadText = GetCell(row, columns, "工作量(天)");
            int? workloadDays = null;
            if (workloadText != "-" && workloadText.Length > 0)
            {
                if (!int.TryParse(workloadText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedWorkload)
                    || parsedWorkload <= 0)
                {
                    throw new FormatException($"工作表“{sheetName}”第 {row.Number} 行工作量无效：{workloadText}");
                }

                workloadDays = parsedWorkload;
            }

            DateTime startDate = ParseDate(GetCell(row, columns, "开始时间"), sheetName, row.Number);
            DateTime endDate = ParseDate(GetCell(row, columns, "结束时间"), sheetName, row.Number);
            if (endDate < startDate)
                throw new FormatException($"工作表“{sheetName}”第 {row.Number} 行结束时间早于开始时间");

            activities.Add(new SenScheduleActivity
            {
                Sequence = activities.Count + 1,
                Title = title,
                Owner = columns.TryGetValue("Owner", out int ownerColumn)
                    ? GetCell(row, ownerColumn)
                    : string.Empty,
                WorkloadDays = workloadDays,
                StartDate = startDate,
                EndDate = endDate
            });
        }

        if (activities.Count == 0)
            throw new FormatException($"工作表“{sheetName}”没有有效活动行");

        return new SenScheduleIteration
        {
            Name = sheetName,
            Activities = activities
        };
    }

    private static Dictionary<string, int>? FindColumns(IReadOnlyDictionary<int, string> values)
    {
        Dictionary<string, int> columns = values
            .Select(pair => (Name: NormalizeHeader(pair.Value), Index: pair.Key))
            .Where(pair => pair.Name.Length > 0)
            .GroupBy(pair => pair.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Index, StringComparer.OrdinalIgnoreCase);

        string[] required = { "序号", "活动", "工作量(天)", "开始时间", "结束时间" };
        return required.All(columns.ContainsKey) ? columns : null;
    }

    private static string NormalizeHeader(string value)
        => string.Concat(value
                .Replace('（', '(')
                .Replace('）', ')')
                .Where(character => !char.IsWhiteSpace(character)))
            .Trim();

    private static string GetCell(ExcelRow row, IReadOnlyDictionary<string, int> columns, string name)
        => columns.TryGetValue(name, out int column) ? GetCell(row, column) : string.Empty;

    private static string GetCell(ExcelRow row, int column)
        => row.Values.TryGetValue(column, out string? value) ? value.Trim() : string.Empty;

    private static DateTime ParseDate(string value, string sheetName, int rowNumber)
    {
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double serial)
            && serial >= 1 && serial <= 2958465)
        {
            try
            {
                return DateTime.FromOADate(serial).Date;
            }
            catch (ArgumentException)
            {
                // Continue with text parsing to produce the normal row-specific error.
            }
        }

        if (DateTime.TryParseExact(
                value,
                DateFormats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces,
                out DateTime date)
            || DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out date))
        {
            return date.Date;
        }

        throw new FormatException($"工作表“{sheetName}”第 {rowNumber} 行日期无效：{value}");
    }

    private static List<ExcelRow> ReadRows(XDocument document, IReadOnlyList<string> sharedStrings)
    {
        XNamespace main = SpreadsheetNamespace;
        List<ExcelRow> rows = new();
        XElement? sheetData = document.Root?.Element(main + "worksheet")?.Element(main + "sheetData")
            ?? document.Root?.Element(main + "sheetData");
        if (sheetData is null)
            return rows;

        int nextRow = 1;
        foreach (XElement row in sheetData.Elements(main + "row"))
        {
            int rowNumber = int.TryParse((string?)row.Attribute("r"), out int parsedRow)
                ? parsedRow
                : nextRow;
            Dictionary<int, string> values = new();
            foreach (XElement cell in row.Elements(main + "c"))
            {
                string reference = (string?)cell.Attribute("r") ?? string.Empty;
                int column = GetColumnNumber(reference);
                if (column <= 0)
                    continue;

                string type = (string?)cell.Attribute("t") ?? string.Empty;
                string value = type == "inlineStr"
                    ? string.Concat(cell.Descendants(main + "t").Select(item => (string?)item ?? string.Empty))
                    : (string?)cell.Element(main + "v") ?? string.Empty;
                if (type == "s"
                    && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int stringIndex)
                    && stringIndex >= 0
                    && stringIndex < sharedStrings.Count)
                {
                    value = sharedStrings[stringIndex];
                }

                values[column] = value;
            }

            rows.Add(new ExcelRow(rowNumber, values));
            nextRow = rowNumber + 1;
        }

        return rows;
    }

    private static int GetColumnNumber(string reference)
    {
        int column = 0;
        foreach (char character in reference)
        {
            if (character is < 'A' or > 'Z')
                break;

            column = column * 26 + character - 'A' + 1;
        }

        return column;
    }

    private static List<string> ReadSharedStrings(ZipArchive archive)
    {
        ZipArchiveEntry? entry = archive.GetEntry("xl/sharedStrings.xml");
        if (entry is null)
            return new List<string>();

        XDocument document = ReadXml(entry);
        XNamespace main = SpreadsheetNamespace;
        return document.Root?.Elements(main + "si")
            .Select(item => string.Concat(item.Descendants(main + "t").Select(text => (string?)text ?? string.Empty)))
            .ToList() ?? new List<string>();
    }

    private static Dictionary<string, string> ReadSheetTargets(XDocument relationships)
    {
        XNamespace package = PackageRelationshipNamespace;
        return relationships.Root?.Elements(package + "Relationship")
            .Where(item => string.Equals((string?)item.Attribute("Type"),
                "http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet",
                StringComparison.Ordinal))
            .Where(item => !string.IsNullOrWhiteSpace((string?)item.Attribute("Id")))
            .ToDictionary(
                item => (string)item.Attribute("Id")!,
                item => (string)item.Attribute("Target")!,
                StringComparer.Ordinal)
            ?? new Dictionary<string, string>(StringComparer.Ordinal);
    }

    private static string ResolveWorksheetEntry(string target)
    {
        string normalized = target.Replace('\\', '/').TrimStart('/');
        return normalized.StartsWith("xl/", StringComparison.Ordinal)
            ? normalized
            : $"xl/{normalized}";
    }

    private static XDocument ReadXml(ZipArchive archive, string entryName)
    {
        ZipArchiveEntry? entry = archive.GetEntry(entryName)
            ?? throw new FormatException($"Excel 文件缺少 {entryName}");
        return ReadXml(entry);
    }

    private static XDocument ReadXml(ZipArchiveEntry entry)
    {
        using Stream stream = entry.Open();
        return XDocument.Load(stream, LoadOptions.PreserveWhitespace);
    }

    private sealed record ExcelRow(int Number, Dictionary<int, string> Values);
}