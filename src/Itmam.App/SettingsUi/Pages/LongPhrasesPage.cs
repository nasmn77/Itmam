using System.Globalization;
using Itmam.App.Resources;
using Itmam.Core.Input;
using Itmam.Core.Suggestions;

namespace Itmam.App.SettingsUi.Pages;

/// <summary>Long texts: named texts of several lines, chosen from a list opened with a shortcut.</summary>
internal sealed class LongPhrasesPage(SettingsWindow window) : SettingsPageBase(window)
{
    // User decision (1.1): long phrases (support replies of several lines) are chosen by name from a list
    // opened with a shortcut anywhere (LongPhrasePicker). Kept in settings.json.
    public override void Build()
    {
        var phrases = App.Settings.LongPhrases;
        var shortcut = "\u202A" + HotkeyNames.Format(App.Settings.Hotkeys.OpenLongPhrases) + "\u202C";
        AddTitle(Strings.NavLongPhrases);
        AddText(string.Format(CultureInfo.CurrentCulture, Strings.LongPhrasesIntro, shortcut), secondary: true);

        // The same shortcut as on the Shortcuts page, here too (user decision); the intro names it.
        AddHotkeyCard(HotkeyAction.OpenLongPhrases, Strings.ShortcutLongPhrases, Strings.ShortcutLongPhrasesHint, () => BeginInvoke(RebuildPage));

        var add = new ThemedButton { Text = Strings.AddWordButton };
        add.Click += (_, _) => EditLongPhrase(null);
        AddCard(Strings.AddLongPhrase, Strings.AddLongPhraseHint, add);
        AddImportExportCard(
            Strings.LongPhrasesFileName,
            writer => UserListFiles.WriteLongPhrases(writer, App.Settings.LongPhrases),
            reader => UserListFiles.ReadLongPhrases(reader, App.Settings.LongPhrases),
            (s, merged) => s with { LongPhrases = merged });

        AddSection(string.Format(CultureInfo.CurrentCulture, Strings.SectionLongPhrases, phrases.Count));
        if (phrases.Count > 1)
        {
            AddText(Strings.LongPhrasesDragHint, secondary: true);
        }

        // User decision (after 2.0): put in order by dragging; the shortcut's list follows this order.
        var cards = new List<SettingCard>();
        foreach (var phrase in phrases)
        {
            var change = new ThemedButton { Text = Strings.EditWordButton };
            change.Click += (_, _) => EditLongPhrase(phrase);
            var remove = new ThemedButton { Text = Strings.DeleteButton };
            remove.Click += (_, _) =>
            {
                if (Confirm(Strings.ConfirmDeleteLongPhrase, phrase.Name, leftToRight: false))
                {
                    Update(s => s with { LongPhrases = [.. s.LongPhrases.Where(p => p.Name != phrase.Name)] });
                    BeginInvoke(RebuildPage); // after this click is handled
                }
            };
            cards.Add(AddCard(phrase.Name, LongPhrases.Preview(phrase.Text), new ButtonRow(change, remove)));
        }

        // Saved by name, without rebuilding the page: the cards are already in their places.
        CardReorder.Attach(Content, cards, PageControls, LayoutPage, order => Update(s =>
        {
            var names = order.Select(i => phrases[i].Name).ToList();
            var ordered = names.Select(name => s.LongPhrases.FirstOrDefault(p => p.Name == name)).OfType<LongPhrase>();
            return s with { LongPhrases = [.. ordered, .. s.LongPhrases.Where(p => !names.Contains(p.Name))] };
        }));
    }

    // Add (existing = null) or edit a long phrase; a name already used is replaced after a confirmation.
    private void EditLongPhrase(LongPhrase? existing)
    {
        if (LongPhraseDialog.Ask(Window, existing is null ? Strings.AddLongPhrase : Strings.EditLongPhrase, existing, Palette, Mirrored) is not { } edited
            || edited == existing)
        {
            return;
        }

        if (App.Settings.LongPhrases.Any(p => p.Name == edited.Name && p.Name != existing?.Name)
            && !Confirm(Strings.ConfirmReplaceLongPhrase, edited.Name, leftToRight: false))
        {
            return;
        }

        Update(s =>
        {
            // An edited phrase keeps its place; a new one goes last.
            var list = s.LongPhrases.ToList();
            var index = existing is null ? -1 : list.FindIndex(p => p.Name == existing.Name);
            list.RemoveAll(p => p.Name == edited.Name || p.Name == existing?.Name);
            list.Insert(index < 0 ? list.Count : Math.Min(index, list.Count), edited);
            return s with { LongPhrases = list };
        });
        BeginInvoke(RebuildPage);
    }
}
