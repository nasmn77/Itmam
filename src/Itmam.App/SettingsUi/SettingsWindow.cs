using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Reflection;
using Itmam.App.Resources;
using Itmam.Core.Settings;
using Itmam.Win32;
using Microsoft.Win32;

namespace Itmam.App.SettingsUi;

internal enum SettingsPage
{
    General,
    About,
}

/// <summary>
/// The settings window, styled like the Windows 11 Settings app: navigation on the start side
/// (right in Arabic), pages of cards. Every change is applied and saved immediately.
/// Never shown in the taskbar; closing it only hides it (Itmam keeps running in the tray).
/// </summary>
internal sealed class SettingsWindow : Form
{
    private const string PrivacyUrl = "https://github.com/nasmn77/Itmam/blob/main/docs/PRIVACY.md";
    private const int WS_EX_LAYOUTRTL = 0x00400000;
    private const int WS_EX_NOINHERITLAYOUT = 0x00100000;

    private readonly ItmamApp _app;
    private readonly Action _exit;
    private readonly bool _mirrored;
    private readonly Panel _navigation = new() { Dock = DockStyle.Left, Width = 240 };
    private readonly Panel _content = new() { Dock = DockStyle.Fill, AutoScroll = true };
    private readonly Dictionary<SettingsPage, NavItem> _navItems = [];
    private readonly List<Control> _pageControls = [];
    private SettingsPalette _palette;
    private SettingsPage _page;
    private bool _loading;

    /// <param name="app">The running app (settings are read and saved through it).</param>
    /// <param name="exit">Exits Itmam (the "Close Itmam" section).</param>
    /// <param name="language">Overrides the layout direction (Debug demo only); normally the settings' language.</param>
    public SettingsWindow(ItmamApp app, Action exit, UiLanguage? language = null)
    {
        _app = app;
        _exit = exit;
        _palette = SettingsPalette.For(app.Settings.Theme);
        var rtl = (language ?? app.Settings.Language) == UiLanguage.Arabic;
        _mirrored = rtl;

        Text = Strings.SettingsTitle;
        Icon = Tray.AppIcon.Create(32, enabled: true);
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 10f);
        MinimumSize = new Size(760, 540);
        Size = new Size(940, 680);
        RightToLeft = rtl ? RightToLeft.Yes : RightToLeft.No;

        Controls.Add(_content);
        Controls.Add(_navigation);
        BuildNavigation();
        _content.Resize += (_, _) => LayoutPage();
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        ApplyPalette();
    }

    /// <summary>The page currently shown.</summary>
    public SettingsPage CurrentPage => _page;

#if DEBUG
    /// <summary>Debug demo only: scrolls to the end of the page (screenshots).</summary>
    public void ScrollToEnd() => _content.AutoScrollPosition = new Point(0, _content.AutoScrollMinSize.Height);
