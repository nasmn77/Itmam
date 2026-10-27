using Windows.Win32;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Itmam.Win32;

public static class KeyboardLayouts
{
    /// <summary>
    /// Keyboard layout (HKL) of the window that receives keyboard input. Uses the thread of the
    /// focused window rather than the top-level window, because in UWP apps and consoles the
    /// foreground window's thread is a host thread whose layout can differ.
    /// </summary>
    public static unsafe nint ForegroundLayout()
    {
        var foreground = PInvoke.GetForegroundWindow();
        if (foreground.IsNull)
        {
            return 0;
        }

        var threadId = PInvoke.GetWindowThreadProcessId(foreground, null);
        var info = new GUITHREADINFO { cbSize = (uint)sizeof(GUITHREADINFO) };
        if (PInvoke.GetGUIThreadInfo(threadId, &info) && !info.hwndFocus.IsNull)
        {
            threadId = PInvoke.GetWindowThreadProcessId(info.hwndFocus, null);
        }

        return (nint)PInvoke.GetKeyboardLayout(threadId).Value;
    }
}
