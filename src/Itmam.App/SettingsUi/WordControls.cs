using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Globalization;
using Itmam.App.Resources;
using Itmam.Core.Learning;
using Itmam.Core.Suggestions;
using Itmam.Win32;

namespace Itmam.App.SettingsUi;

/// <summary>Buttons side by side, from the start of the line (right in Arabic).</summary>
internal sealed class ButtonRow : ThemedControl
{
    private const int Gap = 8;
    private readonly ThemedButton[] _buttons;

    public ButtonRow(params ThemedButton[] buttons)
    {
        _buttons = buttons;
        Controls.AddRange(buttons);
        Height = 32;
        Width = buttons.Sum(b => b.Width) + (Gap * Math.Max(0, buttons.Length - 1));
    }

    /// <summary>Takes the width it is given (a full-width page row) instead of fitting its buttons (in a card).</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Stretch { get; init; }

    protected override void OnPaletteChanged()
    {
        foreach (var button in _buttons)
        {
            button.Palette = Palette;
        }
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        var gap = (int)(Gap * DpiScale);
        var needed = _buttons.Sum(b => b.Width) + (gap * Math.Max(0, _buttons.Length - 1));
        if (!Stretch && Width != needed)
        {
            Width = needed; // a button was re-measured with the window's font; the card re-lays out
        }

        var rtl = RightToLeft == RightToLeft.Yes;
        var x = rtl ? Width : 0;
        foreach (var button in _buttons)
        {
            button.Top = (Height - button.Height) / 2;
            button.Left = rtl ? x - button.Width : x;
            x += (rtl ? -1 : 1) * (button.Width + gap);
        }
    }

    protected override void OnPaint(PaintEventArgs e) => e.Graphics.Clear(BackColor);
}

/// <summary>
/// The user's words: a virtual, owner-drawn list (only visible rows are drawn, so thousands of
/// words stay fast) with its own scroll bar. Each row shows the word and its uses, or "blocked".
/// </summary>
internal sealed class WordList : ThemedControl
{
    private const int RowHeight = 40;
    private readonly VScrollBar _scroll = new() { SmallChange = 1 };
    private IReadOnlyList<StoredWord> _words = [];
    private int _selected = -1;
    private int _hovered = -1;

    public WordList()
    {
        Height = 400;
        TabStop = true;
        Controls.Add(_scroll);
        _scroll.ValueChanged += (_, _) => Invalidate();
    }

    /// <summary>The selection changed.</summary>
    public event EventHandler? SelectionChanged;

    /// <summary>Double-click or Enter on a word (edit it).</summary>
    public event EventHandler? ItemActivated;

    /// <summary>Delete pressed on a word.</summary>
    public event EventHandler? DeleteRequested;

