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

    // #1 颜色主题：浅色/深色/跟随系统
    public ThemeMode ThemeMode { get; set; } = ThemeMode.FollowSystem;

    // #2 字体大小偏移（-2, -1, 0, +1, +2）
    public int FontSizeOffset { get; set; } = 2;

    // #3 开机自启动
    public bool AutoStartup { get; set; } = false;

    // #4 数据源类型
    public DataSourceType DataSource { get; set; } = DataSourceType.SystemCalendar;

    // #4 .ics 远程 URL 列表（支持多个订阅）
    public List<string> IcsUrls { get; set; } = new();

    // #4 .ics 订阅别名列表（与 IcsUrls 一一对应）
    public List<string> IcsAliases { get; set; } = new();

    // #4 .ics 刷新频率（分钟）
    public int IcsRefreshMinutes { get; set; } = 30;

    // #5 近期事件显示天数
    public int UpcomingDays { get; set; } = 3;

    // #6 周起始日
    public WeekStartDay WeekStartDay { get; set; } = WeekStartDay.Sunday;

    // 是否在月历左侧显示 ISO 周数
    public bool ShowWeekNumbers { get; set; } = false;

    // 覆盖任务栏时钟的 DateTime.ToString 格式，使用字面量 \\n 换行
    public string TaskbarTimeFormat { get; set; } = "HH:mm\\nyyyy/M/d";

    // 天气位置列表：空字符串表示使用公网 IP 自动定位
    public List<string> WeatherLocations { get; set; } = new();

    // 天气服务地址：需兼容 Open-Meteo 当前天气接口格式
    public string WeatherApiUrl { get; set; } = "https://api.open-meteo.com/v1/forecast";

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

public enum DataSourceType
{
    SystemCalendar,
    IcsUrl,
    Both
}

public enum WeekStartDay
{
    Sunday,
    Monday
}