using Itmam.App.Resources;
using Itmam.Core.Suggestions;
using Itmam.Win32;

namespace Itmam.App.SettingsUi;

/// <summary>
/// Inserts a variable into a long phrase (user decisions 1.2): one asked for when the phrase is
/// inserted (a name the user types, written <c>{name}</c>) or an automatic one chosen from a list,
/// never typed (written <c>{{name}}</c>).
/// </summary>
internal sealed class VariableDialog : Form
{
    private readonly RadioButton _asked = new() { AutoSize = false, Checked = true };
    private readonly RadioButton _automatic = new() { AutoSize = false };
    private readonly TextBox _name = new() { BorderStyle = BorderStyle.FixedSingle, MaxLength = LongPhraseVariables.MaxNameLength };
    private readonly ComboBox _list = new() { DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat };
    private readonly Label _error = new() { AutoSize = false, UseMnemonic = false };
    private readonly bool _arabic;

    private VariableDialog(SettingsPalette palette, bool rtl)
    {
        _arabic = rtl;
        Text = Strings.InsertVariableTitle;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = MaximizeBox = ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleDimensions = new SizeF(96f, 96f); // laid out at 100 %, scaled to the monitor
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 10f);
        ClientSize = new Size(420, 200);
        RightToLeft = rtl ? RightToLeft.Yes : RightToLeft.No;
        RightToLeftLayout = rtl;
        KeyPreview = true;
        BackColor = palette.Window;
        ForeColor = palette.Text;

        _asked.Text = Strings.VariableAsked;
        _automatic.Text = Strings.VariableAutomatic;
        _asked.Bounds = new Rectangle(16, 14, 190, 28);
        _automatic.Bounds = new Rectangle(214, 14, 190, 28);
        var prompt = new Label { Text = Strings.VariableNamePrompt, AutoSize = false, Bounds = new Rectangle(16, 52, 388, 24), UseMnemonic = false };
        _name.Bounds = new Rectangle(16, 78, 388, 30);
        _list.Bounds = new Rectangle(16, 78, 388, 30);
        foreach (Control box in new Control[] { _name, _list })
        {
            box.Font = new Font("Segoe UI", 11f);
            box.BackColor = palette.ControlFill;
            box.ForeColor = palette.Text;
        }

        // Each automatic variable with what it would give now, so the user sees what will be inserted.
        var now = DateTime.Now;
        foreach (var kind in LongPhraseVariables.All)
        {
            _list.Items.Add($"{LongPhraseVariables.NameOf(kind, rtl)} — {LongPhraseVariables.Format(kind, now, rtl)}");
        }

        _list.SelectedIndex = 0;
        _error.Bounds = new Rectangle(16, 112, 388, 28);
        _error.ForeColor = Color.FromArgb(0xE8, 0x11, 0x23);

        var ok = new ThemedButton { Text = Strings.InsertButton, Palette = palette, Top = 152 };
        var cancel = new ThemedButton { Text = Strings.CancelButton, Palette = palette, Top = 152 };
        ok.Left = 404 - ok.Width - cancel.Width - 8;
        cancel.Left = 404 - cancel.Width;
        ok.Click += (_, _) => Accept();
        cancel.Click += (_, _) => DialogResult = DialogResult.Cancel;

        // The text box for a variable asked for, the list for an automatic one: only one is shown.
        void ShowKind()
        {
            _name.Visible = _asked.Checked;
            _list.Visible = _automatic.Checked;
            prompt.Text = _asked.Checked ? Strings.VariableNamePrompt : Strings.VariableChoosePrompt;
            _error.Text = string.Empty;
            (_asked.Checked ? (Control)_name : _list).Focus();
        }

        _asked.CheckedChanged += (_, _) => ShowKind();
        _automatic.CheckedChanged += (_, _) => ShowKind();
        Controls.AddRange([_asked, _automatic, prompt, _name, _list, _error, ok, cancel]);
        Shown += (_, _) =>
        {
            WindowChrome.SetDarkTitleBar(Handle, palette.IsDark);
            ShowKind();
        };
    }

    /// <summary>The text to insert, such as <c>{اسم العميل}</c> or <c>{{التاريخ}}</c>.</summary>
    public string? Token { get; private set; }

    /// <summary>Shows the dialog; null when cancelled.</summary>
    /// <param name="startAutomatic">Open with "Automatic" chosen (screenshots).</param>
    public static string? Ask(IWin32Window owner, SettingsPalette palette, bool rtl, bool startAutomatic = false)
    {
        using var dialog = new VariableDialog(palette, rtl) { TopMost = owner is Form { TopMost: true } };
        dialog._automatic.Checked = startAutomatic;
        return dialog.ShowDialog(owner) == DialogResult.OK ? dialog.Token : null;
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
        if (_automatic.Checked)
        {
            Token = LongPhraseVariables.AutoToken(LongPhraseVariables.All[_list.SelectedIndex], _arabic);
        }
        else if (LongPhraseVariables.IsValidName(_name.Text))
        {
            Token = LongPhraseVariables.InputToken(_name.Text);
        }
        else
        {
            _error.Text = Strings.NotAVariableName;
            _name.Focus();
            return;
        }

        DialogResult = DialogResult.OK;
    }
}
