using System.Runtime;
using Itmam.App.Debugging;
using Itmam.App.Suggestions;
using Itmam.Core.Dictionary;
using Itmam.Core.Input;
using Itmam.Core.Learning;
using Itmam.Core.Presentation;
using Itmam.Core.Suggestions;
using Itmam.Win32;

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

        // Avoid blocking full (gen2) collections while the user types: a GC pauses the hook thread too.
        GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency;

        // Defaults until the settings window exists (Phase 6); learned words arrive in Phase 5.
        var excludedApps = new ExcludedApps();
        var hotkeys = new HotkeySettings();
        var dictionary = DictionaryFile.Load(Path.Combine(AppContext.BaseDirectory, "itmam-ar.dict"));
        var engine = new SuggestionEngine(dictionary, new UserWordIndex());
        var popupState = new PopupState();

        using var hooks = new InputHookService(popupState, hotkeys);
        using var uiAutomation = new UiaWorker();
        var caretContext = new UiaCaretContextSource(uiAutomation, excludedApps.IsExcluded);
        var pipeline = new InputPipeline(
            hooks.Events,
            caretContext,
            new InputPipelineOptions { CanReadContext = () => !excludedApps.IsExcluded(ProcessNames.ForegroundProcessName()) },
            new SendInputInjector());

        using var popup = new SuggestionPopup(popupState);
        _ = popup.Handle; // create the (hidden) window now so the first suggestion is not delayed
        popup.SuggestionClicked += (index, word) => hooks.Post(InputEvent.Accept(index, word));
        using var suggestions = new SuggestionController(engine, new CaretLocator(uiAutomation), excludedApps, popup, () => hooks.IsEnabled);
        pipeline.Updated += suggestions.OnPipelineUpdated;

        // Phase 2–4: hooks + temporary debug window. The tray ApplicationContext replaces it in Phase 6.
        using var debug = new DebugWindow(hooks, pipeline, suggestions);
        hooks.Start();
        using var stop = new CancellationTokenSource();
        _ = Task.Run(() => pipeline.RunAsync(stop.Token));
        Application.Run(debug);
        stop.Cancel();
    }
}
