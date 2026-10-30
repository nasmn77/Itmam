using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using Itmam.App.Resources;
using Itmam.App.SettingsUi;
using Itmam.Core.Input;
using Itmam.Core.Settings;
using Itmam.Win32;
using Microsoft.Win32;

namespace Itmam.App.Tray;

/// <summary>
/// The app as the user sees it: no main window, no taskbar button, only an icon in the
/// notification area with a menu (Enable · Pause for 1 hour · Settings · About · Exit).
/// Double-clicking the icon opens Settings. Exit (or log-off) shuts down cleanly.
/// </summary>
/// <remarks>
/// WinForms' NotifyIcon re-adds itself when Explorer restarts (it handles the TaskbarCreated
/// message), so the icon survives Explorer crashes.
/// </remarks>
internal sealed class TrayApplication : ApplicationContext
{
    private readonly ItmamApp _app;
    private readonly NotifyIcon _icon;
    private readonly ContextMenuStrip _menu;
    private readonly ToolStripMenuItem _enabled = new();
    private readonly ToolStripMenuItem _pause = new();
    private readonly ToolStripMenuItem _excludeApp = new();
    private readonly ToolStripMenuItem _settingsItem = new();
    private readonly ToolStripMenuItem _aboutItem = new();
    private readonly ToolStripMenuItem _exitItem = new();
    private readonly System.Windows.Forms.Timer _pauseTimer = new() { Interval = 15_000 };
    private readonly Icon _iconOn;
    private readonly Icon _iconOff;
    private SettingsWindow? _settings;
    private LongPhrasePicker? _longPhrases;
#if DEBUG
    private readonly ToolStripMenuItem _diagnosticsItem = new();
    private Debugging.DebugWindow? _diagnostics;
#endif
    private bool _exited;
    private bool _wasEnabled;
    private DateTime? _wasPausedUntil;
    private Action? _balloonClick; // what clicking the notification currently shown does

    /// <param name="app">The running Itmam components.</param>
    public TrayApplication(ItmamApp app)
    {
        _app = app;
        var size = SystemInformation.SmallIconSize.Width;
        _iconOn = AppIcon.Create(size, enabled: true);
        _iconOff = AppIcon.Create(size, enabled: false);

        _menu = new ContextMenuStrip { ShowImageMargin = false, ShowCheckMargin = false };
        _enabled.Click += (_, _) => _app.SetEnabled(!_app.IsEnabled);
        _pause.Click += (_, _) => TogglePause();
        _excludeApp.Click += (_, _) => ToggleLastAppExcluded();
        _menu.Opening += (_, _) => RefreshExcludeApp();
        _settingsItem.Font = new Font(_menu.Font, FontStyle.Bold);
        _settingsItem.Click += (_, _) => ShowSettings(SettingsPage.General);
        _aboutItem.Click += (_, _) => ShowSettings(SettingsPage.About);
        _exitItem.Click += (_, _) => ConfirmAndExit();

        _menu.Items.AddRange([_enabled, _pause, _excludeApp, new ToolStripSeparator(), _settingsItem, _aboutItem]);
#if DEBUG
        _diagnosticsItem.Click += (_, _) => ShowDiagnostics();
        _menu.Items.Add(_diagnosticsItem);
#endif
        _menu.Items.AddRange([new ToolStripSeparator(), _exitItem]);

        _icon = new NotifyIcon { ContextMenuStrip = _menu, Visible = true };
        _icon.MouseUp += (_, e) => ShowMenuOnLeftClick(e); // user decision: either button opens the menu
        _icon.BalloonTipClicked += (_, _) => Interlocked.Exchange(ref _balloonClick, null)?.Invoke();
        _icon.BalloonTipClosed += (_, _) => _balloonClick = null;

        _wasEnabled = _app.IsEnabled;
        _pauseTimer.Tick += (_, _) => _app.EndPauseIfDue();
        _app.EnabledChanged += OnEnabledChanged;
        _app.LongPhrasesRequested += ShowLongPhrases;
        _app.LanguageChanged += OnLanguageChanged;
        SystemEvents.SessionEnding += OnSessionEnding;
        ApplyLanguage();

        // User decision: confirm on every start (Itmam has no window), including start with Windows.
        ShowRunningNoticeSoon();
        CheckForUpdatesSoon();
    }

