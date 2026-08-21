using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;

namespace CornerCalendar.Views;

/// <summary>
/// 覆盖 Windows 任务栏时钟的轻量窗口。
/// 优先隐藏独立的系统时钟子窗口并使用透明覆盖层；无法取得独立句柄时，
/// 退回到任务栏右下角的覆盖区域，保证点击和右键菜单仍由本程序接管。
/// </summary>
public partial class TaskbarClockWindow : Window
{
    private const int SW_HIDE = 0;
    private const int SW_SHOWNA = 8;
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_SHOWWINDOW = 0x0040;
    private static readonly nint HwndTopmost = new(-1);

    private readonly nint _taskbarHandle;
    private readonly HashSet<nint> _hiddenClockHandles = new();
    private RECT _clockRect;
    private RECT _taskbarRect;
    private bool _hasClockRect;
    private bool _hasTaskbarRect;
    private bool _isClosed;
    private System.Windows.Media.Brush? _clockTextBrush;

    public event Action<nint>? ClockClicked;

    public TaskbarClockWindow(nint taskbarHandle)
    {
        _taskbarHandle = taskbarHandle;
        InitializeComponent();
        ClockText.SetResourceReference(
            System.Windows.Controls.TextBlock.ForegroundProperty, "TaskbarClockTextBrush");
        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
    }

    public void UpdateText(string text)
    {
        if (_isClosed)
            return;

        ClockText.Text = text;
        Dispatcher.BeginInvoke(new Action(Reposition), DispatcherPriority.Loaded);
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        nint hwnd = new WindowInteropHelper(this).Handle;
        nint exStyle = GetWindowLongPtr(hwnd, GWL_EXSTYLE);
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, exStyle.ToInt64() | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Reposition();
    }

