using System.Globalization;

namespace CornerCalendar.Core.Helpers;

/// <summary>
/// 任务栏时钟格式化，格式遵循 DateTime.ToString。
/// </summary>
public static class TaskbarClockFormatter
{
    public const string DefaultFormat = "HH:mm\\nyyyy/M/d";

    public static string Format(DateTime dateTime, string? format)
    {
        string effectiveFormat = string.IsNullOrWhiteSpace(format)
            ? DefaultFormat
            : format.Trim();

        effectiveFormat = effectiveFormat.Replace("\\n", Environment.NewLine, StringComparison.Ordinal);

        try
        {
            return dateTime.ToString(effectiveFormat, CultureInfo.CurrentCulture);
        }
        catch (FormatException)
        {
            return dateTime.ToString(DefaultFormat.Replace("\\n", Environment.NewLine, StringComparison.Ordinal), CultureInfo.CurrentCulture);
        }
    }
}