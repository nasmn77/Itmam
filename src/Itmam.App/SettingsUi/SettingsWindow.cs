using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Reflection;
using Itmam.App.Resources;
using Itmam.App.Suggestions;
using Itmam.Core.Input;
using Itmam.Core.Learning;
using Itmam.Core.Presentation;
using Itmam.Core.Settings;
using Itmam.Core.Suggestions;
using Itmam.Win32;
using Microsoft.Win32;

namespace Itmam.App.SettingsUi;

internal enum SettingsPage
{
    General,
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
    private const string RepositoryUrl = "https://github.com/nasmn77/Itmam";
    // The website's policy, the same one registered in Partner Center; English for the English interface.
    private const string PrivacyUrl = "https://nasmn77.github.io/Itmam/privacy.html";
    private const string PrivacyUrlEnglish = "https://nasmn77.github.io/Itmam/en/privacy.html";
    private const string DeveloperEmail = "nasmn77@gmail.com";
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
    private string _excludedSearch = string.Empty; // kept when the page is rebuilt after add/remove
    private string _wordsFilter = string.Empty;
    private int _wordsSort;
    private Label? _wordsHeader;
    private SearchBox? _wordsSearch;
    private WordList? _wordList;
    private ThemedButton? _editWord;
    private ThemedButton? _toggleBlock;
    private ThemedButton? _deleteWord;
    private SettingsPalette _palette;
    private SettingsPage _page;
    private bool _loading;
    private BarPreview? _preview;
    private SettingCard? _textCard;

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
        foreach (var (page, title) in new[] { (SettingsPage.General, Strings.NavGeneral), (SettingsPage.Appearance, Strings.NavAppearance), (SettingsPage.Shortcuts, Strings.NavShortcuts), (SettingsPage.Excluded, Strings.NavExcluded), (SettingsPage.Words, Strings.NavWords), (SettingsPage.Phrases, Strings.NavPhrases), (SettingsPage.TextShortcuts, Strings.NavTextShortcuts), (SettingsPage.LongPhrases, Strings.NavLongPhrases), (SettingsPage.Updates, Strings.NavUpdates), (SettingsPage.About, Strings.NavAbout), (SettingsPage.Exit, Strings.NavExit) })
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
        if (page != _page)
        {
            _excludedSearch = string.Empty; // a search applies only while staying on its page
            _content.Offset = 0;
            _wordsFilter = string.Empty;
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
        _preview = null;
        _wordsHeader = null;
        _wordsSearch = null;
        _wordList = null;
        _editWord = _toggleBlock = _deleteWord = null;
        _textCard = null;
        _loading = true;
        switch (page)
        {
            case SettingsPage.General:
                BuildGeneralPage();
                break;
            case SettingsPage.Appearance:
                BuildAppearancePage();
                break;
            case SettingsPage.Shortcuts:
                BuildShortcutsPage();
                break;
            case SettingsPage.Excluded:
                BuildExcludedPage();
                break;
            case SettingsPage.Words:
                BuildWordsPage();
                break;
            case SettingsPage.Phrases:
                BuildPhrasesPage();
                break;
            case SettingsPage.TextShortcuts:
                BuildTextShortcutsPage();
                break;
            case SettingsPage.LongPhrases:
                BuildLongPhrasesPage();
                break;
            case SettingsPage.Updates:
                BuildUpdatesPage();
                break;
            case SettingsPage.About:
                BuildAboutPage();
                break;
            case SettingsPage.Exit:
                BuildExitPage();
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

        AddSection(Strings.SectionNotifications);
        var notify = new ToggleSwitch { Checked = settings.NotifyOnToggle };
        notify.CheckedChanged += (_, _) => Update(s => s with { NotifyOnToggle = notify.Checked });
        AddCard(Strings.NotifyOnToggle, Strings.NotifyOnToggleHint, notify);

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
        var learn = new ToggleSwitch { Checked = !settings.LearningPaused };
        learn.CheckedChanged += (_, _) => Update(s => s with { LearningPaused = !learn.Checked });
        AddCard(Strings.LearnNewWords, null, learn);

        var uses = Numbers(1, 5, settings.MinUsesToSuggest);
        uses.SelectedIndexChanged += (_, _) => Update(s => s with { MinUsesToSuggest = uses.SelectedIndex + 1 });
        AddCard(Strings.MinUses, null, uses);

        AddSection(Strings.SectionLanguage);
        var language = new SegmentedChoice { Options = [Strings.LanguageArabic, Strings.LanguageEnglish] };
        language.SelectedIndex = settings.Language == UiLanguage.Arabic ? 0 : 1;
        language.SelectedIndexChanged += (_, _) => Update(s => s with { Language = language.SelectedIndex == 0 ? UiLanguage.Arabic : UiLanguage.English });
        AddCard(Strings.UiLanguage, null, language);

        // Statistics (user decision 1.1): counts only, kept in user.db.
        AddSection(Strings.SectionStats);
        var usage = _app.Learning.Usage;
        var reset = new ThemedButton { Text = Strings.ResetStatsButton };
        reset.Click += (_, _) =>
        {
            if (Confirm(Strings.ConfirmResetStats, null))
            {
                _app.Learning.ResetUsage();
                BeginInvoke(RebuildPage);
            }
        };
        AddCard(string.Format(CultureInfo.CurrentCulture, Strings.StatsSummary, usage.Completions, usage.CharactersSaved),
            string.Format(CultureInfo.CurrentCulture, Strings.StatsSince, usage.Since.ToLocalTime().DateTime), reset);
    }

    // Every change shows at once in the preview (and in the real bar).
    private void BuildAppearancePage()
    {
        var settings = _app.Settings;
        AddTitle(Strings.NavAppearance);

        AddSection(Strings.SectionPreview);
        _preview = new BarPreview { Appearance = PopupAppearance.From(settings) };
        AddPageControl(_preview);

        AddSection(Strings.SectionTheme);
        var theme = new SegmentedChoice { Options = [Strings.ThemeSystem, Strings.ThemeLight, Strings.ThemeDark] };
        theme.SelectedIndex = (int)settings.Theme;
        theme.SelectedIndexChanged += (_, _) => UpdateLook(s => s with { Theme = (ThemeChoice)theme.SelectedIndex });
        AddCard(Strings.ThemeLabel, null, theme);

        AddSection(Strings.SectionColors);
        var background = Swatches(BarColorPresets.All.Select(p => p.Hex), settings.BarBackground);
        background.ValueChanged += (_, _) => UpdateLook(s => s with { BarBackground = Hex(background.Value) });
        AddCard(Strings.BarBackground, Strings.BarBackgroundHint, background, stacked: true);

        var text = Swatches(BarColorPresets.Texts, settings.BarText);
        text.ValueChanged += (_, _) => UpdateLook(s => s with { BarText = Hex(text.Value) });
        _textCard = AddCard(Strings.BarText, Strings.BarTextHint, text, stacked: true);

        var highlight = Swatches(BarColorPresets.Highlights, settings.BarHighlight);
        highlight.ValueChanged += (_, _) => UpdateLook(s => s with { BarHighlight = Hex(highlight.Value) });
        AddCard(Strings.BarHighlight, Strings.BarHighlightHint, highlight, stacked: true);

        AddSection(Strings.SectionBarLook);
        int[] opacities = [100, 95, 90, 85, 80];
        var opacity = new SegmentedChoice { Options = [.. opacities.Select(o => $"{o}%")] };
        opacity.SelectedIndex = Math.Max(0, Array.IndexOf(opacities, settings.BarOpacity - (settings.BarOpacity % 5)));
        opacity.SelectedIndexChanged += (_, _) => UpdateLook(s => s with { BarOpacity = opacities[opacity.SelectedIndex] });
        AddCard(Strings.BarOpacity, null, opacity);

        int[] sizes = [12, 13, 14, 16, 18, 20];
        var size = new SegmentedChoice { Options = [.. sizes.Select(n => n.ToString(CultureInfo.CurrentCulture))] };
        size.SelectedIndex = Array.IndexOf(sizes, settings.BarFontSize) is var index and >= 0 ? index : Array.IndexOf(sizes, 14);
        size.SelectedIndexChanged += (_, _) => UpdateLook(s => s with { BarFontSize = sizes[size.SelectedIndex] });
        AddCard(Strings.BarFontSize, null, size);

        var first = new ToggleSwitch { Checked = settings.HighlightFirstSuggestion };
        first.CheckedChanged += (_, _) => UpdateLook(s => s with { HighlightFirstSuggestion = first.Checked });
        AddCard(Strings.HighlightFirst, Strings.HighlightFirstHint, first);

        var hints = new ToggleSwitch { Checked = settings.ShowHotkeyHints };
        hints.CheckedChanged += (_, _) => UpdateLook(s => s with { ShowHotkeyHints = hints.Checked });
        AddCard(Strings.HotkeyHints, Strings.HotkeyHintsHint, hints);

        var reset = new ThemedButton { Text = Strings.ResetButton };
        reset.Click += (_, _) =>
        {
            var defaults = new AppSettings();
            UpdateLook(s => s with
            {
                Theme = defaults.Theme,
                BarBackground = null,
                BarHighlight = null,
                BarText = null,
                BarOpacity = defaults.BarOpacity,
                BarFontSize = defaults.BarFontSize,
                HighlightFirstSuggestion = defaults.HighlightFirstSuggestion,
                ShowHotkeyHints = defaults.ShowHotkeyHints,
            });
            BeginInvoke(() => SelectPage(SettingsPage.Appearance)); // rebuild after this click is handled
        };
        AddCard(Strings.ResetLook, Strings.ResetLookHint, reset);
        UpdateContrastWarning();
    }

    private ColorSwatches Swatches(IEnumerable<string> presets, string? selected)
    {
        var swatches = new ColorSwatches([.. presets.Select(h => ColorContrast.Parse(h)!.Value)], Strings.ColorAuto, Strings.ColorCustom)
        {
            Value = ColorContrast.Parse(selected),
        };
        swatches.CustomRequested += (_, _) =>
        {
            using var dialog = new ColorDialog { FullOpen = true, AnyColor = true, Color = swatches.Value ?? Color.White };
            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                swatches.Value = Color.FromArgb(dialog.Color.R, dialog.Color.G, dialog.Color.B);
            }
        };
        return swatches;
    }

