namespace Itmam.App;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        // The tray ApplicationContext, hooks and popup are wired up here in later phases.
    }
}
