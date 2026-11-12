using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Globalization;
using Itmam.App.Resources;
using Itmam.App.SettingsUi.Pages;
using Itmam.Core.Settings;
using Itmam.Win32;
using Microsoft.Win32;

namespace Itmam.App.SettingsUi;

internal enum SettingsPage
{
    General,
    Languages,
    Appearance,
    Shortcuts,
    Excluded,
    Words,
    Phrases,
    TextShortcuts,
    LongPhrases,
    Updates,
    About,
    Exit,
}

/// <summary>
/// The settings window, styled like the Windows 11 Settings app: navigation on the start side
/// (right in Arabic), pages of cards. Every change is applied and saved immediately.
/// Never shown in the taskbar; closing it only hides it (Itmam keeps running in the tray).
/// </summary>
internal sealed class SettingsWindow : Form
{
    private const int WS_EX_LAYOUTRTL = 0x00400000;
    private const int WS_EX_NOINHERITLAYOUT = 0x00100000;

    private readonly ItmamApp _app;
    private readonly Action _exit;
    private readonly bool _mirrored;
    private readonly Panel _navigation = new() { Dock = DockStyle.Left, Width = 240 };
    private readonly PageScroller _content = new() { Dock = DockStyle.Fill };
    private readonly Dictionary<SettingsPage, NavItem> _navItems = [];
    private readonly List<Control> _pageControls = [];
    private readonly HashSet<Control> _filteredOut = [];
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

        // User decision: taller, as much of a page as fits on the screen at once.
        var area = Screen.FromPoint(Cursor.Position).WorkingArea;
        Size = new Size(940, Math.Clamp(area.Height * 96 / DeviceDpi - 80, 680, 900));
        RightToLeft = rtl ? RightToLeft.Yes : RightToLeft.No;

        Controls.Add(_content);
        Controls.Add(_navigation);
        BuildNavigation();
        _content.Resize += (_, _) => LayoutPage();
        _content.OffsetChanged += (_, _) => LayoutPage();
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        ApplyPalette();
    }

    /// <summary>The page currently shown.</summary>
    public SettingsPage CurrentPage => _page;

    // What the pages (SettingsUi/Pages) use: the app, the state kept between builds, and the page being built.
    internal ItmamApp App => _app;

    internal SettingsViewState State { get; } = new();

    internal SettingsPalette Palette => _palette;

    internal bool Mirrored => _mirrored;

    internal PageScroller Content => _content;

    internal List<Control> PageControls => _pageControls;

    internal HashSet<Control> FilteredOut => _filteredOut;

    internal bool IsLoading => _loading;

    internal void SetLoading(bool loading) => _loading = loading;

    internal void ExitApp() => _exit();

