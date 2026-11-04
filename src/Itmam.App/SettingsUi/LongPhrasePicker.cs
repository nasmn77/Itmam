using Itmam.App.Resources;
using Itmam.Core.Input;
using Itmam.Core.Suggestions;
using Itmam.Win32;

namespace Itmam.App.SettingsUi;

/// <summary>
/// The long phrases list (user decision 1.1), opened from anywhere with a shortcut (default
/// Ctrl+Shift+Space): type to filter by name or text, Up/Down to choose, Enter or a double-click to
/// insert, Esc to close. Unlike the suggestion bar it takes the focus (to type in its search box); on
/// insert it gives the focus back to the app the user was typing in and pastes the text there.
/// The phrase's text can be edited before inserting (1.3); after an edit, Itmam asks whether to save it
/// in the phrase or insert it this time only.
/// A phrase with variables asked for (1.2) then shows a field for each in place of the list: Tab moves
/// between them, Enter in the last one inserts, Esc goes back to the list. The values are not kept; the
/// filled text can be edited too, which locks the fields so they do not overwrite the edit.
/// </summary>
internal sealed class LongPhrasePicker : Form
{
    private readonly TextBox _search = new() { BorderStyle = BorderStyle.FixedSingle };
    private readonly ListBox _list = new() { BorderStyle = BorderStyle.None, IntegralHeight = false };
    private readonly Label _textLabel = new() { AutoSize = false, UseMnemonic = false };

    // The phrase's text (with its {variables}) in the list, the filled reply while filling; editable.
    private readonly TextBox _text = new()
    {
        Multiline = true,
        AcceptsReturn = true, // Enter starts a new line here; Ctrl+Enter or Insert inserts
        WordWrap = true,
        ScrollBars = ScrollBars.None, // shown by ShowText when the text is taller
        BorderStyle = BorderStyle.FixedSingle,
    };

    private readonly Label _fillHint = new() { AutoSize = false, UseMnemonic = false, Visible = false };
    private readonly Panel _fields = new() { AutoScroll = true, Visible = false };
    private readonly ThemedButton _insertButton = new();
    private readonly ThemedButton _cancelButton = new();
    private readonly ThemedButton _addButton = new();
    private readonly List<(string Name, TextBox Box)> _boxes = [];
    private readonly SettingsPalette _palette;
    private readonly Rectangle _near;
    private readonly nint _target;
    private readonly ITextInjector _injector;
    private readonly Action<int>? _inserted;
    private readonly Action<LongPhrase, string>? _saveEdit;
    private readonly Func<IWin32Window, (IReadOnlyList<LongPhrase> All, LongPhrase Added)?>? _addNew;
    private readonly System.Windows.Forms.Timer _keysWatch = new() { Interval = 30 };
    private IReadOnlyList<LongPhrase> _all;
    private IReadOnlyList<LongPhrase> _shown;
    private LongPhrase? _filling;
    private bool _settingText;   // the text box is being filled by code, not edited by the user
    private bool _finalEdited;   // the filled reply was edited by hand: the fields no longer change it
    private bool _asking;        // a question is open: losing the focus to it must not close the list
    private bool _inserting;
    private long _keysUpAt;
    private bool _laidOut;       // the first layout is done: resizing lays the controls out again
    private bool _userSized;     // resized or maximized by the user: that size is kept from then on
    private Size _sizeAtResizeStart;
    private int _fillFieldsHeight; // every field's row, while filling
    private int _fillTextWanted;   // the height the reply needs, while filling
    private int _focusRetries;

