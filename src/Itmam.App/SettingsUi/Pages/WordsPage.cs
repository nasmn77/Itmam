using System.Globalization;
using Itmam.App.Resources;
using Itmam.Core.Languages;
using Itmam.Core.Learning;

namespace Itmam.App.SettingsUi.Pages;

/// <summary>My words: the learned words of one language.</summary>
internal sealed class WordsPage(SettingsWindow window) : SettingsPageBase(window)
{
    private Label? _wordsHeader;
    private SearchBox? _wordsSearch;
    private WordList? _wordList;
    private ThemedButton? _editWord;
    private ThemedButton? _toggleBlock;
    private ThemedButton? _deleteWord;

    // "My words": only word → count / last use is stored (see docs/PRIVACY.md). Every change goes through
    // LearningService, under the lock the suggestion engine also holds.
    public override void Build()
    {
        var settings = App.Settings;
        AddTitle(Strings.NavWords);
        AddText(Strings.WordsIntro, secondary: true);

        var learn = new ToggleSwitch { Checked = !settings.LearningPaused };
        learn.CheckedChanged += (_, _) => Update(s => s with { LearningPaused = !learn.Checked });
        AddCard(Strings.LearnNewWords, Strings.LearnNewWordsHint, learn);

        var add = new ThemedButton { Text = Strings.AddWordButton };
        add.Click += (_, _) => AddWord(blocked: false);
        var block = new ThemedButton { Text = Strings.BlockWordButton };
        block.Click += (_, _) => AddWord(blocked: true);
        AddCard(Strings.AddWord, Strings.AddWordHint, new ButtonRow(add, block));

        var import = new ThemedButton { Text = Strings.ImportButton };
        import.Click += (_, _) => ImportWords();
        var export = new ThemedButton { Text = Strings.ExportButton };
        export.Click += (_, _) => ExportWords();
        AddCard(Strings.ImportExport, Strings.ImportExportHint, new ButtonRow(import, export));

        AddSection(string.Empty);
        _wordsHeader = (Label)PageControls[^1];
        AddLanguageChoice();

        string[] sorts = [Strings.SortMostUsed, Strings.SortRecent, Strings.SortAlphabetical];
        var sort = new SegmentedChoice { Options = sorts };
        sort.SelectedIndex = State.WordsSort;
        sort.SelectedIndexChanged += (_, _) =>
        {
            State.WordsSort = sort.SelectedIndex;
            RefreshWords();
        };
        AddCard(Strings.SortWords, null, sort);

        _wordsSearch = new SearchBox { Placeholder = Strings.SearchWords, Text = State.WordsFilter };
        _wordsSearch.TextChanged += (_, _) =>
        {
            State.WordsFilter = _wordsSearch.Text.Trim();
            RefreshWords();
        };
        AddPageControl(_wordsSearch);

        _wordList = new WordList();
        _wordList.ItemActivated += (_, _) => EditSelectedWord();
        _wordList.DeleteRequested += (_, _) => DeleteSelectedWord();
        _wordList.ContextRequested += ShowWordMenu;
        AddPageControl(_wordList);

        _editWord = new ThemedButton { Text = Strings.EditWordButton };
        _editWord.Click += (_, _) => EditSelectedWord();
        _toggleBlock = new ThemedButton { Text = Strings.UnblockButton }; // the longer label sizes the button
        _toggleBlock.Click += (_, _) => ToggleSelectedBlock();
        _deleteWord = new ThemedButton { Text = Strings.DeleteButton };
        _deleteWord.Click += (_, _) => DeleteSelectedWord();
        AddPageControl(new ButtonRow(_editWord, _toggleBlock, _deleteWord) { Stretch = true });
        _wordList.SelectionChanged += (_, _) => UpdateWordButtons();

        AddSection(Strings.SectionClearWords);
        var clear = new ThemedButton { Text = Strings.ClearAllButton };
        clear.Click += (_, _) =>
        {
            if (Confirm(Strings.ConfirmClearWords, null))
            {
                App.Learning.ClearAll(State.WordsLanguage);
                RefreshWords();
            }
        };
        AddCard(Strings.ClearWords, Strings.ClearWordsHint, clear);
        RefreshWords();
    }

    // Reads a fresh copy of the words, then filters and sorts it for the list.
    private void RefreshWords()
    {
        if (_wordList is null || _wordsHeader is null)
        {
            return;
        }

        var all = App.Learning.Snapshot(State.WordsLanguage);
        _wordsHeader.Text = string.Format(CultureInfo.CurrentCulture, Strings.SectionWordsList, all.Count);
        IEnumerable<StoredWord> words = all;
        if (State.WordsFilter.Length > 0)
        {
            words = words.Where(w => w.Word.Contains(State.WordsFilter, StringComparison.Ordinal));
        }

        words = State.WordsSort switch
        {
            1 => words.OrderByDescending(w => w.LastUsed),
            2 => words.OrderBy(w => w.Word, StringComparer.Create(SortCulture(State.WordsLanguage), ignoreCase: false)),
            _ => words.OrderByDescending(w => w.Count).ThenByDescending(w => w.LastUsed),
        };
        _wordList.Words = [.. words];
        UpdateWordButtons();
    }

    private string? CleanWord(string word) => State.WordsLanguage.CleanWord(word);

    // Alphabetical order of the language's own alphabet.
    private static CultureInfo SortCulture(LanguageProfile language)
    {
        try
        {
            return CultureInfo.GetCultureInfo(language.Id);
        }
        catch (CultureNotFoundException)
        {
            return CultureInfo.InvariantCulture;
        }
    }

