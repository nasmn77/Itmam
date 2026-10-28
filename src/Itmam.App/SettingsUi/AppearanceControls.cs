using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using Itmam.App.Suggestions;
using Itmam.Core.Presentation;

namespace Itmam.App.SettingsUi;

/// <summary>
/// A row of round color swatches: "Automatic" first, then presets, then "Custom…" (which asks
/// the page to open a color picker). Flows right to left in Arabic.
/// </summary>
internal sealed class ColorSwatches : ThemedControl
{
    private const int SwatchSize = 28;
    private const int Gap = 8;

    private readonly IReadOnlyList<Color> _presets;
    private readonly string _autoName;
    private readonly string _customName;
    private readonly ToolTip _toolTip = new();
    private Color? _value;
    private int _hovered = -1;

    /// <param name="presets">Preset colors (after "Automatic").</param>
    /// <param name="autoName">Tooltip of the "Automatic" swatch.</param>
    /// <param name="customName">Tooltip of the "Custom…" swatch.</param>
    public ColorSwatches(IReadOnlyList<Color> presets, string autoName, string customName)
    {
        _presets = presets;
        _autoName = autoName;
        _customName = customName;
        Cursor = Cursors.Hand;
        TabStop = true;
        Size = new Size((Count * (SwatchSize + Gap)) - Gap + 4, SwatchSize + 4);
    }

    /// <summary>The value changed (by a click on a swatch or the keyboard).</summary>
    public event EventHandler? ValueChanged;

    /// <summary>"Custom…" was clicked: the page shows a color picker and sets <see cref="Value"/>.</summary>
    public event EventHandler? CustomRequested;

