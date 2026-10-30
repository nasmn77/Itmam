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
/// </summary>
internal sealed class LongPhrasePicker : Form
{
    private readonly TextBox _search = new() { BorderStyle = BorderStyle.FixedSingle };
    private readonly ListBox _list = new() { BorderStyle = BorderStyle.None, IntegralHeight = false };
    private readonly Label _preview = new() { AutoSize = false, UseMnemonic = false };
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
        _search.TextChanged += (_, _) => Filter();
        _list.SelectedIndexChanged += (_, _) => ShowPreview();
        _list.DoubleClick += (_, _) => InsertSelected();
        Controls.AddRange([_search, _list, _preview]);

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

    private void ShowPreview() =>
        _preview.Text = _all.Count == 0 ? Strings.LongPhrasesEmpty
            : _list.SelectedIndex >= 0 && _list.SelectedIndex < _shown.Count ? _shown[_list.SelectedIndex].Text
            : Strings.LongPhrasesNoMatch;

    // Focus back to the app, then paste the text there (the clipboard is restored afterwards).
    private async void InsertSelected()
    {
        if (_inserting || _list.SelectedIndex < 0 || _list.SelectedIndex >= _shown.Count)
        {
            return;
        }

        _inserting = true;
        var text = _shown[_list.SelectedIndex].Text;
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
