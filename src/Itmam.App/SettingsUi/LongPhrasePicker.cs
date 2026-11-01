using Itmam.App.Resources;
using Itmam.Core.Input;
using Itmam.Core.Suggestions;
using Itmam.Win32;

namespace Itmam.App.SettingsUi;

/// <summary>
/// The long phrases list (user decision 1.1), opened from anywhere with a shortcut (default
/// Ctrl+Shift+Space): type to filter by name or text, Up/Down to choose, Enter or a double-click to
/// insert, Esc to close. Unlike the suggestion bar it takes the focus (to type in its search box); on
/// insert it gives the focus back to the app the user was typing in and types the text there.
/// A phrase with variables asked for (1.2) first shows a field for each in place of the list: Tab moves
/// between them, Enter in the last one inserts, Esc goes back to the list. The values are not kept.
/// </summary>
internal sealed class LongPhrasePicker : Form
{
    private readonly TextBox _search = new() { BorderStyle = BorderStyle.FixedSingle };
    private readonly ListBox _list = new() { BorderStyle = BorderStyle.None, IntegralHeight = false };
    // Read-only text box rather than a label: a long reply scrolls instead of being cut off.
    private readonly TextBox _preview = new()
    {
        ReadOnly = true,
        Multiline = true,
        WordWrap = true,
        ScrollBars = ScrollBars.None, // shown by ShowPreview when the text is taller
        BorderStyle = BorderStyle.None,
        TabStop = false,
    };
    private readonly Label _fillHint = new() { AutoSize = false, UseMnemonic = false, Visible = false };
    private readonly Panel _fields = new() { AutoScroll = true, Visible = false };
    private readonly ThemedButton _insertButton = new() { Visible = false };
    private readonly ThemedButton _cancelButton = new() { Visible = false };
    private readonly List<(string Name, TextBox Box)> _boxes = [];
    private readonly SettingsPalette _palette;
    private readonly Rectangle _near;
    private LongPhrase? _filling;
    private Size _listClientSize;
    private Rectangle _listPreviewBounds;
    private readonly IReadOnlyList<LongPhrase> _all;
    private readonly nint _target;
    private readonly ITextInjector _injector;
    private readonly Action<int>? _inserted;
    private IReadOnlyList<LongPhrase> _shown;
    private bool _inserting;
    private readonly System.Windows.Forms.Timer _keysWatch = new() { Interval = 30 };
    private long _keysUpAt;
    private int _focusRetries;

    /// <param name="phrases">The user's long phrases.</param>
    /// <param name="target">The window the user was typing in.</param>
    /// <param name="injector">Types the chosen text.</param>
    /// <param name="palette">Settings colors (light or dark).</param>
    /// <param name="rtl">Arabic interface.</param>
    /// <param name="near">Where to open (the caret, or the mouse pointer), in screen pixels.</param>
    /// <param name="inserted">Called with the length of the inserted text (statistics).</param>
    public LongPhrasePicker(IReadOnlyList<LongPhrase> phrases, nint target, ITextInjector injector, SettingsPalette palette, bool rtl, Rectangle near,
        Action<int>? inserted = null)
    {
        _inserted = inserted;
        _palette = palette;
        _near = near;
        _all = phrases;
        _shown = phrases;
        _target = target;
        _injector = injector;

        Text = Strings.LongPhrasesTitle;
        FormBorderStyle = FormBorderStyle.FixedToolWindow;
        ShowInTaskbar = false;
        TopMost = true;
        KeyPreview = true;
        StartPosition = FormStartPosition.Manual;
        AutoScaleDimensions = new SizeF(96f, 96f); // laid out at 100 %, scaled to the monitor
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 10f);
        ClientSize = new Size(440, 400);
        RightToLeft = rtl ? RightToLeft.Yes : RightToLeft.No;
        RightToLeftLayout = rtl;
        BackColor = palette.Window;
        ForeColor = palette.Text;

        _search.Bounds = new Rectangle(12, 12, 416, 30);
        _search.PlaceholderText = Strings.LongPhrasesSearch;
        _list.Bounds = new Rectangle(12, 50, 416, 190);
        _list.Font = new Font("Segoe UI", 11f);
        _preview.Bounds = new Rectangle(12, 248, 416, 140);
        foreach (Control control in new Control[] { _search, _list })
        {
            control.BackColor = palette.ControlFill;
            control.ForeColor = palette.Text;
        }

