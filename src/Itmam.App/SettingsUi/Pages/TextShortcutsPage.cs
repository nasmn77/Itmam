using System.Globalization;
using Itmam.App.Resources;
using Itmam.Core.Suggestions;

namespace Itmam.App.SettingsUi.Pages;

/// <summary>Text shortcuts: a short key that shows its text first in the bar.</summary>
internal sealed class TextShortcutsPage(SettingsWindow window) : SettingsPageBase(window)
{
    // User decision (1.1): a text shortcut («ع1») typed exactly shows its text first in the bar; Tab inserts
    // it. Kept in settings.json, like "My phrases".
    public override void Build()
    {
        var shortcuts = App.Settings.TextShortcuts;
        AddTitle(Strings.NavTextShortcuts);
        AddText(Strings.TextShortcutsIntro, secondary: true);

        var add = new ThemedButton { Text = Strings.AddWordButton };
        add.Click += (_, _) => EditTextShortcut(null);
        AddCard(Strings.AddTextShortcut, Strings.AddTextShortcutHint, add);
        AddImportExportCard(
            Strings.TextShortcutsFileName,
            writer => UserListFiles.WriteShortcuts(writer, App.Settings.TextShortcuts),
            reader => UserListFiles.ReadShortcuts(reader, App.Settings.TextShortcuts),
            (s, merged) => s with { TextShortcuts = merged });

        AddSection(string.Format(CultureInfo.CurrentCulture, Strings.SectionTextShortcuts, shortcuts.Count));
        foreach (var shortcut in shortcuts)
        {
            var change = new ThemedButton { Text = Strings.EditWordButton };
            change.Click += (_, _) => EditTextShortcut(shortcut);
            var remove = new ThemedButton { Text = Strings.DeleteButton };
            remove.Click += (_, _) =>
            {
                if (Confirm(Strings.ConfirmDeleteTextShortcut, shortcut.Key, leftToRight: false))
                {
                    Update(s => s with { TextShortcuts = [.. s.TextShortcuts.Where(t => t.Key != shortcut.Key)] });
                    BeginInvoke(RebuildPage); // after this click is handled
                }
            };
            AddCard(shortcut.Key, shortcut.Text, new ButtonRow(change, remove));
        }
    }

    // Add (existing = null) or edit a text shortcut; a key already used is replaced after a confirmation.
    private void EditTextShortcut(TextShortcut? existing)
    {
        if (TextShortcutDialog.Ask(Window, existing is null ? Strings.AddTextShortcut : Strings.EditTextShortcut, existing, Palette, Mirrored) is not { } edited
            || edited == existing)
        {
            return;
        }

        var replaced = App.Settings.TextShortcuts.Any(t => t.Key == edited.Key && t.Key != existing?.Key);
        if (replaced && !Confirm(Strings.ConfirmReplaceTextShortcut, edited.Key, leftToRight: false))
        {
            return;
        }

        Update(s =>
        {
            // An edited shortcut keeps its place; a new one goes last.
            var list = s.TextShortcuts.ToList();
            var index = existing is null ? -1 : list.FindIndex(t => t.Key == existing.Key);
            list.RemoveAll(t => t.Key == edited.Key || t.Key == existing?.Key);
            list.Insert(index < 0 ? list.Count : Math.Min(index, list.Count), edited);
            return s with { TextShortcuts = list };
        });
        BeginInvoke(RebuildPage);
    }
}
