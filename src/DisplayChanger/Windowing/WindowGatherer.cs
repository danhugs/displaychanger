using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using static DisplayChanger.Native.NativeMethods;

namespace DisplayChanger.Windowing;

/// <summary>Moves top-level application windows between displays.</summary>
public sealed class WindowGatherer
{
    /// <summary>Outcome of a gather.</summary>
    /// <param name="TargetDeviceName">GDI name of the display the windows were moved to, e.g. <c>\\.\DISPLAY2</c>.</param>
    /// <param name="Moved">Windows repositioned onto the target.</param>
    /// <param name="Failed">Windows that refused the move (typically elevated apps when this app is not elevated).</param>
    /// <param name="AlreadyThere">Windows that were already on the target and were left alone.</param>
    public sealed record Result(string TargetDeviceName, int Moved, int Failed, int AlreadyThere);

    // Shell and system windows that are top-level and visible but must never be moved.
    private static readonly HashSet<string> IgnoredClasses = new(StringComparer.Ordinal)
    {
        "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "NotifyIconOverflowWindow",
        "Windows.UI.Core.CoreWindow", "Xaml_WindowedPopupClass", "ForegroundStaging", "MultitaskingViewFrame",
    };

    /// <summary>
    /// Moves every ordinary top-level window onto the display that contains the foreground window
    /// (or the primary display if there is no usable foreground window). Z-order and minimised /
    /// maximised state are preserved; the foreground window stays in front.
    /// </summary>
    public Result GatherToActiveDisplay()
    {
        var foreground = GetForegroundWindow();
        var target = foreground != IntPtr.Zero ? MonitorFromWindow(foreground, MONITOR_DEFAULTTONEAREST) : IntPtr.Zero;
        if (target == IntPtr.Zero) target = MonitorFromPoint(new POINT(), MONITOR_DEFAULTTOPRIMARY);

        var targetInfo = GetMonitor(target) ?? throw new InvalidOperationException("Could not read the target display.");

        var windows = new List<IntPtr>();
        EnumWindows((h, _) => { if (IsCandidate(h)) windows.Add(h); return true; }, IntPtr.Zero);

        int moved = 0, failed = 0, already = 0;

        // EnumWindows yields front-to-back. Work back-to-front so that any activation caused by
        // re-showing a maximised window leaves the original z-order intact.
        for (int i = windows.Count - 1; i >= 0; i--)
        {
            var h = windows[i];
            if (MonitorFromWindow(h, MONITOR_DEFAULTTONEAREST) == target) { already++; continue; }
            if (MoveToMonitor(h, targetInfo)) moved++;
            else failed++;
        }

        if (foreground != IntPtr.Zero) SetForegroundWindow(foreground);

        return new Result(targetInfo.szDevice, moved, failed, already);
    }

    /// <summary>
    /// Moves one window onto <paramref name="monitor"/>, keeping its offset within the work area where possible
    /// and its minimised / maximised state. Returns false if Windows rejected the change.
    /// </summary>
    public bool MoveToMonitor(IntPtr hwnd, IntPtr monitor)
    {
        var info = GetMonitor(monitor);
        return info is not null && MoveToMonitor(hwnd, info.Value);
    }

    private static bool MoveToMonitor(IntPtr hwnd, MONITORINFOEX targetInfo)
    {
        if (IsHungAppWindow(hwnd)) return false;

        var wp = new WINDOWPLACEMENT { length = (uint)Marshal.SizeOf<WINDOWPLACEMENT>() };
        if (!GetWindowPlacement(hwnd, ref wp)) return false;

        // rcNormalPosition is in "workspace" coordinates: screen coordinates shifted by the primary
        // display's work-area origin. Convert to screen space, do the maths there, convert back.
        var wsOffset = WorkspaceOffset();
        var normal = wp.rcNormalPosition.ToRectangle();
        normal.Offset(wsOffset);
        if (normal.Width <= 0 || normal.Height <= 0) return false;

        var normalRect = RECT.FromRectangle(normal);
        var sourceInfo = GetMonitor(MonitorFromRect(ref normalRect, MONITOR_DEFAULTTONEAREST));
        var sourceWork = sourceInfo?.rcWork.ToRectangle() ?? normal;
        var targetWork = targetInfo.rcWork.ToRectangle();

        int width = Math.Min(normal.Width, targetWork.Width);
        int height = Math.Min(normal.Height, targetWork.Height);
        int left = targetWork.Left + (normal.Left - sourceWork.Left);
        int top = targetWork.Top + (normal.Top - sourceWork.Top);
        left = Math.Clamp(left, targetWork.Left, Math.Max(targetWork.Left, targetWork.Right - width));
        top = Math.Clamp(top, targetWork.Top, Math.Max(targetWork.Top, targetWork.Bottom - height));

        var dest = new Rectangle(left, top, width, height);
        dest.Offset(-wsOffset.X, -wsOffset.Y);
        wp.rcNormalPosition = RECT.FromRectangle(dest);

        bool wasMaximised = wp.showCmd == SW_SHOWMAXIMIZED;
        bool wasMinimised = wp.showCmd is SW_SHOWMINIMIZED or SW_SHOWMINNOACTIVE or SW_MINIMIZE;

        // Re-show without stealing focus. A maximised window ignores a new normal rect while it stays
        // maximised, so restore it onto the target first, then maximise again. That second step activates
        // the window; GatherToActiveDisplay compensates by processing back-to-front.
        wp.showCmd = wasMinimised ? SW_SHOWMINNOACTIVE : SW_SHOWNOACTIVATE;
        if (!SetWindowPlacement(hwnd, ref wp)) return false;

        if (wasMaximised)
        {
            wp.showCmd = SW_SHOWMAXIMIZED;
            return SetWindowPlacement(hwnd, ref wp);
        }
        return true;
    }

    /// <summary>The Alt-Tab style filter: visible, titled, un-owned (or explicitly an app window), not cloaked, not shell.</summary>
    private static bool IsCandidate(IntPtr hwnd)
    {
        if (!IsWindowVisible(hwnd)) return false;
        if (GetWindowTextLength(hwnd) == 0) return false;

        int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
        if ((exStyle & WS_EX_TOOLWINDOW) != 0) return false;
        if ((exStyle & WS_EX_APPWINDOW) == 0 && GetAncestor(hwnd, GA_ROOTOWNER) != hwnd) return false;

        // Suspended UWP apps and windows on other virtual desktops are "cloaked": present but not shown.
        if (DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0) return false;

        var cls = new StringBuilder(256);
        if (GetClassName(hwnd, cls, cls.Capacity) > 0 && IgnoredClasses.Contains(cls.ToString())) return false;

        GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == (uint)Environment.ProcessId) return false;

        return true;
    }

    private static MONITORINFOEX? GetMonitor(IntPtr monitor)
    {
        if (monitor == IntPtr.Zero) return null;
        var info = new MONITORINFOEX { cbSize = (uint)Marshal.SizeOf<MONITORINFOEX>() };
        return GetMonitorInfo(monitor, ref info) ? info : null;
    }

    private static Point WorkspaceOffset()
    {
        var rc = new RECT();
        return SystemParametersInfo(SPI_GETWORKAREA, 0, ref rc, 0) ? new Point(rc.Left, rc.Top) : Point.Empty;
    }
}
