using System.Drawing;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.System.Com;
using Windows.Win32.UI.Accessibility;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Itmam.Win32;

public enum CaretSource
{
    /// <summary>System caret reported by GetGUIThreadInfo (classic Win32 edits, Notepad, Office).</summary>
    Win32Caret,

    /// <summary>UI Automation TextPattern2 caret range / TextPattern selection.</summary>
    UiAutomation,

    /// <summary>MSAA caret object (Chromium, Electron).</summary>
    Msaa,

    /// <summary>No caret found: the bar is shown below the mouse pointer.</summary>
    Mouse,
}

/// <summary>Caret rectangle in physical screen pixels.</summary>
public readonly record struct CaretLocation(Rectangle Bounds, CaretSource Source);

/// <summary>
/// Finds the text caret of the focused window, trying in order GetGUIThreadInfo, UI Automation,
/// MSAA and finally the mouse pointer, and using the first result that lies on a monitor.
/// UI Automation and MSAA run on the <see cref="UiaWorker"/> thread with a short timeout.
/// The method that worked last for a focused window is tried first next time.
/// </summary>
public sealed class CaretLocator(UiaWorker worker)
{
    private static readonly TimeSpan AccessibilityTimeout = TimeSpan.FromMilliseconds(150);
    private static readonly Guid IAccessibleId = typeof(IAccessible).GUID;

    private HWND _lastFocus;
    private CaretSource _lastSource = CaretSource.Win32Caret;

    /// <param name="rightToLeft">Text direction, to put the caret on the correct side of the previous character.</param>
    /// <param name="cancellation">Cancels the wait for UI Automation.</param>
    public async Task<CaretLocation> LocateAsync(bool rightToLeft, CancellationToken cancellation)
    {
        var (focus, win32Caret) = SystemCaret();
        var preferAccessibility = focus == _lastFocus && _lastSource is CaretSource.UiAutomation or CaretSource.Msaa;

        if (!preferAccessibility && win32Caret is { } fromWin32)
        {
            return Remember(focus, new CaretLocation(fromWin32, CaretSource.Win32Caret));
        }

        var fromAccessibility = await worker.RunAsync(
            automation => AccessibleCaret(automation, focus, rightToLeft),
            (CaretLocation?)null,
            AccessibilityTimeout,
            cancellation).ConfigureAwait(false);
        if (fromAccessibility is { } located)
        {
            return Remember(focus, located);
        }

        if (win32Caret is { } fallbackWin32)
        {
            return Remember(focus, new CaretLocation(fallbackWin32, CaretSource.Win32Caret));
        }

        return Remember(focus, new CaretLocation(MouseRectangle(), CaretSource.Mouse));
    }

    private CaretLocation Remember(HWND focus, CaretLocation location)
    {
        _lastFocus = focus;
        _lastSource = location.Source;
        return location;
    }

    private static unsafe (HWND Focus, Rectangle? Caret) SystemCaret()
    {
        var foreground = PInvoke.GetForegroundWindow();
        if (foreground.IsNull)
        {
            return (default, null);
        }

        var info = new GUITHREADINFO { cbSize = (uint)sizeof(GUITHREADINFO) };
        if (!PInvoke.GetGUIThreadInfo(PInvoke.GetWindowThreadProcessId(foreground, null), &info))
        {
            return (default, null);
        }

        var focus = info.hwndFocus.IsNull ? foreground : info.hwndFocus;
        var rect = info.rcCaret;
        if (info.hwndCaret.IsNull || rect.bottom - rect.top <= 0)
        {
            return (focus, null);
        }

        var topLeft = new Point(rect.left, rect.top);
        if (!PInvoke.ClientToScreen(info.hwndCaret, &topLeft))
        {
            return (focus, null);
        }

        var caret = new Rectangle(topLeft.X, topLeft.Y, Math.Max(1, rect.right - rect.left), rect.bottom - rect.top);
        return (focus, IsOnAMonitor(caret) ? caret : null);
    }

