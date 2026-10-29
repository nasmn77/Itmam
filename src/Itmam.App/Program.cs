using System.Globalization;
using Itmam.App.SettingsUi;
using Itmam.App.Tray;
using Itmam.Core.Settings;
using Itmam.Win32;

namespace Itmam.App;

internal static class Program
{
    // Per sign-in session: each user on a shared PC can run their own Itmam.
    private const string InstanceMutexName = @"Local\Itmam.SingleInstance";
    private const string ShowSettingsEventName = @"Local\Itmam.ShowSettings";

    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

#if DEBUG
        // Visual check of the bar without typing: --demo-popup <x> <y> [light|dark] [nohighlight] [hints] [navy] [translucent] [large] [hide]
        if (args is ["--demo-popup", var x, var y, ..])
        {
            Debugging.PopupDemo.Run(int.Parse(x, CultureInfo.InvariantCulture), int.Parse(y, CultureInfo.InvariantCulture), args[3..]);
            return;
        }

        // Store screenshots: real suggestions at the foreground window's caret: --demo-caret <typed> [seconds]
        if (args is ["--demo-caret", var typed, ..])
        {
            Debugging.PopupDemo.RunAtCaret(typed, args is [_, _, var s, ..] ? int.Parse(s, CultureInfo.InvariantCulture) : 6);
            return;
        }

        // Visual check of the settings window (no hooks installed): --demo-settings [general|appearance|shortcuts|excluded|words|updates|about|exit] [en] [end]
        if (args is ["--demo-settings", ..])
        {
            SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
            using var demoApp = new ItmamApp(SynchronizationContext.Current!);
            var english = args.Contains("en");
            UiCulture.Apply(english ? UiLanguage.English : UiLanguage.Arabic);
            using var window = new SettingsWindow(demoApp, Application.ExitThread, english ? UiLanguage.English : UiLanguage.Arabic) { TopMost = true };
            window.ShowPage(
                args.Contains("about") ? SettingsPage.About
                : args.Contains("exit") ? SettingsPage.Exit
                : args.Contains("appearance") ? SettingsPage.Appearance
                : args.Contains("shortcuts") ? SettingsPage.Shortcuts
                : args.Contains("excluded") ? SettingsPage.Excluded
                : args.Contains("words") ? SettingsPage.Words
                : args.Contains("updates") ? SettingsPage.Updates
                : SettingsPage.General);
            if (args.Contains("end"))
            {
                window.ScrollToEnd();
            }
            using var timer = new System.Windows.Forms.Timer { Interval = 6000 };
            timer.Tick += (_, _) => Application.ExitThread();
            timer.Start();
            Application.Run();
            return;
        }
#endif

        // Single instance: a second launch asks the running copy to open its Settings, then exits.
        using var instance = new Mutex(initiallyOwned: true, InstanceMutexName, out var isFirst);
        if (!isFirst)
        {
            if (EventWaitHandle.TryOpenExisting(ShowSettingsEventName, out var signal))
            {
                using (signal)
                {
                    WindowChrome.AllowAnyProcessToSetForeground();
                    signal.Set();
                }
            }

            return;
        }

        using var showSettings = new EventWaitHandle(false, EventResetMode.AutoReset, ShowSettingsEventName);
        SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
        var ui = SynchronizationContext.Current!;

        using var app = new ItmamApp(ui);
        UiCulture.Apply(app.Settings.Language);

        using var tray = new TrayApplication(app);
        var wait = ThreadPool.RegisterWaitForSingleObject(
            showSettings,
            (_, _) => ui.Post(_ => tray.ShowSettings(SettingsPage.General), null),
            null,
            Timeout.Infinite,
            executeOnlyOnce: false);

        _ = SyncStartupAsync(app);
        app.Start();
        Application.Run(tray);

        wait.Unregister(null);
        app.Shutdown();
        instance.ReleaseMutex();
    }

    // Installed from the Store, "start with Windows" follows the setting (on by default).
    // Unpackaged development builds are never registered automatically: only from Settings.
    private static async Task SyncStartupAsync(ItmamApp app)
    {
        if (!StartupRegistration.IsPackaged)
        {
            return;
        }

        try
        {
            if (await StartupRegistration.IsEnabledAsync() != app.Settings.StartWithWindows)
            {
                var actual = await StartupRegistration.SetEnabledAsync(app.Settings.StartWithWindows);
                app.UpdateSettings(app.Settings with { StartWithWindows = actual });
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException or ArgumentException)
        {
            // The StartupTask is missing from the manifest or blocked by policy: keep running without it.
        }
    }
}
