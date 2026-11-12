using System.Globalization;
using Itmam.App.Resources;
using Itmam.Core.Learning;
using Itmam.Win32;

namespace Itmam.App.SettingsUi.Pages;

/// <summary>General: user guide, start with Windows, notifications, suggestions, learning, full backup, statistics.</summary>
internal sealed class GeneralPage(SettingsWindow window) : SettingsPageBase(window)
{
    public override void Build()
    {
        var settings = App.Settings;
        AddTitle(Strings.NavGeneral);

        // User guide (user decision 1.1.1): a PDF shipped in the package, in the interface language.
        var guide = new ThemedButton { Text = Strings.OpenButton };
        guide.Click += (_, _) =>
        {
            if (!UserGuide.Open(App.Settings.Language))
            {
                ShowInfo(Strings.GuideUnavailable);
            }
        };
        AddCard(Strings.UserGuide, Strings.UserGuideHint, guide);

        AddSection(Strings.SectionStartup);
        var startup = new ToggleSwitch();
        AddCard(Strings.StartWithWindows, Strings.StartWithWindowsHint, startup);
        _ = LoadStartupStateAsync(startup);
        startup.CheckedChanged += async (_, _) =>
        {
            if (Loading)
            {
                return;
            }

            var actual = await StartupRegistration.SetEnabledAsync(startup.Checked);
            App.UpdateSettings(App.Settings with { StartWithWindows = actual });
            Loading = true;
            startup.Checked = actual; // the user or a policy may have refused
            Loading = false;
        };

        AddSection(Strings.SectionNotifications);
        var notify = new ToggleSwitch { Checked = settings.NotifyOnToggle };
        notify.CheckedChanged += (_, _) => Update(s => s with { NotifyOnToggle = notify.Checked });
        AddCard(Strings.NotifyOnToggle, Strings.NotifyOnToggleHint, notify);

        AddSection(Strings.SectionSuggestions);
        var bar = new ToggleSwitch { Checked = settings.ShowSuggestionBar };
        bar.CheckedChanged += (_, _) => Update(s => s with { ShowSuggestionBar = bar.Checked });
        AddCard(Strings.ShowSuggestionBar, Strings.ShowSuggestionBarHint, bar);

        var count = Numbers(1, 5, settings.MaxSuggestions);
        count.SelectedIndexChanged += (_, _) => Update(s => s with { MaxSuggestions = count.SelectedIndex + 1 });
        AddCard(Strings.MaxSuggestions, null, count);

        var letters = Numbers(1, 4, settings.MinLetters);
        letters.SelectedIndexChanged += (_, _) => Update(s => s with { MinLetters = letters.SelectedIndex + 1 });
        AddCard(Strings.MinLetters, null, letters);

        var space = new ToggleSwitch { Checked = settings.AddSpaceAfterAccept };
        space.CheckedChanged += (_, _) => Update(s => s with { AddSpaceAfterAccept = space.Checked });
        AddCard(Strings.AddSpace, null, space);

        AddSection(Strings.SectionLearning);
        var learn = new ToggleSwitch { Checked = !settings.LearningPaused };
        learn.CheckedChanged += (_, _) => Update(s => s with { LearningPaused = !learn.Checked });
        AddCard(Strings.LearnNewWords, null, learn);

        var uses = Numbers(1, 5, settings.MinUsesToSuggest);
        uses.SelectedIndexChanged += (_, _) => Update(s => s with { MinUsesToSuggest = uses.SelectedIndex + 1 });
        AddCard(Strings.MinUses, null, uses);

        // Full backup (user decision 1.1): one file to move everything to another PC.
        AddSection(Strings.SectionBackup);
        var backup = new ThemedButton { Text = Strings.BackupButton };
        backup.Click += (_, _) => SaveFullBackup();
        var restore = new ThemedButton { Text = Strings.RestoreBackupButton };
        restore.Click += (_, _) => RestoreFullBackup();
        AddCard(Strings.FullBackup, Strings.FullBackupHint, new ButtonRow(backup, restore));

        // Statistics (user decision 1.1): counts only, kept in user.db.
        AddSection(Strings.SectionStats);
        var usage = App.Learning.Usage;
        var reset = new ThemedButton { Text = Strings.ResetStatsButton };
        reset.Click += (_, _) =>
        {
            if (Confirm(Strings.ConfirmResetStats, null))
            {
                App.Learning.ResetUsage();
                BeginInvoke(RebuildPage);
            }
        };
        AddCard(string.Format(CultureInfo.CurrentCulture, Strings.StatsSummary, usage.Completions, usage.CharactersSaved),
            string.Format(CultureInfo.CurrentCulture, Strings.StatsSince, usage.Since.ToLocalTime().DateTime), reset);
    }

    private async Task LoadStartupStateAsync(ToggleSwitch toggle)
    {
        var enabled = await StartupRegistration.IsEnabledAsync();
        Loading = true;
        toggle.Checked = enabled;
        Loading = false;
    }

    private static SegmentedChoice Numbers(int from, int to, int selected)
    {
        var choice = new SegmentedChoice
        {
            Options = Enumerable.Range(from, to - from + 1).Select(n => n.ToString(CultureInfo.CurrentCulture)).ToArray(),
        };
        choice.SelectedIndex = selected - from;
        return choice;
    }

    private void SaveFullBackup()
    {
        using var dialog = new SaveFileDialog
        {
            Filter = Strings.BackupFileFilter,
            FileName = string.Format(CultureInfo.CurrentCulture, Strings.BackupFileName, DateTime.Now),
        };
        if (dialog.ShowDialog(Window) != DialogResult.OK)
        {
            return;
        }

        try
        {
            App.Learning.Flush();
            using (var file = File.Create(dialog.FileName))
            {
                FullBackup.Write(file, App.Settings, App.Learning);
            }

            ShowInfo(Strings.BackupDone);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowInfo(Strings.FileError);
        }
    }

    // Replaces everything with the backup's content, after a confirmation (user decision). Start with
    // Windows and on/off stay as they are on this PC.
    private void RestoreFullBackup()
    {
        using var dialog = new OpenFileDialog { Filter = Strings.BackupFileFilter };
        if (dialog.ShowDialog(Window) != DialogResult.OK)
        {
            return;
        }

        BackupContent? content;
        try
        {
            using var file = File.OpenRead(dialog.FileName);
            content = FullBackup.Read(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowInfo(Strings.FileError);
            return;
        }

        if (content is null)
        {
            ShowInfo(Strings.NotABackup);
            return;
        }

        if (!Confirm(Strings.ConfirmRestoreBackup, null))
        {
            return;
        }

        FullBackup.RestoreLearning(content, App.Learning);
        var current = App.Settings;
        App.UpdateSettings(content.Settings with { StartWithWindows = current.StartWithWindows, Enabled = current.Enabled });
        ShowInfo(Strings.RestoreDone);
        BeginInvoke(RebuildPage);
    }
}
