using System.Text;
using Itmam.Core.Dictionary.Building;
using Itmam.Core.Languages;
using Itmam.Core.Learning;

namespace Itmam.Core.Tests.Learning;

public sealed class TextWordsTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "itmam-tests", Guid.NewGuid().ToString("N"));
    private readonly List<IDisposable> _open = [];

    public void Dispose()
    {
        _open.ForEach(d => d.Dispose());
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void Arabic_words_are_counted_without_tashkeel_digits_or_latin()
    {
        var counts = TextWords.Count("ذهبَ الطالبُ إلى المدرسة، والمدرسة قريبة. 2026 hello و", LanguageProfile.Arabic);

        counts.ShouldBe(new Dictionary<string, int>
        {
            ["ذهب"] = 1, ["الطالب"] = 1, ["إلى"] = 1, ["المدرسة"] = 1, ["والمدرسة"] = 1, ["قريبة"] = 1,
        }, ignoreOrder: true);
    }

    [Fact]
    public void English_words_keep_their_apostrophes_and_a_sentence_capital_joins_the_lower_case_word()
    {
        var counts = TextWords.Count("The cat. the dog's bowl, don't 'quoted' London and London.", LanguageProfile.English);

        counts["the"].ShouldBe(2);
        counts.ShouldNotContainKey("The");
        counts["don't"].ShouldBe(1);
        counts["quoted"].ShouldBe(1);
        counts["London"].ShouldBe(2); // no «london» in the text: the capital stays
        counts["dog's"].ShouldBe(1);
    }

    [Fact]
    public void Persian_words_keep_the_zero_width_non_joiner() =>
        TextWords.Count("من می‌خواهم بروم", LanguageRegistry.ForTag("fa")).ShouldContainKey("می‌خواهم");

    [Theory]
    [InlineData("# Itmam (إتمام) personal dictionary\nكتاب\t3\n!كلمة\n", true)]
    [InlineData("كتاب\nقلم\nمدرسة\n", true)]
    [InlineData("كتاب 3\nقلم 1\n", true)]
    [InlineData("ذهب الطالب إلى المدرسة\n", false)]
    [InlineData("", false)]
    public void An_exported_file_or_a_plain_list_is_told_from_ordinary_text(string text, bool list) =>
        TextWords.IsWordList(text).ShouldBe(list);

    [Fact]
    public void Old_notepad_files_in_the_windows_code_page_are_read()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var ansi = Encoding.GetEncoding(1256).GetBytes("مدرسة جميلة");

        TextWords.Decode(ansi, LanguageProfile.Arabic).ShouldBe("مدرسة جميلة");
        TextWords.Decode(Encoding.UTF8.GetBytes("مدرسة"), LanguageProfile.Arabic).ShouldBe("مدرسة");
        TextWords.Decode([.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes("مدرسة")], LanguageProfile.Arabic).ShouldBe("مدرسة");
    }

    [Fact]
    public void Imported_words_are_suggested_at_once_and_blocked_ones_are_skipped()
    {
        var store = new SqliteUserWordStore(Path.Combine(_directory, "user.db"));
        var learning = new LearningService(new UserWordIndex(), store, Blocklist.Read(new StringReader("سيارة\n")),
            new LearningOptions { SaveDelay = TimeSpan.FromHours(1) });
        learning.Load();
        _open.Add(learning);
        _open.Add(store);
        learning.Learn("كتاب");
        learning.SetBlocked("قلم", blocked: true);

        var result = learning.ImportCounts(new Dictionary<string, int> { ["كتاب"] = 5, ["مدرسة"] = 1, ["سيارة"] = 2, ["قلم"] = 3 }, minCount: 2);

        result.ShouldBe(new ImportResult(Added: 1, Updated: 2, Skipped: 1));
        var words = learning.Snapshot().ToDictionary(w => w.Word);
        words["كتاب"].Count.ShouldBe(5);
        words["مدرسة"].Count.ShouldBe(2); // seen once, but suggested at once
        words["قلم"].Blocked.ShouldBeTrue(); // the user's choice stays
        words.ShouldNotContainKey("سيارة");
    }
}
