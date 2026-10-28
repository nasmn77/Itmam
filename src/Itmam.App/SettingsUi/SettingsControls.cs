using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace Itmam.App.SettingsUi;

/// <summary>Shared drawing helpers for the Windows 11 style settings controls.</summary>
internal static class Drawing
{
    public static GraphicsPath Rounded(RectangleF bounds, float radius)
    {
        var diameter = Math.Max(1, radius * 2);
        var path = new GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}

/// <summary>Base for owner-drawn controls that follow the settings palette.</summary>
internal abstract class ThemedControl : Control
{
    private SettingsPalette _palette = SettingsPalette.For(Core.Settings.ThemeChoice.System);

    protected ThemedControl()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public SettingsPalette Palette
    {
        get => _palette;
        set
        {
            _palette = value;
            Invalidate();
        }
    }

    protected float DpiScale => DeviceDpi / 96f;
}

/// <summary>Windows 11 toggle switch: a pill with a sliding knob, accent-filled when on.</summary>
internal sealed class ToggleSwitch : ThemedControl
{
    private bool _checked;

    public ToggleSwitch()
    {
        Size = new Size(40, 20);
        Cursor = Cursors.Hand;
        TabStop = true;
    }

    public event EventHandler? CheckedChanged;

    [DefaultValue(false)]
    public bool Checked
    {
        get => _checked;
        set
        {
            if (_checked != value)
            {
                _checked = value;
                Invalidate();
                CheckedChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);
        Checked = !Checked;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Space)
        {
            Checked = !Checked;
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var track = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        using var path = Drawing.Rounded(track, track.Height / 2);
        if (Checked)
        {
            using var fill = new SolidBrush(Palette.Accent);
            g.FillPath(fill, path);
        }
        else
        {
            using var pen = new Pen(Palette.ControlBorder, DpiScale);
            g.DrawPath(pen, path);
        }

        // RTL interfaces mirror the switch: "on" is on the left.
        var onRight = Checked != (RightToLeft == RightToLeft.Yes);
        var knob = Checked ? Height * 0.6f : Height * 0.5f;
        var x = onRight ? Width - Height / 2f - knob / 2 - 1 : Height / 2f - knob / 2;
        using var knobBrush = new SolidBrush(Checked ? Palette.OnAccent : Palette.SecondaryText);
        g.FillEllipse(knobBrush, x, (Height - knob) / 2f, knob, knob);

        if (Focused && ShowFocusCues)
        {
            using var focus = new Pen(Palette.Text, DpiScale);
            using var outline = Drawing.Rounded(new RectangleF(-0.5f, -0.5f, Width, Height), Height / 2f);
            g.DrawPath(focus, outline);
        }
    }
}

/// <summary>Windows 11 standard button: rounded, subtle fill and border, hover highlight.</summary>
internal sealed class ThemedButton : ThemedControl
{
    private bool _hovered;
    private bool _pressed;

    public ThemedButton()
    {
        Height = 32;
        Cursor = Cursors.Hand;
        TabStop = true;
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    [System.Diagnostics.CodeAnalysis.AllowNull]
    public override string Text
    {
        get => base.Text;
        set
        {
            base.Text = value;
            Width = Math.Max(120, TextRenderer.MeasureText(value ?? string.Empty, Font).Width + 40);
            Invalidate();
        }
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        _hovered = true;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hovered = _pressed = false;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        _pressed = true;
        Focus();
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        _pressed = false;
        Invalidate();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode is Keys.Space or Keys.Enter)
        {
            OnClick(EventArgs.Empty);
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var bounds = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        using var path = Drawing.Rounded(bounds, 4 * DpiScale);
        var fill = _pressed ? Palette.NavSelected : _hovered ? Palette.NavHover : Palette.ControlFill;
        using (var brush = new SolidBrush(fill))
        using (var border = new Pen(Palette.CardBorder, DpiScale))
        {
            g.FillPath(brush, path);
            g.DrawPath(border, path);
        }

        TextRenderer.DrawText(g, Text, Font, ClientRectangle, Palette.Text,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);

        if (Focused && ShowFocusCues)
        {
            using var focus = new Pen(Palette.Text, DpiScale);
            g.DrawPath(focus, path);
        }
    }
}

/// <summary>A row of mutually exclusive options (e.g. 1 2 3 4 5, or العربية | English).</summary>
internal sealed class SegmentedChoice : ThemedControl
{
    private string[] _options = [];
    private int _selected;
    private int _hovered = -1;

    public SegmentedChoice()
    {
        Height = 32;
        Cursor = Cursors.Hand;
        TabStop = true;
    }

