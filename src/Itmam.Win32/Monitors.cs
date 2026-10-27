using System.Drawing;
using Windows.Win32;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.HiDpi;

namespace Itmam.Win32;

/// <summary>Work area and DPI of the monitor containing a point (physical pixels; the app is Per-Monitor V2).</summary>
public readonly record struct MonitorInfo(Rectangle WorkArea, uint Dpi)
{
    public float Scale => Dpi / 96f;
}

public static class Monitors
{
    public static unsafe MonitorInfo FromPoint(Point point)
    {
        var monitor = PInvoke.MonitorFromPoint(point, MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONEAREST);
        var info = new MONITORINFO { cbSize = (uint)sizeof(MONITORINFO) };
        PInvoke.GetMonitorInfo(monitor, &info);
        var work = info.rcWork;

        if (PInvoke.GetDpiForMonitor(monitor, MONITOR_DPI_TYPE.MDT_EFFECTIVE_DPI, out var dpi, out _).Failed)
        {
            dpi = 96;
        }

        return new MonitorInfo(Rectangle.FromLTRB(work.left, work.top, work.right, work.bottom), dpi);
    }
}
