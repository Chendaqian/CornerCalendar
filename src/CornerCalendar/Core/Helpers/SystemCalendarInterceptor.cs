using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;

namespace CornerCalendar.Core.Helpers;

/// <summary>
/// 监控并替换 Windows 系统日历弹窗。
/// 当检测到系统日历/通知中心弹出时，立即隐藏它并触发我们的日历面板。
/// </summary>
public class SystemCalendarInterceptor : IDisposable
{
    private nint _hook;
    private GCHandle _gcHandle;
    private readonly Dispatcher _dispatcher;
    private Action? _showPopupCallback;
    private bool _disposed;
    private DateTime _lastInterceptTime;
    private static readonly TimeSpan InterceptCooldown = TimeSpan.FromMilliseconds(500);
    private readonly HashSet<nint> _hiddenWindows = new();

    // Win32 常量
    private const uint EVENT_MIN = 0x0001;  // EVENT_MIN
    private const uint EVENT_OBJECT_SHOW = 0x8002;
    private const uint EVENT_OBJECT_HIDE = 0x8003;
    private const uint EVENT_OBJECT_CREATE = 0x8001;
    private const uint EVENT_OBJECT_UNCLOAK = 0x8018;
    private const uint EVENT_OBJECT_CLOAK = 0x8017;
    private const uint EVENT_OBJECT_NAMECHANGE = 0x800C;
    private const uint EVENT_OBJECT_STATECHANGE = 0x800A;
    private const uint EVENT_MAX = 0x80FF;
    private const int WINEVENT_OUTOFCONTEXT = 0;
    private const int SW_HIDE = 0;
    private const int SW_SHOW = 5;

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
    private static extern uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(nint hWnd, out RECT lpRect);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(nint hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(nint hWnd);

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

    /// <summary>
    /// 启动拦截器，传入弹出日历面板的回调
    /// </summary>
    private static void Log(string msg) => Debug.WriteLine(msg);

    public void Start(Action showPopupCallback)
    {
        _showPopupCallback = showPopupCallback;

        // 使用 GC handle 防止委托被垃圾回收
        _gcHandle = GCHandle.Alloc(new WinEventDelegate(WinEventProc));

        // 捕获委托引用
        WinEventDelegate callback = (WinEventDelegate)_gcHandle.Target!;

        // 监听更广范围的事件（捕捉系统日历的各种显示方式）
        _hook = SetWinEventHook(
            EVENT_OBJECT_SHOW, EVENT_MAX,
            nint.Zero, callback,
            0, 0,
            WINEVENT_OUTOFCONTEXT);

        if (_hook == nint.Zero)
        {
            Log("CornerCalendar: ✗ Failed to set WinEvent hook!");
        }
        else
        {
            Log("CornerCalendar: ✓ SystemCalendarInterceptor started, hook=" + _hook);
        }
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
            string? procName = null;
            try { procName = Process.GetProcessById((int)processId).ProcessName; } catch { }

            // 记录 ShellExperienceHost 的所有事件
            if (string.Equals(procName, "ShellExperienceHost", StringComparison.OrdinalIgnoreCase))
            {
                System.Text.StringBuilder className = new System.Text.StringBuilder(256);
                GetClassName(hwnd, className, 256);
                GetWindowRect(hwnd, out RECT r);
                string evtName = EventTypeNames.GetValueOrDefault(eventType, $"0x{eventType:X}");
                Log($"★★★ ShellExperienceHost evt={evtName}(0x{eventType:X}) hwnd={hwnd} idObj={idObject} idChild={idChild} " +
                    $"Class='{className}' Rect:({r.Left},{r.Top})-({r.Right},{r.Bottom})");
            }

            // 只处理可能触发的显示事件
            if (eventType is not (0x8002 or 0x8001 or 0x8018 or 0x800A or 0x800C))
                return;

            // 检查是否是系统日历/通知中心窗口
            // 注意：不检查 IsWindowVisible，因为 UNCLOAK 事件时窗口可能尚未完全可见
            if (!IsSystemCalendarWindow(hwnd))
                return;

            // 防抖：500ms 内只处理第一次拦截
            DateTime now = DateTime.UtcNow;
            if (now - _lastInterceptTime < InterceptCooldown)
            {
                Log($"Cooldown: skipping duplicate intercept ({(now - _lastInterceptTime).TotalMilliseconds:F0}ms)");
                // 仍然隐藏系统窗口
                ShowWindow(hwnd, SW_HIDE);
                return;
            }
            _lastInterceptTime = now;

            Log($"Detected system calendar window: {hwnd}");

            // 立即隐藏系统日历窗口，并记录句柄以便退出时恢复
            ShowWindow(hwnd, SW_HIDE);
            lock (_hiddenWindows)
            {
                _hiddenWindows.Add(hwnd);
            }

            // 在 UI 线程上触发我们的日历面板
            _dispatcher.BeginInvoke(new Action(() =>
            {
                Log("Firing ShowPopup callback on UI thread");
                _showPopupCallback?.Invoke();
            }));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"CornerCalendar: WinEventProc error: {ex.Message}");
        }
    }

    /// <summary>
    /// 判断窗口是否是 Windows 系统日历/通知中心
    /// </summary>
    private static bool IsSystemCalendarWindow(nint hwnd)
    {
        // 获取进程 ID
        GetWindowThreadProcessId(hwnd, out uint processId);

        // 获取进程名
        string? processName = null;
        try
        {
            Process proc = Process.GetProcessById((int)processId);
            processName = proc.ProcessName;
        }
        catch
        {
            return false;
        }

        // 调试：记录所有可见窗口
        System.Text.StringBuilder className = new System.Text.StringBuilder(256);
        GetClassName(hwnd, className, 256);
        string classStr = className.ToString();

        System.Text.StringBuilder title = new System.Text.StringBuilder(256);
        GetWindowText(hwnd, title, 256);

        GetWindowRect(hwnd, out RECT rectInfo);
        Log($"Window shown - PID:{processId}({processName}) " +
            $"Class:'{classStr}' Title:'{title}' " +
            $"Rect:({rectInfo.Left},{rectInfo.Top})-({rectInfo.Right},{rectInfo.Bottom})");

        // Windows 11: 系统日历/通知中心属于 ShellExperienceHost 进程
        // Windows 10: 可能属于 ShellExperienceHost 或 SearchUI
        if (!string.Equals(processName, "ShellExperienceHost", StringComparison.OrdinalIgnoreCase))
            return false;

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
            Log("✓ System calendar INTERCEPTED!");
            return true;
        }

        return false;
    }

    /// <summary>
    /// 恢复所有被隐藏的系统日历窗口，确保退出后系统日历正常工作
    /// </summary>
    public void RestoreHiddenWindows()
    {
        lock (_hiddenWindows)
        {
            foreach (nint hwnd in _hiddenWindows)
            {
                try
                {
                    ShowWindow(hwnd, SW_SHOW);
                    Log($"Restored hidden window: {hwnd}");
                }
                catch { }
            }
            _hiddenWindows.Clear();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // 退出前恢复被隐藏的系统窗口
        RestoreHiddenWindows();

        if (_hook != nint.Zero)
        {
            UnhookWinEvent(_hook);
            _hook = nint.Zero;
        }

        if (_gcHandle.IsAllocated)
        {
            _gcHandle.Free();
        }

        Debug.WriteLine("CornerCalendar: SystemCalendarInterceptor disposed");
    }
}
