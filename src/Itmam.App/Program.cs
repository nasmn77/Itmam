using Itmam.App.Debugging;
using Itmam.Win32;

namespace Itmam.App;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        // Phase 2: hooks + temporary debug window. The tray ApplicationContext replaces this in Phase 6.
        using var hooks = new InputHookService();
        hooks.Start();
        Application.Run(new DebugWindow(hooks));
    }
}