    /// <summary>Itmam's page in the Microsoft Store (update notification, Settings → Updates).</summary>
    public static void OpenStorePage() =>
        Process.Start(new ProcessStartInfo(StoreUpdates.StorePage.ToString()) { UseShellExecute = true });

    // User decision: a notification when Itmam is turned off, paused or resumed (tray, Ctrl+Alt, end of a pause).
    private void OnEnabledChanged()
    {
        Refresh();
        var enabled = _app.IsEnabled;
        var pausedUntil = _app.PausedUntil;
        if (enabled == _wasEnabled && pausedUntil == _wasPausedUntil)
        {
            return;
        }

        _wasEnabled = enabled;
        _wasPausedUntil = pausedUntil;
        if (!_app.Settings.NotifyOnToggle || _exited)
        {
            return;
        }

        var (title, text) = enabled
            ? (Strings.NotifyOnTitle, Strings.NotifyOnText)
            : pausedUntil is { } until
                ? (Strings.NotifyPausedTitle, string.Format(CultureInfo.CurrentCulture, Strings.NotifyPausedText, until.ToString("t", CultureInfo.CurrentCulture)))
                : (Strings.NotifyOffTitle, string.Format(CultureInfo.CurrentCulture, Strings.NotifyOffText, "‪" + HotkeyNames.Format(_app.Settings.Hotkeys.ToggleEnabled) + "‬"));
        ShowNotification(title, text, onClick: null);
    }

    // User decision: ask Microsoft Store once, shortly after start-up (not to slow it down), whether a
    // newer version exists; the notification opens Itmam's Store page.
    private void CheckForUpdatesSoon()
    {
        if (!_app.Settings.CheckForUpdates || !StartupRegistration.IsPackaged)
        {
            return;
        }

        var delay = new System.Windows.Forms.Timer { Interval = 12_000 };
        delay.Tick += async (_, _) =>
        {
            delay.Dispose();
            if (await StoreUpdates.CheckAsync() == UpdateCheck.UpdateAvailable && !_exited)
            {
                ShowNotification(Strings.UpdateTitle, Strings.UpdateText, OpenStorePage);
            }
        };
        delay.Start();
    }

    private void ShowNotification(string title, string text, Action? onClick)
    {
        _balloonClick = onClick;
        _icon.ShowBalloonTip(5_000, title, text, ToolTipIcon.Info);
    }

    // User decision: exiting asks first (tray menu, Settings → Exit). Windows sign-out never asks.
    private void ConfirmAndExit()
    {
        if (_exited)
        {
            return;
        }

        var arabic = _app.Settings.Language == UiLanguage.Arabic;
        var text = string.Format(CultureInfo.CurrentCulture, Strings.ConfirmExit, "‪" + HotkeyNames.Format(_app.Settings.Hotkeys.ToggleEnabled) + "‬");
        var options = arabic ? MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign : 0;

        // From Settings the window owns the question; from the tray (no window) a hidden topmost owner keeps it
        // in front of other apps.
        IWin32Window? settings = _settings is { Visible: true } ? _settings : null;
        using var hidden = settings is null ? new Form { TopMost = true, ShowInTaskbar = false, Opacity = 0, StartPosition = FormStartPosition.CenterScreen } : null;
        hidden?.Show();
        var answer = MessageBox.Show(settings ?? hidden, text, Strings.AppName,
            MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2, options);
        if (answer == DialogResult.Yes)
        {
            Exit();
        }
    }

