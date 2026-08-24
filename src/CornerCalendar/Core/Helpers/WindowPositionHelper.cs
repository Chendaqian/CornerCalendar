using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace CornerCalendar.Core.Helpers;

/// <summary>
/// 弹出窗口定位工具：使用 Win32 API 获取正确的显示器工作区，处理 DPI 缩放
/// </summary>
public static class WindowPositionHelper
{
    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    private static extern nint MonitorFromWindow(nint hwnd, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern nint MonitorFromPoint(POINT point, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(nint hMonitor, ref MONITORINFO lpmi);

    /// <summary>
    /// 将窗口定位到任务栏右下角上方，同时限制最大高度不超出屏幕。
    /// 必须在窗口 Show() 之后调用（需要窗口句柄和 DPI 信息）。
    /// </summary>
    public static void PositionNearTaskbar(Window window)
    {
        PositionNearTaskbar(window, nint.Zero);
    }

    public static void PositionNearTaskbar(Window window, nint monitorHandle)
    {
        PresentationSource source = PresentationSource.FromVisual(window);
        if (source?.CompositionTarget == null) return;

        // DPI 缩放因子：物理像素 → DIPs
        double dpiScaleX = source.CompositionTarget.TransformFromDevice.M11;
        double dpiScaleY = source.CompositionTarget.TransformFromDevice.M22;

        // 获取窗口所在显示器的句柄
        nint hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == nint.Zero) return;

        // Windows 的主显示器包含虚拟屏幕坐标 (0, 0)，通过系统 API 获取主显示器，
        // 不使用触发来源显示器，确保程序窗口始终只出现在主显示器。
        nint hMonitor = MonitorFromPoint(new POINT { X = 0, Y = 0 }, 2 /* MONITOR_DEFAULTTONEAREST */);
        if (hMonitor == nint.Zero)
            hMonitor = monitorHandle != nint.Zero
                ? monitorHandle
                : MonitorFromWindow(hwnd, 2 /* MONITOR_DEFAULTTONEAREST */);
        MONITORINFO monitorInfo = new MONITORINFO { cbSize = Marshal.SizeOf(typeof(MONITORINFO)) };

        double workRight, workBottom, workTop;

        if (GetMonitorInfo(hMonitor, ref monitorInfo))
        {
            // Win32 返回的是物理像素，转为 DIPs
            workRight = monitorInfo.rcWork.Right * dpiScaleX;
            workBottom = monitorInfo.rcWork.Bottom * dpiScaleY;
            workTop = monitorInfo.rcWork.Top * dpiScaleY;
        }
        else
        {
            // 回退
            Rect workArea = SystemParameters.WorkArea;
            workRight = workArea.Right;
            workBottom = workArea.Bottom;
            workTop = workArea.Top;
        }

        // 可用高度（工作区顶部到底部）
        double availableHeight = workBottom - workTop;
        double margin = 8; // 底部留白

        // 限制窗口最大高度不超过可用高度
        double maxHeight = availableHeight - margin;
        if (window.MaxHeight > 0 && window.MaxHeight < maxHeight)
        {
            // XAML 中设置的 MaxHeight 更小则尊重它
            maxHeight = window.MaxHeight;
        }
        window.MaxHeight = maxHeight;

        // 使用窗口实际渲染尺寸
        double windowWidth = window.ActualWidth;
        double windowHeight = window.ActualHeight;

        if (windowWidth <= 0 || windowHeight <= 0)
        {
            window.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            windowWidth = window.DesiredSize.Width;
            windowHeight = window.DesiredSize.Height;
        }

        // 确保不会超出最大高度
        windowHeight = Math.Min(windowHeight, maxHeight);

        double left = workRight - windowWidth - 12;
        double top = workBottom - windowHeight - margin;

        if (left < 0) left = 4;
        if (top < workTop) top = workTop;

        window.Left = left;
        window.Top = top;
    }

    public static void PositionBeside(Window popup, Window anchor)
    {
        PresentationSource? source = PresentationSource.FromVisual(popup);
        if (source?.CompositionTarget == null)
            return;

        nint anchorHandle = new WindowInteropHelper(anchor).Handle;
        nint monitor = MonitorFromWindow(anchorHandle, 2);
        MONITORINFO monitorInfo = new() { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(monitor, ref monitorInfo))
            return;

        double scaleX = source.CompositionTarget.TransformFromDevice.M11;
        double scaleY = source.CompositionTarget.TransformFromDevice.M22;
        double workLeft = monitorInfo.rcWork.Left * scaleX;
        double workRight = monitorInfo.rcWork.Right * scaleX;
        double workTop = monitorInfo.rcWork.Top * scaleY;
        double workBottom = monitorInfo.rcWork.Bottom * scaleY;
        double left = anchor.Left - popup.ActualWidth - 8;
        if (left < workLeft)
            left = anchor.Left + anchor.ActualWidth + 8;

        popup.Left = Math.Max(workLeft, Math.Min(left, workRight - popup.ActualWidth));
        // 与主窗口底部对齐；空间不足时再向工作区内收拢。
        double top = anchor.Top + anchor.ActualHeight - popup.ActualHeight;
        popup.Top = Math.Max(workTop, Math.Min(top, workBottom - popup.ActualHeight));
    }

    public static void PositionAbove(Window popup, Window anchor)
    {
        PresentationSource? source = PresentationSource.FromVisual(popup);
        if (source?.CompositionTarget == null)
            return;

        nint anchorHandle = new WindowInteropHelper(anchor).Handle;
        nint monitor = MonitorFromWindow(anchorHandle, 2);
        MONITORINFO monitorInfo = new() { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(monitor, ref monitorInfo))
            return;

        double scaleX = source.CompositionTarget.TransformFromDevice.M11;
        double scaleY = source.CompositionTarget.TransformFromDevice.M22;
        double workLeft = monitorInfo.rcWork.Left * scaleX;
        double workRight = monitorInfo.rcWork.Right * scaleX;
        double workTop = monitorInfo.rcWork.Top * scaleY;
        double workBottom = monitorInfo.rcWork.Bottom * scaleY;
        double popupWidth = popup.ActualWidth;
        double popupHeight = popup.ActualHeight;

        double left = anchor.Left + (anchor.ActualWidth - popupWidth) / 2;
        left = Math.Max(workLeft, Math.Min(left, workRight - popupWidth));

        double top = anchor.Top - popupHeight - 8;
        if (top < workTop)
            top = anchor.Top + anchor.ActualHeight + 8;
        top = Math.Max(workTop, Math.Min(top, workBottom - popupHeight));

        popup.Left = left;
        popup.Top = top;
    }

    public static void PositionLeftAligned(Window popup, Window anchor)
    {
        PresentationSource? source = PresentationSource.FromVisual(popup);
        if (source?.CompositionTarget == null)
            return;

        nint anchorHandle = new WindowInteropHelper(anchor).Handle;
        nint monitor = MonitorFromWindow(anchorHandle, 2);
        MONITORINFO monitorInfo = new() { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(monitor, ref monitorInfo))
            return;

        double scaleX = source.CompositionTarget.TransformFromDevice.M11;
        double scaleY = source.CompositionTarget.TransformFromDevice.M22;
        double workLeft = monitorInfo.rcWork.Left * scaleX;
        double workRight = monitorInfo.rcWork.Right * scaleX;
        double workTop = monitorInfo.rcWork.Top * scaleY;
        double workBottom = monitorInfo.rcWork.Bottom * scaleY;
        double popupWidth = popup.ActualWidth;
        double popupHeight = popup.ActualHeight;

        double left = anchor.Left - popupWidth - 8;
        if (left < workLeft)
            left = anchor.Left + anchor.ActualWidth + 8;
        left = Math.Max(workLeft, Math.Min(left, workRight - popupWidth));

        double top = Math.Max(workTop, Math.Min(anchor.Top, workBottom - popupHeight));
        popup.Left = left;
        popup.Top = top;
    }
}