using System.Collections.Concurrent;
using System.Runtime;
using Itmam.App.Suggestions;
using Itmam.Core.Dictionary;
using Itmam.Core.Dictionary.Building;
using Itmam.Core.Input;
using Itmam.Core.Languages;
using Itmam.Core.Learning;
using Itmam.Core.Presentation;
using Itmam.Core.Settings;
using Itmam.Core.Suggestions;
using Itmam.Win32;

namespace Itmam.App;

/// <summary>
/// Composition root: builds every component, applies <see cref="AppSettings"/> live, and shuts
/// everything down cleanly (unhook, save learned words, dispose) exactly once.
/// </summary>
internal sealed class ItmamApp : IDisposable
{
    private readonly SettingsStore _settingsStore;
    private readonly SynchronizationContext _ui;
    private readonly CancellationTokenSource _stop = new();
    private readonly CaretLocator _caret;

    // What each language typed so far suggests from; Arabic is built at start-up, the others on first use.
    private readonly ConcurrentDictionary<string, LanguageSuggestions> _languages = new(StringComparer.Ordinal);
    private readonly Corrections _corrections;

    // The phrases shipped for each language that has them (data/phrases.txt, data/phrases.en.txt).
    private readonly ConcurrentDictionary<string, PhraseIndex> _shippedPhrases = new(StringComparer.Ordinal);
    private Task? _running;
    private int _shutDown;

    /// <param name="ui">The WinForms UI thread context (events are raised there).</param>
    public ItmamApp(SynchronizationContext ui)
    {
        _ui = ui;
        DataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Itmam");
#if DEBUG
        // Screenshots with sample data, never the developer's own words and phrases (Debug builds only).
        DataFolder = Environment.GetEnvironmentVariable("ITMAM_DEMO_DATA") is { Length: > 0 } demo ? demo : DataFolder;
#endif
        _settingsStore = new SettingsStore(Path.Combine(DataFolder, "settings.json"));
        Settings = _settingsStore.Load();

        // Two dictionaries are shipped; the setting chooses one (user decision 1.1).
        UsesWikipediaDictionary = Settings.UseWikipediaDictionary;
        Dictionary = DictionaryFile.Load(DictionaryPath(UsesWikipediaDictionary));
        var blocklist = Blocklist.Load(Path.Combine(AppContext.BaseDirectory, "blocklist.txt"));
        ShippedPhrases = LoadPhrases(Path.Combine(AppContext.BaseDirectory, "phrases.txt"));
        _shippedPhrases[LanguageProfile.Arabic.Id] = ShippedPhrases;
        _corrections = LoadCorrections(Path.Combine(AppContext.BaseDirectory, "corrections.txt"));

        // Learned words: %LOCALAPPDATA%\Itmam\user.db (MSIX redirects it into the package's private folder).
        Store = new SqliteUserWordStore(Path.Combine(DataFolder, "user.db"));
        UserWords = new UserWordIndex();
        Learning = new LearningService(UserWords, Store, blocklist);
        Learning.Load();
        Learning.LearnedPhrasesChanged += UpdatePhrases;

        Engine = new SuggestionEngine(Dictionary, UserWords);
        ExcludedApps = new ExcludedApps(Settings.ExcludedApps);
        var popupState = new PopupState();

        Hooks = new InputHookService(popupState, Settings.Hotkeys);
        UiAutomation = new UiaWorker();
        Pipeline = new InputPipeline(
            Hooks.Events,
            new UiaCaretContextSource(UiAutomation, ExcludedApps.IsExcluded),
            new InputPipelineOptions(),
            Injector,
            new ForegroundGuard(ExcludedApps));

        Popup = new SuggestionPopup(popupState);
        _ = Popup.Handle; // create the (hidden) window now so the first suggestion is not delayed
        Popup.SuggestionClicked += (index, word) => Hooks.Post(InputEvent.Accept(index, word));
        Popup.SuggestionMenuRequested += ShowSuggestionMenu;
        _caret = new CaretLocator(UiAutomation);
        _languages[LanguageProfile.Arabic.Id] = new LanguageSuggestions(Engine, PhraseIndex.Empty, _corrections);
        Suggestions = new SuggestionController(SuggestionsFor, _caret, Popup, () => Hooks.IsEnabled, Learning.SyncRoot);

        // Learning first, so a just-forgotten word is gone before the bar is recomputed. Same thread as the engine.
        Pipeline.UndoAvailableChanged += popupState.SetUndoAccept;
        Pipeline.Updated += OnPipelineUpdated;
        Pipeline.Updated += Suggestions.OnPipelineUpdated;

        Apply(Settings);

        // English has a dictionary too: loaded now when it is on, so the first English word is not slower
        // (otherwise on first use).
        if (Settings.EnabledLanguages.Contains(LanguageProfile.English.Id))
        {
            SuggestionsFor(LanguageProfile.English);
        }

        // Loading leaves tens of MB of temporary buffers: compact once now, before any typing, then
        // avoid blocking full (gen2) collections — a GC pauses the hook thread too.
        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
        GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency;
    }