    /// <param name="phrases">The user's long phrases.</param>
    /// <param name="target">The window the user was typing in.</param>
    /// <param name="injector">Types the chosen text.</param>
    /// <param name="palette">Settings colors (light or dark).</param>
    /// <param name="rtl">Arabic interface.</param>
    /// <param name="near">Where the user is typing (the caret, or the mouse pointer): picks the screen.</param>
    /// <param name="inserted">Called with the length of the inserted text (statistics).</param>
    /// <param name="saveEdit">Saves an edited text in the phrase (Settings), when the user says so.</param>
    /// <param name="addNew">
    /// Asks for a new long text and saves it (Settings): returns every long text and the one added, or null
    /// if cancelled. Without it there is no "add" button.
    /// </param>
    public LongPhrasePicker(IReadOnlyList<LongPhrase> phrases, nint target, ITextInjector injector, SettingsPalette palette, bool rtl, Rectangle near,
        Action<int>? inserted = null, Action<LongPhrase, string>? saveEdit = null,
        Func<IWin32Window, (IReadOnlyList<LongPhrase> All, LongPhrase Added)?>? addNew = null)
    {
        _inserted = inserted;
        _saveEdit = saveEdit;
        _addNew = addNew;
        _palette = palette;
        _near = near;
        _all = phrases;
        _shown = phrases;
        _target = target;
        _injector = injector;

        Text = Strings.LongPhrasesTitle;
        // Resizable, with a maximize button (user decision after 2.1); the layout follows the size.
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = true;
        MinimizeBox = false;
        ShowIcon = false;
        ShowInTaskbar = false;
        TopMost = true;
        KeyPreview = true;
        StartPosition = FormStartPosition.Manual;
        AutoScaleDimensions = new SizeF(96f, 96f); // laid out at 100 %, scaled to the monitor
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 10f);
        ClientSize = new Size(640, 560);
        RightToLeft = rtl ? RightToLeft.Yes : RightToLeft.No;
        RightToLeftLayout = rtl;
        BackColor = palette.Window;
        ForeColor = palette.Text;

        _search.PlaceholderText = Strings.LongPhrasesSearch;
        _list.Font = new Font("Segoe UI", 11f);
        _text.Font = new Font("Segoe UI", 10.5f);
        foreach (Control control in new Control[] { _search, _list, _text })
        {
            control.BackColor = palette.ControlFill;
            control.ForeColor = palette.Text;
        }

