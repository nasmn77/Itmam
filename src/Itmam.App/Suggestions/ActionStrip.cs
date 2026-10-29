using Itmam.Core.Presentation;
using Itmam.Win32;

namespace Itmam.App.Suggestions;

/// <summary>
/// The actions of a right-clicked suggestion («حذف»، «إضافة إلى عباراتي»), drawn like the bar in a
/// separate window next to it. Never activates, for the same reasons as <see cref="SuggestionPopup"/>.
/// </summary>
internal sealed class ActionStrip : Form
{
    private const int WS_POPUP = unchecked((int)0x80000000);

    private readonly SuggestionPopup _bar;
    private IReadOnlyList<(string Label, Action Run)> _actions = [];
    private SuggestionBarLayout? _layout;
    private SuggestionBarMetrics _metrics = new();
    private PopupColors _colors = PopupColors.Dark;
    private PopupAppearance _appearance = new();
    private Font? _font;
    private bool _rightToLeft = true;
    private int _hovered = -1;

    public ActionStrip(SuggestionPopup bar)
    {
        _bar = bar;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        DoubleBuffered = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
    }

    public bool IsShowing { get; private set; }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.Style = WS_POPUP;
            cp.ExStyle |= PopupWindows.WS_EX_NOACTIVATE | PopupWindows.WS_EX_TOOLWINDOW | PopupWindows.WS_EX_TOPMOST;
            return cp;
        }
    }

    /// <summary>Lays the actions out with the bar's look; returns the strip's size.</summary>
    public Size Prepare(IReadOnlyList<(string Label, Action Run)> actions, SuggestionBarMetrics metrics, PopupColors colors,
        PopupAppearance appearance, Font font, bool rightToLeft)
    {
        _actions = actions;
        _metrics = metrics;
        _colors = colors;
        _appearance = appearance;
        _font = font;
        _rightToLeft = rightToLeft;
        _hovered = -1;
        using var measure = Graphics.FromHwnd(Handle);
        _layout = BarRenderer.Measure(measure, [.. actions.Select(a => a.Label)], font, metrics, rightToLeft);
        return _layout.Size;
    }

    public void ShowAt(Rectangle bounds)
    {
        PopupWindows.ShowWithoutActivation(Handle, bounds);
        PopupWindows.ApplyWindows11Style(Handle, _colors.Border);
        IsShowing = true;
        Invalidate();
    }

    public void HideStrip()
    {
        if (IsHandleCreated && IsShowing)
        {
            PopupWindows.Hide(Handle);
            IsShowing = false;
        }
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == PopupWindows.WM_MOUSEACTIVATE)
        {
            m.Result = PopupWindows.MA_NOACTIVATE;
            return;
        }

        base.WndProc(ref m);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var item = ItemAt(e.Location);
        if (item != _hovered)
        {
            _hovered = item;
            Invalidate();
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hovered = -1;
        Invalidate();
    }

    // A click runs the action (left button) and closes the strip either way.
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        var action = e.Button == MouseButtons.Left && ItemAt(e.Location) is var index and >= 0 ? _actions[index].Run : null;
        _bar.HideActions();
        action?.Invoke();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(_colors.Background);
        if (_layout is not null && _font is not null)
        {
            var frame = new BarRenderer.Frame([.. _actions.Select(a => a.Label)], _layout, _metrics, _colors, _appearance, _rightToLeft, -1, _hovered);
            BarRenderer.Paint(e.Graphics, frame, _font, _font);
        }
    }

    private int ItemAt(Point point)
    {
        if (_layout is null)
        {
            return -1;
        }

        for (var i = 0; i < _layout.Items.Count && i < _actions.Count; i++)
        {
            if (_layout.Items[i].Contains(point))
            {
                return i;
            }
        }

        return -1;
    }
}