    /// <summary>
    /// The long phrases shortcut was pressed: (the window the user was typing in, where its caret is).
    /// Raised on the UI thread.
    /// </summary>
    public event Action<nint, Rectangle>? LongPhrasesRequested;

    /// <summary>
    /// The app the user was last working in (executable name), for "Turn off Itmam in …" in the tray
    /// menu; null until known. Updated on the pipeline thread.
    /// </summary>
    public string? LastApp { get; private set; }

    /// <summary>Types text into the focused app (suggestions, long phrases).</summary>
    public ITextInjector Injector { get; } = new SendInputInjector();

    /// <summary>Enabled/disabled changed (tray, the toggle shortcut, pause). Raised on the UI thread.</summary>
    public event Action? EnabledChanged;

    public string DataFolder { get; }

    public AppSettings Settings { get; private set; }

    /// <summary>Set while "Pause for 1 hour" is active.</summary>
    public DateTime? PausedUntil { get; private set; }

    public bool IsEnabled => Hooks.IsEnabled;

    public WordDictionary Dictionary { get; private set; }

    /// <summary>The Wikipedia dictionary is in use (else the basic one).</summary>
    public bool UsesWikipediaDictionary { get; private set; }

    private static string DictionaryPath(bool wikipedia) =>
        Path.Combine(AppContext.BaseDirectory, wikipedia ? "itmam-ar-wikipedia.dict" : "itmam-ar.dict");

