using System.Runtime;
using Itmam.App.Debugging;
using Itmam.App.Suggestions;
using Itmam.Core.Dictionary;
using Itmam.Core.Dictionary.Building;
using Itmam.Core.Input;
using Itmam.Core.Learning;
using Itmam.Core.Presentation;
using Itmam.Core.Suggestions;
using Itmam.Win32;
using Microsoft.Win32;

namespace Itmam.App;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

#if DEBUG
        // Visual check of the bar without typing: --demo-popup <x> <y> [light|dark] [nohighlight] [hints] [hide]
        if (args is ["--demo-popup", var x, var y, ..])
        {
            PopupDemo.Run(int.Parse(x, System.Globalization.CultureInfo.InvariantCulture), int.Parse(y, System.Globalization.CultureInfo.InvariantCulture), args[3..]);
            return;
        }
#endif

        // Defaults until the settings window exists (Phase 6).
        var excludedApps = new ExcludedApps();
        var hotkeys = new HotkeySettings();
        var dictionary = DictionaryFile.Load(Path.Combine(AppContext.BaseDirectory, "itmam-ar.dict"));
        var blocklist = Blocklist.Load(Path.Combine(AppContext.BaseDirectory, "blocklist.txt"));

        // Learned words: %LOCALAPPDATA%\Itmam\user.db (MSIX redirects it into the package's private folder).
        var dataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Itmam");
        using var store = new SqliteUserWordStore(Path.Combine(dataFolder, "user.db"));
        var userWords = new UserWordIndex();
        using var learning = new LearningService(userWords, store, blocklist);
        learning.Load();

        var engine = new SuggestionEngine(dictionary, userWords);
        var popupState = new PopupState();

        // Loading the dictionary leaves ~tens of MB of temporary buffers: compact once now, before any
        // typing, then avoid blocking full (gen2) collections — a GC pauses the hook thread too.
        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
        GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency;

        using var hooks = new InputHookService(popupState, hotkeys);
        using var uiAutomation = new UiaWorker();
        var caretContext = new UiaCaretContextSource(uiAutomation, excludedApps.IsExcluded);
        var pipeline = new InputPipeline(hooks.Events, caretContext, new InputPipelineOptions(), new SendInputInjector(), new ForegroundGuard(excludedApps));

        using var popup = new SuggestionPopup(popupState);
        _ = popup.Handle; // create the (hidden) window now so the first suggestion is not delayed
        popup.SuggestionClicked += (index, word) => hooks.Post(InputEvent.Accept(index, word));
        popup.SuggestionBlockRequested += (index, word) => hooks.Post(InputEvent.Block(index, word));
        using var suggestions = new SuggestionController(engine, new CaretLocator(uiAutomation), popup, () => hooks.IsEnabled);

        // Learning first, so a just-blocked word is gone before the bar is recomputed. Same thread as the engine.
        pipeline.Updated += update => OnPipelineUpdated(update, learning);
        pipeline.Updated += suggestions.OnPipelineUpdated;

        // Phase 2–5: hooks + temporary debug window. The tray ApplicationContext replaces it in Phase 6.
        using var debug = new DebugWindow(hooks, pipeline, suggestions, store.RecoveredBackupPath, () => userWords.Count);
        hooks.Start();
        using var stop = new CancellationTokenSource();
        var running = Task.Run(() => pipeline.RunAsync(stop.Token));

        // Log-off/shutdown: save everything (the process may not get to the end of Main).
        SystemEvents.SessionEnding += (_, _) => SaveOnExit(pipeline, learning);
        Application.Run(debug);
        stop.Cancel();
        running.Wait(TimeSpan.FromSeconds(1));
        SaveOnExit(pipeline, learning);
    }

    private static void OnPipelineUpdated(PipelineUpdate update, LearningService learning)
    {
        if (update.WordToLearn is { } word)
        {
            learning.Learn(word);
        }

        if (update.Input?.Input is { Kind: InputEventKind.BlockSuggestion, Text: { } blocked })
        {
            learning.SetBlocked(blocked, blocked: true);
        }
    }

    // The word finished last is still pending (it could have been deleted); on exit it is final.
    private static void SaveOnExit(InputPipeline pipeline, LearningService learning)
    {
        if (pipeline.TakePendingWord() is { } word)
        {
            learning.Learn(word);
        }

        learning.Flush();
    }
}
