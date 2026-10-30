using System.Drawing;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;
using Windows.Win32.UI.Controls;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Itmam.Win32;

/// <summary>Window operations for the suggestion popup that must never take focus.</summary>
public static class PopupWindows
{
    public const int WS_EX_NOACTIVATE = 0x08000000;
    public const int WS_EX_TOOLWINDOW = 0x00000080;
    public const int WS_EX_TOPMOST = 0x00000008;
    public const int WM_MOUSEACTIVATE = 0x0021;
    public const int MA_NOACTIVATE = 3;

    private static readonly HWND Topmost = new(-1);

    /// <summary>Shows (or moves) the window at <paramref name="bounds"/> as topmost, without activating it.</summary>
    public static void ShowWithoutActivation(nint window, Rectangle bounds) =>
        PInvoke.SetWindowPos(
            new HWND(window),
            Topmost,
            bounds.X,
            bounds.Y,
            bounds.Width,
            bounds.Height,
            SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE | SET_WINDOW_POS_FLAGS.SWP_SHOWWINDOW | SET_WINDOW_POS_FLAGS.SWP_NOOWNERZORDER);

    public static void Hide(nint window) => PInvoke.ShowWindow(new HWND(window), SHOW_WINDOW_CMD.SW_HIDE);

    /// <summary>The window the user is typing in (to give the focus back after the long phrases list).</summary>
    public static nint Foreground() => PInvoke.GetForegroundWindow();

    /// <summary>Gives the focus back to <paramref name="window"/>; allowed while our own window is in front.</summary>
    public static bool Activate(nint window) => PInvoke.SetForegroundWindow(new HWND(window));

    /// <summary>Shift, Ctrl, Alt or Win is held down (the keys of a shortcut not released yet).</summary>
    public static bool AnyModifierDown() =>
        PInvoke.GetAsyncKeyState(0x10) < 0 || PInvoke.GetAsyncKeyState(0x11) < 0 || PInvoke.GetAsyncKeyState(0x12) < 0
        || PInvoke.GetAsyncKeyState(0x5B) < 0 || PInvoke.GetAsyncKeyState(0x5C) < 0;

    /// <summary>
    /// Brings our <paramref name="window"/> to the front from a global shortcut. Windows refuses
    /// SetForegroundWindow to a background process (the hook does not count as its input), so the
    /// input of the foreground thread is attached to ours for the call.
    /// </summary>
    public static unsafe void BringToFront(nint window)
    {
        var foreground = PInvoke.GetForegroundWindow();
        var foregroundThread = PInvoke.GetWindowThreadProcessId(foreground, null);
        var ownThread = PInvoke.GetCurrentThreadId();
        var attached = foregroundThread != 0 && foregroundThread != ownThread && PInvoke.AttachThreadInput(foregroundThread, ownThread, true);
        try
        {
            PInvoke.BringWindowToTop(new HWND(window));
            PInvoke.SetForegroundWindow(new HWND(window));
        }
        finally
        {
            if (attached)
            {
                PInvoke.AttachThreadInput(foregroundThread, ownThread, false);
            }
        }
    }

    /// <summary>
    /// Windows 11 look: rounded corners, a 1 px border in <paramref name="border"/>, and the DWM
    /// shadow (enabled by extending the frame 1 px into the client area). No-op before Windows 11.
    /// </summary>
    public static unsafe void ApplyWindows11Style(nint window, Color border)
    {
        var hwnd = new HWND(window);
        var corners = DWM_WINDOW_CORNER_PREFERENCE.DWMWCP_ROUND;
        PInvoke.DwmSetWindowAttribute(hwnd, DWMWINDOWATTRIBUTE.DWMWA_WINDOW_CORNER_PREFERENCE, &corners, sizeof(DWM_WINDOW_CORNER_PREFERENCE));

        var colorRef = (uint)(border.R | (border.G << 8) | (border.B << 16));
        PInvoke.DwmSetWindowAttribute(hwnd, DWMWINDOWATTRIBUTE.DWMWA_BORDER_COLOR, &colorRef, sizeof(uint));

        var margins = new MARGINS { cxLeftWidth = 1, cxRightWidth = 1, cyTopHeight = 1, cyBottomHeight = 1 };
        PInvoke.DwmExtendFrameIntoClientArea(hwnd, &margins);
    }
}