    /// <summary>
    /// Switches between the basic and the Wikipedia dictionary. Live: the next keystroke already uses
    /// it. UI thread.
    /// </summary>
    private void UseDictionary(bool wikipedia)
    {
        var dictionary = DictionaryFile.Load(DictionaryPath(wikipedia));
        lock (Learning.SyncRoot)
        {
            Engine = new SuggestionEngine(dictionary, UserWords, Engine.Options);
            _languages.AddOrUpdate(LanguageProfile.Arabic.Id, _ => throw new InvalidOperationException(), (_, arabic) => arabic with { Engine = Engine });
            Dictionary = dictionary;
            UsesWikipediaDictionary = wikipedia;
        }

        GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true); // free the old one
    }

    /// <summary>The Arabic user words (the other languages' are in <see cref="Learning"/>).</summary>
    public UserWordIndex UserWords { get; }

    public LearningService Learning { get; }

    public SqliteUserWordStore Store { get; }

    /// <summary>The dictionaries loaded so far (Arabic, and English once it is on), for the About page.</summary>
    public IReadOnlyList<WordDictionary> LoadedDictionaries =>
        [.. _languages.Values.Select(l => l.Engine.Dictionary).Where(d => d.Count > 0)];

    /// <summary>The Arabic engine.</summary>
    public SuggestionEngine Engine { get; private set; }

    /// <summary>Fixed Arabic expressions shipped in data/phrases.txt (the user's own come from the settings).</summary>
    public PhraseIndex ShippedPhrases { get; }

    public ExcludedApps ExcludedApps { get; }

    public InputHookService Hooks { get; }

    public UiaWorker UiAutomation { get; }

    public InputPipeline Pipeline { get; }

    public SuggestionPopup Popup { get; }

    public SuggestionController Suggestions { get; }

    public void Start()
    {
        Hooks.Start();
        _running = Task.Run(() => Pipeline.RunAsync(_stop.Token));
    }

    /// <summary>The interface language changed (applied at once). Raised on the UI thread.</summary>
    public event Action? LanguageChanged;

    /// <summary>Saves and applies edited settings (Settings window). Call on the UI thread.</summary>
    public void UpdateSettings(AppSettings settings)
    {
        var languageChanged = settings.Language != Settings.Language;
        Settings = settings.Normalized();
        _settingsStore.Save(Settings);
        Apply(Settings);
        if (languageChanged)
        {
            UiCulture.Apply(Settings.Language);
            LanguageChanged?.Invoke();
        }
    }

    /// <summary>
    /// While a shortcut is being recorded in Settings, Itmam's own shortcuts are off (virtual key 0
    /// never arrives), so pressing e.g. Ctrl+Alt reaches the settings window instead of toggling Itmam.
    /// </summary>
    public void SuspendHotkeys(bool suspend) => Hooks.Hotkeys = suspend
        ? new HotkeySettings { AcceptFirst = default, Dismiss = default, ToggleEnabled = default, OpenLongPhrases = default, NativeNavigation = false }
        : Settings.Hotkeys;

    /// <summary>Tray "Enabled" / the toggle shortcut: remembered across restarts.</summary>
    public void SetEnabled(bool enabled)
    {
        PausedUntil = null;
        Hooks.IsEnabled = enabled;
        PersistEnabled();
        EnabledChanged?.Invoke();
    }

    /// <summary>Tray "Pause for 1 hour": not remembered; Itmam is enabled again afterwards or on restart.</summary>
    public void Pause(TimeSpan duration)
    {
        PausedUntil = DateTime.Now + duration;
        Hooks.IsEnabled = false;
        EnabledChanged?.Invoke();
    }

    /// <summary>Called by the tray's timer when the pause has elapsed.</summary>
    public void EndPauseIfDue()
    {
        if (PausedUntil is { } until && DateTime.Now >= until)
        {
            SetEnabled(true);
        }
    }

    /// <summary>Unhook, save learned words, release everything. Safe to call more than once (Exit, log-off).</summary>
    public void Shutdown()
    {
        if (Interlocked.Exchange(ref _shutDown, 1) != 0)
        {
            return;
        }

        Hooks.Dispose();                 // no more keystrokes are seen or swallowed
        _stop.Cancel();
        _running?.Wait(TimeSpan.FromSeconds(1));

        // The word finished last is still pending (it could have been deleted); on exit it is final.
        var pendingLanguage = Pipeline.PendingLanguage;
        if (Pipeline.TakePendingWord() is { } word)
        {
            Learning.Learn(word, pendingLanguage);
        }

        if (Pipeline.TakePendingPhrases() is { } phrases)
        {
            Learning.LearnPhrases(phrases, pendingLanguage);
        }

        Learning.Dispose();              // flushes to disk
        Store.Dispose();
        Suggestions.Dispose();
        UiAutomation.Dispose();
        Popup.Dispose();
    }

    public void Dispose() => Shutdown();

    // Right-click on the bar (user decision 1.1): a strip above the clicked suggestion shows what applies
    // to it. For a phrase the bar shows only its rest, so the actions use the whole phrase it comes from.
    // "Delete" removes the phrase from wherever it comes from — learned (learned again if typed again),
    // "My phrases", the built-in list (hidden, restorable in Settings) — since one of them alone would
    // leave it in the bar. A single word: delete it if learned, block it (never suggested again). UI thread.
    private void ShowSuggestionMenu(int index, string text)
    {
        var actions = new List<(string, Action)>();
        var language = Suggestions.ShownLanguage;
        if (text.Contains(' ', StringComparison.Ordinal) && !Settings.TextShortcuts.Any(s => s.Text == text))
        {
            var whole = Suggestions.PhraseSources.GetValueOrDefault(text) ?? text;
            var key = LearningService.PhraseKey(whole, language);
            var mine = Settings.MyPhrases.Where(p => LearningService.PhraseKey(p, language) == key).ToList();
            var learned = Learning.IsLearnedPhrase(whole, language);
            var shipped = _shippedPhrases.TryGetValue(language.Id, out var builtIn) && builtIn.Contains(whole);
            if (learned || mine.Count > 0 || shipped)
            {
                actions.Add((Resources.Strings.BarMenuDelete, () =>
                {
                    if (learned)
                    {
                        Hooks.Post(InputEvent.Forget(index, whole));
                    }

                    if (mine.Count > 0 || shipped)
                    {
                        UpdateSettings(Settings with
                        {
                            MyPhrases = [.. Settings.MyPhrases.Except(mine)],
                            HiddenPhrases = shipped ? [.. Settings.HiddenPhrases, whole] : Settings.HiddenPhrases,
                        });
                    }
                }));
            }

            if (mine.Count == 0 && PhraseIndex.CleanAny(whole) is { } phrase)
            {
                actions.Add((Resources.Strings.BarMenuAddToMyPhrases,
                    () => UpdateSettings(Settings with { MyPhrases = [.. Settings.MyPhrases, phrase] })));
            }
        }
        else if (!text.Contains(' ', StringComparison.Ordinal))
        {
            if (Learning.IsLearnedWord(text, language))
            {
                actions.Add((Resources.Strings.BarMenuDelete, () => Hooks.Post(InputEvent.Forget(index, text))));
            }

            // User decision: block from the bar, without a confirmation (unblock in "My words").
            actions.Add((Resources.Strings.BarMenuBlock, () =>
            {
                Learning.SetBlocked(text, blocked: true, language);
                Popup.HideSuggestions(); // the next key shows the bar without it
            }));
        }

        Popup.ShowActions(index, actions);
    }

    // Finds the caret of the app the shortcut was pressed in, then opens the list there (UI thread).
    private async Task OpenLongPhrasesAsync(nint target)
    {
        Rectangle near;
        try
        {
            near = (await _caret.LocateAsync(Pipeline.Tracker.Buffer.Language.RightToLeft, CancellationToken.None).ConfigureAwait(false)).Bounds;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException or TimeoutException or OperationCanceledException)
        {
            near = new Rectangle(Cursor.Position, Size.Empty);
        }

        _ui.Post(_ => LongPhrasesRequested?.Invoke(target, near), null);
    }

    // A missing or unreadable list only turns the corrections off.
    private static Corrections LoadCorrections(string path, LanguageProfile? language = null)
    {
        try
        {
            using var reader = File.OpenText(path);
            return Corrections.Read(reader, language);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Corrections.Empty;
        }
    }

    // A missing or unreadable list only turns the shipped phrases off; the user's own still work.
    private static PhraseIndex LoadPhrases(string path, LanguageProfile? language = null)
    {
        try
        {
            using var reader = File.OpenText(path);
            return PhraseIndex.Read(reader, language);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return PhraseIndex.Empty;
        }
    }

    private void Apply(AppSettings settings)
    {
        Hooks.Hotkeys = settings.Hotkeys;
        if (settings.UseWikipediaDictionary != UsesWikipediaDictionary)
        {
            UseDictionary(settings.UseWikipediaDictionary);
        }

        if (PausedUntil is null && Hooks.IsEnabled != settings.Enabled)
        {
            Hooks.IsEnabled = settings.Enabled;
        }

        var options = Engine.Options with
        {
            MaxSuggestions = settings.MaxSuggestions,
            MinChars = settings.MinLetters,
            MinUsesToSuggest = settings.MinUsesToSuggest,
        };
        foreach (var language in _languages.Values)
        {
            language.Engine.Options = options;
        }

        Pipeline.Tracker.EnabledLanguages = settings.EnabledLanguages.ToHashSet(StringComparer.Ordinal);
        Pipeline.Options = Pipeline.Options with { AppendSpaceOnAccept = settings.AddSpaceAfterAccept };
        Learning.IsPaused = settings.LearningPaused;
        Learning.LearnsPhrases = settings.LearnPhrases;
        ExcludedApps.Replace(settings.ExcludedApps);
        Popup.Appearance = PopupAppearance.From(settings);
        UpdatePhrases();
        Suggestions.Shortcuts = new TextShortcuts(settings.TextShortcuts);
    }

    // What a language suggests from, built the first time it is typed (pipeline thread). English has its
    // shipped dictionary, blocklist and common mistakes; the other languages only the user's words.
    private LanguageSuggestions SuggestionsFor(LanguageProfile language) =>
        _languages.GetOrAdd(language.Id, _ =>
        {
            var english = language == LanguageProfile.English;
            var dictionary = english ? LoadDictionary("itmam-en.dict", language) : WordDictionary.Build([], language: language);
            var blocklist = english ? LoadBlocklist("blocklist.en.txt", language) : null;
            var corrections = english ? LoadCorrections(Path.Combine(AppContext.BaseDirectory, "corrections.en.txt"), language) : Corrections.Empty;
            if (english)
            {
                _shippedPhrases[language.Id] = LoadPhrases(Path.Combine(AppContext.BaseDirectory, "phrases.en.txt"), language);
            }

            var engine = new SuggestionEngine(dictionary, Learning.IndexFor(language, blocklist), Engine.Options);
            return new LanguageSuggestions(engine, Phrases(language, Settings), corrections);
        });

    // A missing or damaged file only leaves the language with the user's own words.
    private static WordDictionary LoadDictionary(string file, LanguageProfile language)
    {
        try
        {
            return DictionaryFile.Load(Path.Combine(AppContext.BaseDirectory, file));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return WordDictionary.Build([], language: language);
        }
    }

    private static Blocklist? LoadBlocklist(string file, LanguageProfile language)
    {
        try
        {
            return Blocklist.Load(Path.Combine(AppContext.BaseDirectory, file), language);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    // Rebuilt when a phrase is learned or forgotten (pipeline thread, before the bar is recomputed) or the
    // settings change.
    private void UpdatePhrases()
    {
        var settings = Settings;
        foreach (var (id, language) in _languages)
        {
            _languages[id] = language with { Phrases = Phrases(language.Language, settings) };
        }
    }

    // "My phrases" first, then the learned ones (most used first), then the shipped list (Arabic, English). My
    // phrases hold every language: a language keeps the ones made of its words.
    private PhraseIndex Phrases(LanguageProfile language, AppSettings settings)
    {
        if (!settings.SuggestPhrases)
        {
            return PhraseIndex.Empty;
        }

        var shipped = _shippedPhrases.TryGetValue(language.Id, out var builtIn)
            ? builtIn.Without(settings.HiddenPhrases)
            : PhraseIndex.Read(TextReader.Null, language);
        return shipped.With([.. settings.MyPhrases, .. Learning.LearnedPhrases(language: language).Select(p => p.Text)]);
    }

    private void PersistEnabled()
    {
        if (Settings.Enabled != Hooks.IsEnabled)
        {
            Settings = Settings with { Enabled = Hooks.IsEnabled };
            _settingsStore.Save(Settings);
        }
    }

    // Pipeline thread.
    private void OnPipelineUpdated(PipelineUpdate update)
    {
        if (update.WordToLearn is { } word)
        {
            Learning.Learn(word, update.LearnLanguage);
        }

        if (update.PhrasesToLearn is { } phrases)
        {
            Learning.LearnPhrases(phrases, update.LearnLanguage);
        }

        if (update.Usage is { } usage)
        {
            Learning.RecordUsage(usage.Completions, usage.Characters);
        }

        if (update.Input?.Input.Kind is InputEventKind.ForegroundChanged or InputEventKind.FocusChanged || (LastApp is null && update.Input is not null))
        {
            LastApp = ProcessNames.ForegroundAppName() ?? LastApp;
        }

        if (update.Input?.Input.Kind == InputEventKind.OpenLongPhrases)
        {
            _ = OpenLongPhrasesAsync(PopupWindows.Foreground());
        }

        // Right-click / Ctrl+Delete: forget the learned word or phrase (learned again by using it).
        if (update.Input?.Input is { Kind: InputEventKind.ForgetSuggestion, Text: { } forgotten })
        {
            if (forgotten.Contains(' ', StringComparison.Ordinal))
            {
                Learning.ForgetPhrase(forgotten, update.Language);
            }
            else
            {
                Learning.Remove(forgotten, update.Language);
            }
        }

        // The toggle shortcut (or the tray) toggled Itmam: keep the tray and settings in sync.
        if (update.Input?.Input.Kind == InputEventKind.ToggleEnabled)
        {
            _ui.Post(_ =>
            {
                if (Hooks.IsEnabled)
                {
                    PausedUntil = null;
                }

                if (PausedUntil is null)
                {
                    PersistEnabled();
                }

                EnabledChanged?.Invoke();
            }, null);
        }
    }
}