    /// <summary>Right-click on a word: show its actions at this point (client coordinates).</summary>
    public event Action<Point>? ContextRequested;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public IReadOnlyList<StoredWord> Words
    {
        get => _words;
        set
        {
            var keep = SelectedWord?.Word;
            _words = value;
            _selected = keep is null ? -1 : IndexOf(keep);
            _hovered = -1;
            UpdateScroll();
            Invalidate();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public StoredWord? SelectedWord => _selected >= 0 && _selected < _words.Count ? _words[_selected] : null;

    private int RowPixels => (int)(RowHeight * DpiScale);

    private int VisibleRows => Math.Max(1, Height / RowPixels);

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        WindowChrome.SetDarkScrollbars(_scroll.Handle, Palette.IsDark);
    }

    protected override void OnPaletteChanged()
    {
        if (_scroll.IsHandleCreated)
        {
            WindowChrome.SetDarkScrollbars(_scroll.Handle, Palette.IsDark);
        }
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        var width = SystemInformation.VerticalScrollBarWidth;
        _scroll.SetBounds(RightToLeft == RightToLeft.Yes ? 1 : Width - width - 1, 1, width, Height - 2);
        UpdateScroll();
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        ScrollTo(_scroll.Value - (e.Delta / 120 * 3));
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var row = RowAt(e.Location);
        if (row != _hovered)
        {
            _hovered = row;
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
        if (RowAt(e.Location) is var row and >= 0)
        {
            Select(row);
            if (e.Button == MouseButtons.Right)
            {
                ContextRequested?.Invoke(e.Location);
            }
        }
    }

    protected override void OnDoubleClick(EventArgs e)
    {
        base.OnDoubleClick(e);
        if (SelectedWord is not null)
        {
            ItemActivated?.Invoke(this, EventArgs.Empty);
        }
    }

    protected override bool IsInputKey(Keys keyData) =>
        keyData is Keys.Up or Keys.Down or Keys.PageUp or Keys.PageDown or Keys.Home or Keys.End or Keys.Enter || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        switch (e.KeyCode)
        {
            case Keys.Down: Select(_selected + 1); break;
            case Keys.Up: Select(Math.Max(0, _selected - 1)); break;
            case Keys.PageDown: Select(_selected + VisibleRows); break;
            case Keys.PageUp: Select(Math.Max(0, _selected - VisibleRows)); break;
            case Keys.Home: Select(0); break;
            case Keys.End: Select(_words.Count - 1); break;
            case Keys.Enter when SelectedWord is not null: ItemActivated?.Invoke(this, EventArgs.Empty); break;
            case Keys.Delete when SelectedWord is not null: DeleteRequested?.Invoke(this, EventArgs.Empty); break;
        }
    }

    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);
        Invalidate();
    }

    protected override void OnLostFocus(EventArgs e)
    {
        base.OnLostFocus(e);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Palette.Window);
        var bounds = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        using (var path = Drawing.Rounded(bounds, 4 * DpiScale))
        using (var fill = new SolidBrush(Palette.Card))
        using (var border = new Pen(Focused ? Palette.ControlBorder : Palette.CardBorder))
        {
            g.FillPath(fill, path);
            g.DrawPath(border, path);
        }

        var rtl = RightToLeft == RightToLeft.Yes;
        var scrollWidth = _scroll.Visible ? _scroll.Width + 2 : 0;
        var left = rtl ? scrollWidth + 4 : 4;
        var width = Width - scrollWidth - 8;
        var padding = (int)(14 * DpiScale);
        var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
        var start = flags | (rtl ? TextFormatFlags.Right | TextFormatFlags.RightToLeft : TextFormatFlags.Left);
        var end = flags | (rtl ? TextFormatFlags.Left | TextFormatFlags.RightToLeft : TextFormatFlags.Right);
        using var wordFont = new Font(Font.FontFamily, Font.Size * 1.15f);

        var first = _scroll.Visible ? _scroll.Value : 0;
        for (var i = first; i < _words.Count && (i - first) * RowPixels < Height; i++)
        {
            var row = new Rectangle(left, 2 + ((i - first) * RowPixels), width, RowPixels);
            if (i == _selected || i == _hovered)
            {
                using var highlight = Drawing.Rounded(Rectangle.Inflate(row, 0, -2), 4 * DpiScale);
                using var brush = new SolidBrush(i == _selected ? Palette.NavSelected : Palette.NavHover);
                g.FillPath(brush, highlight);
                if (i == _selected)
                {
                    var bar = new RectangleF(rtl ? row.Right - (3 * DpiScale) : row.Left, row.Top + (row.Height * 0.3f), 3 * DpiScale, row.Height * 0.4f);
                    using var accentPath = Drawing.Rounded(bar, 1.5f * DpiScale);
                    using var accent = new SolidBrush(Palette.Accent);
                    g.FillPath(accent, accentPath);
                }
            }

            var word = _words[i];
            var text = Rectangle.Inflate(row, -padding, 0);
            TextRenderer.DrawText(g, word.Word, wordFont, text, word.Blocked ? Palette.SecondaryText : Palette.Text, start);
            var details = word.Blocked
                ? Strings.WordBlocked
                : string.Format(CultureInfo.CurrentCulture, Strings.WordUses, word.Count, word.LastUsed.ToLocalTime().DateTime);
            TextRenderer.DrawText(g, details, Font, text, Palette.SecondaryText, end);
        }
    }

    private void Select(int row)
    {
        if (_words.Count == 0)
        {
            return;
        }

        row = Math.Clamp(row, 0, _words.Count - 1);
        if (row != _selected)
        {
            _selected = row;
            Invalidate();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }

        // Keep the selection on screen.
        if (row < _scroll.Value)
        {
            ScrollTo(row);
        }
        else if (row >= _scroll.Value + VisibleRows)
        {
            ScrollTo(row - VisibleRows + 1);
        }
    }

    private void ScrollTo(int value)
    {
        if (_scroll.Visible)
        {
            _scroll.Value = Math.Clamp(value, 0, Math.Max(0, _scroll.Maximum - _scroll.LargeChange + 1));
        }
    }

    private void UpdateScroll()
    {
        var visible = VisibleRows;
        _scroll.Visible = _words.Count > visible;
        _scroll.Minimum = 0;
        _scroll.LargeChange = visible;
        _scroll.Maximum = Math.Max(0, _words.Count - 1);
        ScrollTo(_scroll.Value);
    }

    private int RowAt(Point point)
    {
        var first = _scroll.Visible ? _scroll.Value : 0;
        var row = first + ((point.Y - 2) / RowPixels);
        var overScroll = _scroll.Visible && _scroll.Bounds.Contains(point);
        return !overScroll && point.Y >= 2 && row < _words.Count ? row : -1;
    }

    private int IndexOf(string word)
    {
        for (var i = 0; i < _words.Count; i++)
        {
            if (_words[i].Word == word)
            {
                return i;
            }
        }

        return -1;
    }
}

