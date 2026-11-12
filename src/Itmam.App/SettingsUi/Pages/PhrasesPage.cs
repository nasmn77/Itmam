using System.Globalization;
using Itmam.App.Resources;
using Itmam.Core.Learning;
using Itmam.Core.Suggestions;

namespace Itmam.App.SettingsUi.Pages;

/// <summary>My phrases: the user's own phrases, the learned ones and the hidden built-in ones.</summary>
internal sealed class PhrasesPage(SettingsWindow window) : SettingsPageBase(window)
{
    // User decision (1.1): fixed expressions are completed whole (Tab) or word by word: the user's own
    // ("My phrases", kept in settings.json), the learned ones (sequences typed twice, in user.db) and the
    // shipped list.
    public override void Build()
    {
        var settings = App.Settings;
        AddTitle(Strings.NavPhrases);
        AddText(string.Format(CultureInfo.CurrentCulture, Strings.PhrasesIntro, App.ShippedPhrases.Count), secondary: true);

        var suggest = new ToggleSwitch { Checked = settings.SuggestPhrases };
        suggest.CheckedChanged += (_, _) => Update(s => s with { SuggestPhrases = suggest.Checked });
        AddCard(Strings.SuggestPhrases, Strings.SuggestPhrasesHint, suggest);

        // User decision (after 2.0): phrases before the words (default) or after them.
        var position = new SegmentedChoice { Options = [Strings.PhrasesPositionFirst, Strings.PhrasesPositionLast] };
        position.SelectedIndex = settings.PhrasesLast ? 1 : 0;
        position.SelectedIndexChanged += (_, _) => Update(s => s with { PhrasesLast = position.SelectedIndex == 1 });
        AddCard(Strings.PhrasesPosition, Strings.PhrasesPositionHint, position);

        var learn = new ToggleSwitch { Checked = settings.LearnPhrases };
        learn.CheckedChanged += (_, _) => Update(s => s with { LearnPhrases = learn.Checked });
        AddCard(Strings.LearnPhrases, Strings.LearnPhrasesHint, learn);

        AddSection(string.Format(CultureInfo.CurrentCulture, Strings.SectionMyPhrases, settings.MyPhrases.Count));
        var add = new ThemedButton { Text = Strings.AddWordButton };
        add.Click += (_, _) => EditPhrase(null);
        AddCard(Strings.AddPhrase, Strings.AddPhraseHint, add);
        AddImportExportCard(
            Strings.PhrasesFileName,
            writer => UserListFiles.WritePhrases(writer, App.Settings.MyPhrases,
                App.Learning.LearnedPhrases(all: true, State.WordsLanguage).Select(p => (p.Text, p.Count))),
            reader =>
            {
                // The learned phrases in the file go back to the learned list (of the language chosen below), with their counts.
                var learned = new List<(string Text, int Count)>();
                var mine = UserListFiles.ReadPhrases(reader, App.Settings.MyPhrases, learned, State.WordsLanguage);
                return mine with { Added = mine.Added + App.Learning.ImportPhrases(learned, State.WordsLanguage) };
            },
            (s, merged) => s with { MyPhrases = merged },
            Strings.ImportExportPhrasesHint);

        foreach (var phrase in settings.MyPhrases)
        {
            var change = new ThemedButton { Text = Strings.EditWordButton };
            change.Click += (_, _) => EditPhrase(phrase);
            var remove = new ThemedButton { Text = Strings.DeleteButton };
            remove.Click += (_, _) =>
            {
                if (Confirm(Strings.ConfirmDeletePhrase, phrase, leftToRight: false))
                {
                    Update(s => s with { MyPhrases = [.. s.MyPhrases.Where(p => p != phrase)] });
                    BeginInvoke(RebuildPage); // after this click is handled
                }
            };
            AddCard(phrase, null, new ButtonRow(change, remove));
        }

        BuildLearnedPhrases();

        // Built-in phrases deleted from the bar.
        if (settings.HiddenPhrases.Count > 0)
        {
            var restore = new ThemedButton { Text = Strings.RestoreButton };
            restore.Click += (_, _) =>
            {
                Update(s => s with { HiddenPhrases = [] });
                BeginInvoke(RebuildPage);
            };
            AddCard(string.Format(CultureInfo.CurrentCulture, Strings.HiddenPhrases, settings.HiddenPhrases.Count),
                string.Join("، ", settings.HiddenPhrases), restore);
        }
    }

    // The learned phrases: forget one (learned again if typed again), keep it for good in "My phrases",
    // or delete them all together with every sequence counted so far.
    private void BuildLearnedPhrases()
    {
        var phrases = App.Learning.LearnedPhrases(language: State.WordsLanguage); // like the bar: not the parts of a longer learned phrase
        AddSection(string.Format(CultureInfo.CurrentCulture, Strings.SectionLearnedPhrases, phrases.Count));
        AddLanguageChoice();
        if (phrases.Count == 0)
        {
            AddText(Strings.NoLearnedPhrases, secondary: true);
        }
        else
        {
            var list = new WordList { Height = 280 };
            list.Words = [.. phrases.Select(p => new StoredWord(p.Text, p.Count, p.LastUsed, Blocked: false))];
            var keep = new ThemedButton { Text = Strings.KeepPhraseButton };
            var forget = new ThemedButton { Text = Strings.DeleteButton };
            void UpdateButtons()
            {
                foreach (var button in new[] { keep, forget })
                {
                    button.Enabled = list.SelectedWord is not null;
                    button.Invalidate();
                }
            }

            void Forget()
            {
                if (list.SelectedWord is { } selected && Confirm(Strings.ConfirmForgetPhrase, selected.Word, leftToRight: false))
                {
                    App.Learning.ForgetPhrase(selected.Word, State.WordsLanguage);
                    BeginInvoke(RebuildPage);
                }
            }

            keep.Click += (_, _) =>
            {
                if (list.SelectedWord is { } selected)
                {
                    Update(s => s with { MyPhrases = [.. s.MyPhrases.Append(selected.Word).Distinct()] });
                    App.Learning.ForgetPhrase(selected.Word, State.WordsLanguage);
                    BeginInvoke(RebuildPage);
                }
            };
            forget.Click += (_, _) => Forget();
            list.DeleteRequested += (_, _) => Forget();
            list.SelectionChanged += (_, _) => UpdateButtons();
            AddPageControl(list);
            AddPageControl(new ButtonRow(keep, forget) { Stretch = true });
            UpdateButtons();
        }

        var clear = new ThemedButton { Text = Strings.ClearAllButton };
        clear.Click += (_, _) =>
        {
            if (Confirm(Strings.ConfirmClearPhrases, null))
            {
                App.Learning.ClearPhrases(State.WordsLanguage);
                BeginInvoke(RebuildPage);
            }
        };
        AddCard(Strings.ClearPhrases, Strings.ClearPhrasesHint, clear);
    }

    // Add (existing = null) or edit one of "My phrases".
    private void EditPhrase(string? existing)
    {
        var text = WordDialog.Ask(Window, existing is null ? Strings.AddPhrase : Strings.EditPhrase, Strings.PhrasePrompt,
            existing ?? string.Empty, Palette, Mirrored, PhraseIndex.CleanAny, Strings.NotAPhrase);
        if (text is null || text == existing)
        {
            return;
        }

        Update(s => s with { MyPhrases = [.. s.MyPhrases.Where(p => p != existing).Append(text).Distinct()] });
        BeginInvoke(RebuildPage);
    }
}
