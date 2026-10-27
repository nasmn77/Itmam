using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Itmam.Win32;

public static class KeyboardLayouts
{
    /// <summary>Keyboard layout (HKL) of the window that receives keyboard input.</summary>
    public static unsafe nint ForegroundLayout()
    {
        var foreground = PInvoke.GetForegroundWindow();
        return foreground.IsNull ? 0 : (nint)PInvoke.GetKeyboardLayout(InputThreadOf(foreground)).Value;
    }

    /// <summary>
    /// Thread that owns keyboard focus inside the given top-level window. Uses the focused window's
    /// thread rather than the top-level window's, because in UWP apps and consoles the top-level
    /// window belongs to a host thread whose layout can differ.
    /// </summary>
    internal static unsafe uint InputThreadOf(HWND foreground)
    {
        var threadId = PInvoke.GetWindowThreadProcessId(foreground, null);
        var info = new GUITHREADINFO { cbSize = (uint)sizeof(GUITHREADINFO) };
        if (PInvoke.GetGUIThreadInfo(threadId, &info) && !info.hwndFocus.IsNull)
        {
            threadId = PInvoke.GetWindowThreadProcessId(info.hwndFocus, null);
        }

        return threadId;
    }
}

/// <summary>
/// Per-keystroke layout lookup for the hook thread. The input thread of the foreground window is
/// cached and recomputed only when the foreground window changes or focus moves; each keystroke
/// then costs one GetForegroundWindow and one GetKeyboardLayout call. The layout itself is never
/// cached, because switching it (Alt+Shift, Win+Space) changes no window and sends us no event.
/// Not thread-safe: used only on the hook thread.
/// </summary>
internal sealed class ForegroundLayoutCache
{
    private HWND _foreground;
    private uint _inputThread;

    public unsafe nint Current()
    {
        var foreground = PInvoke.GetForegroundWindow();
        if (foreground.IsNull)
        {
            return 0;
        }

        if (foreground != _foreground)
        {
            _foreground = foreground;
            _inputThread = KeyboardLayouts.InputThreadOf(foreground);
        }

        return (nint)PInvoke.GetKeyboardLayout(_inputThread).Value;
    }

    /// <summary>Focus moved (possibly to a child window owned by another thread).</summary>
    public void Invalidate() => _foreground = default;
}
