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

    private readonly HashSet<nint> _hiddenClockHandles = new();
    private RECT _clockRect;
    private RECT _taskbarRect;
    private bool _hasClockRect;
    private bool _hasTaskbarRect;
    private bool _isClosed;

    public event RoutedEventHandler? ClockClicked;

    public TaskbarClockWindow()
    {
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
        ClockClicked?.Invoke(this, new RoutedEventArgs());
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
        nint taskbar = FindWindow("Shell_TrayWnd", null);
        nint clock = FindClockWindow(taskbar);

        if (taskbar != nint.Zero && GetWindowRect(taskbar, out RECT currentTaskbarRect))
        {
            _taskbarRect = currentTaskbarRect;
            _hasTaskbarRect = true;
        }

        System.Windows.Media.Brush? taskbarBrush = TryGetTaskbarBackgroundBrush();

        if (clock != nint.Zero && IsWindow(clock))
        {
            _hiddenClockHandles.Add(clock);

            if (GetWindowRect(clock, out RECT rect))
            {
                _clockRect = rect;
                _hasClockRect = true;
            }

            ShowWindow(clock, SW_HIDE);
            ClockSurface.Background = System.Windows.Media.Brushes.Transparent;
            ApplyClockTextContrast(taskbarBrush);
            return;
        }

        _hasClockRect = false;
        // 没有独立时钟句柄时，使用任务栏背景色遮盖系统时间。
        // 取色失败时不能使用透明背景，否则系统原生时间会透出来。
        if (taskbarBrush is not null)
        {
            ClockSurface.Background = taskbarBrush;
            ApplyClockTextContrast(taskbarBrush);
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

        int sampleX = _taskbarRect.Left + Math.Min(2, _taskbarRect.Right - _taskbarRect.Left - 1);
        int sampleY = _taskbarRect.Top + Math.Max(1, (_taskbarRect.Bottom - _taskbarRect.Top) / 2);
        nint screenDc = GetDC(nint.Zero);
        if (screenDc == nint.Zero)
            return null;

        try
        {
            uint pixel = GetPixel(screenDc, sampleX, sampleY);
            if (pixel == 0xFFFFFFFF)
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

    private delegate bool EnumWindowProc(nint hwnd, nint lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint FindWindow(string? lpClassName, string? lpWindowName);

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

    [DllImport("gdi32.dll")]
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
}