    /// <summary>Selected color; null = Automatic.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color? Value
    {
        get => _value;
        set
        {
            if (_value != value)
            {
                _value = value;
                Invalidate();
                ValueChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    // Automatic + presets + Custom.
    private int Count => _presets.Count + 2;

    private int CustomIndex => Count - 1;

    private int SelectedIndex
    {
        get
        {
            if (_value is not { } color)
            {
                return 0;
            }

            for (var i = 0; i < _presets.Count; i++)
            {
                if (_presets[i].ToArgb() == color.ToArgb())
                {
                    return i + 1;
                }
            }

            return CustomIndex;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _toolTip.Dispose();
        }

        base.Dispose(disposing);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var index = IndexAt(e.Location);
        if (index != _hovered)
        {
            _hovered = index;
            _toolTip.SetToolTip(this, index switch
            {
                < 0 => null,
                0 => _autoName,
                _ when index == CustomIndex => _customName,
                _ => ColorContrast.ToHex(_presets[index - 1]),
            });
            Invalidate();
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hovered = -1;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        if (e.Button == MouseButtons.Left && IndexAt(e.Location) is var index and >= 0)
        {
            Choose(index);
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        var rtl = RightToLeft == RightToLeft.Yes;
        if (e.KeyCode == (rtl ? Keys.Left : Keys.Right) && SelectedIndex < CustomIndex - 1)
        {
            Choose(SelectedIndex + 1);
        }
        else if (e.KeyCode == (rtl ? Keys.Right : Keys.Left) && SelectedIndex > 0)
        {
            Choose(SelectedIndex - 1);
        }
        else if (e.KeyCode is Keys.Enter or Keys.Space)
        {
            Choose(CustomIndex);
        }
    }

    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Left or Keys.Right || base.IsInputKey(keyData);

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(BackColor);
        var selected = SelectedIndex;
        for (var i = 0; i < Count; i++)
        {
            var bounds = SwatchBounds(i);
            var circle = RectangleF.Inflate(bounds, -3 * DpiScale, -3 * DpiScale);
            if (i == 0)
            {
                PaintAutomatic(g, circle);
            }
            else if (i == CustomIndex)
            {
                PaintCustom(g, circle, selected == CustomIndex ? _value : null);
            }
            else
            {
                using var fill = new SolidBrush(_presets[i - 1]);
                g.FillEllipse(fill, circle);
            }

            using (var edge = new Pen(Palette.ControlBorder, DpiScale))
            {
                g.DrawEllipse(edge, circle);
            }

            if (i == selected || i == _hovered)
            {
                using var ring = new Pen(i == selected ? Palette.Accent : Palette.SecondaryText, 2 * DpiScale);
                g.DrawEllipse(ring, RectangleF.Inflate(bounds, -1 * DpiScale, -1 * DpiScale));
            }
        }

        if (Focused && ShowFocusCues)
        {
            using var focus = new Pen(Palette.Text, DpiScale) { DashStyle = DashStyle.Dot };
            g.DrawEllipse(focus, SwatchBounds(selected));
        }
    }

    // Half light, half dark: "follows the theme".
    private static void PaintAutomatic(Graphics g, RectangleF circle)
    {
        using var light = new SolidBrush(PopupColors.Light.Background);
        using var dark = new SolidBrush(PopupColors.Dark.Background);
        g.FillPie(light, circle.X, circle.Y, circle.Width, circle.Height, 135, 180);
        g.FillPie(dark, circle.X, circle.Y, circle.Width, circle.Height, 315, 180);
    }

    // The custom color when one is chosen, otherwise a "+".
    private void PaintCustom(Graphics g, RectangleF circle, Color? custom)
    {
        using var fill = new SolidBrush(custom ?? Palette.ControlFill);
        g.FillEllipse(fill, circle);
        var plusColor = custom is { } c ? ColorContrast.AutoText(c) : Palette.Text;
        using var pen = new Pen(plusColor, 1.5f * DpiScale);
        var center = new PointF(circle.X + (circle.Width / 2), circle.Y + (circle.Height / 2));
        var arm = circle.Width / 4;
        g.DrawLine(pen, center.X - arm, center.Y, center.X + arm, center.Y);
        g.DrawLine(pen, center.X, center.Y - arm, center.X, center.Y + arm);
    }

    private void Choose(int index)
    {
        if (index == CustomIndex)
        {
            CustomRequested?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            Value = index == 0 ? null : _presets[index - 1];
        }
    }

    private RectangleF SwatchBounds(int index)
    {
        var size = SwatchSize * DpiScale;
        var step = (SwatchSize + Gap) * DpiScale;
        var slot = RightToLeft == RightToLeft.Yes ? Count - 1 - index : index;
        return new RectangleF((2 * DpiScale) + (slot * step), 2 * DpiScale, size, size);
    }

    private int IndexAt(Point point)
    {
        for (var i = 0; i < Count; i++)
        {
            if (SwatchBounds(i).Contains(point))
            {
                return i;
            }
        }

        return -1;
    }
}

/// <summary>
/// Live preview of the suggestion bar on top of sample "document" lines (so opacity is visible):
/// as it looks while typing, and while choosing with the arrow keys. Painted by
/// <see cref="BarRenderer"/>, exactly like the real bar.
/// </summary>
internal sealed class BarPreview : ThemedControl
{
    private static readonly string[] SampleWords = ["مرحبا", "مرحلة", "مرحى"];
    private PopupAppearance _appearance = new();

    public BarPreview() => Height = (int)(150 * DpiScale);

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public PopupAppearance Appearance
    {
        get => _appearance;
        set
        {
            _appearance = value;
            Invalidate();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Palette.Window);
        var card = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        using (var path = Drawing.Rounded(card, 4 * DpiScale))
        using (var fill = new SolidBrush(Palette.Card))
        using (var border = new Pen(Palette.CardBorder))
        {
            g.FillPath(fill, path);
            g.DrawPath(border, path);
        }

        // Sample text lines behind the bars.
        using (var line = new SolidBrush(Palette.ControlBorder))
        {
            for (var y = 22 * DpiScale; y < Height - (16 * DpiScale); y += 18 * DpiScale)
            {
                using var bar = Drawing.Rounded(new RectangleF(24 * DpiScale, y, Width - (48 * DpiScale), 6 * DpiScale), 3 * DpiScale);
                g.FillPath(line, bar);
            }
        }

        var metrics = SuggestionBarMetrics.ForFontSize(_appearance.FontPixels).Scale(DpiScale);
        var colors = PopupColors.For(_appearance);
        using var font = new Font("Segoe UI", metrics.FontPixels, FontStyle.Regular, GraphicsUnit.Pixel);
        using var hintFont = new Font("Segoe UI", metrics.FontPixels * 0.7f, FontStyle.Regular, GraphicsUnit.Pixel);
        var layout = BarRenderer.Measure(g, SampleWords, font, metrics, rightToLeft: true);

        var gap = (Height - (2 * layout.Size.Height)) / 3;
        var x = (Width - layout.Size.Width) / 2;
        PaintBar(g, new Point(x, gap), new BarRenderer.Frame(SampleWords, layout, metrics, colors, _appearance, true, Selected: -1, Hovered: -1), font, hintFont);
        PaintBar(g, new Point(x, (2 * gap) + layout.Size.Height), new BarRenderer.Frame(SampleWords, layout, metrics, colors, _appearance, true, Selected: 1, Hovered: -1), font, hintFont);
    }

    // Drawn off-screen, then blended with the bar's opacity and clipped to Windows 11 rounded corners.
    private void PaintBar(Graphics g, Point location, BarRenderer.Frame frame, Font font, Font hintFont)
    {
        var size = frame.Layout.Size;
        using var bitmap = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppArgb);
        using (var bar = Graphics.FromImage(bitmap))
        {
            bar.Clear(frame.Colors.Background);
            BarRenderer.Paint(bar, frame, font, hintFont);
        }

        var bounds = new RectangleF(location.X, location.Y, size.Width, size.Height);
        using var outline = Drawing.Rounded(bounds, 8 * DpiScale);
        var clip = g.Clip;
        g.SetClip(outline);
        using (var attributes = new ImageAttributes())
        {
            attributes.SetColorMatrix(new ColorMatrix { Matrix33 = (float)_appearance.Opacity });
            g.DrawImage(bitmap, Rectangle.Round(bounds), 0, 0, size.Width, size.Height, GraphicsUnit.Pixel, attributes);
        }

        g.Clip = clip;
        using var border = new Pen(frame.Colors.Border, DpiScale);
        g.DrawPath(border, outline);
    }
}