        _preview.ForeColor = palette.SecondaryText;
        _preview.BackColor = palette.Window;
        _fillHint.Bounds = new Rectangle(12, 12, 416, 30);
        _fillHint.ForeColor = palette.SecondaryText;
        _fields.Bounds = _list.Bounds;
        _search.TextChanged += (_, _) => Filter();
        _list.SelectedIndexChanged += (_, _) => ShowPreview();
        _list.DoubleClick += (_, _) => InsertSelected();
        _insertButton.Palette = _cancelButton.Palette = palette;
        _insertButton.Text = Strings.InsertButton;
        _cancelButton.Text = Strings.CancelButton;
        _insertButton.Click += (_, _) => InsertSelected();
        _cancelButton.Click += (_, _) => LeaveFill();
        Controls.AddRange([_search, _list, _fillHint, _fields, _preview, _insertButton, _cancelButton]);

        // Shown at once, keys still held (user decision). Some apps (the new Notepad) take the focus back
        // when the shortcut's keys go up: until shortly after that, losing the focus does not close it.
        _keysWatch.Tick += (_, _) =>
        {
            if (!PopupWindows.AnyModifierDown())
            {
                _keysUpAt = Environment.TickCount64;
                _keysWatch.Stop();
            }
        };
        Load += (_, _) =>
        {
            _keysUpAt = Environment.TickCount64;
            _keysWatch.Start();
            WindowChrome.SetDarkTitleBar(Handle, palette.IsDark);
            WindowChrome.SetDarkScrollbars(_list.Handle, palette.IsDark);
            WindowChrome.SetDarkScrollbars(_preview.Handle, palette.IsDark);
            WindowChrome.SetDarkScrollbars(_fields.Handle, palette.IsDark);
            PlaceNear(near);
            Filter();
        };
        Shown += (_, _) =>
        {
            PopupWindows.BringToFront(Handle);
            Activate();
            _search.Focus();
        };
        Deactivate += (_, _) =>
        {
            if (_inserting)
            {
                return;
            }

            // The app took the focus back while or right after the shortcut's keys went up: take it again.
            var keysJustUsed = _keysWatch.Enabled || Environment.TickCount64 - _keysUpAt < 700;
            if (keysJustUsed && _focusRetries++ < 5)
            {
                BeginInvoke(() =>
                {
                    PopupWindows.BringToFront(Handle);
                    Activate();
                    _search.Focus();
                });
                return;
            }

            Close(); // clicked elsewhere
        };
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _keysWatch.Dispose();
        }

        base.Dispose(disposing);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_filling is not null)
        {
            // Filling the variables: Enter moves to the next field and inserts from the last one (or acts as
            // the focused button), Esc goes back to the list; the arrows stay with the text boxes.
            if (e.KeyCode == Keys.Enter)
            {
                var current = _boxes.FindIndex(b => b.Box.Focused);
                if (_cancelButton.Focused)
                {
                    LeaveFill();
                }
                else if (current >= 0 && current < _boxes.Count - 1)
                {
                    _boxes[current + 1].Box.Focus();
                }
                else
                {
                    InsertSelected();
                }

                e.Handled = e.SuppressKeyPress = true;
            }
            else if (e.KeyCode == Keys.Escape)
            {
                LeaveFill();
                e.Handled = e.SuppressKeyPress = true;
            }

            return;
        }

        switch (e.KeyCode)
        {
            case Keys.Down when _list.Items.Count > 0:
                _list.SelectedIndex = Math.Min(_list.SelectedIndex + 1, _list.Items.Count - 1);
                e.Handled = e.SuppressKeyPress = true;
                break;
            case Keys.Up when _list.Items.Count > 0:
                _list.SelectedIndex = Math.Max(_list.SelectedIndex - 1, 0);
                e.Handled = e.SuppressKeyPress = true;
                break;
            case Keys.Enter:
                InsertSelected();
                e.Handled = e.SuppressKeyPress = true;
                break;
            case Keys.Escape:
                Close();
                e.Handled = e.SuppressKeyPress = true;
                break;
        }
    }

    private void Filter()
    {
        _shown = LongPhrases.Filter(_all, _search.Text);
        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (var phrase in _shown)
        {
            _list.Items.Add(phrase.Name);
        }

        _list.EndUpdate();
        if (_shown.Count > 0)
        {
            _list.SelectedIndex = 0;
        }

        ShowPreview();
    }

    // Automatic variables filled in; the ones asked for show their typed value, or {name} while empty.
    // The scroll bar only when the text is taller than the box.
    private void ShowPreview()
    {
        var text = PreviewText();
        var height = TextRenderer.MeasureText(text, _preview.Font, new Size(_preview.ClientSize.Width - SystemInformation.VerticalScrollBarWidth, int.MaxValue),
            TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl).Height;
        var scrollBars = height > _preview.ClientSize.Height ? ScrollBars.Vertical : ScrollBars.None;
        if (_preview.ScrollBars != scrollBars)
        {
            _preview.ScrollBars = scrollBars;
            WindowChrome.SetDarkScrollbars(_preview.Handle, _palette.IsDark); // the handle is recreated
        }

        _preview.Text = text.Replace("\n", "\r\n", StringComparison.Ordinal);
    }

    private string PreviewText() =>
        _filling is not null ? LongPhraseVariables.Fill(_filling.Text, TypedValues(placeholders: true), DateTime.Now)
            : _all.Count == 0 ? Strings.LongPhrasesEmpty
            : _list.SelectedIndex >= 0 && _list.SelectedIndex < _shown.Count ? LongPhraseVariables.Fill(_shown[_list.SelectedIndex].Text, null, DateTime.Now)
            : Strings.LongPhrasesNoMatch;

    private Dictionary<string, string> TypedValues(bool placeholders) =>
        _boxes.ToDictionary(b => b.Name, b => placeholders && b.Box.TextLength == 0 ? LongPhraseVariables.InputToken(b.Name) : b.Box.Text);

    // A field for each variable asked for, in place of the search box and the list. The window grows so
    // every field and the whole preview show: wider (640, user decision, so long replies take fewer lines)
    // and taller, both within the screen (small screens such as 1366×768); beyond that they scroll.
    private void EnterFill(LongPhrase phrase)
    {
        _filling = phrase;
        _listClientSize = ClientSize;
        _listPreviewBounds = _preview.Bounds;
        var scale = DeviceDpi / 96f; // laid out at 100 %, like the constructor
        int Px(int logical) => (int)Math.Round(logical * scale);

        var area = Screen.FromControl(this).WorkingArea;
        var frameWidth = Width - ClientSize.Width;
        ClientSize = new Size(Math.Max(ClientSize.Width, Math.Min(Px(640), area.Width - frameWidth - Px(32))), ClientSize.Height);

        var names = LongPhraseVariables.InputNames(phrase.Text);
        var margin = Px(12);
        var width = ClientSize.Width - 2 * margin;
        var rowHeight = Px(62);
        var fieldsHeight = names.Count * rowHeight;
        var top = _list.Top;
        _fillHint.Width = width;

        // Room on the screen for the client area, then the fields and the preview share it.
        var maxClient = area.Height - (Height - ClientSize.Height) - Px(16);
        var previewWanted = TextRenderer.MeasureText(
            LongPhraseVariables.Fill(phrase.Text, null, DateTime.Now) + "\n",
            _preview.Font,
            new Size(width - SystemInformation.VerticalScrollBarWidth, int.MaxValue),
            TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl).Height + Px(8);
        previewWanted = Math.Max(previewWanted, Px(80));
        var buttonsRow = Px(12) + _insertButton.Height; // the Insert and Cancel buttons under the preview
        var room = maxClient - top - Px(8) - buttonsRow - margin;
        // Fields first, but the preview keeps at least half the room it needs (up to half the screen).
        var shownFields = Math.Clamp(room - Math.Min(previewWanted, room / 2), rowHeight, fieldsHeight);
        var previewHeight = Math.Max(Px(60), Math.Min(previewWanted, room - shownFields));

        _fields.SuspendLayout();
        _fields.Controls.Clear();
        _boxes.Clear();
        _fields.Bounds = new Rectangle(margin, top, width, shownFields);
        var rowWidth = shownFields < fieldsHeight ? width - SystemInformation.VerticalScrollBarWidth : width;
        var y = 0;
        foreach (var name in names)
        {
            var label = new Label { Text = name, AutoSize = false, Bounds = new Rectangle(0, y, rowWidth, Px(24)), UseMnemonic = false, ForeColor = _palette.Text };
            var box = new TextBox
            {
                BorderStyle = BorderStyle.FixedSingle,
                Bounds = new Rectangle(0, y + Px(24), rowWidth, Px(30)),
                BackColor = _palette.ControlFill,
                ForeColor = _palette.Text,
                TabIndex = _boxes.Count,
            };
            box.TextChanged += (_, _) => ShowPreview();
            _fields.Controls.AddRange([label, box]);
            _boxes.Add((name, box));
            y += rowHeight;
        }

        _fields.ResumeLayout();
        _preview.Bounds = new Rectangle(margin, top + shownFields + Px(8), width, previewHeight);
        _insertButton.TabIndex = _boxes.Count;
        _cancelButton.TabIndex = _boxes.Count + 1;
        _insertButton.Top = _cancelButton.Top = _preview.Bottom + Px(12);
        _cancelButton.Left = ClientSize.Width - margin - _cancelButton.Width;
        _insertButton.Left = _cancelButton.Left - Px(8) - _insertButton.Width;
        ClientSize = new Size(ClientSize.Width, _insertButton.Bottom + margin);

        // Centred on the screen while filling (user decision): the window is now too big to sit by the caret.
        Location = new Point(area.Left + Math.Max(0, (area.Width - Width) / 2), area.Top + Math.Max(0, (area.Height - Height) / 2));

        _fillHint.Text = string.Format(System.Globalization.CultureInfo.CurrentCulture, Strings.LongPhraseFillHint, phrase.Name);
        _search.Visible = _list.Visible = false;
        _fillHint.Visible = _fields.Visible = _insertButton.Visible = _cancelButton.Visible = true;
        ShowPreview();
        _boxes[0].Box.Focus();
    }

