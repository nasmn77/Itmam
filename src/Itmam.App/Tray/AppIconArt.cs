using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace Itmam.App.Tray;

/// <summary>
/// The Itmam icon (original artwork, concept "suggestion bar"): a rounded teal square with a small
/// white suggestion bar (three dots) above a white «ت». At 16–24 px only the letter is drawn so it
/// stays legible. Shared by the app (tray icon, drawn at run time for the current DPI) and
/// tools/IconGenerator (Itmam.ico and, in Phase 7, the Store logos). Depends on System.Drawing only.
/// </summary>
internal static class AppIconArt
{
    private static readonly (Color Light, Color Dark) Teal = (Color.FromArgb(0x2D, 0xD4, 0xBF), Color.FromArgb(0x0E, 0x74, 0x90));
    private static readonly (Color Light, Color Dark) Gray = (Color.FromArgb(0xA8, 0xA8, 0xA8), Color.FromArgb(0x6B, 0x6B, 0x6B));

    /// <param name="size">Pixel size.</param>
    /// <param name="enabled">Gray when Itmam is off (tray).</param>
    public static Bitmap Render(int size, bool enabled = true)
    {
        var bitmap = new Bitmap(size, size);
        using var g = Graphics.FromImage(bitmap);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.Clear(Color.Transparent);
        Draw(g, size, enabled);
        return bitmap;
    }

    private static void Draw(Graphics g, float s, bool enabled)
    {
        var (light, dark) = enabled ? Teal : Gray;
        var bounds = new RectangleF(0, 0, s - 1, s - 1);
        using (var back = Rounded(bounds, s * 0.22f))
        using (var gradient = new LinearGradientBrush(bounds, light, dark, 55f))
        {
            g.FillPath(gradient, back);
        }

        if (s < 32)
        {
            Letter(g, new RectangleF(0, -s * 0.02f, s, s), s * 0.62f);
            return;
        }

        // The suggestion bar: a white pill with three dots.
        var bar = new RectangleF(s * 0.2f, s * 0.14f, s * 0.6f, s * 0.17f);
        using (var pill = Rounded(bar, bar.Height / 2))
        using (var white = new SolidBrush(Color.FromArgb(235, 255, 255, 255)))
        {
            g.FillPath(white, pill);
        }

        using var dot = new SolidBrush(dark);
        var d = bar.Height * 0.36f;
        for (var i = 0; i < 3; i++)
        {
            g.FillEllipse(dot, bar.Left + (bar.Width * (0.25f + (i * 0.25f))) - (d / 2), bar.Top + ((bar.Height - d) / 2), d, d);
        }

        Letter(g, new RectangleF(0, s * 0.28f, s, s * 0.66f), s * 0.50f);
    }

    private static void Letter(Graphics g, RectangleF box, float pixels)
    {
        using var font = new Font("Segoe UI", pixels, FontStyle.Bold, GraphicsUnit.Pixel);
        using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.DirectionRightToLeft };
        g.DrawString("ت", font, Brushes.White, box, format);
    }

    private static GraphicsPath Rounded(RectangleF r, float radius)
    {
        var d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(r.Left, r.Top, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
