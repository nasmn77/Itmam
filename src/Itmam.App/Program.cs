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

        // Caret context length in the focused field (web editors), logged to %TEMP%\itmam-context.txt: --demo-context
        if (args is ["--demo-context", ..])
        {
            Debugging.PopupDemo.ReadContext();
            return;
        }

        // Store screenshots: real suggestions at the foreground window's caret: --demo-caret <typed> [seconds]
        if (args is ["--demo-caret", var typed, ..])
        {
            Debugging.PopupDemo.RunAtCaret(typed, args is [_, _, var s, ..] ? int.Parse(s, CultureInfo.InvariantCulture) : 6);
            return;
        }

        // Visual check of the long phrases list with sample phrases (nothing is typed): --demo-longphrases [en]
        if (args is ["--demo-longphrases", ..])
        {
            var english = args.Contains("en");
            UiCulture.Apply(english ? UiLanguage.English : UiLanguage.Arabic);
            Core.Suggestions.LongPhrase[] samples = english
                ?
                [
                    new("Welcome", "Dear customer,\nThank you for contacting technical support.\nHow can we help you?"),
                    new("Password reset", "To reset your password:\n1. Open the sign-in page and choose \"Forgot password\".\n2. Enter your email address.\n3. Follow the link we send you."),
                    new("Ask for the order number", "Could you send us your order number so we can check its status?"),
                    new("Close the ticket", "The issue is solved and the ticket is closed.\nDo not hesitate to contact us again.\nBest regards"),
                ]
                :
                [
                    new("ترحيب بالعميل", "عزيزي العميل،\nشكرًا لتواصلك مع الدعم الفني.\nكيف يمكننا مساعدتك؟"),
                    new("استعادة كلمة المرور", "لاستعادة كلمة المرور:\n١. افتح صفحة الدخول واختر «نسيت كلمة المرور».\n٢. اكتب بريدك الإلكتروني.\n٣. اتبع الرابط الذي يصلك."),
                    new("طلب رقم الطلب", "نرجو تزويدنا برقم الطلب لنتابع حالته."),
                    new("إغلاق التذكرة", "تم حل المشكلة وإغلاق التذكرة.\nلا تتردد في التواصل معنا.\nمع التحية"),
                ];
            using var picker = new SettingsUi.LongPhrasePicker(samples, 0, new SendInputInjector(), SettingsUi.SettingsPalette.For(ThemeChoice.System),
                !english, new Rectangle(Screen.PrimaryScreen!.WorkingArea.Width / 2, 200, 2, 20));
            Application.Run(picker);
            return;
        }

        // Visual check of the settings window (no hooks installed): --demo-settings [general|appearance|shortcuts|excluded|words|phrases|textshortcuts|longphrases|updates|about|exit] [en] [end]
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
                : args.Contains("phrases") ? SettingsPage.Phrases
                : args.Contains("textshortcuts") ? SettingsPage.TextShortcuts
                : args.Contains("longphrases") ? SettingsPage.LongPhrases
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
#if DEBUG
        // Test of the long phrases path without a key press (injected keys are ignored): --test-longphrases
        if (args.Contains("--test-longphrases"))
        {
            _ = Task.Delay(3000).ContinueWith(_ => app.Hooks.Post(Core.Input.InputEvent.Of(Core.Input.InputEventKind.OpenLongPhrases)), TaskScheduler.Default);
        }
#endif
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