/// <summary>Asks for a text shortcut: its key («ع1») and the text it inserts.</summary>
internal sealed class TextShortcutDialog : Form
{
    private readonly TextBox _key = new() { BorderStyle = BorderStyle.FixedSingle };
    private readonly TextBox _text = new() { BorderStyle = BorderStyle.FixedSingle };
    private readonly Label _error = new() { AutoSize = false, UseMnemonic = false };

    private TextShortcutDialog(string title, TextShortcut? initial, SettingsPalette palette, bool rtl)
    {
        Text = title;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = MaximizeBox = ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleDimensions = new SizeF(96f, 96f); // laid out at 100 %, scaled to the monitor
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 10f);
        ClientSize = new Size(460, 250);
        RightToLeft = rtl ? RightToLeft.Yes : RightToLeft.No;
        RightToLeftLayout = rtl;
        KeyPreview = true;
        BackColor = palette.Window;
        ForeColor = palette.Text;

        var keyLabel = new Label { Text = Strings.TextShortcutKeyPrompt, AutoSize = false, Bounds = new Rectangle(16, 14, 428, 24), UseMnemonic = false };
        _key.Bounds = new Rectangle(16, 40, 160, 30);
        var textLabel = new Label { Text = Strings.TextShortcutTextPrompt, AutoSize = false, Bounds = new Rectangle(16, 82, 428, 24), UseMnemonic = false };
        _text.Bounds = new Rectangle(16, 108, 428, 30);
        foreach (var box in new[] { _key, _text })
        {
            box.Font = new Font("Segoe UI", 12f);
            box.BackColor = palette.ControlFill;
            box.ForeColor = palette.Text;
        }

        _key.Text = initial?.Key ?? string.Empty;
        _text.Text = initial?.Text ?? string.Empty;
        _text.MaxLength = TextShortcuts.MaxTextLength;
        _key.MaxLength = TextShortcuts.MaxKeyLength;
        _error.Bounds = new Rectangle(16, 146, 428, 48);
        _error.ForeColor = Color.FromArgb(0xE8, 0x11, 0x23);

        var ok = new ThemedButton { Text = Strings.OkButton, Palette = palette, Top = 204 };
        var cancel = new ThemedButton { Text = Strings.CancelButton, Palette = palette, Top = 204 };
        ok.Left = 444 - ok.Width - cancel.Width - 8;
        cancel.Left = 444 - cancel.Width;
        ok.Click += (_, _) => Accept();
        cancel.Click += (_, _) => DialogResult = DialogResult.Cancel;
        Controls.AddRange([keyLabel, _key, textLabel, _text, _error, ok, cancel]);
        Shown += (_, _) =>
        {
            WindowChrome.SetDarkTitleBar(Handle, palette.IsDark);
            _key.SelectAll();
            _key.Focus();
        };
    }

    public TextShortcut? Shortcut { get; private set; }

    /// <summary>Shows the dialog; null when cancelled.</summary>
    public static TextShortcut? Ask(IWin32Window owner, string title, TextShortcut? initial, SettingsPalette palette, bool rtl)
    {
        using var dialog = new TextShortcutDialog(title, initial, palette, rtl) { TopMost = owner is Form { TopMost: true } };
        return dialog.ShowDialog(owner) == DialogResult.OK ? dialog.Shortcut : null;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Enter)
        {
            e.SuppressKeyPress = true;
            Accept();
        }
        else if (e.KeyCode == Keys.Escape)
        {
            DialogResult = DialogResult.Cancel;
        }
    }

    private void Accept()
    {
        if (TextShortcuts.CleanKey(_key.Text) is not { } key)
        {
            _error.Text = Strings.NotATextShortcutKey;
            _key.Focus();
        }
        else if (TextShortcuts.CleanText(_text.Text) is not { } text)
        {
            _error.Text = Strings.NotATextShortcutText;
            _text.Focus();
        }
        else
        {
            Shortcut = new TextShortcut(key, text);
            DialogResult = DialogResult.OK;
        }
    }
}