        _textLabel.Text = Strings.LongPhraseEditHint;
        _textLabel.ForeColor = _fillHint.ForeColor = palette.SecondaryText;
        _search.TextChanged += (_, _) => Filter();
        _list.SelectedIndexChanged += (_, _) => ShowText();
        _list.DoubleClick += (_, _) => InsertSelected();
        _text.TextChanged += (_, _) => TextEdited();
        _insertButton.Palette = _cancelButton.Palette = _addButton.Palette = palette;
        _insertButton.Text = Strings.InsertButton;
        _cancelButton.Text = Strings.CancelButton;
        _addButton.Text = Strings.AddLongPhraseShort;
        _addButton.Visible = addNew is not null;
        _insertButton.Click += (_, _) => InsertSelected();
        _cancelButton.Click += (_, _) => Cancel();
        _addButton.Click += (_, _) => AddNew();
        Controls.AddRange([_search, _list, _fillHint, _fields, _textLabel, _text, _addButton, _insertButton, _cancelButton]);

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
            WindowChrome.SetDarkScrollbars(_text.Handle, palette.IsDark);
            WindowChrome.SetDarkScrollbars(_fields.Handle, palette.IsDark);
            MinimumSize = SizeFromClientSize(new Size(Px(420), Px(360)));
            LayOutList();
            Filter();
            _laidOut = true;
        };
        ResizeBegin += (_, _) => _sizeAtResizeStart = Size;
        ResizeEnd += (_, _) => _userSized |= Size != _sizeAtResizeStart; // not a plain move
        Shown += (_, _) =>
        {
            PopupWindows.BringToFront(Handle);
            Activate();
            _search.Focus();
        };
        Deactivate += (_, _) =>
        {
            if (_inserting || _asking)
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
        var handled = true;
        switch (e.KeyCode)
        {
            // In the text box Enter is a new line; Ctrl+Enter inserts from anywhere.
            case Keys.Enter when _text.Focused && !e.Control:
                handled = false;
                break;
            case Keys.Enter when _cancelButton.Focused:
                Cancel();
                break;
            case Keys.Enter when _addButton.Focused:
                AddNew();
                break;
            case Keys.Enter when _filling is not null && !e.Control && _boxes.FindIndex(b => b.Box.Focused) is >= 0 and var field && field < _boxes.Count - 1:
                _boxes[field + 1].Box.Focus(); // the next field; the last one inserts
                break;
            case Keys.Enter:
                InsertSelected();
                break;
            case Keys.Escape:
                Cancel();
                break;

            // The arrows choose in the list, except while editing the text.
            case Keys.Down when _filling is null && !_text.Focused && _list.Items.Count > 0:
                _list.SelectedIndex = Math.Min(_list.SelectedIndex + 1, _list.Items.Count - 1);
                break;
            case Keys.Up when _filling is null && !_text.Focused && _list.Items.Count > 0:
                _list.SelectedIndex = Math.Max(_list.SelectedIndex - 1, 0);
                break;
            default:
                handled = false;
                break;
        }

        if (handled)
        {
            e.Handled = e.SuppressKeyPress = true;
        }
    }

    // User decision (after 2.0): a new long text can be added from here. It is saved in Settings and shown
    // chosen in the list; the user inserts it or not. An unsaved edit of the chosen text is dropped.
    private void AddNew()
    {
        if (_addNew is null || _filling is not null)
        {
            return;
        }

        (IReadOnlyList<LongPhrase> All, LongPhrase Added)? result;
        _asking = true; // the dialog takes the focus: the list must stay open
        try
        {
            result = _addNew(this);
        }
        finally
        {
            _asking = false;
        }

        Activate();
        if (result is not { } added)
        {
            _search.Focus();
            return;
        }

        _all = added.All;
        if (_search.Text.Length > 0)
        {
            _search.Text = string.Empty; // filters again
        }
        else
        {
            Filter();
        }

        var index = _shown.ToList().FindIndex(p => p.Name == added.Added.Name);
        if (index >= 0)
        {
            _list.SelectedIndex = index;
        }

        _list.Focus();
    }

    // Cancel and Esc: back to the list while filling, otherwise close (an unsaved edit is dropped).
    private void Cancel()
    {
        if (_filling is not null)
        {
            LeaveFill();
        }
        else
        {
            Close();
        }
    }

    private int Px(int logical) => (int)Math.Round(logical * DeviceDpi / 96f); // laid out at 100 %

    // The window size (640 × 560, within the screen) centred on the screen the user is typing on (user
    // decision 1.3: like the fields, so the window does not jump when they open).
    private void SizeWindow(int clientHeight)
    {
        if (_userSized || WindowState != FormWindowState.Normal)
        {
            return; // the size the user chose stays
        }

        var area = Screen.FromRectangle(_near).WorkingArea;
        var frame = Size - ClientSize;
        ClientSize = new Size(Math.Min(Px(640), area.Width - frame.Width - Px(32)), Math.Min(clientHeight, area.Height - frame.Height - Px(16)));
        Location = new Point(area.Left + Math.Max(0, (area.Width - Width) / 2), area.Top + Math.Max(0, (area.Height - Height) / 2));
    }

    // Search, list, the editable text, then the buttons.
    private void LayOutList()
    {
        SizeWindow(Px(560));
        ArrangeList();
    }

    // At the window's current size: the list takes two fifths of the height left, the text the rest.
    private void ArrangeList()
    {
        var margin = Px(12);
        var width = ClientSize.Width - 2 * margin;
        _search.Bounds = new Rectangle(margin, margin, width, Px(30));
        var buttonsTop = ClientSize.Height - margin - _insertButton.Height;
        var free = buttonsTop - Px(12) - Px(50) - Px(8) - Px(24) - Px(4);
        _list.Bounds = new Rectangle(margin, Px(50), width, Math.Max(Px(80), free * 2 / 5));
        _textLabel.Bounds = new Rectangle(margin, _list.Bottom + Px(8), width, Px(24));
        LayOutBottom(_textLabel.Bottom + Px(4), margin, width);
        FitScrollBars();
    }

    // Fields first, but the reply keeps at least half the room it needs (up to half the room there is).
    private void ArrangeFill()
    {
        var margin = Px(12);
        var width = ClientSize.Width - 2 * margin;
        var top = Px(50);
        var rowHeight = Px(62);
        var buttonsRow = Px(12) + _insertButton.Height;
        var room = ClientSize.Height - top - Px(8) - buttonsRow - margin;
        var shownFields = Math.Clamp(room - Math.Min(_fillTextWanted, room / 2), Math.Min(rowHeight, _fillFieldsHeight), _fillFieldsHeight);
        _fillHint.Bounds = new Rectangle(margin, margin, width, Px(30));
        _fields.Bounds = new Rectangle(margin, top, width, shownFields);
        var rowWidth = shownFields < _fillFieldsHeight ? width - SystemInformation.VerticalScrollBarWidth : width;
        foreach (Control row in _fields.Controls)
        {
            row.Width = rowWidth;
        }

        LayOutBottom(top + shownFields + Px(8), margin, width);
        FitScrollBars();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (!_laidOut || WindowState == FormWindowState.Minimized)
        {
            return;
        }

        if (WindowState == FormWindowState.Maximized)
        {
            _userSized = true;
        }

        if (_filling is null)
        {
            ArrangeList();
        }
        else
        {
            ArrangeFill();
        }
    }

    // The text box shows a scroll bar only when its text is taller than it (after a resize too).
    private void FitScrollBars()
    {
        var height = TextRenderer.MeasureText(_text.Text, _text.Font, new Size(Math.Max(1, _text.ClientSize.Width - SystemInformation.VerticalScrollBarWidth), int.MaxValue),
            TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl).Height;
        var scrollBars = height > _text.ClientSize.Height ? ScrollBars.Vertical : ScrollBars.None;
        if (_text.ScrollBars != scrollBars)
        {
            var caret = _text.SelectionStart;
            _text.ScrollBars = scrollBars;
            if (_text.IsHandleCreated)
            {
                WindowChrome.SetDarkScrollbars(_text.Handle, _palette.IsDark); // the handle is recreated
            }

            _text.SelectionStart = caret;
        }
    }

    // The text box fills what the buttons leave.
    private void LayOutBottom(int textTop, int margin, int width)
    {
        _insertButton.Top = _cancelButton.Top = _addButton.Top = ClientSize.Height - margin - _insertButton.Height;
        _cancelButton.Left = ClientSize.Width - margin - _cancelButton.Width;
        _insertButton.Left = _cancelButton.Left - Px(8) - _insertButton.Width;
        _addButton.Left = margin; // the other end of the row (mirrored for Arabic)
        _text.Bounds = new Rectangle(margin, textTop, width, _insertButton.Top - Px(12) - textTop);
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

        ShowText();
    }

    private LongPhrase? Selected => _list.SelectedIndex >= 0 && _list.SelectedIndex < _shown.Count ? _shown[_list.SelectedIndex] : null;

    // In the list: the chosen phrase's text as saved, {variables} included, to edit. While filling: the
    // reply with automatic variables filled in and the asked ones as typed, or {name} while empty.
    private void ShowText()
    {
        string text;
        if (_filling is not null)
        {
            if (_finalEdited)
            {
                return; // edited by hand: the fields no longer change it
            }

            text = LongPhraseVariables.Fill(_filling.Text, TypedValues(placeholders: true), DateTime.Now);
        }
        else
        {
            text = Selected?.Text ?? (_all.Count == 0 ? Strings.LongPhrasesEmpty : Strings.LongPhrasesNoMatch);
            _text.ReadOnly = Selected is null;
        }

        _settingText = true;
        var height = TextRenderer.MeasureText(text, _text.Font, new Size(_text.ClientSize.Width - SystemInformation.VerticalScrollBarWidth, int.MaxValue),
            TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl).Height;
        var scrollBars = height > _text.ClientSize.Height ? ScrollBars.Vertical : ScrollBars.None;
        if (_text.ScrollBars != scrollBars)
        {
            _text.ScrollBars = scrollBars;
            WindowChrome.SetDarkScrollbars(_text.Handle, _palette.IsDark); // the handle is recreated
        }

        _text.Text = text.Replace("\n", "\r\n", StringComparison.Ordinal);
        _settingText = false;
    }

    // Edited by hand: a long text gets its scroll bar; while filling, the fields stop changing the reply.
    private void TextEdited()
    {
        if (_settingText)
        {
            return;
        }

        if (_text.ScrollBars == ScrollBars.None && _text.GetLineFromCharIndex(_text.TextLength) * _text.Font.Height > _text.ClientSize.Height)
        {
            var caret = _text.SelectionStart;
            _text.ScrollBars = ScrollBars.Vertical;
            WindowChrome.SetDarkScrollbars(_text.Handle, _palette.IsDark);
            _text.SelectionStart = caret;
        }

        if (_filling is not null && !_finalEdited)
        {
            _finalEdited = true;
            foreach (var (_, box) in _boxes)
            {
                box.Enabled = false;
            }

            _fillHint.Text = Strings.LongPhraseFinalEdited;
        }
    }

    private Dictionary<string, string> TypedValues(bool placeholders) =>
        _boxes.ToDictionary(b => b.Name, b => placeholders && b.Box.TextLength == 0 ? LongPhraseVariables.InputToken(b.Name) : b.Box.Text);

    // A field for each variable asked for, in place of the search box and the list, then the reply. The
    // window grows to show every field and the whole reply, up to the screen's height; beyond that they scroll.
    private void EnterFill(LongPhrase phrase)
    {
        _filling = phrase;
        _finalEdited = false;
        var names = LongPhraseVariables.InputNames(phrase.Text);
        var margin = Px(12);
        var area = Screen.FromRectangle(_near).WorkingArea;
        var width = Math.Min(Px(640), area.Width - (Width - ClientSize.Width) - Px(32)) - 2 * margin;
        var rowHeight = Px(62);
        var fieldsHeight = names.Count * rowHeight;
        var top = Px(50);

        // Room on the screen for the client area, then the fields and the reply share it.
        var maxClient = area.Height - (Height - ClientSize.Height) - Px(16);
        var textWanted = TextRenderer.MeasureText(
            LongPhraseVariables.Fill(phrase.Text, null, DateTime.Now) + "\n",
            _text.Font,
            new Size(width - SystemInformation.VerticalScrollBarWidth, int.MaxValue),
            TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl).Height + Px(12);
        textWanted = Math.Max(textWanted, Px(100));
        var buttonsRow = Px(12) + _insertButton.Height;
        var room = maxClient - top - Px(8) - buttonsRow - margin;
        // Fields first, but the reply keeps at least half the room it needs (up to half the screen).
        var shownFields = Math.Clamp(room - Math.Min(textWanted, room / 2), rowHeight, fieldsHeight);
        var textHeight = Math.Max(Px(80), Math.Min(textWanted, room - shownFields));
        SizeWindow(top + shownFields + Px(8) + textHeight + buttonsRow + margin);
        _fillFieldsHeight = fieldsHeight;
        _fillTextWanted = textWanted;

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
            box.TextChanged += (_, _) => ShowText();
            _fields.Controls.AddRange([label, box]);
            _boxes.Add((name, box));
            y += rowHeight;
        }

        _fields.ResumeLayout();
        _fillHint.Text = string.Format(System.Globalization.CultureInfo.CurrentCulture, Strings.LongPhraseFillHint, phrase.Name);
        ArrangeFill(); // at the window's size (the user's, if they chose one)
        _text.ReadOnly = false;
        _text.TabIndex = _boxes.Count;
        _insertButton.TabIndex = _boxes.Count + 1;
        _cancelButton.TabIndex = _boxes.Count + 2;
        _search.Visible = _list.Visible = _textLabel.Visible = _addButton.Visible = false;
        _fillHint.Visible = _fields.Visible = true;
        ShowText();
        _boxes[0].Box.Focus();
    }

