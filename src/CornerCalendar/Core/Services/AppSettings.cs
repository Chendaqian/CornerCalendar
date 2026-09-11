using CornerCalendar.Core.Helpers;
using CornerCalendar.Core.Models;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CornerCalendar.Core.Services;

/// <summary>
/// 应用设置持久化服务，存储到 %LOCALAPPDATA%/CornerCalendar/settings.json。
/// </summary>
public class AppSettings
{
    private static readonly object Sync = new();
    private static AppSettings? _current;

    private static readonly string SettingsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CornerCalendar");

    private static readonly string SettingsPath = Path.Combine(SettingsDir, "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    // 默认配置与当前使用的配置保持一致。
    // #1 颜色主题：浅色/深色/跟随系统
    public ThemeMode ThemeMode { get; set; } = ThemeMode.Light;

    // #2 字体大小偏移（-2, -1, 0, +1, +2）
    public int FontSizeOffset { get; set; } = 2;

    // #3 开机自启动
    public bool AutoStartup { get; set; } = false;

    // #4 .ics 远程 URL 列表（支持多个订阅）
    public List<string> IcsUrls { get; set; } = new();

    // #4 .ics 订阅别名列表（与 IcsUrls 一一对应）
    public List<string> IcsAliases { get; set; } = new();

    // #4 .ics 刷新频率（分钟）
    public int IcsRefreshMinutes { get; set; } = 120;

    // 森日程显示开关，默认关闭
    public bool SenScheduleEnabled { get; set; } = false;

    // 森日程迭代（在线清单拉取合并结果与本地历史数据）
    public List<SenScheduleIteration> SenSchedules { get; set; } = new();

    // 森日程阶段圆圈显示开关（日期格上的非选中态圆圈），默认显示
    public bool ShowSenPhaseCircles { get; set; } = true;

    // 森日程在线数据根地址（应用拉取其下 manifest.yaml），清空则停用在线
    public string SenOnlineUrl { get; set; } = "https://raw.giteeusercontent.com/LuckBUBU/CornerCalendar/raw/master";

    // 内置中国日历中被隐藏的节日名称
    public List<string> HiddenHolidayNames { get; set; } = new();

    // #5 近期事件显示天数
    public int UpcomingDays { get; set; } = 7;

    // #6 周起始日
    public WeekStartDay WeekStartDay { get; set; } = WeekStartDay.Monday;

    // 是否在月历左侧显示 ISO 周数
    public bool ShowWeekNumbers { get; set; } = true;

    // 当前选中的托盘跑者（Resources\Runners 下的文件夹名），缺失时回退默认值
    public string RunnerName { get; set; } = RunnerLibrary.DefaultRunnerName;

    // 天气位置列表：空字符串表示使用公网 IP 自动定位
    public List<string> WeatherLocations { get; set; } = new() { "北京", "大连", "成都" };

    // 天气服务地址：需兼容 Open-Meteo 当前天气接口格式
    public string WeatherApiUrl { get; set; } = "https://api.open-meteo.com/v1/forecast";

    // 天气后台刷新频率（分钟）
    public int WeatherRefreshMinutes { get; set; } = 120;

    // 是否在日期详情中显示历史资料
    public bool ShowHistoryToday { get; set; } = true;

    // 历史资料类型：事件、出生、逝世、节日
    public List<string> HistoryCategories { get; set; } = new() { "事件" };

    // 历史资料最多显示条数，0 表示全部
    public int HistoryMaxItems { get; set; } = 10;

    // 历史资料最早年份，0 表示不限
    public int HistoryMinYear { get; set; } = 1900;

    /// <summary>
    /// 创建一份默认配置。恢复默认和新建设置均使用这里的值。
    /// </summary>
    /// <remark>
    /// 默认值来自内置的 Resources/default-settings.json（出厂配置快照），
    /// 读取失败时回退属性初始化器。
    /// </remark>
    public static AppSettings CreateDefaults() => LoadEmbeddedDefaults();

    /// <summary>
    /// 全局单例设置实例（ISSUES #15）：所有窗口共用同一份，
    /// 避免各自 Load 副本后 Save 互相覆盖。
    /// </summary>
    public static AppSettings Current
    {
        get
        {
            lock (Sync)
            {
                return _current ??= LoadFromDisk();
            }
        }
    }

    /// <summary>
    /// 加载设置（返回全局单例；文件不存在时为默认设置）
    /// </summary>
    public static AppSettings Load() => Current;

    private static AppSettings LoadFromDisk()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                string json = File.ReadAllText(SettingsPath);
                return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"CornerCalendar: Failed to load settings: {ex}");
        }
        return LoadEmbeddedDefaults();
    }

    /// <summary>
    /// 读取程序集内嵌的出厂默认配置（Resources/default-settings.json）。
    /// </summary>
    private static AppSettings LoadEmbeddedDefaults()
    {
        try
        {
            using Stream? stream = typeof(AppSettings).Assembly
                .GetManifestResourceStream("CornerCalendar.Resources.default-settings.json");
            if (stream != null)
            {
                using StreamReader reader = new(stream);
                return JsonSerializer.Deserialize<AppSettings>(reader.ReadToEnd(), JsonOptions) ?? new AppSettings();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"CornerCalendar: Failed to load embedded defaults: {ex}");
        }
        return new AppSettings();
    }

    /// <summary>
    /// 保存设置到文件。
    /// 加锁 + 先写临时文件再原子替换，避免多线程写入与写一半崩溃损坏设置（ISSUES #15）。
    /// </summary>
    public void Save()
    {
        lock (Sync)
        {
            try
            {
                Directory.CreateDirectory(SettingsDir);
                string json = JsonSerializer.Serialize(this, JsonOptions);
                string tempPath = SettingsPath + ".tmp";
                File.WriteAllText(tempPath, json);
                File.Move(tempPath, SettingsPath, overwrite: true);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"CornerCalendar: Failed to save settings: {ex}");
            }
        }
    }
}

public enum ThemeMode
{
    FollowSystem,
    Light,
    Dark
}

public enum WeekStartDay
{
    Sunday,
    Monday
}