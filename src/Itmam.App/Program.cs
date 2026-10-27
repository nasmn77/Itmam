using System.Runtime;
using Itmam.App.Debugging;
using Itmam.Core.Input;
using Itmam.Win32;

namespace Itmam.App;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        // Avoid blocking full (gen2) collections while the user types: a GC pauses the hook thread too.
        GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency;

        // Phase 2: hooks + temporary debug window. The tray ApplicationContext replaces this in Phase 6.
        // Defaults until the settings window exists (Phase 6).
        var excludedApps = new ExcludedApps();

        using var hooks = new InputHookService();
        using var caretContext = new UiaCaretContextSource(excludedApps.IsExcluded);
        hooks.Start();
        Application.Run(new DebugWindow(hooks, caretContext, excludedApps));
    }
}