    private void UpdateWordButtons()
    {
        var selected = _wordList?.SelectedWord;
        foreach (var button in new[] { _editWord, _toggleBlock, _deleteWord })
        {
            if (button is not null)
            {
                button.Enabled = selected is not null;
                button.Invalidate();
            }
        }

        if (_toggleBlock is not null)
        {
            _toggleBlock.Text = selected is { Blocked: true } ? Strings.UnblockButton : Strings.BlockButton;
        }
    }

    // Add: suggested at once (counted as used enough times). Block: never suggested, even from the dictionary.
    private void AddWord(bool blocked)
    {
        var word = WordDialog.Ask(Window, blocked ? Strings.BlockWord : Strings.AddWord, blocked ? Strings.BlockWordPrompt : Strings.AddWordPrompt, string.Empty, Palette, Mirrored, CleanWord);
        if (word is null || (blocked && !Confirm(Strings.ConfirmBlockWord, word)))
        {
            return;
        }

        if (blocked)
        {
            App.Learning.SetBlocked(word, blocked: true, State.WordsLanguage);
        }
        else
        {
            App.Learning.AddManually(word, App.Settings.MinUsesToSuggest, State.WordsLanguage);
        }

        RefreshWords();
    }

    private void EditSelectedWord()
    {
        if (_wordList?.SelectedWord is not { } selected)
        {
            return;
        }

        var word = WordDialog.Ask(Window, Strings.EditWord, Strings.EditWordPrompt, selected.Word, Palette, Mirrored, CleanWord);
        if (word is not null && word != selected.Word)
        {
            App.Learning.Rename(selected.Word, word, State.WordsLanguage);
            RefreshWords();
        }
    }

    private void ToggleSelectedBlock()
    {
        if (_wordList?.SelectedWord is not { } selected
            || !Confirm(selected.Blocked ? Strings.ConfirmUnblockWord : Strings.ConfirmBlockWord, selected.Word))
        {
            return;
        }

        if (selected.Blocked && selected.Count == 0)
        {
            App.Learning.Remove(selected.Word, State.WordsLanguage); // only existed to be blocked
        }
        else
        {
            App.Learning.SetBlocked(selected.Word, !selected.Blocked, State.WordsLanguage);
        }

        RefreshWords();
    }

    private void DeleteSelectedWord()
    {
        if (_wordList?.SelectedWord is { } selected && Confirm(Strings.ConfirmDeleteWord, selected.Word))
        {
            App.Learning.Remove(selected.Word, State.WordsLanguage);
            RefreshWords();
        }
    }

    private void ShowWordMenu(Point location)
    {
        if (_wordList?.SelectedWord is not { } selected)
        {
            return;
        }

        var menu = new ContextMenuStrip { RightToLeft = RightToLeft, ShowImageMargin = false };
        menu.Items.Add(Strings.EditWordButton, null, (_, _) => EditSelectedWord());
        menu.Items.Add(selected.Blocked ? Strings.UnblockButton : Strings.BlockButton, null, (_, _) => ToggleSelectedBlock());
        menu.Items.Add(Strings.DeleteButton, null, (_, _) => DeleteSelectedWord());
        menu.Closed += (_, _) => BeginInvoke(menu.Dispose);
        menu.Show(_wordList, location);
    }

    // The file is the user's choice (user decision 2.0): any text — the words of the chosen language are taken
    // from it with their counts, all suggested at once — or a file exported from "My words" (one word per line,
    // "word<TAB>count", "!word" = blocked), imported as such. Only words and counts are kept, never the text.
    private void ImportWords()
    {
        using var dialog = new OpenFileDialog { Filter = Strings.WordsFileFilter };
        if (dialog.ShowDialog(Window) != DialogResult.OK)
        {
            return;
        }

        try
        {
            if (new FileInfo(dialog.FileName).Length > TextWords.MaxBytes)
            {
                ShowInfo(Strings.ImportFileTooLarge);
                return;
            }

            var text = TextWords.Decode(File.ReadAllBytes(dialog.FileName), State.WordsLanguage);
            ImportResult result;
            if (TextWords.IsWordList(text))
            {
                // Words of other languages follow their own line; a plain word is suggested at once.
                result = App.Learning.Import(new StringReader(text), State.WordsLanguage, App.Settings.MinUsesToSuggest);
            }
            else
            {
                var words = TextWords.Count(text, State.WordsLanguage);
                if (words.Count == 0)
                {
                    ShowInfo(Strings.ImportTextNone);
                    return;
                }

                var known = App.Learning.Snapshot(State.WordsLanguage).Select(w => w.Word).ToHashSet(StringComparer.Ordinal);
                var message = string.Format(CultureInfo.CurrentCulture, Strings.ImportTextConfirm, words.Count, words.Keys.Count(w => !known.Contains(w)), LanguageName(State.WordsLanguage.Id));
                if (!Confirm(message, null))
                {
                    return;
                }

                result = App.Learning.ImportCounts(words, App.Settings.MinUsesToSuggest, State.WordsLanguage);
            }

            RefreshWords();
            ShowInfo(string.Format(CultureInfo.CurrentCulture, Strings.ImportDone, result.Added, result.Updated, result.Skipped));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowInfo(Strings.FileError);
        }
    }

    private void ExportWords()
    {
        using var dialog = new SaveFileDialog { Filter = Strings.WordsFileFilter, FileName = Strings.WordsFileName };
        if (dialog.ShowDialog(Window) != DialogResult.OK)
        {
            return;
        }

        try
        {
            using var writer = new StreamWriter(dialog.FileName, append: false, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            App.Learning.Export(writer);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowInfo(Strings.FileError);
        }
    }
}