    private static string? Hex(Color? color) => color is { } c ? ColorContrast.ToHex(c) : null;

    // Saves, then refreshes the preview, the window's own theme and the contrast warning.
    private void UpdateLook(Func<AppSettings, AppSettings> change)
    {
        if (_loading)
        {
            return;
        }

        var theme = _app.Settings.Theme;
        Update(change);
        if (_preview is not null)
        {
            _preview.Appearance = PopupAppearance.From(_app.Settings);
        }

        if (_app.Settings.Theme != theme)
        {
            _palette = SettingsPalette.For(_app.Settings.Theme);
            ApplyPalette();
            Invalidate(invalidateChildren: true);
        }

        UpdateContrastWarning();
    }

    // A custom text color can be hard to read on the bar: say so under "Text color".
    private void UpdateContrastWarning()
    {
        var colors = PopupColors.For(PopupAppearance.From(_app.Settings));
        var ratio = ColorContrast.Ratio(colors.Background, colors.Text);
        _textCard?.SetDescription(ratio < ColorContrast.MinimumReadable
            ? string.Format(CultureInfo.CurrentCulture, Strings.ContrastWarning, ratio)
            : Strings.BarTextHint);
    }

    // Shortcuts are checked (HotkeyRules) before they are saved; a refused one shows why under its card.
    private void BuildShortcutsPage()
    {
        var hotkeys = _app.Settings.Hotkeys;
        AddTitle(Strings.NavShortcuts);
        AddText(Strings.ShortcutsIntro, secondary: true);

        AddSection(Strings.SectionBarKeys);
        AddHotkeyCard(HotkeyAction.AcceptFirst, Strings.ShortcutAcceptFirst, null);

        KeyModifiers[] nthModifiers = [KeyModifiers.Ctrl, KeyModifiers.Alt];
        var nth = new SegmentedChoice { Options = [.. nthModifiers.Select(HotkeyNames.Format)] };
        nth.SelectedIndex = Math.Max(0, Array.IndexOf(nthModifiers, hotkeys.AcceptNthModifier));
        var nthCard = AddCard(Strings.ShortcutAcceptNth, Strings.ShortcutAcceptNthHint, nth);
        nth.SelectedIndexChanged += (_, _) =>
        {
            if (_loading)
            {
                return;
            }

            var modifier = nthModifiers[nth.SelectedIndex];
            var problem = HotkeyRules.CheckAcceptNthModifier(_app.Settings.Hotkeys, modifier);
            if (problem == HotkeyProblem.None)
            {
                Update(s => s with { Hotkeys = s.Hotkeys with { AcceptNthModifier = modifier } });
                nthCard.SetDescription(Strings.ShortcutAcceptNthHint);
                return;
            }

            nthCard.SetDescription(ProblemText(problem, new Hotkey('1', modifier)));
            _loading = true;
            nth.SelectedIndex = Array.IndexOf(nthModifiers, _app.Settings.Hotkeys.AcceptNthModifier);
            _loading = false;
        };

        AddHotkeyCard(HotkeyAction.Dismiss, Strings.ShortcutDismiss, Strings.ShortcutDismissHint);

        var navigation = new ToggleSwitch { Checked = hotkeys.NativeNavigation };
        navigation.CheckedChanged += (_, _) => Update(s => s with { Hotkeys = s.Hotkeys with { NativeNavigation = navigation.Checked } });
        AddCard(Strings.NativeNavigation, Strings.NativeNavigationHint, navigation);

        AddSection(Strings.SectionEverywhere);
        AddHotkeyCard(HotkeyAction.ToggleEnabled, Strings.ShortcutToggle, Strings.ShortcutToggleHint);
        AddHotkeyCard(HotkeyAction.OpenLongPhrases, Strings.ShortcutLongPhrases, Strings.ShortcutLongPhrasesHint);

        var reset = new ThemedButton { Text = Strings.ResetButton };
        reset.Click += (_, _) =>
        {
            Update(s => s with { Hotkeys = new HotkeySettings() });
            BeginInvoke(() => SelectPage(SettingsPage.Shortcuts)); // rebuild after this click is handled
        };
        AddCard(Strings.ResetShortcuts, Strings.ResetShortcutsHint, reset);
    }

