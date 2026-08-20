using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace CornerCalendar.Core.Helpers;

/// <summary>
/// 监控并替换 Windows 系统日历弹窗。
/// 当检测到系统日历/通知中心弹出时，立即隐藏它并触发我们的日历面板。
/// </summary>
public class SystemCalendarInterceptor : IDisposable
{
    private readonly List<nint> _hooks = new();
    private GCHandle _gcHandle;
    private readonly Dispatcher _dispatcher;
    private Action? _showPopupCallback;
    private bool _disposed;
    private DateTime _lastInterceptTime;
    private static readonly TimeSpan InterceptCooldown = TimeSpan.FromMilliseconds(500);

    // 被我们 SW_HIDE 隐藏的系统窗口句柄。
    // 重要：Win11 的 shell 弹窗（flyout）通过 DWM cloak 隐藏/显示，shell 重新显示时只 uncloak，
    // 不会重新设置 WS_VISIBLE。因此凡是我们清掉过 WS_VISIBLE 的窗口，必须在 shell 关闭它（cloak）
    // 或本应用退出时恢复 WS_VISIBLE，否则系统日历之后永远无法再显示出来。
    private readonly HashSet<nint> _hiddenWindows = new();

    // PID -> 进程名缓存：WinEvent 回调高频触发，避免每次都分配 Process 对象
    private readonly Dictionary<uint, string> _processNameCache = new();

    // 刚被我们恢复过 WS_VISIBLE 的窗口 → 恢复时间。
    // 恢复动作（SW_SHOWNA）会让系统补发一个 SHOW 事件，抑制窗口内不能把它当成"用户点击时钟"，
    // 否则面板会在"点击别处隐藏后自己又显示一次"。
    private readonly Dictionary<nint, DateTime> _recentlyRestored = new();

    private static readonly TimeSpan RestoreSuppressWindow = TimeSpan.FromSeconds(2);

    // Win32 常量（只订阅实际需要的少数事件类型，避免全系统事件洪泛）
    private const uint EVENT_OBJECT_CREATE = 0x8001;

    private const uint EVENT_OBJECT_SHOW = 0x8002;
    private const uint EVENT_OBJECT_DESTROY = 0x8004;
    private const uint EVENT_OBJECT_STATECHANGE = 0x800A;
    private const uint EVENT_OBJECT_NAMECHANGE = 0x800C;
    private const uint EVENT_OBJECT_CLOAK = 0x8017;
    private const uint EVENT_OBJECT_UNCLOAK = 0x8018;
    private const int WINEVENT_OUTOFCONTEXT = 0;
    private const int SW_HIDE = 0;
    private const int SW_SHOWNA = 8; // 显示但不激活（仅恢复 WS_VISIBLE 标志，不抢焦点）

    // Win32 API
    [DllImport("user32.dll")]
    private static extern nint SetWinEventHook(
        uint eventMin, uint eventMax,
        nint hmodWinEventProc,
        WinEventDelegate lpfnWinEventProc,
        uint idProcess, uint idThread,
        uint dwFlags);