#endif

    /// <summary>Shows the window on <paramref name="page"/> and brings it to the front.</summary>
    public void ShowPage(SettingsPage page)
    {
        SelectPage(page);
        if (!Visible)
        {
            Show();
        }

        if (WindowState == FormWindowState.Minimized)
        {
            WindowState = FormWindowState.Normal;
        }

        Activate();
    }

    /// <summary>
    /// Arabic: the whole window is mirrored like any right-to-left Windows app — title and icon on
    /// the right, caption buttons on the left, navigation on the right (Dock = Left in mirrored
    /// coordinates). NOINHERITLAYOUT keeps each child's own drawing unmirrored; the controls lay
    /// out their content right to left themselves.
    /// </summary>
    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            if (_mirrored)
            {
                cp.ExStyle |= WS_EX_LAYOUTRTL | WS_EX_NOINHERITLAYOUT;
            }

            return cp;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        WindowChrome.SetDarkTitleBar(Handle, _palette.IsDark);
        WindowChrome.SetDarkScrollbars(_content.Handle, _palette.IsDark);
    }

    // Closing hides the window; the app keeps running in the tray.
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        }

        base.Dispose(disposing);
    }

    private void BuildNavigation()
    {
        var header = new Label
        {
            Text = Strings.AppName,
            Dock = DockStyle.Top,
            Height = 64,
            Font = new Font("Segoe UI Semibold", 14f),
            TextAlign = ContentAlignment.MiddleCenter,
        };
        _navigation.Controls.Add(header);

        var top = 72;
        foreach (var (page, title) in new[] { (SettingsPage.General, Strings.NavGeneral), (SettingsPage.About, Strings.NavAbout) })
        {
            var item = new NavItem { Text = title, Left = 8, Top = top, Width = _navigation.Width - 16, Height = 40, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            item.Click += (_, _) => SelectPage(page);
            _navigation.Controls.Add(item);
            _navItems[page] = item;
            top += 44;
        }
    }

    private void SelectPage(SettingsPage page)
    {
        _page = page;
        foreach (var (key, item) in _navItems)
        {
            item.Selected = key == page;
        }

        _content.SuspendLayout();
        foreach (var control in _pageControls)
        {
            _content.Controls.Remove(control);
            control.Dispose();
        }

        _pageControls.Clear();
        _loading = true;
        switch (page)
        {
            case SettingsPage.General:
                BuildGeneralPage();
                break;
            case SettingsPage.About:
                BuildAboutPage();
                break;
        }

        _loading = false;
        ApplyPalette();
        _content.ResumeLayout();
        LayoutPage();
    }

    private void BuildGeneralPage()
    {
        var settings = _app.Settings;
        AddTitle(Strings.NavGeneral);

        AddSection(Strings.SectionStartup);
        var startup = new ToggleSwitch();
        AddCard(Strings.StartWithWindows, Strings.StartWithWindowsHint, startup);
        _ = LoadStartupStateAsync(startup);
        startup.CheckedChanged += async (_, _) =>
        {
            if (_loading)
            {
                return;
            }

            var actual = await StartupRegistration.SetEnabledAsync(startup.Checked);
            _app.UpdateSettings(_app.Settings with { StartWithWindows = actual });
            _loading = true;
            startup.Checked = actual; // the user or a policy may have refused
            _loading = false;
        };

        AddSection(Strings.SectionSuggestions);
        var count = Numbers(1, 5, settings.MaxSuggestions);
        count.SelectedIndexChanged += (_, _) => Update(s => s with { MaxSuggestions = count.SelectedIndex + 1 });
        AddCard(Strings.MaxSuggestions, null, count);

        var letters = Numbers(1, 4, settings.MinLetters);
        letters.SelectedIndexChanged += (_, _) => Update(s => s with { MinLetters = letters.SelectedIndex + 1 });
        AddCard(Strings.MinLetters, null, letters);

        var space = new ToggleSwitch { Checked = settings.AddSpaceAfterAccept };
        space.CheckedChanged += (_, _) => Update(s => s with { AddSpaceAfterAccept = space.Checked });
        AddCard(Strings.AddSpace, null, space);

        AddSection(Strings.SectionLearning);
        var pause = new ToggleSwitch { Checked = settings.LearningPaused };
        pause.CheckedChanged += (_, _) => Update(s => s with { LearningPaused = pause.Checked });
        AddCard(Strings.PauseLearning, null, pause);

        var uses = Numbers(1, 5, settings.MinUsesToSuggest);
        uses.SelectedIndexChanged += (_, _) => Update(s => s with { MinUsesToSuggest = uses.SelectedIndex + 1 });
        AddCard(Strings.MinUses, null, uses);

        AddSection(Strings.SectionLanguage);
        var language = new SegmentedChoice { Options = [Strings.LanguageArabic, Strings.LanguageEnglish] };
        language.SelectedIndex = settings.Language == UiLanguage.Arabic ? 0 : 1;
        language.SelectedIndexChanged += (_, _) => Update(s => s with { Language = language.SelectedIndex == 0 ? UiLanguage.Arabic : UiLanguage.English });
        AddCard(Strings.UiLanguage, null, language);

        AddSection(Strings.SectionExit);
        var exit = new ThemedButton { Text = Strings.ExitButton };
        exit.Click += (_, _) => _exit();
        AddCard(Strings.ExitTitle, Strings.ExitHint, exit);
    }

    private void BuildAboutPage()
    {
        AddTitle(Strings.AppName);
        var version = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion?.Split('+')[0] ?? "1.0";
        AddText(string.Format(CultureInfo.CurrentCulture, Strings.AboutVersion, version), secondary: true);
        AddText(Strings.AboutTagline, secondary: false);
        AddText(string.Format(CultureInfo.CurrentCulture, Strings.AboutDictionary, _app.Dictionary.Count), secondary: false);
        AddText(string.Format(CultureInfo.CurrentCulture, Strings.AboutLearned, _app.UserWords.Count), secondary: false);

        AddSection(Strings.AboutSources);
        foreach (var source in _app.Dictionary.Sources)
        {
            AddLink($"{source.Name} — {source.License}", source.Url);
        }

        AddSection(Strings.AboutPrivacyLink);
        AddText(Strings.AboutPrivacy, secondary: false);
        AddLink(Strings.AboutPrivacyLink, PrivacyUrl);
    }

    private async Task LoadStartupStateAsync(ToggleSwitch toggle)
    {
        var enabled = await StartupRegistration.IsEnabledAsync();
        _loading = true;
        toggle.Checked = enabled;
        _loading = false;
    }

    private void Update(Func<AppSettings, AppSettings> change)
    {
        if (!_loading)
        {
            _app.UpdateSettings(change(_app.Settings));
        }
    }

    private static SegmentedChoice Numbers(int from, int to, int selected)
    {
        var choice = new SegmentedChoice
        {
            Options = Enumerable.Range(from, to - from + 1).Select(n => n.ToString(CultureInfo.CurrentCulture)).ToArray(),
        };
        choice.SelectedIndex = selected - from;
        return choice;
    }

    private void AddTitle(string text) => AddPageControl(new Label { Text = text, Font = new Font("Segoe UI Semibold", 20f), Height = 56, UseMnemonic = false });

    private void AddSection(string text) => AddPageControl(new Label { Text = text, Font = new Font("Segoe UI Semibold", 10.5f), Height = 40, TextAlign = ContentAlignment.BottomLeft, UseMnemonic = false });

    private void AddText(string text, bool secondary) =>
        AddPageControl(new Label { Text = text, Height = 28, Tag = secondary ? "secondary" : null, UseMnemonic = false });

    private void AddLink(string text, string url)
    {
        var link = new LinkLabel { Text = text, Height = 28, UseMnemonic = false };
        link.LinkClicked += (_, _) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); // opens the user's browser
        AddPageControl(link);
    }

    private void AddCard(string title, string? description, Control control)
    {
        var card = new SettingCard(title, description, control);
        card.SizeChanged += (_, _) => LayoutPage(); // a wrapped description made it taller
        AddPageControl(card);
    }

    private void AddPageControl(Control control)
    {
        control.RightToLeft = RightToLeft;
        _content.Controls.Add(control);
        _pageControls.Add(control);
    }

    // Stacks the page's controls vertically at full width.
    private void LayoutPage()
    {
        var scale = DeviceDpi / 96f;
        var margin = (int)(28 * scale);
        var width = Math.Max(200, _content.ClientSize.Width - (2 * margin));
        var y = (int)(12 * scale) + _content.AutoScrollPosition.Y;
        foreach (var control in _pageControls)
        {
            control.SetBounds(margin, y, width, control.Height);
            if (control is Label label and not LinkLabel)
            {
                label.TextAlign = RightToLeft == RightToLeft.Yes ? ContentAlignment.BottomRight : ContentAlignment.BottomLeft;
            }

            y += control.Height + (control is SettingCard ? (int)(4 * scale) : 0);
        }

        _content.AutoScrollMinSize = new Size(0, y - _content.AutoScrollPosition.Y + margin);
    }

    private void ApplyPalette()
    {
        BackColor = _palette.Window;
        _navigation.BackColor = _palette.Window;
        _content.BackColor = _palette.Window;
        foreach (Control control in _navigation.Controls)
        {
            control.ForeColor = _palette.Text;
            control.BackColor = _palette.Window;
            if (control is NavItem item)
            {
                item.Palette = _palette;
            }
        }

        foreach (var control in _pageControls)
        {
            switch (control)
            {
                case SettingCard card:
                    card.Palette = _palette;
                    break;
                case LinkLabel link:
                    link.BackColor = _palette.Window;
                    link.LinkColor = link.ActiveLinkColor = link.VisitedLinkColor = _palette.Accent;
                    break;
                default:
                    control.BackColor = _palette.Window;
                    control.ForeColor = Equals(control.Tag, "secondary") ? _palette.SecondaryText : _palette.Text;
                    break;
            }
        }

        if (IsHandleCreated)
        {
            WindowChrome.SetDarkTitleBar(Handle, _palette.IsDark);
            WindowChrome.SetDarkScrollbars(_content.Handle, _palette.IsDark);
        }
    }

    // Follows Windows light/dark and accent changes live.
    private void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.Color or UserPreferenceCategory.VisualStyle)
        {
            _palette = SettingsPalette.For(_app.Settings.Theme);
            ApplyPalette();
            Invalidate(invalidateChildren: true);
        }
    }

    /// <summary>A navigation entry: rounded highlight, accent bar on the start side when selected.</summary>
    private sealed class NavItem : ThemedControl
    {
        private bool _selected;
        private bool _hovered;

        public NavItem() => Cursor = Cursors.Hand;

        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public bool Selected
        {
            get => _selected;
            set
            {
                _selected = value;
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
            _hovered = false;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Palette.Window);
            if (_selected || _hovered)
            {
                using var path = Drawing.Rounded(new RectangleF(0, 2, Width - 1, Height - 4), 4 * DpiScale);
                using var fill = new SolidBrush(_selected ? Palette.NavSelected : Palette.NavHover);
                g.FillPath(fill, path);
            }

            var rtl = RightToLeft == RightToLeft.Yes;
            if (_selected)
            {
                var bar = new RectangleF(rtl ? Width - 3 * DpiScale - 1 : 1, Height * 0.3f, 3 * DpiScale, Height * 0.4f);
                using var accentPath = Drawing.Rounded(bar, 1.5f * DpiScale);
                using var accent = new SolidBrush(Palette.Accent);
                g.FillPath(accent, accentPath);
            }

            var text = new Rectangle((int)(16 * DpiScale), 0, Width - (int)(32 * DpiScale), Height);
            TextRenderer.DrawText(g, Text, Font, text, Palette.Text,
                TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | (rtl ? TextFormatFlags.Right | TextFormatFlags.RightToLeft : TextFormatFlags.Left));
        }
    }
}