    // "Turn off Itmam in notepad.exe" / "Turn on Itmam in …" for the app the user was last working in
    // (user decision 1.1). Hidden until that app is known.
    private void RefreshExcludeApp()
    {
        var app = _app.LastApp;
        _excludeApp.Visible = app is not null;
        if (app is not null)
        {
            var excluded = _app.Settings.ExcludedApps.Contains(ExcludedApps.NormalizeName(app), StringComparer.OrdinalIgnoreCase);
            _excludeApp.Text = string.Format(CultureInfo.CurrentCulture, excluded ? Strings.MenuIncludeApp : Strings.MenuExcludeApp, "\u202A" + app + "\u202C");
        }
    }

    // Adds or removes the app in the excluded apps, after a confirmation (as on the Excluded apps page).
    private void ToggleLastAppExcluded()
    {
        if (_app.LastApp is not { } app)
        {
            return;
        }

        var name = ExcludedApps.NormalizeName(app);
        var excluded = _app.Settings.ExcludedApps.Contains(name, StringComparer.OrdinalIgnoreCase);
        var question = string.Format(CultureInfo.CurrentCulture, excluded ? Strings.ConfirmIncludeApp : Strings.ConfirmExcludeApp, "\u202A" + app + "\u202C");
        if (!Ask(question))
        {
            return;
        }

        var settings = _app.Settings;
        _app.UpdateSettings(settings with
        {
            ExcludedApps = excluded ? ExcludedApps.Without(settings.ExcludedApps, name) : ExcludedApps.With(settings.ExcludedApps, name),
        });
    }

    // Yes/No question in front of other apps ("No" is the default).
    private bool Ask(string text)
    {
        var options = _app.Settings.Language == UiLanguage.Arabic ? MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign : 0;
        using var owner = new Form { TopMost = true, ShowInTaskbar = false, Opacity = 0, StartPosition = FormStartPosition.CenterScreen };
        owner.Show();
        return MessageBox.Show(owner, text, Strings.AppName, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2, options) == DialogResult.Yes;
    }

    // The right button opens the menu by itself. NotifyIcon's own (non-public) ShowContextMenu also
    // brings the menu to the foreground so it closes when clicking elsewhere; Show() is the fallback.
    private void ShowMenuOnLeftClick(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        var show = typeof(NotifyIcon).GetMethod("ShowContextMenu", BindingFlags.Instance | BindingFlags.NonPublic, Type.EmptyTypes);
        if (show is not null)
        {
            show.Invoke(_icon, null);
        }
        else
        {
            _menu.Show(Cursor.Position);
        }
    }

    // The long phrases shortcut: one list at a time, near the caret of the app the user is typing in.
    private void ShowLongPhrases(nint target, Rectangle near)
    {
        _longPhrases?.Close();
        var settings = _app.Settings;
        _longPhrases = new LongPhrasePicker(settings.LongPhrases, target, _app.Injector, SettingsPalette.For(settings.Theme),
            settings.Language == UiLanguage.Arabic, near, chars => _app.Learning.RecordUsage(1, chars));
        _longPhrases.FormClosed += (_, _) => _longPhrases = null;
        _longPhrases.Show();
    }

    /// <summary>Opens Settings (also used when a second copy of Itmam is started).</summary>
    public void ShowSettings(SettingsPage page)
    {
        if (_exited)
        {
            return;
        }

        _settings ??= new SettingsWindow(_app, ExitLater);
        _settings.ShowPage(page);
    }

    // "Itmam is running in the tray" notification, on every start. Shown a moment after the icon is added:
    // a notification requested in the same instant the icon appears can be dropped by Windows.
    private void ShowRunningNoticeSoon()
    {
        var delay = new System.Windows.Forms.Timer { Interval = 1500 };
        delay.Tick += (_, _) =>
        {
            delay.Dispose();
            if (_exited)
            {
                return;
            }

            _icon.ShowBalloonTip(10_000, Strings.FirstRunTitle, Strings.FirstRunText, ToolTipIcon.Info);
        };
        delay.Start();
    }