    public event EventHandler? SelectedIndexChanged;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string[] Options
    {
        get => _options;
        set
        {
            _options = value;
            Width = (int)((_options.Sum(o => Math.Max(40, TextRenderer.MeasureText(o, Font).Width + 24))) * 1f);
            Invalidate();
        }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int SelectedIndex
    {
        get => _selected;
        set
        {
            var clamped = Math.Clamp(value, 0, Math.Max(0, _options.Length - 1));
            if (_selected != clamped)
            {
                _selected = clamped;
                Invalidate();
                SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var index = IndexAt(e.X);
        if (index != _hovered)
        {
            _hovered = index;
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
        if (IndexAt(e.X) is var index and >= 0)
        {
            SelectedIndex = index;
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        var forward = RightToLeft == RightToLeft.Yes ? Keys.Left : Keys.Right;
        var back = RightToLeft == RightToLeft.Yes ? Keys.Right : Keys.Left;
        if (e.KeyCode == forward)
        {
            SelectedIndex++;
        }
        else if (e.KeyCode == back)
        {
            SelectedIndex--;
        }
    }

    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Left or Keys.Right || base.IsInputKey(keyData);

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var bounds = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        var radius = 4 * DpiScale;
        using (var outer = Drawing.Rounded(bounds, radius))
        using (var fill = new SolidBrush(Palette.ControlFill))
        using (var border = new Pen(Palette.CardBorder, DpiScale))
        {
            g.FillPath(fill, outer);
            g.DrawPath(border, outer);
        }

        for (var i = 0; i < _options.Length; i++)
        {
            var item = ItemBounds(i);
            if (i == _selected)
            {
                var inset = RectangleF.Inflate(item, -3 * DpiScale, -3 * DpiScale);
                using var path = Drawing.Rounded(inset, 3 * DpiScale);
                using var accent = new SolidBrush(Palette.Accent);
                g.FillPath(accent, path);
            }
            else if (i == _hovered)
            {
                var inset = RectangleF.Inflate(item, -3 * DpiScale, -3 * DpiScale);
                using var path = Drawing.Rounded(inset, 3 * DpiScale);
                using var hover = new SolidBrush(Palette.NavHover);
                g.FillPath(hover, path);
            }

            TextRenderer.DrawText(g, _options[i], Font, Rectangle.Round(item), i == _selected ? Palette.OnAccent : Palette.Text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
        }

        if (Focused && ShowFocusCues)
        {
            using var focus = new Pen(Palette.Text, DpiScale);
            using var outline = Drawing.Rounded(bounds, radius);
            g.DrawPath(focus, outline);
        }
    }

    // Options flow in reading order: right to left in Arabic.
    private RectangleF ItemBounds(int index)
    {
        var width = (float)Width / Math.Max(1, _options.Length);
        var slot = RightToLeft == RightToLeft.Yes ? _options.Length - 1 - index : index;
        return new RectangleF(slot * width, 0, width, Height);
    }

    private int IndexAt(int x)
    {
        for (var i = 0; i < _options.Length; i++)
        {
            if (ItemBounds(i).Contains(x, Height / 2f))
            {
                return i;
            }
        }

        return -1;
    }
}

/// <summary>
/// A settings card: title (and optional description) at the start, a control at the end, on a
/// rounded panel — like a row in the Windows 11 Settings app. Lays itself out for RTL and LTR.
/// </summary>
internal sealed class SettingCard : Panel
{
    private readonly Label _title = new() { AutoSize = false, TextAlign = ContentAlignment.BottomLeft, UseMnemonic = false };
    private readonly Label _description = new() { AutoSize = false, TextAlign = ContentAlignment.TopLeft, UseMnemonic = false };
    private SettingsPalette _palette = SettingsPalette.For(Core.Settings.ThemeChoice.System);

    public SettingCard(string title, string? description, Control? control)
    {
        DoubleBuffered = true;
        Control = control;
        _title.Text = title;
        _description.Text = description ?? string.Empty;
        _description.Visible = description is not null;
        Controls.Add(_title);
        Controls.Add(_description);
        if (control is not null)
        {
            Controls.Add(control);
        }

        Height = description is null ? 64 : 76;
    }

    public Control? Control { get; }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public SettingsPalette Palette
    {
        get => _palette;
        set
        {
            _palette = value;
            BackColor = value.Window;
            _title.ForeColor = value.Text;
            _description.ForeColor = value.SecondaryText;
            _title.BackColor = _description.BackColor = value.Card;
            if (Control is ThemedControl themed)
            {
                themed.Palette = value;
            }

            if (Control is not null)
            {
                Control.BackColor = value.Card; // otherwise it shows the window color as a dark box
            }

            Invalidate();
        }
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        var scale = DeviceDpi / 96f;
        var padding = (int)(16 * scale);
        var rtl = RightToLeft == RightToLeft.Yes;
        var controlWidth = Control?.Width ?? 0;
        if (Control is not null)
        {
            Control.Top = (Height - Control.Height) / 2;
            Control.Left = rtl ? padding : Width - padding - controlWidth;
        }

        var textWidth = Math.Max(0, Width - (2 * padding) - controlWidth - (controlWidth > 0 ? padding : 0));
        var textLeft = rtl ? Width - padding - textWidth : padding;

        // "Left" means "start of the line": a Label with RightToLeft = Yes mirrors it to the right.
        const ContentAlignment align = ContentAlignment.BottomLeft;
        if (_description.Visible)
        {
            // Long descriptions wrap: the card grows to fit them.
            var top = (int)(12 * scale);
            var titleHeight = TextRenderer.MeasureText(_title.Text, _title.Font).Height;
            var descriptionHeight = TextRenderer.MeasureText(_description.Text, _description.Font, new Size(Math.Max(1, textWidth), int.MaxValue), TextFormatFlags.WordBreak).Height;
            var needed = Math.Max(top + titleHeight + descriptionHeight + top, (int)(64 * scale));
            if (Height != needed)
            {
                Height = needed; // the page re-stacks the cards (SizeChanged)
                if (Control is not null)
                {
                    Control.Top = (Height - Control.Height) / 2;
                }
            }

            _title.SetBounds(textLeft, top, textWidth, titleHeight);
            _description.SetBounds(textLeft, top + titleHeight, textWidth, descriptionHeight);
            _title.TextAlign = align;
            _description.TextAlign = ContentAlignment.TopLeft;
        }
        else
        {
            _title.SetBounds(textLeft, 0, textWidth, Height);
            _title.TextAlign = ContentAlignment.MiddleLeft;
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var bounds = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        using var path = Drawing.Rounded(bounds, 4 * DeviceDpi / 96f);
        using var fill = new SolidBrush(_palette.Card);
        using var border = new Pen(_palette.CardBorder);
        g.FillPath(fill, path);
        g.DrawPath(border, path);
    }
}
