using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using Itmam.Core.Presentation;

namespace Itmam.App.Suggestions;

/// <summary>
/// Measures and paints the suggestion bar's content. Shared by the bar itself and the live
/// preview on the Appearance page, so the preview is exactly what the user will see.
/// </summary>
internal static class BarRenderer
{
    /// <summary>One frame of the bar: what to draw and how.</summary>
    /// <param name="Selected">Navigation selection (-1: none), painted in <see cref="PopupColors.Selection"/>.</param>
    /// <param name="Hovered">Item under the mouse (-1: none).</param>
    public sealed record Frame(
        IReadOnlyList<string> Words,
        SuggestionBarLayout Layout,
        SuggestionBarMetrics Metrics,
        PopupColors Colors,
        PopupAppearance Appearance,
        bool RightToLeft,
        int Selected,
        int Hovered);

    public static SuggestionBarLayout Measure(Graphics g, IReadOnlyList<string> words, Font font, SuggestionBarMetrics metrics, bool rightToLeft)
    {
        using var format = TextFormat(rightToLeft, centered: false);
        var widths = words.Select(w => (int)Math.Ceiling(g.MeasureString(w, font, PointF.Empty, format).Width)).ToArray();
        return SuggestionBarLayout.Compute(widths, metrics, rightToLeft);
    }

    /// <summary>Paints the items (not the background) in bar coordinates.</summary>
    public static void Paint(Graphics g, Frame frame, Font font, Font hintFont)
    {
        // Grayscale antialiasing like the native (DirectWrite) bar, not ClearType color fringes.
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        using var format = TextFormat(frame.RightToLeft, centered: true);
        using var textBrush = new SolidBrush(frame.Colors.Text);
        using var hintBrush = new SolidBrush(frame.Colors.Hint);
        var words = frame.Words;
        for (var i = 0; i < words.Count && i < frame.Layout.Items.Count; i++)
        {
            var item = frame.Layout.Items[i];
            if (i == frame.Selected)
            {
                FillHighlight(g, item, frame.Metrics, frame.Colors.Selection);
            }
            else if (i == frame.Hovered || (i == 0 && frame.Selected < 0 && frame.Appearance.HighlightFirst))
            {
                FillHighlight(g, item, frame.Metrics, frame.Colors.FirstHighlight);
            }

            g.DrawString(words[i], font, textBrush, item, format);

            if (frame.Appearance.ShowHotkeyHints)
            {
                var padding = frame.Metrics.ItemPadding;
                var hint = new Rectangle(item.Right - padding, item.Top + 2, padding, item.Height / 2);
                g.DrawString((i + 1).ToString(CultureInfo.InvariantCulture), hintFont, hintBrush, hint, format);
            }
        }
    }

    private static StringFormat TextFormat(bool rightToLeft, bool centered)
    {
        var format = (StringFormat)StringFormat.GenericTypographic.Clone();
        format.FormatFlags |= StringFormatFlags.NoWrap | StringFormatFlags.MeasureTrailingSpaces;
        if (rightToLeft)
        {
            format.FormatFlags |= StringFormatFlags.DirectionRightToLeft;
        }

        if (centered)
        {
            format.Alignment = StringAlignment.Center;
            format.LineAlignment = StringAlignment.Center;
        }

        return format;
    }

    private static void FillHighlight(Graphics g, Rectangle item, SuggestionBarMetrics metrics, Color color)
    {
        var inset = metrics.HighlightInset;
        var area = Rectangle.Inflate(item, -inset / 2, -inset);
        var radius = metrics.HighlightRadius * 2;
        using var path = new GraphicsPath();
        path.AddArc(area.Left, area.Top, radius, radius, 180, 90);
        path.AddArc(area.Right - radius, area.Top, radius, radius, 270, 90);
        path.AddArc(area.Right - radius, area.Bottom - radius, radius, radius, 0, 90);
        path.AddArc(area.Left, area.Bottom - radius, radius, radius, 90, 90);
        path.CloseFigure();
        var smoothing = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var brush = new SolidBrush(color);
        g.FillPath(brush, path);
        g.SmoothingMode = smoothing;
    }
}