    private void AddHotkeyCard(HotkeyAction action, string title, string? hint, Action? changed = null)
    {
        var box = new HotkeyBox { Hotkey = HotkeyRules.Get(_app.Settings.Hotkeys, action), RecordingText = Strings.ShortcutRecording };
        var card = AddCard(title, hint, box);
        box.RecordingChanged += _app.SuspendHotkeys;
        box.Recorded += hotkey =>
        {
            var problem = HotkeyRules.Check(_app.Settings.Hotkeys, action, hotkey);
            if (problem == HotkeyProblem.None)
            {
                Update(s => s with { Hotkeys = HotkeyRules.With(s.Hotkeys, action, hotkey) });
                box.Hotkey = hotkey;
                card.SetDescription(hint);
                changed?.Invoke();
            }
            else
            {
                card.SetDescription(ProblemText(problem, hotkey));
            }
        };
    }

    private static string ProblemText(HotkeyProblem problem, Hotkey hotkey)
    {
        var format = problem switch
        {
            HotkeyProblem.Duplicate => Strings.HotkeyProblemDuplicate,
            HotkeyProblem.TypesText => Strings.HotkeyProblemTypesText,
            HotkeyProblem.EditingKey => Strings.HotkeyProblemEditingKey,
            HotkeyProblem.NeedsModifier => Strings.HotkeyProblemNeedsModifier,
            HotkeyProblem.CommonShortcut => Strings.HotkeyProblemCommonShortcut,
            HotkeyProblem.ModifiersOnly => Strings.HotkeyProblemModifiersOnly,
            _ => Strings.HotkeyProblemAcceptsNth,
        };

        // A left-to-right embedding keeps "Ctrl+Alt+P" in order inside Arabic text.
        return string.Format(CultureInfo.CurrentCulture, format, "\u202A" + HotkeyNames.Format(hotkey) + "\u202C");
    }

    // Process names only: nothing about what the user does in these apps is read or stored.
    private void BuildExcludedPage()
    {
        var excluded = _app.Settings.ExcludedApps;
        var running = RunningApps();
        AddTitle(Strings.NavExcluded);
        AddText(Strings.ExcludedIntro, secondary: true);

        var add = new ThemedButton { Text = Strings.AddButton };
        add.Click += (_, _) => ShowAddAppMenu(add);
        AddCard(Strings.AddApp, Strings.AddAppHint, add);

        AddSection(string.Format(CultureInfo.CurrentCulture, Strings.SectionExcludedList, excluded.Count));
        if (excluded.Count == 0)
        {
            AddText(Strings.ExcludedEmpty, secondary: true);
        }

        var search = new SearchBox { Placeholder = Strings.SearchApps, Text = _excludedSearch };
        AddPageControl(search);
        AddText(Strings.NoMatchingApps, secondary: true);
        var noMatches = _pageControls[^1];

        // Open apps first (they show their description), then the rest in list order.
        var cards = new List<(Control Card, string Searchable)>();
        foreach (var name in excluded.OrderBy(n => running.ContainsKey(ExcludedApps.NormalizeName(n)) ? 0 : 1))
        {
            var remove = new ThemedButton { Text = Strings.RemoveButton };
            remove.Click += (_, _) =>
            {
                if (!Confirm(Strings.ConfirmRemoveExcluded, name))
                {
                    return;
                }

                Update(s => s with { ExcludedApps = ExcludedApps.Without(s.ExcludedApps, name) });
                BeginInvoke(RebuildPage); // after this click is handled
            };
            var description = running.TryGetValue(ExcludedApps.NormalizeName(name), out var found) ? found : null;
            cards.Add((AddCard(name, description, remove), $"{name} {description}"));
        }

        void Filter()
        {
            _excludedSearch = search.Text.Trim();
            foreach (var (card, searchable) in cards)
            {
                if (searchable.Contains(_excludedSearch, StringComparison.CurrentCultureIgnoreCase))
                {
                    _filteredOut.Remove(card);
                }
                else
                {
                    _filteredOut.Add(card);
                }
            }

            if (cards.Count > 0 && cards.All(c => _filteredOut.Contains(c.Card)))
            {
                _filteredOut.Remove(noMatches);
            }
            else
            {
                _filteredOut.Add(noMatches);
            }

            if (excluded.Count == 0)
            {
                _filteredOut.Add(search);
            }

            LayoutPage();
        }

        search.TextChanged += (_, _) => Filter();
        Filter();

        var reset = new ThemedButton { Text = Strings.ResetButton };
        reset.Click += (_, _) =>
        {
            if (!Confirm(Strings.ConfirmResetExcluded, null))
            {
                return;
            }

            Update(s => s with { ExcludedApps = [.. ExcludedApps.Defaults] });
            BeginInvoke(RebuildPage);
        };
        AddCard(Strings.ResetExcluded, Strings.ResetExcludedHint, reset);
    }

