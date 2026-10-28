using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Itmam.Win32;

/// <summary>Native window details for the settings window and the tray icon.</summary>
public static class WindowChrome
{
    /// <summary>Dark or light title bar (Windows 10 20H1+ / Windows 11).</summary>
    public static unsafe void SetDarkTitleBar(nint window, bool dark)
    {
        BOOL value = dark;
        PInvoke.DwmSetWindowAttribute(new HWND(window), DWMWINDOWATTRIBUTE.DWMWA_USE_IMMERSIVE_DARK_MODE, &value, (uint)sizeof(BOOL));
    }

    /// <summary>Dark or light scroll bars for a scrollable control (the theme Explorer uses in dark mode).</summary>
    public static void SetDarkScrollbars(nint window, bool dark) =>
        PInvoke.SetWindowTheme(new HWND(window), dark ? "DarkMode_Explorer" : "Explorer", null);

    /// <summary>
    /// Lets any process bring a window to the front. Called by a second copy of Itmam (which the
    /// user just launched, so it may do this) before asking the running copy to show Settings;
    /// otherwise Windows' focus-stealing prevention would keep that window behind the others.
    /// </summary>
    public static void AllowAnyProcessToSetForeground() => PInvoke.AllowSetForegroundWindow(unchecked((uint)-1)); // ASFW_ANY

    /// <summary>Frees a native icon handle (e.g. from <c>Bitmap.GetHicon</c>), which .NET does not own.</summary>
    public static void DestroyIcon(nint icon) => PInvoke.DestroyIcon(new HICON(icon));
}
