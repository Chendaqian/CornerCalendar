using System.Globalization;

namespace CornerCalendar.Core.Helpers;

/// <summary>
/// 中国日历附加信息：24 节气和中国大陆法定节假日。
/// 计算在本地完成，面板打开时不依赖网络页面可用性。
/// </summary>
public static class ChinaCalendarHelper
{
    private static readonly ChineseLunisolarCalendar LunarCalendar = new();
    private static readonly object SolarTermCacheLock = new();
    private static readonly Dictionary<int, IReadOnlyDictionary<DateTime, string>> SolarTermCache = new();

    private static readonly string[] SolarTermNames =
    {
        "春分", "清明", "谷雨", "立夏", "小满", "芒种",
        "夏至", "小暑", "大暑", "立秋", "处暑", "白露",
        "秋分", "寒露", "霜降", "立冬", "小雪", "大雪",
        "冬至", "小寒", "大寒", "立春", "雨水", "惊蛰"
    };

    private const double ChinaUtcOffsetHours = 8;
    private static readonly DateTime J2000 = new(2000, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// 获取日期对应的 24 节气名称；普通日期返回空字符串。
    /// </summary>
    public static string GetSolarTerm(DateTime date)
    {
        return GetSolarTerms(date.Year).TryGetValue(date.Date, out string? term)
            ? term
            : string.Empty;
    }

    /// <summary>
    /// 获取中国大陆法定节假日名称；非节假日返回空字符串。
    /// 这里标记法定节日本身，不把每年临时发布的调休安排伪装成固定规则。
    /// </summary>
    public static string GetLegalHoliday(DateTime date)
    {
        date = date.Date;

        string? fixedHoliday = date switch
        {
            { Month: 1, Day: 1 } => "元旦",
            { Month: 5, Day: 1 } => "劳动节",
            { Month: 10, Day: 1 } or { Month: 10, Day: 2 } or { Month: 10, Day: 3 } => "国庆节",
            _ => null
        };

        if (fixedHoliday != null)
            return fixedHoliday;

        if (GetSolarTerm(date) == "清明")
            return "清明节";

        if (!TryGetLunarDate(date, out int month, out int day))
            return string.Empty;

        return (month, day) switch
        {
            (1, >= 1 and <= 3) => "春节",
            (5, 5) => "端午节",
            (8, 15) => "中秋节",
            _ => string.Empty
        };
    }

    private static IReadOnlyDictionary<DateTime, string> GetSolarTerms(int year)
    {
        lock (SolarTermCacheLock)
        {
            if (SolarTermCache.TryGetValue(year, out IReadOnlyDictionary<DateTime, string>? cached))
                return cached;

            Dictionary<DateTime, string> terms = new();
            DateTime anchor = new DateTime(year, 3, 20);

            for (int index = 0; index < SolarTermNames.Length; index++)
            {
                DateTime approximateLocal = anchor.AddDays(index * 15.2184);
                DateTime termUtc = FindSolarTerm(approximateLocal, index * 15);
                DateTime termLocalDate = termUtc.AddHours(ChinaUtcOffsetHours).Date;

                if (termLocalDate.Year == year)
                    terms[termLocalDate] = SolarTermNames[index];
            }

            // 春分之前的立春、雨水、惊蛰来自上一条以 3 月为锚点的周期。
            DateTime previousAnchor = new DateTime(year - 1, 3, 20);
            for (int index = 19; index < SolarTermNames.Length; index++)
            {
                DateTime approximateLocal = previousAnchor.AddDays(index * 15.2184);
                DateTime termUtc = FindSolarTerm(approximateLocal, index * 15);
                DateTime termLocalDate = termUtc.AddHours(ChinaUtcOffsetHours).Date;

                if (termLocalDate.Year == year)
                    terms[termLocalDate] = SolarTermNames[index];
            }

            SolarTermCache[year] = terms;
            return terms;
        }
    }

    /// <summary>
    /// 在节气的近似日期附近查找太阳黄经达到目标角度的时刻。
    /// 太阳黄经的近似值足以稳定确定中国时区内的节气日期。
    /// </summary>
    private static DateTime FindSolarTerm(DateTime approximateLocal, double targetLongitude)
    {
        // 每 15.2184 天的平均间隔会在年内逐步产生小数日漂移，
        // 预留 7 天窗口可以覆盖闰年和近似值累积误差。
        DateTime startUtc = ToUtc(approximateLocal.AddDays(-7));
        DateTime endUtc = ToUtc(approximateLocal.AddDays(7));
        double startLongitude = SolarLongitude(startUtc);
        double targetDelta = PositiveAngle(targetLongitude - startLongitude);
        double span = PositiveAngle(SolarLongitude(endUtc) - startLongitude);

        if (targetDelta > span)
            return ToUtc(approximateLocal);

        for (int i = 0; i < 32; i++)
        {
            DateTime middleUtc = startUtc + (endUtc - startUtc) / 2;
            double elapsed = PositiveAngle(SolarLongitude(middleUtc) - startLongitude);

            if (elapsed < targetDelta)
                startUtc = middleUtc;
            else
                endUtc = middleUtc;
        }

        return startUtc + (endUtc - startUtc) / 2;
    }

    private static DateTime ToUtc(DateTime localDateTime)
    {
        DateTime unspecified = DateTime.SpecifyKind(localDateTime, DateTimeKind.Unspecified);
        return unspecified.AddHours(-ChinaUtcOffsetHours);
    }

    private static double SolarLongitude(DateTime utc)
    {
        double days = (utc - J2000).TotalDays;
        double meanLongitude = 280.460 + 0.9856474 * days;
        double meanAnomaly = DegreesToRadians(357.528 + 0.9856003 * days);
        double longitude = meanLongitude
            + 1.915 * Math.Sin(meanAnomaly)
            + 0.020 * Math.Sin(2 * meanAnomaly);

        return PositiveAngle(longitude);
    }

    private static double PositiveAngle(double angle)
    {
        angle %= 360;
        return angle < 0 ? angle + 360 : angle;
    }

    private static double DegreesToRadians(double degrees)
    {
        return degrees * Math.PI / 180;
    }

    private static bool TryGetLunarDate(DateTime date, out int month, out int day)
    {
        month = 0;
        day = 0;

        if (date < LunarCalendar.MinSupportedDateTime || date > LunarCalendar.MaxSupportedDateTime)
            return false;

        int lunarYear = LunarCalendar.GetYear(date);
        int lunarMonth = LunarCalendar.GetMonth(date);
        int leapMonth = LunarCalendar.GetLeapMonth(lunarYear);

        // 法定节日按正常农历月计算，闰月日期不匹配。
        if (leapMonth > 0 && lunarMonth == leapMonth)
            return false;

        month = leapMonth > 0 && lunarMonth > leapMonth ? lunarMonth - 1 : lunarMonth;
        day = LunarCalendar.GetDayOfMonth(date);
        return true;
    }
}