    [DllImport("user32.dll")]
    private static extern bool UnhookWinEvent(nint hWinEventHook);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(nint hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(nint hWnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(nint hWnd, out RECT lpRect);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(nint hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(nint hWnd, System.Text.StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern nint MonitorFromWindow(nint hwnd, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(nint hMonitor, ref MONITORINFO lpmi);

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    private delegate void WinEventDelegate(
        nint hWinEventHook, uint eventType, nint hwnd,
        int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    public SystemCalendarInterceptor(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
    }

    private static void Log(string msg) => Debug.WriteLine(msg);

    /// <summary>
    /// 启动拦截器，传入弹出日历面板的回调。
    /// 系统弹窗出现（任一匹配几何特征的窗口级事件）即触发回调——
    /// 实测 Win11 25H2：用户点击时钟时最先到达的事件往往是 NAMECHANGE（shell 先设弹窗标题）。
    /// 我们恢复 WS_VISIBLE 时系统补发的 SHOW 事件已在 WinEventProc 中被抑制，不会误触发面板。
    /// </summary>
    public void Start(Action showPopupCallback)
    {
        _showPopupCallback = showPopupCallback;

        // 使用 GC handle 防止委托被垃圾回收
        _gcHandle = GCHandle.Alloc(new WinEventDelegate(WinEventProc));

        // 捕获委托引用
        WinEventDelegate callback = (WinEventDelegate)_gcHandle.Target!;

        // 只订阅实际用到的事件范围（而不是 0x8002~0x80FF 全范围）：
        //   CREATE+SHOW：捕获窗口显示
        //   DESTROY：清理已销毁的被隐藏窗口句柄
        //   STATECHANGE / NAMECHANGE：系统日历弹出的伴随事件（兼容不同系统版本）
        //   CLOAK+UNCLOAK：shell flyout 的 cloak 关闭 / uncloak 显示
        (uint Min, uint Max)[] ranges =
        {
            (EVENT_OBJECT_CREATE, EVENT_OBJECT_SHOW),
            (EVENT_OBJECT_DESTROY, EVENT_OBJECT_DESTROY),
            (EVENT_OBJECT_STATECHANGE, EVENT_OBJECT_STATECHANGE),
            (EVENT_OBJECT_NAMECHANGE, EVENT_OBJECT_NAMECHANGE),
            (EVENT_OBJECT_CLOAK, EVENT_OBJECT_UNCLOAK),
        };

        foreach ((uint min, uint max) in ranges)
        {
            nint hook = SetWinEventHook(min, max, nint.Zero, callback, 0, 0, WINEVENT_OUTOFCONTEXT);
            if (hook == nint.Zero)
            {
                Log($"CornerCalendar: ✗ Failed to set WinEvent hook for 0x{min:X}-0x{max:X}!");
            }
            else
            {
                _hooks.Add(hook);
            }
        }

        Log($"CornerCalendar: ✓ SystemCalendarInterceptor started, {_hooks.Count} hooks installed");
    }

    /// <summary>
    /// WinEvent 回调 —— 在调用者的线程上下文中执行（WINEVENT_OUTOFCONTEXT）
    /// </summary>
    private static readonly Dictionary<uint, string> EventTypeNames = new()
    {
        [0x8001] = "CREATE",
        [0x8002] = "SHOW",
        [0x8003] = "HIDE",
        [0x8004] = "DESTROY",
        [0x8006] = "LOCATIONCHANGE",
        [0x800A] = "STATECHANGE",
        [0x800C] = "NAMECHANGE",
        [0x8017] = "CLOAK",
        [0x8018] = "UNCLOAK",
    };

    private void WinEventProc(
        nint hWinEventHook, uint eventType, nint hwnd,
        int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        try
        {
            // 只关心窗口级别的对象（idObject == 0）
            if (idObject != 0 || hwnd == nint.Zero)
                return;

            // 获取进程信息来快速过滤
            GetWindowThreadProcessId(hwnd, out uint processId);
            string? procName = GetProcessName(processId);

            // 记录 ShellExperienceHost 的所有事件（调试用，同时写入诊断日志文件）
            if (string.Equals(procName, "ShellExperienceHost", StringComparison.OrdinalIgnoreCase))
            {
                System.Text.StringBuilder className = new System.Text.StringBuilder(256);
                GetClassName(hwnd, className, 256);
                GetWindowRect(hwnd, out RECT r);
                string evtName = EventTypeNames.GetValueOrDefault(eventType, $"0x{eventType:X}");
                LogFile($"★★★ ShellExperienceHost evt={evtName}(0x{eventType:X}) hwnd={hwnd} idObj={idObject} idChild={idChild} " +
                    $"Class='{className}' Rect:({r.Left},{r.Top})-({r.Right},{r.Bottom})");
            }

            // 我们恢复 WS_VISIBLE 时系统会补发 SHOW 事件，不能当成"用户点击时钟"
            if (eventType == EVENT_OBJECT_SHOW)
            {
                lock (_recentlyRestored)
                {
                    if (_recentlyRestored.TryGetValue(hwnd, out DateTime restoredAt))
                    {
                        _recentlyRestored.Remove(hwnd);
                        if (DateTime.UtcNow - restoredAt <= RestoreSuppressWindow)
                        {
                            LogFile($"Ignoring SHOW fired by our own WS_VISIBLE restore: {hwnd}");
                            return;
                        }
                    }
                }
            }

            // 被我们隐藏的系统窗口被 shell 以自己的方式关闭（cloak）→ 恢复我们清掉的 WS_VISIBLE。
            // 此时窗口处于 cloak 状态，恢复 WS_VISIBLE 不会产生可见弹窗；
            // 若不恢复，shell 之后 uncloak 时窗口将因缺少 WS_VISIBLE 而永远无法显示（系统日历打不开）。
            if (eventType == EVENT_OBJECT_CLOAK)
            {
                RestoreTrackedWindow(hwnd);
                return;
            }

            // 被跟踪的窗口已销毁 → 清理过时句柄
            if (eventType == EVENT_OBJECT_DESTROY)
            {
                lock (_hiddenWindows)
                {
                    _hiddenWindows.Remove(hwnd);
                }
                return;
            }

            // 只处理可能触发的显示事件
            if (eventType is not (EVENT_OBJECT_SHOW or EVENT_OBJECT_CREATE or EVENT_OBJECT_UNCLOAK
                or EVENT_OBJECT_STATECHANGE or EVENT_OBJECT_NAMECHANGE))
                return;

            // 检查是否是系统日历/通知中心窗口
            // 注意：不检查 IsWindowVisible，因为 UNCLOAK 事件时窗口可能尚未完全可见
            if (!IsSystemCalendarWindow(hwnd, procName))
                return;

            // 防抖：500ms 内只处理第一次拦截
            DateTime now = DateTime.UtcNow;
            if (now - _lastInterceptTime < InterceptCooldown)
            {
                LogFile($"Cooldown: skipping duplicate intercept ({(now - _lastInterceptTime).TotalMilliseconds:F0}ms)");
                // 仍然隐藏系统窗口（同样必须跟踪，否则退出时无法恢复其显示状态）
                HideSystemWindow(hwnd);
                return;
            }
            _lastInterceptTime = now;

            LogFile($"Detected system calendar window: {hwnd}");

            // 立即隐藏系统日历窗口，并记录句柄以便恢复
            HideSystemWindow(hwnd);

            // 任一匹配事件都可触发面板：实测 Win11 25H2 点击时钟时最先到达的是 NAMECHANGE，
            // 若只认 SHOW/UNCLOAK，NAMECHANGE 先触发防抖、紧随的 UNCLOAK 又被防抖吞掉，面板永远弹不出来。
            // "隐藏后又显示一次"的噪声是我们恢复 WS_VISIBLE 引发的 SHOW 事件，已在前面精确抑制。

            // 在 UI 线程上触发我们的日历面板
            _dispatcher.BeginInvoke(new Action(() =>
            {
                LogFile("Firing ShowPopup callback on UI thread");
                _showPopupCallback?.Invoke();
            }));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"CornerCalendar: WinEventProc error: {ex.Message}");
        }
    }

    /// <summary>
    /// 清理过期的"最近恢复"记录（在新增记录时顺带执行）。
    /// </summary>
    private void PruneRestored()
    {
        DateTime cutoff = DateTime.UtcNow - RestoreSuppressWindow - RestoreSuppressWindow;
        List<nint> stale = new List<nint>();
        foreach (KeyValuePair<nint, DateTime> kv in _recentlyRestored)
        {
            if (kv.Value < cutoff)
                stale.Add(kv.Key);
        }
        foreach (nint key in stale)
        {
            _recentlyRestored.Remove(key);
        }
    }

    private static readonly object LogFileSync = new();

    private static readonly string LogFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CornerCalendar", "interceptor.log");

    /// <summary>
    /// 诊断日志：写 Debug 的同时追加到 %LOCALAPPDATA%\CornerCalendar\interceptor.log（超过 512KB 重写）。
    /// 只记录 ShellExperienceHost 相关事件与关键决策点，用于在真实环境排查拦截问题。
    /// </summary>
    private static void LogFile(string msg)
    {
        Debug.WriteLine(msg);
        try
        {
            lock (LogFileSync)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogFilePath)!);
                if (File.Exists(LogFilePath) && new FileInfo(LogFilePath).Length > 512 * 1024)
                    File.Delete(LogFilePath);
                File.AppendAllText(LogFilePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {msg}{Environment.NewLine}");
            }
        }
        catch
        {
            // 诊断日志失败不影响主流程
        }
    }

    /// <summary>
    /// 获取进程名（带缓存；进程退出后缓存自动失效）。
    /// 注意：PID 复用理论上可能导致缓存命中过期的进程名，
    /// 但后续还有窗口类名与位置检查兜底，不会造成误拦截。
    /// </summary>
    private string? GetProcessName(uint processId)
    {
        if (processId == 0)
            return null;

        if (_processNameCache.TryGetValue(processId, out string? cached))
            return cached;

        try
        {
            using Process proc = Process.GetProcessById((int)processId);
            _processNameCache[processId] = proc.ProcessName;
            return proc.ProcessName;
        }
        catch
        {
            _processNameCache.Remove(processId);
            return null;
        }
    }

    /// <summary>
    /// 判断窗口是否是 Windows 系统日历/通知中心
    /// </summary>
    private static bool IsSystemCalendarWindow(nint hwnd, string? processName)
    {
        // 调试：记录窗口信息
        System.Text.StringBuilder className = new System.Text.StringBuilder(256);
        GetClassName(hwnd, className, 256);
        string classStr = className.ToString();

        System.Text.StringBuilder title = new System.Text.StringBuilder(256);
        GetWindowText(hwnd, title, 256);

        GetWindowRect(hwnd, out RECT rectInfo);
        Log($"Window shown - Process:'{processName}' " +
            $"Class:'{classStr}' Title:'{title}' " +
            $"Rect:({rectInfo.Left},{rectInfo.Top})-({rectInfo.Right},{rectInfo.Bottom})");

        // Windows 11: 系统日历/通知中心属于 ShellExperienceHost 进程
        // Windows 10: 可能属于 ShellExperienceHost 或 SearchUI
        if (!string.Equals(processName, "ShellExperienceHost", StringComparison.OrdinalIgnoreCase))
            return false;

        LogFile($"SEH window check - Class:'{classStr}' Title:'{title}' Rect:({rectInfo.Left},{rectInfo.Top})-({rectInfo.Right},{rectInfo.Bottom})");

        // 系统日历窗口的类名通常是 Windows.UI.Core.CoreWindow
        // 放宽匹配：只要是右下角的 ShellExperienceHost 窗口就拦截
        if (!classStr.StartsWith("Windows.UI.Core.CoreWindow", StringComparison.OrdinalIgnoreCase))
            return false;

        // 获取窗口位置 —— 系统日历在屏幕右下角
        if (!GetWindowRect(hwnd, out RECT rect))
            return false;

        int windowWidth = rect.Right - rect.Left;
        int windowHeight = rect.Bottom - rect.Top;

        // 使用 MonitorFromWindow 获取窗口所在显示器（支持多显示器）
        nint monitor = MonitorFromWindow(hwnd, 0 /* MONITOR_DEFAULTTONULL */);
        if (monitor == nint.Zero)
        {
            Log("ShellExperienceHost: MonitorFromWindow returned null");
            return false;
        }

        MONITORINFO monitorInfo = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(monitor, ref monitorInfo))
        {
            Log("ShellExperienceHost: GetMonitorInfo failed");
            return false;
        }

        RECT workArea = monitorInfo.rcWork;

        Log($"ShellExperienceHost window - " +
            $"Size:{windowWidth}x{windowHeight} " +
            $"Monitor WorkArea:({workArea.Left},{workArea.Top})-({workArea.Right},{workArea.Bottom}) " +
            $"RightDiff:{Math.Abs(rect.Right - workArea.Right)} " +
            $"BottomDiff:{Math.Abs(rect.Bottom - workArea.Bottom)}");

        // 系统日历/通知中心窗口特征：
        // 1. 右边缘贴近显示器工作区右边缘（允许 50px 误差，考虑不同 DPI）
        // 2. 底部贴近任务栏顶部（允许 50px 误差）
        // 3. 窗口宽度 > 200px
        // 4. 窗口高度 > 100px
        bool rightAligned = Math.Abs(rect.Right - workArea.Right) < 50;
        bool bottomAligned = Math.Abs(rect.Bottom - workArea.Bottom) < 50;
        bool validSize = windowWidth > 200 && windowHeight > 100;

        if (rightAligned && bottomAligned && validSize)
        {
            LogFile("✓ System calendar INTERCEPTED!");
            return true;
        }

        LogFile($"SEH window NOT matched: rightAligned={rightAligned} bottomAligned={bottomAligned} validSize={validSize}");
        return false;
    }

    /// <summary>
    /// 隐藏系统窗口并跟踪句柄。所有隐藏操作必须走这里，
    /// 保证之后（shell 关闭弹窗或应用退出时）能恢复其显示状态。
    /// </summary>
    private void HideSystemWindow(nint hwnd)
    {
        ShowWindow(hwnd, SW_HIDE);
        lock (_hiddenWindows)
        {
            _hiddenWindows.Add(hwnd);
        }
    }

    /// <summary>
    /// 恢复被跟踪窗口的 WS_VISIBLE 标志（窗口被 shell cloak 时调用，详见 WinEventProc 中的说明）。
    /// 使用 SW_SHOWNA：只恢复 WS_VISIBLE，不激活窗口；窗口此时已被 cloak，不会产生可见弹窗。
    /// </summary>
    private void RestoreTrackedWindow(nint hwnd)
    {
        bool tracked;
        lock (_hiddenWindows)
        {
            tracked = _hiddenWindows.Remove(hwnd);
        }

        if (tracked && IsWindow(hwnd))
        {
            ShowWindow(hwnd, SW_SHOWNA);

            // 本次恢复会让系统补发一个 SHOW 事件，记下来以便在抑制窗口内忽略它
            lock (_recentlyRestored)
            {
                PruneRestored();
                _recentlyRestored[hwnd] = DateTime.UtcNow;
            }

            LogFile($"Restored WS_VISIBLE for cloaked window: {hwnd}");
        }
    }

    /// <summary>
    /// 恢复所有被隐藏的系统日历窗口，确保退出后系统日历正常工作。
    /// 必须恢复 WS_VISIBLE（SW_SHOWNA，不抢焦点）：Win11 shell flyout 靠 DWM cloak 隐藏/显示，
    /// shell 重新显示时不会重设 WS_VISIBLE，不恢复则系统日历之后无法再显示。
    /// 若窗口已被 shell cloak，此恢复无可见效果；否则系统弹窗会短暂可见（点击时钟即可正常关闭）。
    /// </summary>
    public void RestoreHiddenWindows()
    {
        nint[] pending;
        lock (_hiddenWindows)
        {
            pending = new nint[_hiddenWindows.Count];
            _hiddenWindows.CopyTo(pending);
            _hiddenWindows.Clear();
        }

        foreach (nint hwnd in pending)
        {
            try
            {
                if (!IsWindow(hwnd))
                    continue; // 句柄已销毁，无需恢复

                ShowWindow(hwnd, SW_SHOWNA);
                LogFile($"Restored hidden window: {hwnd}");
            }
            catch { }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // 退出前恢复被隐藏的系统窗口
        RestoreHiddenWindows();

        foreach (nint hook in _hooks)
        {
            UnhookWinEvent(hook);
        }
        _hooks.Clear();

        if (_gcHandle.IsAllocated)
        {
            _gcHandle.Free();
        }

        Debug.WriteLine("CornerCalendar: SystemCalendarInterceptor disposed");
    }
}