    // Open apps (with a window) that are not excluded yet, then "Browse…" for any program file.
    private void ShowAddAppMenu(Control anchor)
    {
        var excluded = _app.Settings.ExcludedApps.Select(ExcludedApps.NormalizeName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var menu = new ContextMenuStrip { RightToLeft = RightToLeft, ShowImageMargin = false };
        foreach (var (name, description) in RunningApps().Where(a => !excluded.Contains(a.Key)).OrderBy(a => a.Value, StringComparer.CurrentCultureIgnoreCase))
        {
            menu.Items.Add($"{description} — {name}", null, (_, _) => AddExcluded(name));
        }

        if (menu.Items.Count > 0)
        {
            menu.Items.Add(new ToolStripSeparator());
        }

        menu.Items.Add(Strings.BrowseApp, null, (_, _) =>
        {
            using var dialog = new OpenFileDialog { Filter = Strings.ExeFilter, CheckFileExists = true };
            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                AddExcluded(dialog.FileName);
            }
        });
        menu.Closed += (_, _) => BeginInvoke(menu.Dispose);
        menu.Show(anchor, new Point(0, anchor.Height));
    }

    private void AddExcluded(string name)
    {
        if (!Confirm(Strings.ConfirmAddExcluded, ExcludedApps.NormalizeName(name)))
        {
            return;
        }

        Update(s => s with { ExcludedApps = ExcludedApps.With(s.ExcludedApps, name) });
        BeginInvoke(RebuildPage);
    }

    // Warning before a change; "No" is the default button. Names are kept left to right (file names,
    // single words) unless they are Arabic phrases, whose word order must stay right to left.
    private bool Confirm(string format, string? name, bool leftToRight = true)
    {
        var shown = leftToRight ? "\u202A" + name + "\u202C" : name;
        var text = name is null ? format : string.Format(CultureInfo.CurrentCulture, format, shown);
        var options = _mirrored ? MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign : 0;
        return MessageBox.Show(this, text, Strings.AppName, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2, options) == DialogResult.Yes;
    }

