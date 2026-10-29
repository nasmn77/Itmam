using System.Runtime;
using Itmam.App.Suggestions;
using Itmam.Core.Dictionary;
using Itmam.Core.Dictionary.Building;
using Itmam.Core.Input;
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
    private Task? _running;
    private int _shutDown;

    /// <param name="ui">The WinForms UI thread context (events are raised there).</param>
    public ItmamApp(SynchronizationContext ui)
    {
        _ui = ui;
        DataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Itmam");
        _settingsStore = new SettingsStore(Path.Combine(DataFolder, "settings.json"));
        Settings = _settingsStore.Load();

        Dictionary = DictionaryFile.Load(Path.Combine(AppContext.BaseDirectory, "itmam-ar.dict"));
        var blocklist = Blocklist.Load(Path.Combine(AppContext.BaseDirectory, "blocklist.txt"));
        ShippedPhrases = LoadPhrases(Path.Combine(AppContext.BaseDirectory, "phrases.txt"));

        // Learned words: %LOCALAPPDATA%\Itmam\user.db (MSIX redirects it into the package's private folder).
        Store = new SqliteUserWordStore(Path.Combine(DataFolder, "user.db"));
        UserWords = new UserWordIndex();
        Learning = new LearningService(UserWords, Store, blocklist);
        Learning.Load();

        Engine = new SuggestionEngine(Dictionary, UserWords);
        ExcludedApps = new ExcludedApps(Settings.ExcludedApps);
        var popupState = new PopupState();

        Hooks = new InputHookService(popupState, Settings.Hotkeys);
        UiAutomation = new UiaWorker();
        Pipeline = new InputPipeline(
            Hooks.Events,
            new UiaCaretContextSource(UiAutomation, ExcludedApps.IsExcluded),
            new InputPipelineOptions(),
            new SendInputInjector(),
            new ForegroundGuard(ExcludedApps));

        Popup = new SuggestionPopup(popupState);
        _ = Popup.Handle; // create the (hidden) window now so the first suggestion is not delayed
        Popup.SuggestionClicked += (index, word) => Hooks.Post(InputEvent.Accept(index, word));
        Popup.SuggestionForgetRequested += (index, word) => Hooks.Post(InputEvent.Forget(index, word));
        Suggestions = new SuggestionController(Engine, new CaretLocator(UiAutomation), Popup, () => Hooks.IsEnabled, Learning.SyncRoot);

        // Learning first, so a just-forgotten word is gone before the bar is recomputed. Same thread as the engine.
        Pipeline.Updated += OnPipelineUpdated;
        Pipeline.Updated += Suggestions.OnPipelineUpdated;

        Apply(Settings);

        // Loading leaves tens of MB of temporary buffers: compact once now, before any typing, then
        // avoid blocking full (gen2) collections — a GC pauses the hook thread too.
        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
        GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency;
    }

    /// <summary>Enabled/disabled changed (tray, the toggle shortcut, pause). Raised on the UI thread.</summary>
    public event Action? EnabledChanged;

    public string DataFolder { get; }

    public AppSettings Settings { get; private set; }

    /// <summary>Set while "Pause for 1 hour" is active.</summary>
    public DateTime? PausedUntil { get; private set; }

    public bool IsEnabled => Hooks.IsEnabled;

    public WordDictionary Dictionary { get; }

    public UserWordIndex UserWords { get; }

    public LearningService Learning { get; }

    public SqliteUserWordStore Store { get; }

    public SuggestionEngine Engine { get; }

    /// <summary>Fixed expressions shipped in data/phrases.txt (the user's own come from the settings).</summary>
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
        ? new HotkeySettings { AcceptFirst = default, Dismiss = default, ToggleEnabled = default, NativeNavigation = false }
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
        if (Pipeline.TakePendingWord() is { } word)
        {
            Learning.Learn(word);
        }

        Learning.Dispose();              // flushes to disk
        Store.Dispose();
        Suggestions.Dispose();
        UiAutomation.Dispose();
        Popup.Dispose();
    }

    public void Dispose() => Shutdown();

    // A missing or unreadable list only turns the shipped phrases off; the user's own still work.
    private static PhraseIndex LoadPhrases(string path)
    {
        try
        {
            using var reader = File.OpenText(path);
            return PhraseIndex.Read(reader);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return PhraseIndex.Empty;
        }
    }

    private void Apply(AppSettings settings)
    {
        Hooks.Hotkeys = settings.Hotkeys;
        if (PausedUntil is null && Hooks.IsEnabled != settings.Enabled)
        {
            Hooks.IsEnabled = settings.Enabled;
        }

        Engine.Options = Engine.Options with
        {
            MaxSuggestions = settings.MaxSuggestions,
            MinChars = settings.MinLetters,
            MinUsesToSuggest = settings.MinUsesToSuggest,
        };
        Pipeline.Options = Pipeline.Options with { AppendSpaceOnAccept = settings.AddSpaceAfterAccept };
        Learning.IsPaused = settings.LearningPaused;
        ExcludedApps.Replace(settings.ExcludedApps);
        Popup.Appearance = PopupAppearance.From(settings);
        Suggestions.Phrases = settings.SuggestPhrases ? ShippedPhrases.With(settings.MyPhrases) : PhraseIndex.Empty;
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
            Learning.Learn(word);
        }

        // Right-click / Ctrl+Delete: forget the learned word (it can be learned again by using it).
        if (update.Input?.Input is { Kind: InputEventKind.ForgetSuggestion, Text: { } forgotten })
        {
            Learning.Remove(forgotten);
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
