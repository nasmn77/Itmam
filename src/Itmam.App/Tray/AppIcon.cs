using System.Drawing.Drawing2D;
using System.Drawing.Text;
using Itmam.Win32;

namespace Itmam.App.Tray;

/// <summary>
/// The app's placeholder icon, drawn at run time: a white «إ» on a rounded teal square, dimmed when
/// Itmam is off. Original artwork; the Store logo assets are made in Phase 7.
/// </summary>
internal static class AppIcon
{
    private static readonly Color Brand = Color.FromArgb(0x0F, 0x76, 0x6E);
    private static readonly Color BrandOff = Color.FromArgb(0x7A, 0x7A, 0x7A);

    /// <param name="size">Pixel size (e.g. the tray's small icon size for the current DPI).</param>
    /// <param name="enabled">Off is drawn in gray.</param>
    public static Icon Create(int size, bool enabled)
    {
        using var bitmap = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.Clear(Color.Transparent);

            var radius = Math.Max(2, size / 4);
            using (var path = RoundedSquare(new RectangleF(0, 0, size - 1, size - 1), radius))
            using (var fill = new SolidBrush(enabled ? Brand : BrandOff))
            {
                g.FillPath(fill, path);
            }

            using var font = new Font("Segoe UI", size * 0.62f, FontStyle.Bold, GraphicsUnit.Pixel);
            using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.DirectionRightToLeft };
            g.DrawString("إ", font, Brushes.White, new RectangleF(0, size * 0.06f, size, size), format);
        }

        var handle = bitmap.GetHicon();
        try
        {
            using var borrowed = Icon.FromHandle(handle);
            return (Icon)borrowed.Clone(); // owns its own native copy
        }
        finally
        {
            WindowChrome.DestroyIcon(handle);
        }
    }

    private static GraphicsPath RoundedSquare(RectangleF bounds, float radius)
    {
        var diameter = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}