    private static CaretLocation? AccessibleCaret(IUIAutomation automation, HWND focus, bool rightToLeft)
    {
        if (UiaCaret(automation, rightToLeft) is { } fromUia && IsOnAMonitor(fromUia))
        {
            return new CaretLocation(fromUia, CaretSource.UiAutomation);
        }

        if (!focus.IsNull && MsaaCaret(focus) is { } fromMsaa && IsOnAMonitor(fromMsaa))
        {
            return new CaretLocation(fromMsaa, CaretSource.Msaa);
        }

        return null;
    }

    private static Rectangle? UiaCaret(IUIAutomation automation, bool rightToLeft)
    {
        var element = automation.GetFocusedElement();
        if (element is null || UiaText.CaretRange(element) is not { } caret)
        {
            return null;
        }

        if (FirstRectangle(caret) is { } exact)
        {
            return exact with { Width = Math.Max(1, exact.Width) };
        }

        // Many apps return no rectangle for an empty range: use the character before the caret
        // (the caret is on its left side in right-to-left text, on its right side otherwise)…
        var previous = caret.Clone();
        if (previous.MoveEndpointByUnit(TextPatternRangeEndpoint.TextPatternRangeEndpoint_Start, TextUnit.TextUnit_Character, -1) != 0
            && FirstRectangle(previous) is { } before)
        {
            return new Rectangle(rightToLeft ? before.Left : before.Right, before.Top, 1, before.Height);
        }

        // …or the character after it (start of the field).
        var next = caret.Clone();
        if (next.MoveEndpointByUnit(TextPatternRangeEndpoint.TextPatternRangeEndpoint_End, TextUnit.TextUnit_Character, 1) != 0
            && FirstRectangle(next) is { } after)
        {
            return new Rectangle(rightToLeft ? after.Right : after.Left, after.Top, 1, after.Height);
        }

        return null;
    }

    // GetBoundingRectangles returns a SAFEARRAY of doubles: left, top, width, height per line.
    private static unsafe Rectangle? FirstRectangle(IUIAutomationTextRange range)
    {
        var array = range.GetBoundingRectangles();
        if (array is null)
        {
            return null;
        }

        try
        {
            if (array->cDims != 1 || array->rgsabound[0].cElements < 4)
            {
                return null;
            }

            var values = (double*)array->pvData;
            var rect = new Rectangle((int)values[0], (int)values[1], (int)Math.Ceiling(values[2]), (int)Math.Ceiling(values[3]));
            return rect.Height > 0 ? rect : null;
        }
        finally
        {
            PInvoke.SafeArrayDestroy(array);
        }
    }

    private static unsafe Rectangle? MsaaCaret(HWND window)
    {
        var id = IAccessibleId;
        if (PInvoke.AccessibleObjectFromWindow(window, unchecked((uint)(int)OBJECT_IDENTIFIER.OBJID_CARET), &id, out var instance).Failed
            || instance is not IAccessible caret)
        {
            return null;
        }

        try
        {
            caret.accLocation(out var left, out var top, out var width, out var height, 0); // CHILDID_SELF
            return height > 0 ? new Rectangle(left, top, Math.Max(1, width), height) : null;
        }
        finally
        {
            Marshal.ReleaseComObject(caret);
        }
    }

    // Below the mouse pointer: a caret-like rectangle under the pointer's hot spot.
    private static Rectangle MouseRectangle()
    {
        PInvoke.GetCursorPos(out var point);
        return new Rectangle(point.X, point.Y, 1, 24);
    }

    private static bool IsOnAMonitor(Rectangle rect) =>
        rect is { Width: > 0, Height: > 0 } && !(rect.X == 0 && rect.Y == 0)
        && !PInvoke.MonitorFromPoint(new Point(rect.X, rect.Y + (rect.Height / 2)), MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONULL).IsNull;
}
