using Itmam.App.Resources;
using Itmam.Core.Suggestions;
using Itmam.Win32;

namespace Itmam.App.SettingsUi;

/// <summary>Asks for a long phrase: its name (shown in the list) and its text of several lines.</summary>
internal sealed class LongPhraseDialog : Form
{
    private readonly TextBox _name = new() { BorderStyle = BorderStyle.FixedSingle, MaxLength = LongPhrases.MaxNameLength };
    private readonly TextBox _text = new()
    {
        BorderStyle = BorderStyle.FixedSingle,
        Multiline = true,
        AcceptsReturn = true, // Enter starts a new line; Ctrl+Enter or OK saves
        ScrollBars = ScrollBars.Vertical,
        MaxLength = LongPhrases.MaxTextLength,
    };

    private readonly Label _error = new() { AutoSize = false, UseMnemonic = false };

    private LongPhraseDialog(string title, LongPhrase? initial, SettingsPalette palette, bool rtl)
    {
        Text = title;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = MaximizeBox = ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleDimensions = new SizeF(96f, 96f); // laid out at 100 %, scaled to the monitor
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 10f);
        ClientSize = new Size(560, 460);
        RightToLeft = rtl ? RightToLeft.Yes : RightToLeft.No;
        RightToLeftLayout = rtl;
        KeyPreview = true;
        BackColor = palette.Window;
        ForeColor = palette.Text;

        var nameLabel = new Label { Text = Strings.LongPhraseNamePrompt, AutoSize = false, Bounds = new Rectangle(16, 14, 528, 24), UseMnemonic = false };
        _name.Bounds = new Rectangle(16, 40, 528, 30);
        var textLabel = new Label { Text = Strings.LongPhraseTextPrompt, AutoSize = false, Bounds = new Rectangle(16, 82, 528, 24), UseMnemonic = false };
        _text.Bounds = new Rectangle(16, 108, 528, 250);
        foreach (var box in new[] { _name, _text })
        {
            box.Font = new Font("Segoe UI", 11f);
            box.BackColor = palette.ControlFill;
            box.ForeColor = palette.Text;
        }

        _name.Text = initial?.Name ?? string.Empty;
        _text.Text = (initial?.Text ?? string.Empty).Replace("\n", "\r\n", StringComparison.Ordinal);
        _error.Bounds = new Rectangle(16, 364, 528, 40);
        _error.ForeColor = Color.FromArgb(0xE8, 0x11, 0x23);

        var ok = new ThemedButton { Text = Strings.OkButton, Palette = palette, Top = 414 };
        var cancel = new ThemedButton { Text = Strings.CancelButton, Palette = palette, Top = 414 };
        ok.Left = 544 - ok.Width - cancel.Width - 8;
        cancel.Left = 544 - cancel.Width;
        ok.Click += (_, _) => Accept();
        cancel.Click += (_, _) => DialogResult = DialogResult.Cancel;
        Controls.AddRange([nameLabel, _name, textLabel, _text, _error, ok, cancel]);
        Shown += (_, _) =>
        {
            WindowChrome.SetDarkTitleBar(Handle, palette.IsDark);
            _name.SelectAll();
            _name.Focus();
        };
    }

    public LongPhrase? Phrase { get; private set; }

    /// <summary>Shows the dialog; null when cancelled.</summary>
    public static LongPhrase? Ask(IWin32Window owner, string title, LongPhrase? initial, SettingsPalette palette, bool rtl)
    {
        using var dialog = new LongPhraseDialog(title, initial, palette, rtl) { TopMost = owner is Form { TopMost: true } };
        return dialog.ShowDialog(owner) == DialogResult.OK ? dialog.Phrase : null;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Enter && (e.Control || _name.Focused))
        {
            e.SuppressKeyPress = true;
            if (_name.Focused && !e.Control)
            {
                _text.Focus(); // from the name to the text
            }
            else
            {
                Accept();
            }
        }
        else if (e.KeyCode == Keys.Escape)
        {
            DialogResult = DialogResult.Cancel;
        }
    }

    private void Accept()
    {
        if (LongPhrases.CleanName(_name.Text) is not { } name)
        {
            _error.Text = Strings.NotALongPhraseName;
            _name.Focus();
        }
        else if (LongPhrases.CleanText(_text.Text) is not { } text)
        {
            _error.Text = Strings.NotALongPhraseText;
            _text.Focus();
        }
        else
        {
            Phrase = new LongPhrase(name, text);
            DialogResult = DialogResult.OK;
        }
    }
}