    private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        ClockClicked?.Invoke(GetTaskbarMonitor());
    }

    private void OnMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (ContextMenu != null)
        {
            ContextMenu.PlacementTarget = this;
            ContextMenu.IsOpen = true;
            e.Handled = true;
        }
    }

    private void Reposition()
    {
        if (_isClosed)
            return;

        if (!IsVisible)
            Show();

        EnsureSystemClockState();

        double dpiScaleX = 1;
        double dpiScaleY = 1;
        if (PresentationSource.FromVisual(this)?.CompositionTarget is { } target)
        {
            dpiScaleX = target.TransformFromDevice.M11;
            dpiScaleY = target.TransformFromDevice.M22;
        }

        ClockText.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double contentWidth = ClockText.DesiredSize.Width + 16;
        double contentHeight = ClockText.DesiredSize.Height + 6;

        double right;
        double bottom;
        double availableWidth;
        double availableHeight;

        if (_hasClockRect)
        {
            right = _clockRect.Right * dpiScaleX;
            bottom = _clockRect.Bottom * dpiScaleY;
            availableWidth = (_clockRect.Right - _clockRect.Left) * dpiScaleX;
            availableHeight = (_clockRect.Bottom - _clockRect.Top) * dpiScaleY;
        }
        else if (_hasTaskbarRect)
        {
            right = _taskbarRect.Right * dpiScaleX;
            bottom = _taskbarRect.Bottom * dpiScaleY;
            availableWidth = 88;
            availableHeight = (_taskbarRect.Bottom - _taskbarRect.Top) * dpiScaleY;
        }
        else
        {
            Rect workArea = SystemParameters.WorkArea;
            right = workArea.Right;
            bottom = workArea.Bottom + 48;
            availableWidth = 88;
            availableHeight = 48;
        }

        Width = Math.Max(availableWidth, contentWidth);
        Height = Math.Max(availableHeight, contentHeight);
        Left = right - Width;
        Top = bottom - Height;

        // Windows 任务栏自身可能处于特殊的置顶层级，仅设置 WPF Topmost 有时仍会被它盖住。
        // 重新定位后强制把覆盖层放到 HWND_TOPMOST，且不激活窗口，避免系统任务中心先响应点击。
        nint hwnd = new WindowInteropHelper(this).Handle;
        SetWindowPos(hwnd, HwndTopmost, 0, 0, 0, 0,
            SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
    }

    private void EnsureSystemClockState()
    {
        nint taskbar = _taskbarHandle != nint.Zero
            ? _taskbarHandle
            : FindWindow("Shell_TrayWnd", null);
        nint clock = FindClockWindow(taskbar);

        if (taskbar != nint.Zero && GetWindowRect(taskbar, out RECT currentTaskbarRect))
        {
            _taskbarRect = currentTaskbarRect;
            _hasTaskbarRect = true;
        }

        if (clock != nint.Zero && IsWindow(clock))
        {
            bool isFirstHide = _hiddenClockHandles.Add(clock);

            if (GetWindowRect(clock, out RECT rect))
            {
                _clockRect = rect;
                _hasClockRect = true;
                // 必须在隐藏原生时钟前读取屏幕像素，否则隐藏后只剩覆盖层，
                // 无法再判断当前显示器实际使用的是深色还是浅色文字。
                if (isFirstHide)
                    _clockTextBrush = TryGetClockTextBrush(rect);
            }

            System.Windows.Media.Brush? taskbarBrush = TryGetTaskbarBackgroundBrush();

            ShowWindow(clock, SW_HIDE);
            ClockSurface.Background = System.Windows.Media.Brushes.Transparent;
            if (_clockTextBrush is not null)
                ClockText.Foreground = _clockTextBrush;
            else
                ApplyClockTextContrast(taskbarBrush);
            return;
        }

        _hasClockRect = false;
        System.Windows.Media.Brush? fallbackTaskbarBrush = TryGetTaskbarBackgroundBrush();
        // 没有独立时钟句柄时，使用任务栏背景色遮盖系统时间。
        // 取色失败时不能使用透明背景，否则系统原生时间会透出来。
        if (fallbackTaskbarBrush is not null)
        {
            ClockSurface.Background = fallbackTaskbarBrush;
            ApplyClockTextContrast(fallbackTaskbarBrush);
        }
        else
        {
            ClockSurface.SetResourceReference(
                System.Windows.Controls.Border.BackgroundProperty, "TaskbarClockFallbackBrush");
            ClockText.SetResourceReference(
                System.Windows.Controls.TextBlock.ForegroundProperty, "TaskbarClockTextBrush");
        }
    }

    private void ApplyClockTextContrast(System.Windows.Media.Brush? taskbarBrush)
    {
        if (taskbarBrush is not System.Windows.Media.SolidColorBrush solidBrush)
        {
            ClockText.SetResourceReference(
                System.Windows.Controls.TextBlock.ForegroundProperty, "TaskbarClockTextBrush");
            return;
        }

        System.Windows.Media.Color color = solidBrush.Color;
        double luminance = (0.2126 * color.R + 0.7152 * color.G + 0.0722 * color.B) / 255.0;
        ClockText.Foreground = luminance > 0.55
            ? System.Windows.Media.Brushes.Black
            : System.Windows.Media.Brushes.White;
    }

    /// <summary>
    /// 读取任务栏非时钟区域的实际屏幕颜色，避免用固定主题色遮罩造成首启色差。
    /// Windows 任务栏可能启用透明效果，取屏幕后颜色比资源字典中的近似色更准确。
    /// </summary>
    private System.Windows.Media.Brush? TryGetTaskbarBackgroundBrush()
    {
        if (!_hasTaskbarRect || _taskbarRect.Right <= _taskbarRect.Left)
            return null;

        int sampleX = _hasClockRect
            ? Math.Max(_taskbarRect.Left, _clockRect.Left - 4)
            : _taskbarRect.Right - Math.Max(2, Math.Min(8, _taskbarRect.Right - _taskbarRect.Left - 1));
        int sampleY = _hasClockRect
            ? (_clockRect.Top + _clockRect.Bottom) / 2
            : _taskbarRect.Bottom - Math.Max(2, (_taskbarRect.Bottom - _taskbarRect.Top) / 2);
        nint screenDc = GetDC(nint.Zero);
        if (screenDc == nint.Zero)
            return null;

        try
        {
            uint pixel = GetPixel(screenDc, sampleX, sampleY);
            if (pixel == 0xFFFFFFFF && Marshal.GetLastWin32Error() != 0)
                return null;

            byte red = (byte)(pixel & 0xFF);
            byte green = (byte)((pixel >> 8) & 0xFF);
            byte blue = (byte)((pixel >> 16) & 0xFF);
            System.Windows.Media.SolidColorBrush brush = new(
                System.Windows.Media.Color.FromRgb(red, green, blue));
            brush.Freeze();
            return brush;
        }
        finally
        {
            ReleaseDC(nint.Zero, screenDc);
        }
    }

    private System.Windows.Media.Brush? TryGetClockTextBrush(RECT clockRect)
    {
        int width = clockRect.Right - clockRect.Left;
        int height = clockRect.Bottom - clockRect.Top;
        if (width <= 0 || height <= 0)
            return null;

        nint screenDc = GetDC(nint.Zero);
        if (screenDc == nint.Zero)
            return null;

        try
        {
            Dictionary<uint, int> pixels = new();
            for (int y = clockRect.Top; y < clockRect.Bottom; y++)
            {
                for (int x = clockRect.Left; x < clockRect.Right; x++)
                {
                    uint pixel = GetPixel(screenDc, x, y);

                    // 将相邻的抗锯齿颜色归并，避免单个边缘像素干扰主色判断。
                    byte red = (byte)(pixel & 0xFF);
                    byte green = (byte)((pixel >> 8) & 0xFF);
                    byte blue = (byte)((pixel >> 16) & 0xFF);
                    uint quantized = (uint)((red / 16) << 16 | (green / 16) << 8 | blue / 16);
                    pixels[quantized] = pixels.TryGetValue(quantized, out int count) ? count + 1 : 1;
                }
            }

            if (pixels.Count < 2)
                return null;

            uint background = pixels.OrderByDescending(pair => pair.Value).First().Key;
            int backgroundRed = (int)((background >> 16) & 0xFF) * 16 + 8;
            int backgroundGreen = (int)((background >> 8) & 0xFF) * 16 + 8;
            int backgroundBlue = (int)(background & 0xFF) * 16 + 8;

            KeyValuePair<uint, int>? textPixel = pixels
                .Where(pair =>
                {
                    int red = (int)((pair.Key >> 16) & 0xFF) * 16 + 8;
                    int green = (int)((pair.Key >> 8) & 0xFF) * 16 + 8;
                    int blue = (int)(pair.Key & 0xFF) * 16 + 8;
                    int distance = Math.Abs(red - backgroundRed)
                        + Math.Abs(green - backgroundGreen)
                        + Math.Abs(blue - backgroundBlue);
                    return distance >= 90;
                })
                .OrderByDescending(pair => pair.Value)
                .Cast<KeyValuePair<uint, int>?>()
                .FirstOrDefault();

            if (textPixel is null)
                return null;

            uint color = textPixel.Value.Key;
            byte textRed = (byte)Math.Min(255, ((color >> 16) & 0xFF) * 16 + 8);
            byte textGreen = (byte)Math.Min(255, ((color >> 8) & 0xFF) * 16 + 8);
            byte textBlue = (byte)Math.Min(255, (color & 0xFF) * 16 + 8);
            System.Windows.Media.SolidColorBrush brush = new(
                System.Windows.Media.Color.FromRgb(textRed, textGreen, textBlue));
            brush.Freeze();
            return brush;
        }
        finally
        {
            ReleaseDC(nint.Zero, screenDc);
        }
    }

    private void RestoreSystemClock()
    {
        foreach (nint handle in _hiddenClockHandles)
        {
            if (IsWindow(handle))
                ShowWindow(handle, SW_SHOWNA);
        }

        _hiddenClockHandles.Clear();
    }

    protected override void OnClosed(EventArgs e)
    {
        _isClosed = true;
        RestoreSystemClock();
        base.OnClosed(e);
    }

    private static nint FindClockWindow(nint taskbar)
    {
        if (taskbar == nint.Zero)
            return nint.Zero;

        nint direct = FindWindowEx(taskbar, nint.Zero, "TrayClockWClass", null);
        if (direct != nint.Zero)
            return direct;

        nint found = nint.Zero;
        EnumChildWindows(taskbar, (hwnd, _) =>
        {
            StringBuilder className = new(128);
            GetClassName(hwnd, className, className.Capacity);
            if (className.ToString().Contains("Clock", StringComparison.OrdinalIgnoreCase))
            {
                found = hwnd;
                return false;
            }

            return true;
        }, nint.Zero);

        return found;
    }

    public static IReadOnlyList<nint> FindTaskbarWindows()
    {
        List<nint> taskbars = new();
        EnumWindows((hwnd, _) =>
        {
            StringBuilder className = new(128);
            GetClassName(hwnd, className, className.Capacity);
            string name = className.ToString();
            if (name is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd")
                taskbars.Add(hwnd);
            return true;
        }, nint.Zero);

        return taskbars;
    }

    public static nint FindPrimaryTaskbarWindow()
    {
        foreach (nint taskbar in FindTaskbarWindows())
        {
            nint monitor = MonitorFromWindow(taskbar, MONITOR_DEFAULTTONEAREST);
            MONITORINFO monitorInfo = new() { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (monitor != nint.Zero && GetMonitorInfo(monitor, ref monitorInfo)
                && (monitorInfo.Flags & MONITORINFOF_PRIMARY) != 0)
            {
                return taskbar;
            }
        }

        // 主任务栏正常情况下就是 Shell_TrayWnd；枚举失败时仍只回退到它。
        return FindWindow("Shell_TrayWnd", null);
    }

    private nint GetTaskbarMonitor()
    {
        nint monitor = MonitorFromWindow(_taskbarHandle, MONITOR_DEFAULTTONEAREST);
        return monitor;
    }

    private delegate bool EnumWindowProc(nint hwnd, nint lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint FindWindow(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowProc callback, nint lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint FindWindowEx(nint parentHandle, nint childAfter, string? className, string? windowTitle);

    [DllImport("user32.dll")]
    private static extern bool EnumChildWindows(nint parentHandle, EnumWindowProc callback, nint lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(nint hwnd, StringBuilder className, int maxCount);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(nint hwnd, out RECT rect);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(nint hwnd, int command);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(nint hwnd);

    [DllImport("user32.dll")]
    private static extern nint MonitorFromWindow(nint hwnd, uint flags);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(nint monitor, ref MONITORINFO monitorInfo);

    private const uint MONITOR_DEFAULTTONEAREST = 2;
    private const uint MONITORINFOF_PRIMARY = 1;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(
        nint hwnd,
        nint hwndInsertAfter,
        int x,
        int y,
        int cx,
        int cy,
        uint flags);

    [DllImport("user32.dll")]
    private static extern nint GetDC(nint hwnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(nint hwnd, nint dc);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern uint GetPixel(nint dc, int x, int y);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr(nint hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern nint SetWindowLongPtr(nint hwnd, int index, long value);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT Monitor;
        public RECT Work;
        public uint Flags;
    }
}