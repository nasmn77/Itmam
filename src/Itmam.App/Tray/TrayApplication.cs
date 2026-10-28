using System.Globalization;
using Itmam.App.Resources;
using Itmam.App.SettingsUi;
using Itmam.Core.Settings;
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
    private readonly ToolStripMenuItem _settingsItem = new();
    private readonly ToolStripMenuItem _aboutItem = new();
    private readonly ToolStripMenuItem _exitItem = new();
    private readonly System.Windows.Forms.Timer _pauseTimer = new() { Interval = 15_000 };
    private readonly Icon _iconOn;
    private readonly Icon _iconOff;
    private SettingsWindow? _settings;
#if DEBUG
    private readonly ToolStripMenuItem _diagnosticsItem = new();
    private Debugging.DebugWindow? _diagnostics;
#endif
    private bool _exited;

    /// <param name="app">The running Itmam components.</param>
    /// <param name="startedAtSignIn">Started by "Start with Windows" (no first-run notice then).</param>
    public TrayApplication(ItmamApp app, bool startedAtSignIn)
    {
        _app = app;
        var size = SystemInformation.SmallIconSize.Width;
        _iconOn = AppIcon.Create(size, enabled: true);
        _iconOff = AppIcon.Create(size, enabled: false);

        _menu = new ContextMenuStrip { ShowImageMargin = false, ShowCheckMargin = true };
        _enabled.Click += (_, _) => _app.SetEnabled(!_app.IsEnabled);
        _pause.Click += (_, _) => TogglePause();
        _settingsItem.Font = new Font(_menu.Font, FontStyle.Bold);
        _settingsItem.Click += (_, _) => ShowSettings(SettingsPage.General);
        _aboutItem.Click += (_, _) => ShowSettings(SettingsPage.About);
        _exitItem.Click += (_, _) => Exit();

        _menu.Items.AddRange([_enabled, _pause, new ToolStripSeparator(), _settingsItem, _aboutItem]);
#if DEBUG
        _diagnosticsItem.Click += (_, _) => ShowDiagnostics();
        _menu.Items.Add(_diagnosticsItem);
#endif
        _menu.Items.AddRange([new ToolStripSeparator(), _exitItem]);

        _icon = new NotifyIcon { ContextMenuStrip = _menu, Visible = true };
        _icon.DoubleClick += (_, _) => ShowSettings(SettingsPage.General);

        _pauseTimer.Tick += (_, _) => _app.EndPauseIfDue();
        _app.EnabledChanged += Refresh;
        _app.LanguageChanged += OnLanguageChanged;
        SystemEvents.SessionEnding += OnSessionEnding;
        ApplyLanguage();

        if (!app.Settings.FirstRunNoticeShown && !startedAtSignIn)
        {
            _icon.ShowBalloonTip(10_000, Strings.FirstRunTitle, Strings.FirstRunText, ToolTipIcon.Info);
            app.UpdateSettings(app.Settings with { FirstRunNoticeShown = true });
        }
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

    // The settings window's exit button: exit after its click has finished (it is disposed by Exit).
    private void ExitLater() => SynchronizationContext.Current?.Post(_ => Exit(), null);

    // Menu texts and direction follow the interface language.
    private void ApplyLanguage()
    {
        _menu.RightToLeft = _app.Settings.Language == UiLanguage.Arabic ? RightToLeft.Yes : RightToLeft.No;
        _enabled.Text = Strings.MenuEnabled;
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
        _enabled.Checked = _app.IsEnabled;
        _enabled.Enabled = paused is null;
        _pause.Text = paused is null ? Strings.MenuPauseHour : Strings.MenuResume;
        _pause.Enabled = paused is not null || _app.IsEnabled;
        _icon.Icon = _app.IsEnabled ? _iconOn : _iconOff;
        _icon.Text = paused is { } until
            ? string.Format(CultureInfo.CurrentCulture, Strings.TrayPausedUntil, until.ToString("t", CultureInfo.CurrentCulture))
            : _app.IsEnabled ? Strings.TrayOn : Strings.TrayOff;

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