#if DEBUG
    /// <summary>Debug demo only: scrolls to the end of the page (screenshots).</summary>
    public void ScrollToEnd() => _content.ScrollToEnd();
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
        foreach (var (page, title) in new[] { (SettingsPage.General, Strings.NavGeneral), (SettingsPage.Languages, Strings.NavLanguages), (SettingsPage.Appearance, Strings.NavAppearance), (SettingsPage.Shortcuts, Strings.NavShortcuts), (SettingsPage.Excluded, Strings.NavExcluded), (SettingsPage.Words, Strings.NavWords), (SettingsPage.Phrases, Strings.NavPhrases), (SettingsPage.TextShortcuts, Strings.NavTextShortcuts), (SettingsPage.LongPhrases, Strings.NavLongPhrases), (SettingsPage.Updates, Strings.NavUpdates), (SettingsPage.About, Strings.NavAbout), (SettingsPage.Exit, Strings.NavExit) })
        {
            var item = new NavItem { Text = title, Left = 8, Top = top, Width = _navigation.Width - 16, Height = 40, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            item.Click += (_, _) => SelectPage(page);
            _navigation.Controls.Add(item);
            _navItems[page] = item;
            top += 44;
        }
    }

    internal void SelectPage(SettingsPage page)
    {
        if (page != _page)
        {
            State.ExcludedSearch = string.Empty; // a search applies only while staying on its page
            _content.Offset = 0;
            State.WordsFilter = string.Empty;
        }

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
        _filteredOut.Clear();
        _loading = true;
        SettingsPageBase? content = page switch
        {
            SettingsPage.General => new GeneralPage(this),
            SettingsPage.Languages => new LanguagesPage(this),
            SettingsPage.Appearance => new AppearancePage(this),
            SettingsPage.Shortcuts => new ShortcutsPage(this),
            SettingsPage.Excluded => new ExcludedPage(this),
            SettingsPage.Words => new WordsPage(this),
            SettingsPage.Phrases => new PhrasesPage(this),
            SettingsPage.TextShortcuts => new TextShortcutsPage(this),
            SettingsPage.LongPhrases => new LongPhrasesPage(this),
            SettingsPage.Updates => new UpdatesPage(this),
            SettingsPage.About => new AboutPage(this),
            SettingsPage.Exit => new ExitPage(this),
            _ => null,
        };
        content?.Build();

        _loading = false;
        ApplyPalette();
        _content.ResumeLayout();
        LayoutPage();
    }


    // Warning before a change; "No" is the default button. Names are kept left to right (file names,
    // single words) unless they are Arabic phrases, whose word order must stay right to left.
    internal bool Confirm(string format, string? name, bool leftToRight = true)
    {
        var shown = leftToRight ? "\u202A" + name + "\u202C" : name;
        var text = name is null ? format : string.Format(CultureInfo.CurrentCulture, format, shown);
        var options = _mirrored ? MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign : 0;
        return MessageBox.Show(this, text, Strings.AppName, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2, options) == DialogResult.Yes;
    }

    // Rebuilds the current page, keeping the scroll position.
    internal void RebuildPage()
    {
        var scroll = _content.Offset;
        SelectPage(_page);
        _content.Offset = scroll;
    }

    internal void ShowInfo(string text)
    {
        var options = _mirrored ? MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign : 0;
        MessageBox.Show(this, text, Strings.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information, MessageBoxDefaultButton.Button1, options);
    }

    internal void Update(Func<AppSettings, AppSettings> change)
    {
        if (!_loading)
        {
            _app.UpdateSettings(change(_app.Settings));
        }
    }

    internal void AddTitle(string text) => AddPageControl(new Label { Text = text, Font = new Font("Segoe UI Semibold", 20f), Height = 56, UseMnemonic = false, Tag = "title" });

    internal void AddSection(string text) => AddPageControl(new Label { Text = text, Font = new Font("Segoe UI Semibold", 10.5f), Height = 40, TextAlign = ContentAlignment.BottomLeft, UseMnemonic = false });

    internal void AddText(string text, bool secondary) =>
        AddPageControl(new Label { Text = text, Height = 28, Tag = secondary ? "secondary" : "text", UseMnemonic = false });

    internal void AddLink(string text, string url)
    {
        var link = new LinkLabel { Text = text, Height = 28, UseMnemonic = false };
        link.LinkClicked += (_, _) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); // opens the user's browser
        AddPageControl(link);
    }

    internal SettingCard AddCard(string title, string? description, Control control, bool stacked = false)
    {
        var card = new SettingCard(title, description, control, stacked);
        card.SizeChanged += (_, _) => LayoutPage(); // a wrapped description made it taller
        AddPageControl(card);
        return card;
    }

    internal void AddPageControl(Control control)
    {
        control.RightToLeft = RightToLeft;
        _content.Controls.Add(control);
        _pageControls.Add(control);
    }

    // Stacks the page's controls vertically at full width.
    internal void LayoutPage()
    {
        var scale = DeviceDpi / 96f;
        var margin = (int)(28 * scale);
        var width = Math.Max(200, _content.ClientSize.Width - (2 * margin) - _content.BarWidth);
        var y = (int)(12 * scale) - _content.Offset;
        foreach (var control in _pageControls)
        {
            // Filtered out by a search box (the parent may not be visible yet, so Visible cannot be read back).
            var shown = !_filteredOut.Contains(control);
            control.Visible = shown;
            if (!shown)
            {
                continue;
            }

            // Paragraphs (AddText) wrap: as tall as their text at this width.
            var height = control is Label { Tag: "text" or "secondary" } paragraph
                ? Math.Max((int)(28 * scale), TextRenderer.MeasureText(paragraph.Text, paragraph.Font, new Size(width, int.MaxValue), TextFormatFlags.WordBreak).Height + (int)(8 * scale))
                : control.Height;
            control.SetBounds(margin, y, width, height);
            if (control is Label label)
            {
                // Start of the line: WinForms mirrors it to the right for RightToLeft labels.
                label.TextAlign = ContentAlignment.BottomLeft;
            }

            y += control.Height + control switch
            {
                SettingCard => (int)(4 * scale),
                Label { Tag: "title" } => (int)(16 * scale), // breathing room under the page title
                _ => 0,
            };
        }

        _content.ContentHeight = y + _content.Offset + margin;
    }

    private void ApplyPalette()
    {
        BackColor = _palette.Window;
        _navigation.BackColor = _palette.Window;
        _content.BackColor = _palette.Window;
        _content.Palette = _palette;
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
                case ThemedControl themed:
                    themed.Palette = _palette;
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
            RefreshTheme();
        }
    }

    // The theme setting or the colors of Windows changed: a new palette for the window and the page shown.
    internal void RefreshTheme()
    {
        _palette = SettingsPalette.For(_app.Settings.Theme);
        ApplyPalette();
        Invalidate(invalidateChildren: true);
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
