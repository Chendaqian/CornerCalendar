using CornerCalendar.Core.Services;

namespace CornerCalendar.Core.Helpers;

/// <summary>
/// 森日程功能的持久化显示门。
/// </summary>
/// <remark>
/// 首次使用默认隐藏；在设置窗口标题点击 5 次可切换显示或隐藏并立即保存。
/// </remark>
public static class SenScheduleFeatureGate
{
    private static bool? _isUnlocked;

    public static bool IsUnlocked
    {
        get
        {
            _isUnlocked ??= AppSettings.Current.SenScheduleFeatureVisible;
            return _isUnlocked.Value;
        }
    }

    public static bool Toggle()
    {
        bool unlocked = !IsUnlocked;
        _isUnlocked = unlocked;
        AppSettings.Current.SenScheduleFeatureVisible = unlocked;
        AppSettings.Current.Save();
        return unlocked;
    }

    internal static void Unlock()
    {
        _isUnlocked = true;
    }

    internal static void Reset()
    {
        _isUnlocked = false;
    }
}