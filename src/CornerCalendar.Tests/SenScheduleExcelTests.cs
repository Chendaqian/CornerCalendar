using CornerCalendar.Core.Models;
using CornerCalendar.Core.Services;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using Xunit;

namespace CornerCalendar.Tests;

public sealed class SenScheduleExcelTests
{
    [Fact]
    public void 每个工作表作为一个迭代并读取表格数据()
    {
        string path = Path.Combine(Path.GetTempPath(), $"corner-calendar-{Guid.NewGuid():N}.xlsx");
        try
        {
            CreateWorkbook(path);

            IReadOnlyList<SenScheduleIteration> iterations = SenScheduleExcelParser.Parse(path);

            Assert.Equal(2, iterations.Count);
            Assert.Equal("2.27", iterations[0].Name);
            Assert.Equal(2, iterations[0].Activities.Count);
            Assert.Equal(1, iterations[0].Activities[0].Sequence);
            Assert.Equal("需求立项评审(TR1)", iterations[0].Activities[0].Title);
            Assert.Null(iterations[0].Activities[0].WorkloadDays);
            Assert.Equal(new DateTime(2026, 7, 2), iterations[0].Activities[0].StartDate);
            Assert.Equal("一条龙经理", iterations[0].Activities[0].Owner);
            Assert.Equal(2, iterations[0].Activities[1].Sequence);
            Assert.Equal(3, iterations[0].Activities[1].WorkloadDays);
            Assert.Equal("2.26", iterations[1].Name);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static void CreateWorkbook(string path)
    {
        using FileStream stream = File.Create(path);
        using ZipArchive archive = new(stream, ZipArchiveMode.Create);
        WriteEntry(archive, "xl/workbook.xml", """
            <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
              <sheets><sheet name="2.27" sheetId="1" r:id="rId1"/><sheet name="2.26" sheetId="2" r:id="rId2"/></sheets>
            </workbook>
            """);
        WriteEntry(archive, "xl/_rels/workbook.xml.rels", """
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
              <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet2.xml"/>
            </Relationships>
            """);
        WriteEntry(archive, "xl/worksheets/sheet1.xml", CreateSheetXml(new[]
        {
            new[] { "序号", "活动", "工作量(天)", "开始时间", "结束时间", "Owner" },
            new[] { "1", "需求立项评审(TR1)", "-", Date(2026, 7, 2), Date(2026, 7, 3), "一条龙经理" },
            new[] { "2", "需求分析", "3", Date(2026, 7, 4), Date(2026, 7, 8), "开发辅Leader" }
        }));
        WriteEntry(archive, "xl/worksheets/sheet2.xml", CreateSheetXml(new[]
        {
            new[] { "序号", "活动", "工作量(天)", "开始时间", "结束时间", "Owner" },
            new[] { "1", "上线", "1", Date(2026, 8, 1), Date(2026, 8, 1), "测试Leader" }
        }));
    }

    private static string CreateSheetXml(IReadOnlyList<string[]> rows)
    {
        StringBuilder builder = new("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>");
        for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            int rowNumber = rowIndex + 1;
            builder.Append($"<row r=\"{rowNumber}\">");
            for (int columnIndex = 0; columnIndex < rows[rowIndex].Length; columnIndex++)
            {
                string reference = $"{(char)('A' + columnIndex)}{rowNumber}";
                string value = System.Security.SecurityElement.Escape(rows[rowIndex][columnIndex]) ?? string.Empty;
                bool isNumber = rowIndex > 0 && (columnIndex == 0 || columnIndex is 2 or 3 or 4);
                if (isNumber)
                {
                    builder.Append($"<c r=\"{reference}\"><v>{value}</v></c>");
                }
                else
                {
                    builder.Append($"<c r=\"{reference}\" t=\"inlineStr\"><is><t>{value}</t></is></c>");
                }
            }

            builder.Append("</row>");
        }

        builder.Append("</sheetData></worksheet>");
        return builder.ToString();
    }

    private static string Date(int year, int month, int day)
        => new DateTime(year, month, day).ToOADate().ToString(CultureInfo.InvariantCulture);

    private static void WriteEntry(ZipArchive archive, string name, string content)
    {
        ZipArchiveEntry entry = archive.CreateEntry(name);
        using StreamWriter writer = new(entry.Open(), new UTF8Encoding(false));
        writer.Write(content);
    }
}