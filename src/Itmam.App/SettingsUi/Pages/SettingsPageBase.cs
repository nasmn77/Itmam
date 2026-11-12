using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using Itmam.App.Resources;
using Itmam.Core.Input;
using Itmam.Core.Languages;
using Itmam.Core.Settings;
using Itmam.Core.Suggestions;
using Itmam.Win32;

namespace Itmam.App.SettingsUi.Pages;

/// <summary>
/// One page of the settings window. A page is built again every time it is shown, and after a change
/// that adds or removes cards, so an instance lives only as long as its controls; what must outlive a
/// rebuild is kept in <see cref="SettingsViewState"/>. The window lays the controls out and themes them;
/// the members here pass on to it, and hold the cards several pages share.
/// </summary>
internal abstract class SettingsPageBase(SettingsWindow window)
{
    /// <summary>The settings window: the owner of dialogs and menus.</summary>
    protected SettingsWindow Window { get; } = window;

    protected ItmamApp App => Window.App;

    protected SettingsViewState State => Window.State;

    protected SettingsPalette Palette => Window.Palette;

    protected bool Mirrored => Window.Mirrored;

    protected RightToLeft RightToLeft => Window.RightToLeft;

    protected PageScroller Content => Window.Content;

    protected List<Control> PageControls => Window.PageControls;

    protected HashSet<Control> FilteredOut => Window.FilteredOut;

    /// <summary>True while controls are being filled: their change events must not save anything.</summary>
    protected bool Loading
    {
        get => Window.IsLoading;
        set => Window.SetLoading(value);
    }

    /// <summary>Adds the page's controls to the window.</summary>
    public abstract void Build();

    protected void Update(Func<AppSettings, AppSettings> change) => Window.Update(change);

    protected bool Confirm(string format, string? name, bool leftToRight = true) => Window.Confirm(format, name, leftToRight);

    protected void ShowInfo(string text) => Window.ShowInfo(text);

    protected void SelectPage(SettingsPage page) => Window.SelectPage(page);

    protected void RebuildPage() => Window.RebuildPage();

    protected void RefreshTheme() => Window.RefreshTheme();

    protected void ExitApp() => Window.ExitApp();

    protected void BeginInvoke(Action action) => Window.BeginInvoke(action);

    protected void LayoutPage() => Window.LayoutPage();

    protected void AddTitle(string text) => Window.AddTitle(text);

    protected void AddSection(string text) => Window.AddSection(text);

    protected void AddText(string text, bool secondary) => Window.AddText(text, secondary);

    protected void AddLink(string text, string url) => Window.AddLink(text, url);

    protected SettingCard AddCard(string title, string? description, Control control, bool stacked = false) => Window.AddCard(title, description, control, stacked);

    protected void AddPageControl(Control control) => Window.AddPageControl(control);

    // Arabic and English in the interface language; any other language by its own name («עברית», «Français»).
    protected static string LanguageName(string tag)
    {
        switch (tag)
        {
            case "ar":
                return Strings.LanguageArabic;
            case "en":
                return Strings.LanguageEnglish;
        }

        try
        {
            var culture = CultureInfo.GetCultureInfo(tag);
            var name = culture.NativeName;
            return name.Length > 0 ? culture.TextInfo.ToUpper(name[0]) + name[1..] : tag;
        }
        catch (CultureNotFoundException)
        {
            return tag;
        }
    }

    // The languages that have something to show in My words / My phrases: those with learned words or
    // phrases, and those turned on that Itmam suggests in. Arabic first.
    private List<LanguageProfile> ListLanguages() =>
        [.. App.Learning.LanguagesWithLearned
            .Concat(App.Settings.EnabledLanguages.Select(LanguageRegistry.ForTag).Where(l => LanguageRegistry.Support(l) != LanguageSupport.Unsupported))
            .DistinctBy(l => l.Id)
            .OrderBy(l => l.Id switch { "ar" => 0, "en" => 1, _ => 2 })
            .ThenBy(l => l.Id, StringComparer.Ordinal)];

    // A choice of language at the top of a list, when there is more than one (rebuilds the page on change).
    protected void AddLanguageChoice()
    {
        var languages = ListLanguages();
        if (!languages.Contains(State.WordsLanguage))
        {
            State.WordsLanguage = LanguageProfile.Arabic;
        }

        if (languages.Count < 2)
        {
            return;
        }

        var choice = new SegmentedChoice { Options = [.. languages.Select(l => LanguageName(l.Id))] };
        choice.SelectedIndex = languages.IndexOf(State.WordsLanguage);
        choice.SelectedIndexChanged += (_, _) =>
        {
            State.WordsLanguage = languages[choice.SelectedIndex];
            BeginInvoke(RebuildPage);
        };
        AddCard(Strings.WordsLanguage, null, choice);
    }

    protected void AddHotkeyCard(HotkeyAction action, string title, string? hint, Action? changed = null)
    {
        var box = new HotkeyBox { Hotkey = HotkeyRules.Get(App.Settings.Hotkeys, action), RecordingText = Strings.ShortcutRecording };
        var card = AddCard(title, hint, box);
        box.RecordingChanged += App.SuspendHotkeys;
        box.Recorded += hotkey =>
        {
            var problem = HotkeyRules.Check(App.Settings.Hotkeys, action, hotkey);
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

    protected static string ProblemText(HotkeyProblem problem, Hotkey hotkey)
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

    // "Import…" / "Export…" for a list kept in settings.json ("My phrases", text shortcuts): a UTF-8 text
    // file. Importing only adds entries; the page is rebuilt to show them.
    protected void AddImportExportCard<T>(string fileName, Action<TextWriter> write, Func<TextReader, ListImport<T>> read,
        Func<AppSettings, IReadOnlyList<T>, AppSettings> apply, string? hint = null)
    {
        var import = new ThemedButton { Text = Strings.ImportButton };
        import.Click += (_, _) =>
        {
            using var dialog = new OpenFileDialog { Filter = Strings.WordsFileFilter };
            if (dialog.ShowDialog(Window) != DialogResult.OK)
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
            if (dialog.ShowDialog(Window) != DialogResult.OK)
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

    // User decision: one button, on the Updates and About pages, for Itmam's review page in the Store app.
    protected void AddRateCard()
    {
        var rate = new ThemedButton { Text = Strings.RateButton };
        rate.Click += (_, _) =>
        {
            try
            {
                Process.Start(new ProcessStartInfo(StoreUpdates.ReviewPage.ToString()) { UseShellExecute = true });
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                ShowInfo(Strings.RateUnavailable);
            }
        };
        AddCard(Strings.RateTitle, Strings.RateHint, rate);
    }

    // The package version when installed from the Store or a test package, else the assembly version.
    protected static string AppVersion() =>
        StoreUpdates.PackageVersion
        ?? Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion?.Split('+')[0]
        ?? "1.0";
}
