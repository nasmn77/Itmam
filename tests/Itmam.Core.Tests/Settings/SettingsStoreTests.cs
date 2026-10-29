using Itmam.Core.Input;
using Itmam.Core.Settings;

namespace Itmam.Core.Tests.Settings;

public sealed class SettingsStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "itmam-tests", Guid.NewGuid().ToString("N"));

    private string SettingsPath => Path.Combine(_directory, "settings.json");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void Missing_file_gives_defaults()
    {
        var settings = new SettingsStore(SettingsPath).Load();

        settings.ShouldBe(new AppSettings() with { ExcludedApps = settings.ExcludedApps });
        settings.Language.ShouldBe(UiLanguage.Arabic);
        settings.MaxSuggestions.ShouldBe(3);
        settings.ExcludedApps.ShouldContain("mstsc.exe");
    }

    [Fact]
    public void Saved_settings_round_trip()
    {
        var store = new SettingsStore(SettingsPath);
        var settings = new AppSettings
        {
            Enabled = false,
            Language = UiLanguage.English,
            MaxSuggestions = 5,
            Theme = ThemeChoice.Dark,
            Hotkeys = new HotkeySettings { NativeNavigation = false, AcceptFirst = new Hotkey(0x27, KeyModifiers.Ctrl) },
            ExcludedApps = ["game.exe"],
        };

        store.Save(settings);
        var loaded = new SettingsStore(SettingsPath).Load();

        loaded.Enabled.ShouldBeFalse();
        loaded.Language.ShouldBe(UiLanguage.English);
        loaded.MaxSuggestions.ShouldBe(5);
        loaded.Theme.ShouldBe(ThemeChoice.Dark);
        loaded.Hotkeys.ShouldBe(settings.Hotkeys);
        loaded.ExcludedApps.ShouldBe(["game.exe"]);
    }

    [Fact]
    public void Saving_leaves_no_temporary_file()
    {
        new SettingsStore(SettingsPath).Save(new AppSettings());

        Directory.GetFiles(_directory).Select(Path.GetFileName).ShouldBe(["settings.json"]);
    }

    [Fact]
    public void Corrupt_file_is_kept_and_defaults_are_used()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(SettingsPath, "{ this is not json");
        var store = new SettingsStore(SettingsPath);

        var settings = store.Load();

        settings.MaxSuggestions.ShouldBe(3);
        store.RecoveredBackupPath.ShouldNotBeNull();
        File.Exists(store.RecoveredBackupPath).ShouldBeTrue();
    }

    [Fact]
    public void Out_of_range_values_are_clamped()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(SettingsPath, """{ "MaxSuggestions": 50, "MinLetters": 0, "MinUsesToSuggest": 9 }""");

        var settings = new SettingsStore(SettingsPath).Load();

        settings.MaxSuggestions.ShouldBe(5);
        settings.MinLetters.ShouldBe(1);
        settings.MinUsesToSuggest.ShouldBe(5);
    }

    [Fact]
    public void Older_files_without_new_settings_get_defaults_for_them()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(SettingsPath, """{ "Enabled": false }""");

        var settings = new SettingsStore(SettingsPath).Load();

        settings.Enabled.ShouldBeFalse();
        settings.AddSpaceAfterAccept.ShouldBeTrue();
        settings.Hotkeys.ShouldBe(new HotkeySettings());
        settings.CheckForUpdates.ShouldBeTrue();  // added in 1.1: on for people updating from 1.0
        settings.NotifyOnToggle.ShouldBeTrue();
    }

    [Fact]
    public void Update_check_and_toggle_notifications_can_be_turned_off()
    {
        var store = new SettingsStore(SettingsPath);
        store.Save(new AppSettings { CheckForUpdates = false, NotifyOnToggle = false });

        var loaded = new SettingsStore(SettingsPath).Load();

        loaded.CheckForUpdates.ShouldBeFalse();
        loaded.NotifyOnToggle.ShouldBeFalse();
    }
}