#if DEBUG
    /// <summary>Opens the fields of a phrase with sample values typed in (--demo-longphrases fill).</summary>
    public void DemoFill(int index, string[] values, bool insert = false)
    {
        _list.SelectedIndex = index;
        EnterFill(_shown[index]);
        for (var i = 0; i < Math.Min(values.Length, _boxes.Count); i++)
        {
            _boxes[i].Box.Text = values[i];
        }

        if (insert)
        {
            BeginInvoke(InsertSelected);
        }
    }

    /// <summary>Adds a line to the text as if typed (--demo-longphrases edit): an edit to save, or a locked reply.</summary>
    public void DemoEdit(string line, bool insert = false)
    {
        _text.AppendText(Environment.NewLine + line);
        if (insert)
        {
            BeginInvoke(InsertSelected); // the save question
        }
    }
#endif

    private void LeaveFill()
    {
        _filling = null;
        _finalEdited = false;
        _fillHint.Visible = _fields.Visible = false;
        _fields.Controls.Clear();
        _boxes.Clear(); // the typed values are not kept
        _search.Visible = _list.Visible = _textLabel.Visible = true;
        _addButton.Visible = _addNew is not null;
        LayOutList();
        ShowText();
        _search.Focus();
    }

    private bool Ask(string question, MessageBoxButtons buttons, out DialogResult answer)
    {
        var rtl = RightToLeft == RightToLeft.Yes;
        _asking = true;
        answer = MessageBox.Show(this, question, Strings.AppName, buttons, MessageBoxIcon.Question,
            buttons == MessageBoxButtons.YesNo ? MessageBoxDefaultButton.Button2 : MessageBoxDefaultButton.Button1,
            rtl ? MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign : 0);
        _asking = false;
        Activate();
        return answer != DialogResult.Cancel;
    }

    // Focus back to the app, then paste the text there (the clipboard is restored afterwards).
    private async void InsertSelected()
    {
        if (_inserting || (_filling ?? Selected) is not { } phrase)
        {
            return;
        }

        string text;
        if (_filling is null)
        {
            // Edited before inserting (user decision 1.3): save it in the phrase ("Yes") or use it this
            // time only ("No"); Cancel goes back to editing.
            if (LongPhrases.CleanText(_text.Text) is not { } edited)
            {
                return; // emptied: nothing to insert
            }

            if (edited != (LongPhrases.CleanText(phrase.Text) ?? phrase.Text))
            {
                if (!Ask(string.Format(System.Globalization.CultureInfo.CurrentCulture, Strings.SaveLongPhraseEdit, phrase.Name), MessageBoxButtons.YesNoCancel, out var answer))
                {
                    _text.Focus();
                    return;
                }

                if (answer == DialogResult.Yes)
                {
                    if (LongPhraseVariables.UnknownAutoVariables(edited) is [var unknown, ..])
                    {
                        Ask(string.Format(System.Globalization.CultureInfo.CurrentCulture, Strings.UnknownAutoVariable, unknown), MessageBoxButtons.OK, out _);
                        _text.Focus();
                        return;
                    }

                    _saveEdit?.Invoke(phrase, edited);
                    var saved = phrase with { Text = edited };
                    _all = [.. _all.Select(p => p.Name == phrase.Name ? saved : p)]; // Esc from the fields shows it
                    _shown = [.. _shown.Select(p => p.Name == phrase.Name ? saved : p)];
                }

                phrase = phrase with { Text = edited };
            }

            if (LongPhraseVariables.HasInputs(phrase.Text))
            {
                EnterFill(phrase);
                return;
            }

            text = LongPhraseVariables.Fill(phrase.Text, new Dictionary<string, string>(), DateTime.Now);
        }
        else if (_finalEdited)
        {
            if (LongPhrases.CleanText(_text.Text) is not { } edited)
            {
                return;
            }

            // Edited by hand after filling: inserted as it is, but the variables still written as {name} were
            // not filled. Same question as for empty fields; "No" selects the first one to type over it, "Yes"
            // leaves them out (never inserted with their braces).
            var unfilled = LongPhraseVariables.InputNames(edited);
            if (unfilled.Count > 0)
            {
                var names = string.Join(RightToLeft == RightToLeft.Yes ? "، " : ", ", unfilled);
                Ask(string.Format(System.Globalization.CultureInfo.CurrentCulture, unfilled.Count == 1 ? Strings.EmptyVariable : Strings.EmptyVariables, names),
                    MessageBoxButtons.YesNo, out var answer);
                if (answer != DialogResult.Yes)
                {
                    var token = LongPhraseVariables.InputToken(unfilled[0]);
                    _text.Focus();
                    _text.Select(Math.Max(0, _text.Text.IndexOf(token, StringComparison.Ordinal)), token.Length);
                    _text.ScrollToCaret();
                    return;
                }
            }

            text = LongPhraseVariables.Fill(edited, unfilled.ToDictionary(n => n, _ => string.Empty), DateTime.Now);
        }
        else
        {
            // Empty fields (user decision): ask, naming them; "No" (the default) goes back to the first empty
            // one, "Yes" inserts the reply as it is (a variable may be optional, such as an unknown e-mail).
            if (_boxes.Where(b => b.Box.TextLength == 0).Select(b => b.Name).ToList() is [_, ..] empty)
            {
                var names = string.Join(RightToLeft == RightToLeft.Yes ? "، " : ", ", empty);
                Ask(string.Format(System.Globalization.CultureInfo.CurrentCulture, empty.Count == 1 ? Strings.EmptyVariable : Strings.EmptyVariables, names),
                    MessageBoxButtons.YesNo, out var answer);
                if (answer != DialogResult.Yes)
                {
                    _boxes.First(b => b.Box.TextLength == 0).Box.Focus();
                    return;
                }
            }

            text = LongPhraseVariables.Fill(_filling.Text, TypedValues(placeholders: false), DateTime.Now);
        }

        _inserting = true;
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
}
