using CornerCalendar.Core.Services;
using Microsoft.Win32;
using System.Windows;

namespace CornerCalendar.Core.Helpers;

/// <summary>
/// 主题切换帮助类
/// </summary>
public static class ThemeHelper
{
    private static readonly string LightThemeUri = "Views/Themes/Light.xaml";
    private static readonly string DarkThemeUri = "Views/Themes/Dark.xaml";

    private static ThemeMode _currentMode = ThemeMode.FollowSystem;
    private static bool _systemTracking;

    /// <summary>
    /// 根据设置应用主题
    /// </summary>
    public static void ApplyTheme(ThemeMode mode)
    {
        _currentMode = mode;

        bool isDark = mode switch
        {
            ThemeMode.Dark => true,
            ThemeMode.Light => false,
            _ => IsSystemDarkMode() // FollowSystem
        };

        SetTheme(isDark);
    }

    /// <summary>
    /// 开始监听系统主题变化（ISSUES #2）。
    /// 仅「跟随系统」模式下生效；系统切换深浅色后自动重新应用主题。
    /// </summary>
    public static void StartSystemThemeTracking()
    {
        if (_systemTracking) return;
        _systemTracking = true;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    /// <summary>
    /// 停止监听（应用退出时调用，避免悬挂订阅）
    /// </summary>
    public static void StopSystemThemeTracking()
    {
        if (!_systemTracking) return;
        _systemTracking = false;
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
    }

    private static void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        try
        {
            // 回调在系统事件线程，需切到 UI 线程操作资源字典
            Application? app = Application.Current;
            if (app == null) return;

            app.Dispatcher.BeginInvoke(new Action(() =>
            {
                // 只有「跟随系统」模式才响应；深浅色模式是用户显式选择，不跟随
                if (_currentMode == ThemeMode.FollowSystem)
                    SetTheme(IsSystemDarkMode());
            }));
        }
        catch
        {
            // 应用退出过程中的竞态等：主题跟随失败绝不影响主流程
        }
    }

    /// <summary>
    /// 检测 Windows 系统是否为深色模式
    /// </summary>
    public static bool IsSystemDarkMode()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            object? value = key?.GetValue("AppsUseLightTheme");
            return value is int intValue && intValue == 0;
        }
        catch
        {
            return false; // 默认浅色
        }
    }

    /// <summary>
    /// 切换应用资源字典
    /// </summary>
    private static void SetTheme(bool isDark)
    {
        Application app = Application.Current;
        if (app == null) return;

        string themeUri = isDark ? DarkThemeUri : LightThemeUri;

        System.Collections.ObjectModel.Collection<ResourceDictionary> dicts = app.Resources.MergedDictionaries;

        // 移除旧主题（Source 会变成 pack:// URI，所以用 Contains 匹配）
        for (int i = dicts.Count - 1; i >= 0; i--)
        {
            string? source = dicts[i].Source?.OriginalString;
            if (source != null &&
                (source.Contains("Light.xaml") || source.Contains("Dark.xaml")))
            {
                dicts.RemoveAt(i);
            }
        }

        // 添加新主题
        dicts.Add(new ResourceDictionary
        {
            Source = new Uri(themeUri, UriKind.Relative)
        });
    }
}