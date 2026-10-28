using Itmam.Core.Dictionary;
using Itmam.Core.Dictionary.Building;
using Itmam.Core.Learning;
using Itmam.Core.Suggestions;

namespace Itmam.Core.Tests.Learning;

public sealed class LearningServiceTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 28, 9, 0, 0, TimeSpan.Zero);

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "itmam-tests", Guid.NewGuid().ToString("N"));
    private readonly FixedTimeProvider _time = new(Now);
    private readonly Blocklist _blocklist = Blocklist.Read(new StringReader("سيارة\n"));
    private readonly List<IDisposable> _open = [];

    private string DatabasePath => Path.Combine(_directory, "user.db");

    public void Dispose()
    {
        _open.ForEach(d => d.Dispose());
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    // A fresh "app start": new store and index loaded from the same file.
    private (LearningService Learning, UserWordIndex Index) Start(LearningOptions? options = null)
    {
        var store = new SqliteUserWordStore(DatabasePath);
        var index = new UserWordIndex();
        var learning = new LearningService(index, store, _blocklist, options ?? new LearningOptions { SaveDelay = TimeSpan.FromHours(1) }, _time);
        learning.Load();
        _open.Add(learning);
        _open.Add(store);
        return (learning, index);
    }

    [Fact]
    public void A_new_word_is_saved_on_its_first_use()
    {
        var (learning, _) = Start();

        learning.Learn("إتمام").ShouldBe(LearnOutcome.Learned);
        learning.Flush();

        var (_, reloaded) = Start();
        reloaded.Get("إتمام").ShouldNotBeNull().Count.ShouldBe(1);
    }

    [Fact]
    public void A_new_word_becomes_suggestible_after_the_threshold_and_survives_a_restart()
    {
        var (learning, index) = Start();
        var engine = new SuggestionEngine(WordDictionary.Empty, index, new SuggestionOptions { MinUsesToSuggest = 2 }, _time);

        learning.Learn("إتمام");
        engine.Suggest("إت").ShouldBeEmpty();
        learning.Learn("إتمام");
        engine.Suggest("إت").Select(s => s.Word).ShouldBe(["إتمام"]);
        learning.Dispose(); // exit: flushes

        var (_, reloaded) = Start();
        new SuggestionEngine(WordDictionary.Empty, reloaded, new SuggestionOptions { MinUsesToSuggest = 2 }, _time)
            .Suggest("إت").Select(s => s.Word).ShouldBe(["إتمام"]);
    }

    [Fact]
    public void Changes_are_saved_automatically_after_the_delay()
    {
        var (learning, _) = Start(new LearningOptions { SaveDelay = TimeSpan.FromMilliseconds(30) });
        learning.Learn("إتمام");

        var deadline = DateTime.UtcNow.AddSeconds(5);
        IReadOnlyList<StoredWord> stored;
        using var probe = new SqliteUserWordStore(DatabasePath);
        do
        {
            Thread.Sleep(20);
            stored = probe.LoadAll();
        }
        while (stored.Count == 0 && DateTime.UtcNow < deadline);

        stored.ShouldHaveSingleItem().Word.ShouldBe("إتمام");
    }

    [Theory]
    [InlineData("ب")]       // too short
    [InlineData("hello")]   // not Arabic
    [InlineData("")]
    public void Non_words_are_not_learned(string word)
    {
        var (learning, index) = Start();

        learning.Learn(word).ShouldBe(LearnOutcome.NotAWord);
        index.Count.ShouldBe(0);
    }

    [Fact]
    public void Tashkeel_is_not_stored()
    {
        var (learning, index) = Start();

        learning.Learn("إِتْمَام");

        index.Get("إتمام").ShouldNotBeNull();
    }

    [Fact]
    public void Blocklisted_words_are_never_learned_automatically()
    {
        var (learning, index) = Start();

        learning.Learn("سيارة").ShouldBe(LearnOutcome.Blocked);
        learning.Learn("السيارة").ShouldBe(LearnOutcome.Blocked);
        index.Count.ShouldBe(0);
    }

    [Fact]
    public void A_blocklisted_word_added_by_the_user_is_respected()
    {
        var (learning, index) = Start();

        learning.AddManually("سيارة", count: 2).ShouldBeTrue();
        learning.Learn("سيارة").ShouldBe(LearnOutcome.Learned);

        index.Get("سيارة").ShouldNotBeNull().Count.ShouldBe(3);
    }

    [Fact]
    public void Nothing_is_learned_while_paused()
    {
        var (learning, index) = Start();
        learning.IsPaused = true;

        learning.Learn("إتمام").ShouldBe(LearnOutcome.Paused);
        index.Count.ShouldBe(0);
    }

    [Fact]
    public void Blocked_words_are_never_suggested_and_the_block_is_saved()
    {
        var dictionary = TestDictionaries.Of(("مدرسة", 100), ("مدرسين", 50));
        var (learning, index) = Start();

        learning.SetBlocked("مدرسة", blocked: true);
        learning.Flush();
        new SuggestionEngine(dictionary, index).Suggest("مدر").Select(s => s.Word).ShouldBe(["مدرسين"]);

        var (_, reloaded) = Start();
        new SuggestionEngine(dictionary, reloaded).Suggest("مدر").Select(s => s.Word).ShouldBe(["مدرسين"]);
    }

    [Fact]
    public void Rename_and_remove_are_saved()
    {
        var (learning, _) = Start();
        learning.Learn("كتبا");
        learning.Learn("قلم");
        learning.Flush();

        learning.Rename("كتبا", "كتاب").ShouldBeTrue();
        learning.Remove("قلم").ShouldBeTrue();
        learning.Flush();

        var (_, reloaded) = Start();
        reloaded.All.Select(w => w.Word).ShouldBe(["كتاب"]);
    }

    [Fact]
    public void Export_then_import_round_trips_words_counts_and_blocks()
    {
        var (learning, _) = Start();
        learning.AddManually("إتمام", 4);
        learning.Learn("قلم");
        learning.SetBlocked("مدرسة", blocked: true);
        var exported = new StringWriter();
        learning.Export(exported);

        learning.ClearAll();
        var result = learning.Import(new StringReader(exported.ToString()));
        learning.Flush();

        result.ShouldBe(new ImportResult(Added: 3, Updated: 0, Skipped: 0));
        var (_, reloaded) = Start();
        reloaded.Get("إتمام").ShouldNotBeNull().Count.ShouldBe(4);
        reloaded.Get("قلم").ShouldNotBeNull().Count.ShouldBe(1);
        reloaded.IsBlocked("مدرسة").ShouldBeTrue();
    }

    [Fact]
    public void Import_accepts_a_plain_word_list_and_skips_invalid_lines()
    {
        var (learning, index) = Start();

        var result = learning.Import(new StringReader("# comment\nإتمام\nقلم 3\nhello\nكتاب\t-1\n\n"));

        result.ShouldBe(new ImportResult(Added: 2, Updated: 0, Skipped: 2));
        index.Get("قلم").ShouldNotBeNull().Count.ShouldBe(3);
    }

    [Fact]
    public void Clear_all_empties_memory_and_disk()
    {
        var (learning, index) = Start();
        learning.Learn("إتمام");
        learning.Flush();

        learning.ClearAll();

        index.Count.ShouldBe(0);
        var (_, reloaded) = Start();
        reloaded.Count.ShouldBe(0);
    }
}
