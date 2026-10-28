using System.ComponentModel;
using System.Drawing.Drawing2D;
using Itmam.Core.Input;

namespace Itmam.App.SettingsUi;

/// <summary>Display names of shortcuts ("Ctrl+Alt+P"), always written left to right like Windows does.</summary>
internal static class HotkeyNames
{
    public static string Format(Hotkey hotkey)
    {
        var parts = new List<string>(4);
        if (hotkey.Modifiers.HasFlag(KeyModifiers.Ctrl))
        {
            parts.Add("Ctrl");
        }

        if (hotkey.Modifiers.HasFlag(KeyModifiers.Alt))
        {
            parts.Add("Alt");
        }

        if (hotkey.Modifiers.HasFlag(KeyModifiers.Shift))
        {
            parts.Add("Shift");
        }

        parts.Add(KeyName(hotkey.VirtualKey));
        return string.Join("+", parts);
    }

    public static string Format(KeyModifiers modifiers) => Format(new Hotkey(0, modifiers)).TrimEnd('+', '?');

    private static string KeyName(int virtualKey) => (Keys)virtualKey switch
    {
        0 => "?",
        Keys.Escape => "Esc",
        Keys.Return => "Enter",
        Keys.Back => "Backspace",
        Keys.Prior => "Page Up",
        Keys.Next => "Page Down",
        Keys.Space => "Space",
        >= Keys.D0 and <= Keys.D9 => ((char)virtualKey).ToString(),
        >= Keys.NumPad0 and <= Keys.NumPad9 => $"Num {virtualKey - (int)Keys.NumPad0}",
        Keys.OemPeriod => ".",
        Keys.Oemcomma => ",",
        Keys.OemMinus => "-",
        Keys.Oemplus => "=",
        Keys.OemQuestion => "/",
        Keys.OemSemicolon => ";",
        Keys.OemQuotes => "'",
        Keys.OemOpenBrackets => "[",
        Keys.OemCloseBrackets => "]",
        Keys.OemPipe => "\\",
        Keys.Oemtilde => "`",
        var key => key.ToString(),
    };
}

/// <summary>
/// Shows a shortcut; click it (or press Enter/Space) and press the new keys to record one.
/// Clicking elsewhere cancels. Every key, Tab and Esc included, can be recorded.
/// </summary>
internal sealed class HotkeyBox : ThemedControl
{
    private Hotkey _hotkey;
    private bool _recording;
    private bool _hovered;

    public HotkeyBox()
    {
        Size = new Size(170, 32);
        Cursor = Cursors.Hand;
        TabStop = true;
    }

    /// <summary>Keys were pressed while recording; the page checks them and sets <see cref="Hotkey"/>.</summary>
    public event Action<Hotkey>? Recorded;

    /// <summary>Recording started (true) or ended (false): Itmam's own shortcuts are paused meanwhile.</summary>
    public event Action<bool>? RecordingChanged;

    /// <summary>Text shown while recording ("Press the new shortcut…").</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string RecordingText { get; set; } = string.Empty;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Hotkey Hotkey
    {
        get => _hotkey;
        set
        {
            _hotkey = value;
            Invalidate();
        }
    }

    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);
        Focus();
        SetRecording(!_recording);
    }

    protected override void OnLostFocus(EventArgs e)
    {
        base.OnLostFocus(e);
        SetRecording(false);
    }

    // While recording every key is ours: Tab, arrows, Esc and Enter must not move focus or close anything.
    protected override bool IsInputKey(Keys keyData) => _recording || keyData is Keys.Enter || base.IsInputKey(keyData);

    protected override bool IsInputChar(char charCode) => _recording || base.IsInputChar(charCode);

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData) =>
        _recording ? false : base.ProcessCmdKey(ref msg, keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        e.Handled = true;
        e.SuppressKeyPress = true;
        if (!_recording)
        {
            if (e.KeyCode is Keys.Enter or Keys.Space)
            {
                SetRecording(true);
            }

            return;
        }

        var key = (int)e.KeyCode;
        if (HotkeyRules.IsModifierKey(key) || e.KeyCode is Keys.Menu or Keys.LWin or Keys.RWin)
        {
            return; // wait for the main key
        }

        var modifiers = (e.Control ? KeyModifiers.Ctrl : KeyModifiers.None)
            | (e.Alt ? KeyModifiers.Alt : KeyModifiers.None)
            | (e.Shift ? KeyModifiers.Shift : KeyModifiers.None);
        SetRecording(false);
        Recorded?.Invoke(new Hotkey(key, modifiers));
    }

    // Alt+key arrives as a system key: record it instead of letting it reach the window menu.
    protected override void WndProc(ref Message m)
    {
        const int WM_SYSCHAR = 0x0106;
        if (_recording && m.Msg == WM_SYSCHAR)
        {
            return;
        }

        base.WndProc(ref m);
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
        _hovered = false;
        Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            SetRecording(false);
        }

        base.Dispose(disposing);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(BackColor);
        var bounds = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        using var path = Drawing.Rounded(bounds, 4 * DpiScale);
        using (var fill = new SolidBrush(_hovered && !_recording ? Palette.NavHover : Palette.ControlFill))
        using (var border = new Pen(_recording ? Palette.Accent : Palette.CardBorder, (_recording ? 2 : 1) * DpiScale))
        {
            g.FillPath(fill, path);
            g.DrawPath(border, path);
        }

        var rtl = RightToLeft == RightToLeft.Yes;
        var text = _recording ? RecordingText : HotkeyNames.Format(_hotkey);
        var flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis;
        if (_recording && rtl)
        {
            flags |= TextFormatFlags.RightToLeft;
        }

        TextRenderer.DrawText(g, text, Font, ClientRectangle, _recording ? Palette.SecondaryText : Palette.Text, flags);

        if (Focused && ShowFocusCues && !_recording)
        {
            using var focus = new Pen(Palette.Text, DpiScale);
            g.DrawPath(focus, path);
        }
    }

    private void SetRecording(bool recording)
    {
        if (_recording != recording)
        {
            _recording = recording;
            Invalidate();
            RecordingChanged?.Invoke(recording);
        }
    }
}