/// <summary>
/// Asks for one Arabic word (add, block, edit), or with a custom check a phrase ("My phrases").
/// Returns the cleaned text (tashkeel removed).
/// </summary>
internal sealed class WordDialog : Form
{
    private readonly TextBox _input = new() { BorderStyle = BorderStyle.FixedSingle };
    private readonly Label _error = new() { AutoSize = false, UseMnemonic = false };
    private Func<string, string?> _clean = UserWordIndex.Clean;
    private string _invalid = Strings.NotAnArabicWord;

    private WordDialog(string title, string prompt, string initial, SettingsPalette palette, bool rtl)
    {
        Text = title;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = MaximizeBox = ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleDimensions = new SizeF(96f, 96f); // laid out at 100 %, scaled to the monitor
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 10f);
        ClientSize = new Size(400, 170);
        RightToLeft = rtl ? RightToLeft.Yes : RightToLeft.No;
        RightToLeftLayout = rtl;
        KeyPreview = true;
        BackColor = palette.Window;
        ForeColor = palette.Text;

        var label = new Label { Text = prompt, AutoSize = false, Bounds = new Rectangle(16, 14, 368, 24), UseMnemonic = false };
        _input.Bounds = new Rectangle(16, 42, 368, 30);
        _input.Text = initial;
        _input.Font = new Font("Segoe UI", 12f);
        _input.BackColor = palette.ControlFill;
        _input.ForeColor = palette.Text;
        _error.Bounds = new Rectangle(16, 80, 368, 36);
        _error.ForeColor = Color.FromArgb(0xE8, 0x11, 0x23);

        var ok = new ThemedButton { Text = Strings.OkButton, Palette = palette, Top = 124 };
        var cancel = new ThemedButton { Text = Strings.CancelButton, Palette = palette, Top = 124 };
        ok.Left = 384 - ok.Width - cancel.Width - 8;
        cancel.Left = 384 - cancel.Width;
        ok.Click += (_, _) => Accept();
        cancel.Click += (_, _) => DialogResult = DialogResult.Cancel;
        Controls.AddRange([label, _input, _error, ok, cancel]);
        Shown += (_, _) =>
        {
            WindowChrome.SetDarkTitleBar(Handle, palette.IsDark);
            _input.SelectAll();
            _input.Focus();
        };
    }

    public string? Word { get; private set; }

    /// <summary>Shows the dialog; null when cancelled.</summary>
    /// <param name="clean">Validates and cleans the input (null = refused); default: one Arabic word.</param>
    /// <param name="invalid">Shown when <paramref name="clean"/> refuses the input.</param>
    public static string? Ask(IWin32Window owner, string title, string prompt, string initial, SettingsPalette palette, bool rtl,
        Func<string, string?>? clean = null, string? invalid = null)
    {
        using var dialog = new WordDialog(title, prompt, initial, palette, rtl) { TopMost = owner is Form { TopMost: true } };
        dialog._clean = clean ?? UserWordIndex.Clean;
        dialog._invalid = invalid ?? Strings.NotAnArabicWord;
        return dialog.ShowDialog(owner) == DialogResult.OK ? dialog.Word : null;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Enter)
        {
            e.SuppressKeyPress = true;
            Accept();
        }
        else if (e.KeyCode == Keys.Escape)
        {
            DialogResult = DialogResult.Cancel;
        }
    }

    private void Accept()
    {
        if (_clean(_input.Text.Trim()) is { } word)
        {
            Word = word;
            DialogResult = DialogResult.OK;
        }
        else
        {
            _error.Text = _invalid;
            _input.Focus();
        }
    }
}
