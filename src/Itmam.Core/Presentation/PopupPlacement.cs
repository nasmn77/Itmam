using System.Drawing;

namespace Itmam.Core.Presentation;

/// <summary>
/// Places the suggestion bar relative to the caret, in physical screen pixels.
/// </summary>
/// <remarks>
/// Like the native bar, it sits just above the caret line and starts at the caret, extending in the
/// writing direction: for Arabic (right-to-left) its right edge is at the caret and it extends to the
/// left, so it never covers the word being typed. If there is no room above (top of the monitor) it
/// goes below the caret; horizontally it is kept inside the monitor's work area.
/// </remarks>
public static class PopupPlacement
{
    /// <param name="caret">Caret rectangle (or the mouse pointer when no caret was found).</param>
    /// <param name="popup">Bar size.</param>
    /// <param name="workArea">Work area of the caret's monitor.</param>
    /// <param name="rightToLeft">Arabic: the bar extends to the left of the caret.</param>
    /// <param name="gap">Distance from the caret.</param>
    /// <param name="preferBelow">Below first (mouse-pointer fallback), above only if it does not fit.</param>
    public static Point Place(Rectangle caret, Size popup, Rectangle workArea, bool rightToLeft, int gap, bool preferBelow = false)
    {
        var x = rightToLeft ? caret.Left - popup.Width : caret.Right;
        var above = caret.Top - gap - popup.Height;
        var below = caret.Bottom + gap;
        var y = preferBelow
            ? (below + popup.Height <= workArea.Bottom ? below : above)
            : (above >= workArea.Top ? above : below);

        // Still outside (caret near the bottom of a short work area): keep it on screen.
        y = Math.Clamp(y, workArea.Top, Math.Max(workArea.Top, workArea.Bottom - popup.Height));
        x = Math.Clamp(x, workArea.Left, Math.Max(workArea.Left, workArea.Right - popup.Width));
        return new Point(x, y);
    }
}
