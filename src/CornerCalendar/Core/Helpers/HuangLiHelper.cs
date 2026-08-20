namespace CornerCalendar.Core.Helpers;

/// <summary>
/// 黄历宜忌计算适配器。
/// 规则来自 MIT 项目 6tail/lunar-csharp，不依赖 ChinaCalendar 年度 ICS 数据。
/// </summary>
public static class HuangLiHelper
{
    /// <summary>
    /// 获取指定公历日期的日宜和日忌。规则库没有结果时返回空字符串。
    /// </summary>
    public static (string SuitableActivities, string AvoidActivities) GetDayYiJi(DateTime date)
    {
        try
        {
            global::Lunar.Lunar lunar = global::Lunar.Lunar.FromDate(date.Date);
            return (
                JoinActivities(lunar.DayYi),
                JoinActivities(lunar.DayJi));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"CornerCalendar: Huangli calculation failed: {ex.Message}");
            return (string.Empty, string.Empty);
        }
    }

    private static string JoinActivities(IEnumerable<string>? activities)
    {
        if (activities == null)
            return string.Empty;

        return string.Join(
            "、",
            activities.Where(activity =>
                !string.IsNullOrWhiteSpace(activity) &&
                !string.Equals(activity, "无", StringComparison.Ordinal))
                .Select(activity => activity.Trim())
                .Distinct(StringComparer.Ordinal))
            .Replace("馀", "余", StringComparison.Ordinal);
    }
}