    // The settings window's exit button: ask, then exit after its click has finished (it is disposed by Exit).
    private void ExitLater() => SynchronizationContext.Current?.Post(_ => ConfirmAndExit(), null);

    // Menu texts and direction follow the interface language.
    private void ApplyLanguage()
    {
        _menu.RightToLeft = _app.Settings.Language == UiLanguage.Arabic ? RightToLeft.Yes : RightToLeft.No;
        _settingsItem.Text = Strings.MenuSettings;
        _aboutItem.Text = Strings.MenuAbout;
        _exitItem.Text = Strings.MenuExit;
#if DEBUG
        _diagnosticsItem.Text = Strings.MenuDiagnostics;
#endif
        Refresh();
    }

    // Applied at once: the menu is retitled, and an open settings window is rebuilt on the same page
    // (after the click that changed the language has been handled).
    private void OnLanguageChanged()
    {
        ApplyLanguage();
        if (_settings is { Visible: true } window)
        {
            var page = window.CurrentPage;
            var bounds = window.Bounds;
            SynchronizationContext.Current?.Post(_ =>
            {
                window.Dispose();
                _settings = new SettingsWindow(_app, ExitLater) { StartPosition = FormStartPosition.Manual, Bounds = bounds };
                _settings.ShowPage(page);
            }, null);
        }
        else
        {
            _settings?.Dispose();
            _settings = null;
        }
    }

    /// <summary>Exit: unhook, save, remove the icon, end the message loop. Starting at sign-in is unchanged.</summary>
    public void Exit()
    {
        if (_exited)
        {
            return;
        }

        _exited = true;
        _pauseTimer.Stop();
        SystemEvents.SessionEnding -= OnSessionEnding;
        _icon.Visible = false;          // no ghost icon left in the tray
        _icon.Dispose();
        _settings?.Dispose();
        _app.Shutdown();
        ExitThread();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _pauseTimer.Dispose();
            _menu.Dispose();
            _iconOn.Dispose();
            _iconOff.Dispose();
        }

        base.Dispose(disposing);
    }

    private void TogglePause()
    {
        if (_app.PausedUntil is not null)
        {
            _app.SetEnabled(true);
        }
        else
        {
            _app.Pause(TimeSpan.FromHours(1));
        }
    }

    private void Refresh()
    {
        if (_exited)
        {
            return;
        }

        var paused = _app.PausedUntil;
        // User decision: an explicit "Turn off" / "Turn on" item rather than a checked "Enabled".
        _enabled.Text = _app.IsEnabled ? Strings.MenuTurnOff : Strings.MenuTurnOn;
        _enabled.Enabled = paused is null;
        _pause.Text = paused is null ? Strings.MenuPauseHour : Strings.MenuResume;
        _pause.Enabled = paused is not null || _app.IsEnabled;
        _icon.Icon = _app.IsEnabled ? _iconOn : _iconOff;
        _icon.Text = paused is { } until
            ? string.Format(CultureInfo.CurrentCulture, Strings.TrayPausedUntil, until.ToString("t", CultureInfo.CurrentCulture))
            : _app.IsEnabled ? Strings.TrayOn
            : string.Format(CultureInfo.CurrentCulture, Strings.TrayOff, "‪" + HotkeyNames.Format(_app.Settings.Hotkeys.ToggleEnabled) + "‬");

        if (paused is null)
        {
            _pauseTimer.Stop();
        }
        else
        {
            _pauseTimer.Start();
        }
    }

    // Log-off or shutdown: same clean exit (learned words are saved).
    private void OnSessionEnding(object? sender, SessionEndingEventArgs e) => Exit();

#if DEBUG
    private void ShowDiagnostics()
    {
        if (_diagnostics is null || _diagnostics.IsDisposed)
        {
            _diagnostics = new Debugging.DebugWindow(_app.Hooks, _app.Pipeline, _app.Suggestions, _app.Store.RecoveredBackupPath, () => _app.UserWords.Count);
        }

        _diagnostics.Show();
        _diagnostics.Activate();
    }
#endif
}
