using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;
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

    /// <summary>The keyboard layouts (HKL) the user has installed (Windows input languages).</summary>
    public static unsafe IReadOnlyList<nint> Installed()
    {
        var count = PInvoke.GetKeyboardLayoutList(0, null);
        if (count <= 0)
        {
            return [];
        }

        var layouts = new HKL[count];
        fixed (HKL* list = layouts)
        {
            count = PInvoke.GetKeyboardLayoutList(count, list);
        }

        return [.. layouts.Take(Math.Max(count, 0)).Select(layout => (nint)layout.Value)];
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
/// Per-keystroke layout lookup for the hook thread. The thread that owns the focused window is cached
/// per (foreground window, focused window): each keystroke costs GetForegroundWindow, GetGUIThreadInfo
/// and GetKeyboardLayout, and the thread is looked up again whenever the focused window changes. The
/// focus is checked on every key because a focus event can arrive before the app has set its focus:
/// the top-level window's thread was cached then, whose layout can stay Arabic after Alt+Shift, and
/// English typing was translated as Arabic until the next window change (found in 2.0 testing). The
/// layout itself is never cached, because switching it (Alt+Shift, Win+Space) sends us no event.
/// Not thread-safe: used only on the hook thread.
/// </summary>
internal sealed class ForegroundLayoutCache
{
    private HWND _foreground;
    private uint _foregroundThread;
    private HWND _focus;
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
            _foregroundThread = PInvoke.GetWindowThreadProcessId(foreground, null);
            _inputThread = 0;
        }

        var info = new GUITHREADINFO { cbSize = (uint)sizeof(GUITHREADINFO) };
        var focus = PInvoke.GetGUIThreadInfo(_foregroundThread, &info) ? info.hwndFocus : default;
        if (_inputThread == 0 || focus != _focus)
        {
            _focus = focus;
            _inputThread = focus.IsNull ? _foregroundThread : PInvoke.GetWindowThreadProcessId(focus, null);
        }

        return (nint)PInvoke.GetKeyboardLayout(_inputThread).Value;
    }

    /// <summary>Focus moved (possibly to a child window owned by another thread).</summary>
    public void Invalidate() => _foreground = default;
}
