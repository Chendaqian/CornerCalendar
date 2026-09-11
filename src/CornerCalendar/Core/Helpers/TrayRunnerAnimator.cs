using CornerCalendar.Core.Models;
using Hardcodet.Wpf.TaskbarNotification;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Threading;

namespace CornerCalendar.Core.Helpers;

/// <summary>
/// 托盘跑者动画器：预缓存跑者帧图标并按帧轮换托盘图标，帧速随 CPU 负载动态变化。
/// </summary>
/// <remark>
/// 帧循环与图标缓存机制移植自 RunCat365（Apache-2.0，Copyright Takuto Nakamura），
/// 见 https://github.com/runcat-dev/RunCat365 。
/// 换帧在 UI 线程 DispatcherTimer 上进行；CPU 采样与系统深浅色检测在后台 1 秒定时器上进行，
/// 系统深色主题下帧自动反白（见 <see cref="RunnerLibrary" />）。
/// </remark>
internal sealed class TrayRunnerAnimator : IDisposable
{
    private static readonly TimeSpan SamplePeriod = TimeSpan.FromSeconds(1);

    private readonly TaskbarIcon _trayIcon;
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _frameTimer;
    private readonly CpuLoadMonitor _cpuMonitor = new();
    private System.Threading.Timer? _sampleTimer;
    private IReadOnlyList<RunnerInfo> _runners = Array.Empty<RunnerInfo>();
    private RunnerInfo? _currentRunner;
    private List<Icon> _icons = new();
    private int _currentFrame;
    private bool _isDarkTheme;
    private bool _disposed;

    internal TrayRunnerAnimator(TaskbarIcon trayIcon)
    {
        _trayIcon = trayIcon;
        _dispatcher = trayIcon.Dispatcher;
        _frameTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(CpuLoadMonitor.CalculateInterval(0f))
        };
        _frameTimer.Tick += OnFrameTick;
    }

    /// <summary>
    /// 启动动画：枚举跑者、加载指定跑者（缺失时回退默认），启动换帧与后台采样定时器。
    /// </summary>
    internal void Start(string? runnerName)
    {
        _runners = RunnerLibrary.EnumerateRunners();
        _isDarkTheme = ThemeHelper.IsSystemDarkMode();
        ApplyRunner(runnerName);
        _frameTimer.Start();
        _sampleTimer = new System.Threading.Timer(OnSampleTick, null, SamplePeriod, SamplePeriod);
    }

    /// <summary>
    /// 热切换跑者并从首帧重新播放（设置保存后调用）。
    /// </summary>
    internal void SetRunner(string? runnerName)
    {
        if (ApplyRunner(runnerName) && !_frameTimer.IsEnabled)
            _frameTimer.Start();
    }

    private bool ApplyRunner(string? runnerName)
    {
        RunnerInfo? runner = RunnerLibrary.FindRunner(_runners, runnerName)
            ?? RunnerLibrary.FindRunner(_runners, RunnerLibrary.DefaultRunnerName)
            ?? _runners.FirstOrDefault();
        if (runner == null || ReferenceEquals(runner, _currentRunner))
            return false;

        _currentRunner = runner;
        RebuildIcons();
        return true;
    }

    /// <summary>
    /// 按当前跑者与系统深浅色重建图标缓存并显示首帧（仅 UI 线程调用）。
    /// </summary>
    private void RebuildIcons()
    {
        if (_currentRunner == null)
            return;

        IReadOnlyList<Icon> loaded = RunnerLibrary.LoadRunnerIcons(_currentRunner, _isDarkTheme);
        if (loaded.Count == 0)
            return; // 全部帧加载失败：保留现有图标，避免托盘空白

        List<Icon> oldIcons = _icons;
        _icons = new List<Icon>(loaded);
        _trayIcon.Icon = _icons[0];
        _currentFrame = 1 % _icons.Count;
        foreach (Icon icon in oldIcons)
            icon.Dispose();
    }

    private void OnFrameTick(object? sender, EventArgs e)
    {
        if (_icons.Count == 0)
            return;

        if (_currentFrame >= _icons.Count)
            _currentFrame = 0;
        _trayIcon.Icon = _icons[_currentFrame];
        _currentFrame = (_currentFrame + 1) % _icons.Count;
    }

    /// <summary>
    /// 后台每秒采样：按 CPU 负载更新帧间隔，系统深浅色翻转时重建图标缓存。
    /// </summary>
    private void OnSampleTick(object? state)
    {
        if (_disposed)
            return;

        float load = _cpuMonitor.SampleLoad();
        int interval = CpuLoadMonitor.CalculateInterval(load);
        bool isDark = ThemeHelper.IsSystemDarkMode();
        try
        {
            _dispatcher.BeginInvoke(new Action(() =>
            {
                if (_disposed)
                    return;

                // 悬停提示与 RunCat365 一致：每秒刷新为 CPU 占用
                _trayIcon.ToolTipText = $"CPU: {load:f1}%";

                if ((int)_frameTimer.Interval.TotalMilliseconds != interval)
                {
                    _frameTimer.Stop();
                    _frameTimer.Interval = TimeSpan.FromMilliseconds(interval);
                    _frameTimer.Start();
                }

                if (isDark != _isDarkTheme)
                {
                    _isDarkTheme = isDark;
                    RebuildIcons();
                }
            }));
        }
        catch (Exception ex)
        {
            // 应用退出过程中 Dispatcher 已关闭的竞态：采样失败不影响主流程
            Debug.WriteLine($"CornerCalendar: 跑者动画采样回调失败：{ex.Message}");
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _sampleTimer?.Dispose();
        _sampleTimer = null;
        _frameTimer.Stop();
        _frameTimer.Tick -= OnFrameTick;
        foreach (Icon icon in _icons)
            icon.Dispose();
        _icons.Clear();
        _cpuMonitor.Dispose();
    }
}