#if DEBUG
    /// <summary>Opens the fields of a phrase with sample values typed in (--demo-longphrases fill).</summary>
    public void DemoFill(int index, string[] values)
    {
        _list.SelectedIndex = index;
        EnterFill(_shown[index]);
        for (var i = 0; i < Math.Min(values.Length, _boxes.Count); i++)
        {
            _boxes[i].Box.Text = values[i];
        }
    }
#endif

    private void LeaveFill()
    {
        _filling = null;
        _fillHint.Visible = _fields.Visible = _insertButton.Visible = _cancelButton.Visible = false;
        _fields.Controls.Clear();
        _boxes.Clear(); // the typed values are not kept
        _preview.Bounds = _listPreviewBounds;
        ClientSize = _listClientSize;
        PlaceNear(_near);
        _search.Visible = _list.Visible = true;
        ShowPreview();
        _search.Focus();
    }

    // Focus back to the app, then paste the text there (the clipboard is restored afterwards).
    private async void InsertSelected()
    {
        if (_inserting || (_filling is null && (_list.SelectedIndex < 0 || _list.SelectedIndex >= _shown.Count)))
        {
            return;
        }

        var phrase = _filling ?? _shown[_list.SelectedIndex];
        if (_filling is null && LongPhraseVariables.HasInputs(phrase.Text))
        {
            EnterFill(phrase);
            return;
        }

        _inserting = true;
        var text = LongPhraseVariables.Fill(phrase.Text, TypedValues(placeholders: false), DateTime.Now);
        Hide();
        PopupWindows.Activate(_target);
        await Task.Delay(150); // let the app take the focus before the keys arrive

        // Pasted (user decision): typing hundreds of characters as key presses made Word stop responding.
        // If the clipboard is held by another app, it is typed instead.
        if (!await ClipboardPaste.PasteAsync(text, _injector))
        {
            _injector.Send(new TextEdit(0, text));
        }

        _inserted?.Invoke(text.Length);

        Close();
    }

    // Under the caret line, kept on the screen.
    private void PlaceNear(Rectangle near)
    {
        var area = Screen.FromRectangle(near).WorkingArea;
        var x = RightToLeft == RightToLeft.Yes ? near.Right - Width : near.Left;
        var y = near.Bottom + 8;
        if (y + Height > area.Bottom)
        {
            y = near.Top - Height - 8;
        }

        Location = new Point(Math.Clamp(x, area.Left, Math.Max(area.Left, area.Right - Width)), Math.Clamp(y, area.Top, Math.Max(area.Top, area.Bottom - Height)));
    }
}