    // Executable name → description ("Visual Studio Code") of processes that have a window.
    private static Dictionary<string, string> RunningApps()
    {
        var apps = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var self = Environment.ProcessId;
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    if (process.Id == self || process.MainWindowHandle == 0 || ProcessNames.PathOf((uint)process.Id) is not { } path)
                    {
                        continue;
                    }

                    var name = ExcludedApps.NormalizeName(path);
                    var description = FileVersionInfo.GetVersionInfo(path).FileDescription;
                    apps.TryAdd(name, string.IsNullOrWhiteSpace(description) ? name : description.Trim());
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or IOException or UnauthorizedAccessException)
                {
                    // The process exited or is protected: skip it.
                }
            }
        }

        return apps;
    }

    // Rebuilds the current page, keeping the scroll position.
    private void RebuildPage()
    {
        var scroll = _content.Offset;
        SelectPage(_page);
        _content.Offset = scroll;
    }

    // "My words": only word → count / last use is stored (see docs/PRIVACY.md). Every change goes through
    // LearningService, under the lock the suggestion engine also holds.
    private void BuildWordsPage()
    {
        var settings = _app.Settings;
        AddTitle(Strings.NavWords);
        AddText(Strings.WordsIntro, secondary: true);

        var learn = new ToggleSwitch { Checked = !settings.LearningPaused };
        learn.CheckedChanged += (_, _) => Update(s => s with { LearningPaused = !learn.Checked });
        AddCard(Strings.LearnNewWords, Strings.LearnNewWordsHint, learn);

        var add = new ThemedButton { Text = Strings.AddWordButton };
        add.Click += (_, _) => AddWord(blocked: false);
        var block = new ThemedButton { Text = Strings.BlockWordButton };
        block.Click += (_, _) => AddWord(blocked: true);
        AddCard(Strings.AddWord, Strings.AddWordHint, new ButtonRow(add, block));

        var import = new ThemedButton { Text = Strings.ImportButton };
        import.Click += (_, _) => ImportWords();
        var export = new ThemedButton { Text = Strings.ExportButton };
        export.Click += (_, _) => ExportWords();
        AddCard(Strings.ImportExport, Strings.ImportExportHint, new ButtonRow(import, export));

        AddSection(string.Empty);
        _wordsHeader = (Label)_pageControls[^1];

        string[] sorts = [Strings.SortMostUsed, Strings.SortRecent, Strings.SortAlphabetical];
        var sort = new SegmentedChoice { Options = sorts };
        sort.SelectedIndex = _wordsSort;
        sort.SelectedIndexChanged += (_, _) =>
        {
            _wordsSort = sort.SelectedIndex;
            RefreshWords();
        };
        AddCard(Strings.SortWords, null, sort);

        _wordsSearch = new SearchBox { Placeholder = Strings.SearchWords, Text = _wordsFilter };
        _wordsSearch.TextChanged += (_, _) =>
        {
            _wordsFilter = _wordsSearch.Text.Trim();
            RefreshWords();
        };
        AddPageControl(_wordsSearch);

        _wordList = new WordList();
        _wordList.ItemActivated += (_, _) => EditSelectedWord();
        _wordList.DeleteRequested += (_, _) => DeleteSelectedWord();
        _wordList.ContextRequested += ShowWordMenu;
        AddPageControl(_wordList);

        _editWord = new ThemedButton { Text = Strings.EditWordButton };
        _editWord.Click += (_, _) => EditSelectedWord();
        _toggleBlock = new ThemedButton { Text = Strings.UnblockButton }; // the longer label sizes the button
        _toggleBlock.Click += (_, _) => ToggleSelectedBlock();
        _deleteWord = new ThemedButton { Text = Strings.DeleteButton };
        _deleteWord.Click += (_, _) => DeleteSelectedWord();
        AddPageControl(new ButtonRow(_editWord, _toggleBlock, _deleteWord) { Stretch = true });
        _wordList.SelectionChanged += (_, _) => UpdateWordButtons();

        AddSection(Strings.SectionClearWords);
        var clear = new ThemedButton { Text = Strings.ClearAllButton };
        clear.Click += (_, _) =>
        {
            if (Confirm(Strings.ConfirmClearWords, null))
            {
                _app.Learning.ClearAll();
                RefreshWords();
            }
        };
        AddCard(Strings.ClearWords, Strings.ClearWordsHint, clear);
        RefreshWords();
    }

    // Reads a fresh copy of the words, then filters and sorts it for the list.
    private void RefreshWords()
    {
        if (_wordList is null || _wordsHeader is null)
        {
            return;
        }

        var all = _app.Learning.Snapshot();
        _wordsHeader.Text = string.Format(CultureInfo.CurrentCulture, Strings.SectionWordsList, all.Count);
        IEnumerable<StoredWord> words = all;
        if (_wordsFilter.Length > 0)
        {
            words = words.Where(w => w.Word.Contains(_wordsFilter, StringComparison.Ordinal));
        }

        words = _wordsSort switch
        {
            1 => words.OrderByDescending(w => w.LastUsed),
            2 => words.OrderBy(w => w.Word, StringComparer.Create(new CultureInfo("ar"), ignoreCase: false)),
            _ => words.OrderByDescending(w => w.Count).ThenByDescending(w => w.LastUsed),
        };
        _wordList.Words = [.. words];
        UpdateWordButtons();
    }

    private void UpdateWordButtons()
    {
        var selected = _wordList?.SelectedWord;
        foreach (var button in new[] { _editWord, _toggleBlock, _deleteWord })
        {
            if (button is not null)
            {
                button.Enabled = selected is not null;
                button.Invalidate();
            }
        }

        if (_toggleBlock is not null)
        {
            _toggleBlock.Text = selected is { Blocked: true } ? Strings.UnblockButton : Strings.BlockButton;
        }
    }

    // Add: suggested at once (counted as used enough times). Block: never suggested, even from the dictionary.
    private void AddWord(bool blocked)
    {
        var word = WordDialog.Ask(this, blocked ? Strings.BlockWord : Strings.AddWord, blocked ? Strings.BlockWordPrompt : Strings.AddWordPrompt, string.Empty, _palette, _mirrored);
        if (word is null || (blocked && !Confirm(Strings.ConfirmBlockWord, word)))
        {
            return;
        }

        if (blocked)
        {
            _app.Learning.SetBlocked(word, blocked: true);
        }
        else
        {
            _app.Learning.AddManually(word, _app.Settings.MinUsesToSuggest);
        }

        RefreshWords();
    }

    private void EditSelectedWord()
    {
        if (_wordList?.SelectedWord is not { } selected)
        {
            return;
        }

        var word = WordDialog.Ask(this, Strings.EditWord, Strings.EditWordPrompt, selected.Word, _palette, _mirrored);
        if (word is not null && word != selected.Word)
        {
            _app.Learning.Rename(selected.Word, word);
            RefreshWords();
        }
    }

    private void ToggleSelectedBlock()
    {
        if (_wordList?.SelectedWord is not { } selected
            || !Confirm(selected.Blocked ? Strings.ConfirmUnblockWord : Strings.ConfirmBlockWord, selected.Word))
        {
            return;
        }

        if (selected.Blocked && selected.Count == 0)
        {
            _app.Learning.Remove(selected.Word); // only existed to be blocked
        }
        else
        {
            _app.Learning.SetBlocked(selected.Word, !selected.Blocked);
        }

        RefreshWords();
    }

    private void DeleteSelectedWord()
    {
        if (_wordList?.SelectedWord is { } selected && Confirm(Strings.ConfirmDeleteWord, selected.Word))
        {
            _app.Learning.Remove(selected.Word);
            RefreshWords();
        }
    }

    private void ShowWordMenu(Point location)
    {
        if (_wordList?.SelectedWord is not { } selected)
        {
            return;
        }

        var menu = new ContextMenuStrip { RightToLeft = RightToLeft, ShowImageMargin = false };
        menu.Items.Add(Strings.EditWordButton, null, (_, _) => EditSelectedWord());
        menu.Items.Add(selected.Blocked ? Strings.UnblockButton : Strings.BlockButton, null, (_, _) => ToggleSelectedBlock());
        menu.Items.Add(Strings.DeleteButton, null, (_, _) => DeleteSelectedWord());
        menu.Closed += (_, _) => BeginInvoke(menu.Dispose);
        menu.Show(_wordList, location);
    }

    // The file is the user's choice: one word per line, "word<TAB>count", "!word" = blocked.
    private void ImportWords()
    {
        using var dialog = new OpenFileDialog { Filter = Strings.WordsFileFilter };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            using var reader = new StreamReader(dialog.FileName, System.Text.Encoding.UTF8);
            var result = _app.Learning.Import(reader);
            RefreshWords();
            ShowInfo(string.Format(CultureInfo.CurrentCulture, Strings.ImportDone, result.Added, result.Updated, result.Skipped));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowInfo(Strings.FileError);
        }
    }

    private void ExportWords()
    {
        using var dialog = new SaveFileDialog { Filter = Strings.WordsFileFilter, FileName = Strings.WordsFileName };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            using var writer = new StreamWriter(dialog.FileName, append: false, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            _app.Learning.Export(writer);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowInfo(Strings.FileError);
        }
    }

    // "Import…" / "Export…" for a list kept in settings.json ("My phrases", text shortcuts): a UTF-8 text
    // file. Importing only adds entries; the page is rebuilt to show them.
    private void AddImportExportCard<T>(string fileName, Action<TextWriter> write, Func<TextReader, ListImport<T>> read,
        Func<AppSettings, IReadOnlyList<T>, AppSettings> apply, string? hint = null)
    {
        var import = new ThemedButton { Text = Strings.ImportButton };
        import.Click += (_, _) =>
        {
            using var dialog = new OpenFileDialog { Filter = Strings.WordsFileFilter };
            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            try
            {
                using var reader = new StreamReader(dialog.FileName, System.Text.Encoding.UTF8);
                var result = read(reader);
                Update(s => apply(s, result.Merged));
                ShowInfo(string.Format(CultureInfo.CurrentCulture, Strings.ImportListDone, result.Added, result.Skipped));
                BeginInvoke(RebuildPage);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                ShowInfo(Strings.FileError);
            }
        };

        var export = new ThemedButton { Text = Strings.ExportButton };
        export.Click += (_, _) =>
        {
            using var dialog = new SaveFileDialog { Filter = Strings.WordsFileFilter, FileName = fileName };
            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            try
            {
                using var writer = new StreamWriter(dialog.FileName, append: false, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
                write(writer);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                ShowInfo(Strings.FileError);
            }
        };

        AddCard(Strings.ImportExport, hint ?? Strings.ImportExportListHint, new ButtonRow(import, export));
    }

    private void ShowInfo(string text)
    {
        var options = _mirrored ? MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign : 0;
        MessageBox.Show(this, text, Strings.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information, MessageBoxDefaultButton.Button1, options);
    }

    // User decision (1.1): fixed expressions are completed whole (Tab) or word by word: the user's own
    // ("My phrases", kept in settings.json), the learned ones (sequences typed twice, in user.db) and the
    // shipped list.
    private void BuildPhrasesPage()
    {
        var settings = _app.Settings;
        AddTitle(Strings.NavPhrases);
        AddText(string.Format(CultureInfo.CurrentCulture, Strings.PhrasesIntro, _app.ShippedPhrases.Count), secondary: true);

        var suggest = new ToggleSwitch { Checked = settings.SuggestPhrases };
        suggest.CheckedChanged += (_, _) => Update(s => s with { SuggestPhrases = suggest.Checked });
        AddCard(Strings.SuggestPhrases, Strings.SuggestPhrasesHint, suggest);

        var learn = new ToggleSwitch { Checked = settings.LearnPhrases };
        learn.CheckedChanged += (_, _) => Update(s => s with { LearnPhrases = learn.Checked });
        AddCard(Strings.LearnPhrases, Strings.LearnPhrasesHint, learn);

        AddSection(string.Format(CultureInfo.CurrentCulture, Strings.SectionMyPhrases, settings.MyPhrases.Count));
        var add = new ThemedButton { Text = Strings.AddWordButton };
        add.Click += (_, _) => EditPhrase(null);
        AddCard(Strings.AddPhrase, Strings.AddPhraseHint, add);
        AddImportExportCard(
            Strings.PhrasesFileName,
            writer => UserListFiles.WritePhrases(writer, _app.Settings.MyPhrases,
                _app.Learning.LearnedPhrases(all: true).Select(p => (p.Text, p.Count))),
            reader =>
            {
                // The learned phrases in the file go back to the learned list, with their counts.
                var learned = new List<(string Text, int Count)>();
                var mine = UserListFiles.ReadPhrases(reader, _app.Settings.MyPhrases, learned);
                return mine with { Added = mine.Added + _app.Learning.ImportPhrases(learned) };
            },
            (s, merged) => s with { MyPhrases = merged },
            Strings.ImportExportPhrasesHint);

        foreach (var phrase in settings.MyPhrases)
        {
            var change = new ThemedButton { Text = Strings.EditWordButton };
            change.Click += (_, _) => EditPhrase(phrase);
            var remove = new ThemedButton { Text = Strings.DeleteButton };
            remove.Click += (_, _) =>
            {
                if (Confirm(Strings.ConfirmDeletePhrase, phrase, leftToRight: false))
                {
                    Update(s => s with { MyPhrases = [.. s.MyPhrases.Where(p => p != phrase)] });
                    BeginInvoke(RebuildPage); // after this click is handled
                }
            };
            AddCard(phrase, null, new ButtonRow(change, remove));
        }

        BuildLearnedPhrases();

        // Built-in phrases deleted from the bar.
        if (settings.HiddenPhrases.Count > 0)
        {
            var restore = new ThemedButton { Text = Strings.RestoreButton };
            restore.Click += (_, _) =>
            {
                Update(s => s with { HiddenPhrases = [] });
                BeginInvoke(RebuildPage);
            };
            AddCard(string.Format(CultureInfo.CurrentCulture, Strings.HiddenPhrases, settings.HiddenPhrases.Count),
                string.Join("، ", settings.HiddenPhrases), restore);
        }
    }

    // The learned phrases: forget one (learned again if typed again), keep it for good in "My phrases",
    // or delete them all together with every sequence counted so far.
    private void BuildLearnedPhrases()
    {
        var phrases = _app.Learning.LearnedPhrases(); // like the bar: not the parts of a longer learned phrase
        AddSection(string.Format(CultureInfo.CurrentCulture, Strings.SectionLearnedPhrases, phrases.Count));
        if (phrases.Count == 0)
        {
            AddText(Strings.NoLearnedPhrases, secondary: true);
        }
        else
        {
            var list = new WordList { Height = 280 };
            list.Words = [.. phrases.Select(p => new StoredWord(p.Text, p.Count, p.LastUsed, Blocked: false))];
            var keep = new ThemedButton { Text = Strings.KeepPhraseButton };
            var forget = new ThemedButton { Text = Strings.DeleteButton };
            void UpdateButtons()
            {
                foreach (var button in new[] { keep, forget })
                {
                    button.Enabled = list.SelectedWord is not null;
                    button.Invalidate();
                }
            }

            void Forget()
            {
                if (list.SelectedWord is { } selected && Confirm(Strings.ConfirmForgetPhrase, selected.Word, leftToRight: false))
                {
                    _app.Learning.ForgetPhrase(selected.Word);
                    BeginInvoke(RebuildPage);
                }
            }

            keep.Click += (_, _) =>
            {
                if (list.SelectedWord is { } selected)
                {
                    Update(s => s with { MyPhrases = [.. s.MyPhrases.Append(selected.Word).Distinct()] });
                    _app.Learning.ForgetPhrase(selected.Word);
                    BeginInvoke(RebuildPage);
                }
            };
            forget.Click += (_, _) => Forget();
            list.DeleteRequested += (_, _) => Forget();
            list.SelectionChanged += (_, _) => UpdateButtons();
            AddPageControl(list);
            AddPageControl(new ButtonRow(keep, forget) { Stretch = true });
            UpdateButtons();
        }

        var clear = new ThemedButton { Text = Strings.ClearAllButton };
        clear.Click += (_, _) =>
        {
            if (Confirm(Strings.ConfirmClearPhrases, null))
            {
                _app.Learning.ClearPhrases();
                BeginInvoke(RebuildPage);
            }
        };
        AddCard(Strings.ClearPhrases, Strings.ClearPhrasesHint, clear);
    }

    // Add (existing = null) or edit one of "My phrases".
    private void EditPhrase(string? existing)
    {
        var text = WordDialog.Ask(this, existing is null ? Strings.AddPhrase : Strings.EditPhrase, Strings.PhrasePrompt,
            existing ?? string.Empty, _palette, _mirrored, PhraseIndex.Clean, Strings.NotAPhrase);
        if (text is null || text == existing)
        {
            return;
        }

        Update(s => s with { MyPhrases = [.. s.MyPhrases.Where(p => p != existing).Append(text).Distinct()] });
        BeginInvoke(RebuildPage);
    }

    // User decision (1.1): a text shortcut («ع1») typed exactly shows its text first in the bar; Tab inserts
    // it. Kept in settings.json, like "My phrases".
    private void BuildTextShortcutsPage()
    {
        var shortcuts = _app.Settings.TextShortcuts;
        AddTitle(Strings.NavTextShortcuts);
        AddText(Strings.TextShortcutsIntro, secondary: true);

        var add = new ThemedButton { Text = Strings.AddWordButton };
        add.Click += (_, _) => EditTextShortcut(null);
        AddCard(Strings.AddTextShortcut, Strings.AddTextShortcutHint, add);
        AddImportExportCard(
            Strings.TextShortcutsFileName,
            writer => UserListFiles.WriteShortcuts(writer, _app.Settings.TextShortcuts),
            reader => UserListFiles.ReadShortcuts(reader, _app.Settings.TextShortcuts),
            (s, merged) => s with { TextShortcuts = merged });

        AddSection(string.Format(CultureInfo.CurrentCulture, Strings.SectionTextShortcuts, shortcuts.Count));
        foreach (var shortcut in shortcuts)
        {
            var change = new ThemedButton { Text = Strings.EditWordButton };
            change.Click += (_, _) => EditTextShortcut(shortcut);
            var remove = new ThemedButton { Text = Strings.DeleteButton };
            remove.Click += (_, _) =>
            {
                if (Confirm(Strings.ConfirmDeleteTextShortcut, shortcut.Key, leftToRight: false))
                {
                    Update(s => s with { TextShortcuts = [.. s.TextShortcuts.Where(t => t.Key != shortcut.Key)] });
                    BeginInvoke(RebuildPage); // after this click is handled
                }
            };
            AddCard(shortcut.Key, shortcut.Text, new ButtonRow(change, remove));
        }
    }

    // Add (existing = null) or edit a text shortcut; a key already used is replaced after a confirmation.
    private void EditTextShortcut(TextShortcut? existing)
    {
        if (TextShortcutDialog.Ask(this, existing is null ? Strings.AddTextShortcut : Strings.EditTextShortcut, existing, _palette, _mirrored) is not { } edited
            || edited == existing)
        {
            return;
        }

        var replaced = _app.Settings.TextShortcuts.Any(t => t.Key == edited.Key && t.Key != existing?.Key);
        if (replaced && !Confirm(Strings.ConfirmReplaceTextShortcut, edited.Key, leftToRight: false))
        {
            return;
        }

        Update(s =>
        {
            // An edited shortcut keeps its place; a new one goes last.
            var list = s.TextShortcuts.ToList();
            var index = existing is null ? -1 : list.FindIndex(t => t.Key == existing.Key);
            list.RemoveAll(t => t.Key == edited.Key || t.Key == existing?.Key);
            list.Insert(index < 0 ? list.Count : Math.Min(index, list.Count), edited);
            return s with { TextShortcuts = list };
        });
        BeginInvoke(RebuildPage);
    }

    // User decision (1.1): long phrases (support replies of several lines) are chosen by name from a list
    // opened with a shortcut anywhere (LongPhrasePicker). Kept in settings.json.
    private void BuildLongPhrasesPage()
    {
        var phrases = _app.Settings.LongPhrases;
        var shortcut = "\u202A" + HotkeyNames.Format(_app.Settings.Hotkeys.OpenLongPhrases) + "\u202C";
        AddTitle(Strings.NavLongPhrases);
        AddText(string.Format(CultureInfo.CurrentCulture, Strings.LongPhrasesIntro, shortcut), secondary: true);

        // The same shortcut as on the Shortcuts page, here too (user decision); the intro names it.
        AddHotkeyCard(HotkeyAction.OpenLongPhrases, Strings.ShortcutLongPhrases, Strings.ShortcutLongPhrasesHint, () => BeginInvoke(RebuildPage));

        var add = new ThemedButton { Text = Strings.AddWordButton };
        add.Click += (_, _) => EditLongPhrase(null);
        AddCard(Strings.AddLongPhrase, Strings.AddLongPhraseHint, add);
        AddImportExportCard(
            Strings.LongPhrasesFileName,
            writer => UserListFiles.WriteLongPhrases(writer, _app.Settings.LongPhrases),
            reader => UserListFiles.ReadLongPhrases(reader, _app.Settings.LongPhrases),
            (s, merged) => s with { LongPhrases = merged });

        AddSection(string.Format(CultureInfo.CurrentCulture, Strings.SectionLongPhrases, phrases.Count));
        foreach (var phrase in phrases)
        {
            var change = new ThemedButton { Text = Strings.EditWordButton };
            change.Click += (_, _) => EditLongPhrase(phrase);
            var remove = new ThemedButton { Text = Strings.DeleteButton };
            remove.Click += (_, _) =>
            {
                if (Confirm(Strings.ConfirmDeleteLongPhrase, phrase.Name, leftToRight: false))
                {
                    Update(s => s with { LongPhrases = [.. s.LongPhrases.Where(p => p.Name != phrase.Name)] });
                    BeginInvoke(RebuildPage); // after this click is handled
                }
            };
            AddCard(phrase.Name, LongPhrases.Preview(phrase.Text), new ButtonRow(change, remove));
        }
    }

    // Add (existing = null) or edit a long phrase; a name already used is replaced after a confirmation.
    private void EditLongPhrase(LongPhrase? existing)
    {
        if (LongPhraseDialog.Ask(this, existing is null ? Strings.AddLongPhrase : Strings.EditLongPhrase, existing, _palette, _mirrored) is not { } edited
            || edited == existing)
        {
            return;
        }

        if (_app.Settings.LongPhrases.Any(p => p.Name == edited.Name && p.Name != existing?.Name)
            && !Confirm(Strings.ConfirmReplaceLongPhrase, edited.Name, leftToRight: false))
        {
            return;
        }

        Update(s =>
        {
            // An edited phrase keeps its place; a new one goes last.
            var list = s.LongPhrases.ToList();
            var index = existing is null ? -1 : list.FindIndex(p => p.Name == existing.Name);
            list.RemoveAll(p => p.Name == edited.Name || p.Name == existing?.Name);
            list.Insert(index < 0 ? list.Count : Math.Min(index, list.Count), edited);
            return s with { LongPhrases = list };
        });
        BeginInvoke(RebuildPage);
    }

    // User decision: updates come from Microsoft Store. Itmam only asks the Store whether a newer version
    // exists (StoreUpdates); it never downloads anything itself, and the button opens the Store page.
    private void BuildUpdatesPage()
    {
        AddTitle(Strings.NavUpdates);
        AddText(string.Format(CultureInfo.CurrentCulture, Strings.AboutVersion, AppVersion()), secondary: true);

        var onStart = new ToggleSwitch { Checked = _app.Settings.CheckForUpdates };
        onStart.CheckedChanged += (_, _) => Update(s => s with { CheckForUpdates = onStart.Checked });
        AddCard(Strings.CheckOnStart, Strings.CheckOnStartHint, onStart);

        var check = new ThemedButton { Text = Strings.CheckNowButton };
        var checkCard = AddCard(Strings.CheckNow, null, check);
        check.Click += async (_, _) =>
        {
            check.Enabled = false;
            check.Invalidate();
            checkCard.SetDescription(Strings.UpdateChecking);
            var result = await StoreUpdates.CheckAsync();
            if (checkCard.IsDisposed)
            {
                return; // the page was left meanwhile
            }

            checkCard.SetDescription(result switch
            {
                UpdateCheck.UpdateAvailable => Strings.UpdateAvailable,
                UpdateCheck.UpToDate => Strings.UpdateUpToDate,
                _ => Strings.UpdateUnavailable,
            });
            check.Enabled = true;
            check.Invalidate();
        };

        var open = new ThemedButton { Text = Strings.OpenButton };
        open.Click += (_, _) => Tray.TrayApplication.OpenStorePage();
        AddCard(Strings.OpenStore, Strings.OpenStoreHint, open);
    }

    // The package version when installed from the Store or a test package, else the assembly version.
    private static string AppVersion() =>
        StoreUpdates.PackageVersion
        ?? Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion?.Split('+')[0]
        ?? "1.0";

    // Its own page: exiting is different from "Enabled" off, and is explained here.
    private void BuildExitPage()
    {
        AddTitle(Strings.SectionExit);

        // Turning off keeps Itmam next to the clock (gray icon) and is remembered, like the toggle shortcut.
        var toggle = new ThemedButton { Text = _app.IsEnabled ? Strings.TurnOffButton : Strings.TurnOnButton };
        toggle.Click += (_, _) =>
        {
            _app.SetEnabled(!_app.IsEnabled);
            BeginInvoke(RebuildPage); // after this click is handled
        };
        var shortcut = "\u202A" + HotkeyNames.Format(_app.Settings.Hotkeys.ToggleEnabled) + "\u202C";
        AddCard(_app.IsEnabled ? Strings.TurnOffTitle : Strings.TurnOnTitle,
            string.Format(CultureInfo.CurrentCulture, Strings.TurnOffHint, shortcut), toggle);
        var exit = new ThemedButton { Text = Strings.ExitButton };
        exit.Click += (_, _) => _exit();
        AddCard(Strings.ExitTitle, Strings.ExitHint, exit);
    }

    private void BuildAboutPage()
    {
        AddTitle(Strings.AppName);
        AddText(string.Format(CultureInfo.CurrentCulture, Strings.AboutVersion, AppVersion()), secondary: true);
        AddText(Strings.AboutTagline, secondary: false);
        AddText(string.Format(CultureInfo.CurrentCulture, Strings.AboutDictionary, _app.Dictionary.Count), secondary: false);
        AddText(string.Format(CultureInfo.CurrentCulture, Strings.AboutLearned, _app.UserWords.Count), secondary: false);

        AddSection(Strings.SectionDeveloper);
        AddText(Strings.DeveloperName, secondary: false);
        AddLink(DeveloperEmail, $"mailto:{DeveloperEmail}");

        AddSection(Strings.SectionOpenSource);
        AddText(Strings.AboutOpenSource, secondary: false);
        AddLink(Strings.AboutSourceCode, RepositoryUrl);

        AddSection(Strings.AboutSources);
        foreach (var source in _app.Dictionary.Sources)
        {
            AddLink($"{source.Name} — {source.License}", source.Url);
        }

        AddSection(Strings.AboutPrivacyLink);
        AddText(Strings.AboutPrivacy, secondary: false);
        AddLink(Strings.AboutPrivacyLink, _app.Settings.Language == UiLanguage.Arabic ? PrivacyUrl : PrivacyUrlEnglish);
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

    private void AddTitle(string text) => AddPageControl(new Label { Text = text, Font = new Font("Segoe UI Semibold", 20f), Height = 56, UseMnemonic = false, Tag = "title" });

    private void AddSection(string text) => AddPageControl(new Label { Text = text, Font = new Font("Segoe UI Semibold", 10.5f), Height = 40, TextAlign = ContentAlignment.BottomLeft, UseMnemonic = false });

    private void AddText(string text, bool secondary) =>
        AddPageControl(new Label { Text = text, Height = 28, Tag = secondary ? "secondary" : "text", UseMnemonic = false });

    private void AddLink(string text, string url)
    {
        var link = new LinkLabel { Text = text, Height = 28, UseMnemonic = false };
        link.LinkClicked += (_, _) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); // opens the user's browser
        AddPageControl(link);
    }

    private SettingCard AddCard(string title, string? description, Control control, bool stacked = false)
    {
        var card = new SettingCard(title, description, control, stacked);
        card.SizeChanged += (_, _) => LayoutPage(); // a wrapped description made it taller
        AddPageControl(card);
        return card;
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
