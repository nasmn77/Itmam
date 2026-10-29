using Itmam.Core.Presentation;
using Itmam.Win32;
using Microsoft.Win32;

namespace Itmam.App.Suggestions;

/// <summary>
/// The suggestion bar: a borderless, topmost tool window that never takes focus. Owner-drawn to
/// match the native Windows 11 text suggestions bar (see <see cref="SuggestionBarMetrics"/>).
/// </summary>
/// <remarks>
/// Never activates: <c>ShowWithoutActivation</c>, <c>WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TOPMOST</c>,
/// <c>WM_MOUSEACTIVATE → MA_NOACTIVATE</c>, and it is shown with <c>SetWindowPos(SWP_NOACTIVATE)</c>.
/// Sizes are computed in physical pixels for the caret's monitor, so <c>WM_DPICHANGED</c> is
/// ignored instead of letting WinForms rescale the window. Text is drawn right-to-left; the item
/// order (best on the right) is computed by <see cref="SuggestionBarLayout"/> rather than by
/// mirroring the window (<c>RightToLeftLayout</c>), which GDI+ fills would not follow.
/// </remarks>
internal sealed class SuggestionPopup : Form
{
    private const int WM_DPICHANGED = 0x02E0;
    private const int WS_POPUP = unchecked((int)0x80000000);

    private readonly Dictionary<float, Font> _fonts = [];
    private readonly PopupState _state;
    private IReadOnlyList<string> _words = [];
    private SuggestionBarLayout? _layout;
    private SuggestionBarMetrics _metrics = new();
    private PopupColors _colors = PopupColors.Dark;
    private PopupAppearance _appearance = new();
    private bool _rightToLeft = true;
    private int _selected = -1;
    private int _hovered = -1;

    /// <param name="state">Published on every show/hide so the keyboard hook knows what is on screen.</param>
    public SuggestionPopup(PopupState state)
    {
        _state = state;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        RightToLeft = RightToLeft.Yes;
        DoubleBuffered = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public PopupAppearance Appearance
    {
        get => _appearance;
        set
        {
            _appearance = value;
            if (Math.Abs(Opacity - value.Opacity) > 0.001)
            {
                Opacity = value.Opacity; // layered window; never shows or activates it
            }

            ApplyTheme();
            Invalidate();
        }
    }

    /// <summary>A suggestion was clicked: (index, word). Raised on the UI thread.</summary>
    public event Action<int, string>? SuggestionClicked;

    /// <summary>A suggestion was right-clicked: (index, word, screen point) for its menu. Raised on the UI thread.</summary>
    public event Action<int, string, Point>? SuggestionMenuRequested;

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

    /// <summary>Shows the bar for <paramref name="words"/> next to <paramref name="caret"/> (screen pixels).</summary>
    /// <param name="words">Suggestions, best first.</param>
    /// <param name="caret">Caret rectangle, or the mouse pointer when <paramref name="belowPointer"/>.</param>
    /// <param name="rightToLeft">Arabic text.</param>
    /// <param name="belowPointer">No caret was found: show below the mouse pointer.</param>
    public void ShowSuggestions(IReadOnlyList<string> words, Rectangle caret, bool rightToLeft, bool belowPointer)
    {
        var monitor = Monitors.FromPoint(new Point(caret.X, caret.Y + (caret.Height / 2)));
        _metrics = SuggestionBarMetrics.ForFontSize(_appearance.FontPixels).Scale(monitor.Scale);
        _words = words;
        _rightToLeft = rightToLeft;
        ApplyTheme();

        using (var measure = Graphics.FromHwnd(Handle))
        {
            _layout = BarRenderer.Measure(measure, words, FontFor(_metrics.FontPixels), _metrics, rightToLeft);
        }

        var position = PopupPlacement.Place(caret, _layout.Size, monitor.WorkArea, rightToLeft, _metrics.CaretGap, belowPointer);
        var bounds = new Rectangle(position, _layout.Size);
        _selected = -1;
        _hovered = -1;
        PopupWindows.ShowWithoutActivation(Handle, bounds);
        IsShowing = true;
        _state.Show(words, bounds);
        Invalidate();
    }

    /// <summary>Native navigation selection (-1: none), painted with the Windows accent color.</summary>
    public void SetSelected(int index)
    {
        if (_selected != index)
        {
            _selected = index;
            Invalidate();
        }
    }

    /// <summary>
    /// Whether the bar is on screen. Tracked here because it is shown with SetWindowPos, which
    /// sends no WM_SHOWWINDOW, so WinForms' <see cref="Control.Visible"/> never becomes true.
    /// </summary>
    public bool IsShowing { get; private set; }

    public void HideSuggestions()
    {
        _state.Hide();
        if (IsHandleCreated && IsShowing)
        {
            PopupWindows.Hide(Handle);
            IsShowing = false;
        }
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

    // The click never activates the bar (MA_NOACTIVATE): the app keeps focus and receives the word.
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (ItemAt(e.Location) is var index and >= 0)
        {
            if (e.Button == MouseButtons.Left)
            {
                SuggestionClicked?.Invoke(index, _words[index]);
            }
            else if (e.Button == MouseButtons.Right)
            {
                SuggestionMenuRequested?.Invoke(index, _words[index], PointToScreen(e.Location));
            }
        }
    }

    private int ItemAt(Point point)
    {
        if (_layout is null)
        {
            return -1;
        }

        for (var i = 0; i < _layout.Items.Count && i < _words.Count; i++)
        {
            if (_layout.Items[i].Contains(point))
            {
                return i;
            }
        }

        return -1;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyTheme();
    }

    protected override void WndProc(ref Message m)
    {
        switch (m.Msg)
        {
            case PopupWindows.WM_MOUSEACTIVATE:
                m.Result = PopupWindows.MA_NOACTIVATE;
                return;
            case WM_DPICHANGED:
                return; // sizes are computed per monitor in ShowSuggestions
        }

        base.WndProc(ref m);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(_colors.Background);
        if (_layout is not null)
        {
            var frame = new BarRenderer.Frame(_words, _layout, _metrics, _colors, _appearance, _rightToLeft, _selected, _hovered);
            BarRenderer.Paint(e.Graphics, frame, FontFor(_metrics.FontPixels), FontFor(_metrics.FontPixels * 0.7f));
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
            foreach (var font in _fonts.Values)
            {
                font.Dispose();
            }
        }

        base.Dispose(disposing);
    }

    private Font FontFor(float pixels)
    {
        if (!_fonts.TryGetValue(pixels, out var font))
        {
            font = new Font("Segoe UI", pixels, FontStyle.Regular, GraphicsUnit.Pixel);
            _fonts[pixels] = font;
        }

        return font;
    }

    private void ApplyTheme()
    {
        _colors = PopupColors.For(_appearance);
        if (IsHandleCreated)
        {
            PopupWindows.ApplyWindows11Style(Handle, _colors.Border);
        }
    }

    // Live switch when Windows changes light/dark mode or accent color.
    private void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.Color or UserPreferenceCategory.VisualStyle)
        {
            ApplyTheme();
            Invalidate();
        }
    }
}
