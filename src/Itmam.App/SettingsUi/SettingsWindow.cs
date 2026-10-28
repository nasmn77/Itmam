using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Reflection;
using Itmam.App.Resources;
using Itmam.App.Suggestions;
using Itmam.Core.Input;
using Itmam.Core.Presentation;
using Itmam.Core.Settings;
using Itmam.Win32;
using Microsoft.Win32;

namespace Itmam.App.SettingsUi;

internal enum SettingsPage
{
    General,
    Appearance,
    Shortcuts,
    Excluded,
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
    private const string PrivacyUrl = RepositoryUrl + "/blob/main/docs/PRIVACY.md";
    private const string DeveloperEmail = "nasmn77@gmail.com";
    private const int WS_EX_LAYOUTRTL = 0x00400000;
    private const int WS_EX_NOINHERITLAYOUT = 0x00100000;

    private readonly ItmamApp _app;
    private readonly Action _exit;
    private readonly bool _mirrored;
    private readonly Panel _navigation = new() { Dock = DockStyle.Left, Width = 240 };
    private readonly Panel _content = new() { Dock = DockStyle.Fill, AutoScroll = true };
    private readonly Dictionary<SettingsPage, NavItem> _navItems = [];
    private readonly List<Control> _pageControls = [];
    private readonly HashSet<Control> _filteredOut = [];
    private string _excludedSearch = string.Empty; // kept when the page is rebuilt after add/remove
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
        foreach (var (page, title) in new[] { (SettingsPage.General, Strings.NavGeneral), (SettingsPage.Appearance, Strings.NavAppearance), (SettingsPage.Shortcuts, Strings.NavShortcuts), (SettingsPage.Excluded, Strings.NavExcluded), (SettingsPage.About, Strings.NavAbout), (SettingsPage.Exit, Strings.NavExit) })
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

        var reset = new ThemedButton { Text = Strings.ResetButton };
        reset.Click += (_, _) =>
        {
            Update(s => s with { Hotkeys = new HotkeySettings() });
            BeginInvoke(() => SelectPage(SettingsPage.Shortcuts)); // rebuild after this click is handled
        };
        AddCard(Strings.ResetShortcuts, Strings.ResetShortcutsHint, reset);
    }

    private void AddHotkeyCard(HotkeyAction action, string title, string? hint)
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
        Update(s => s with { ExcludedApps = ExcludedApps.With(s.ExcludedApps, name) });
        BeginInvoke(RebuildPage);
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
        var scroll = -_content.AutoScrollPosition.Y;
        SelectPage(_page);
        _content.AutoScrollPosition = new Point(0, scroll);
    }

    // Its own page: exiting is different from "Enabled" off, and is explained here.
    private void BuildExitPage()
    {
        AddTitle(Strings.SectionExit);
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

    private void AddTitle(string text) => AddPageControl(new Label { Text = text, Font = new Font("Segoe UI Semibold", 20f), Height = 56, UseMnemonic = false, Tag = "title" });

    private void AddSection(string text) => AddPageControl(new Label { Text = text, Font = new Font("Segoe UI Semibold", 10.5f), Height = 40, TextAlign = ContentAlignment.BottomLeft, UseMnemonic = false });

    private void AddText(string text, bool secondary) =>
        AddPageControl(new Label { Text = text, Height = 28, Tag = secondary ? "secondary" : null, UseMnemonic = false });

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
        var width = Math.Max(200, _content.ClientSize.Width - (2 * margin));
        var y = (int)(12 * scale) + _content.AutoScrollPosition.Y;
        foreach (var control in _pageControls)
        {
            // Filtered out by a search box (the parent may not be visible yet, so Visible cannot be read back).
            var shown = !_filteredOut.Contains(control);
            control.Visible = shown;
            if (!shown)
            {
                continue;
            }

            control.SetBounds(margin, y, width, control.Height);
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
