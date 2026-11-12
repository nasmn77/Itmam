using System.Diagnostics;
using System.Globalization;
using Itmam.App.Resources;
using Itmam.Core.Input;
using Itmam.Win32;

namespace Itmam.App.SettingsUi.Pages;

/// <summary>Excluded apps: the apps Itmam stays silent in.</summary>
internal sealed class ExcludedPage(SettingsWindow window) : SettingsPageBase(window)
{
    // Process names only: nothing about what the user does in these apps is read or stored.
    public override void Build()
    {
        var excluded = App.Settings.ExcludedApps;
        var running = RunningApps();
        AddTitle(Strings.NavExcluded);
        AddText(Strings.ExcludedIntro, secondary: true);

        var add = new ThemedButton { Text = Strings.AddButton };
        add.Click += (_, _) => ShowAddAppMenu(add);
        AddCard(Strings.AddApp, Strings.AddAppHint, add);

        AddSection(string.Format(CultureInfo.CurrentCulture, Strings.SectionExcludedList, excluded.Count));
        if (excluded.Count == 0)
        {
            AddText(Strings.ExcludedEmpty, secondary: true);
        }

        var search = new SearchBox { Placeholder = Strings.SearchApps, Text = State.ExcludedSearch };
        AddPageControl(search);
        AddText(Strings.NoMatchingApps, secondary: true);
        var noMatches = PageControls[^1];

        // Open apps first (they show their description), then the rest in list order.
        var cards = new List<(Control Card, string Searchable)>();
        foreach (var name in excluded.OrderBy(n => running.ContainsKey(ExcludedApps.NormalizeName(n)) ? 0 : 1))
        {
            var remove = new ThemedButton { Text = Strings.RemoveButton };
            remove.Click += (_, _) =>
            {
                if (!Confirm(Strings.ConfirmRemoveExcluded, name))
                {
                    return;
                }

                Update(s => s with { ExcludedApps = ExcludedApps.Without(s.ExcludedApps, name) });
                BeginInvoke(RebuildPage); // after this click is handled
            };
            var description = running.TryGetValue(ExcludedApps.NormalizeName(name), out var found) ? found : null;
            cards.Add((AddCard(name, description, remove), $"{name} {description}"));
        }

        void Filter()
        {
            State.ExcludedSearch = search.Text.Trim();
            foreach (var (card, searchable) in cards)
            {
                if (searchable.Contains(State.ExcludedSearch, StringComparison.CurrentCultureIgnoreCase))
                {
                    FilteredOut.Remove(card);
                }
                else
                {
                    FilteredOut.Add(card);
                }
            }

            if (cards.Count > 0 && cards.All(c => FilteredOut.Contains(c.Card)))
            {
                FilteredOut.Remove(noMatches);
            }
            else
            {
                FilteredOut.Add(noMatches);
            }

            if (excluded.Count == 0)
            {
                FilteredOut.Add(search);
            }

            LayoutPage();
        }

        search.TextChanged += (_, _) => Filter();
        Filter();

        var reset = new ThemedButton { Text = Strings.ResetButton };
        reset.Click += (_, _) =>
        {
            if (!Confirm(Strings.ConfirmResetExcluded, null))
            {
                return;
            }

            Update(s => s with { ExcludedApps = [.. ExcludedApps.Defaults] });
            BeginInvoke(RebuildPage);
        };
        AddCard(Strings.ResetExcluded, Strings.ResetExcludedHint, reset);
    }

    // Open apps (with a window) that are not excluded yet, then "Browse…" for any program file.
    private void ShowAddAppMenu(Control anchor)
    {
        var excluded = App.Settings.ExcludedApps.Select(ExcludedApps.NormalizeName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var menu = new ContextMenuStrip { RightToLeft = RightToLeft, ShowImageMargin = false };
        foreach (var (name, description) in RunningApps().Where(a => !excluded.Contains(a.Key)).OrderBy(a => a.Value, StringComparer.CurrentCultureIgnoreCase))
        {
            menu.Items.Add($"{description} — {name}", null, (_, _) => AddExcluded(name));
        }

        if (menu.Items.Count > 0)
        {
            menu.Items.Add(new ToolStripSeparator());
        }

        menu.Items.Add(Strings.BrowseApp, null, (_, _) =>
        {
            using var dialog = new OpenFileDialog { Filter = Strings.ExeFilter, CheckFileExists = true };
            if (dialog.ShowDialog(Window) == DialogResult.OK)
            {
                AddExcluded(dialog.FileName);
            }
        });
        menu.Closed += (_, _) => BeginInvoke(menu.Dispose);
        menu.Show(anchor, new Point(0, anchor.Height));
    }

    private void AddExcluded(string name)
    {
        if (!Confirm(Strings.ConfirmAddExcluded, ExcludedApps.NormalizeName(name)))
        {
            return;
        }

        Update(s => s with { ExcludedApps = ExcludedApps.With(s.ExcludedApps, name) });
        BeginInvoke(RebuildPage);
    }

    // Executable name → description ("Visual Studio Code") of processes that have a window.
    private static Dictionary<string, string> RunningApps()
    {
        var apps = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var self = Environment.ProcessId;
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    if (process.Id == self || process.MainWindowHandle == 0 || ProcessNames.PathOf((uint)process.Id) is not { } path)
                    {
                        continue;
                    }

                    var name = ExcludedApps.NormalizeName(path);
                    var description = FileVersionInfo.GetVersionInfo(path).FileDescription;
                    apps.TryAdd(name, string.IsNullOrWhiteSpace(description) ? name : description.Trim());
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or IOException or UnauthorizedAccessException)
                {
                    // The process exited or is protected: skip it.
                }
            }
        }

        return apps;
    }
}
