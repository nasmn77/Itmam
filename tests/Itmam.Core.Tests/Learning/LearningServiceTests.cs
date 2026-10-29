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
    public void Snapshot_copies_every_word_with_its_state()
    {
        var (learning, _) = Start();
        learning.Learn("كتاب");
        learning.Learn("كتاب");
        learning.SetBlocked("قلم", blocked: true);

        var snapshot = learning.Snapshot();

        snapshot.Single(w => w.Word == "كتاب").ShouldBe(new StoredWord("كتاب", 2, Now, Blocked: false));
        snapshot.Single(w => w.Word == "قلم").Blocked.ShouldBeTrue();

        learning.Remove("كتاب");
        snapshot.Count.ShouldBe(2); // a copy: later changes do not affect it
    }

    [Fact]
    public async Task Changes_from_another_thread_never_break_readers_holding_the_lock()
    {
        var (learning, index) = Start();
        using var stop = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

        // "My words" edits (UI thread) while the engine reads (pipeline thread).
        var writer = Task.Run(() =>
        {
            for (var i = 0; !stop.IsCancellationRequested; i++)
            {
                learning.AddManually("كلمة" + new string('ب', (i % 20) + 1), 2);
                learning.Remove("كلمة" + new string('ب', ((i + 7) % 20) + 1));
            }
        }, TestContext.Current.CancellationToken);
        var reads = 0;
        while (!stop.IsCancellationRequested)
        {
            lock (learning.SyncRoot)
            {
                index.FindByPrefix("كلم").Count().ShouldBe(index.Count);
            }

            reads++;
        }

        await writer;
        reads.ShouldBeGreaterThan(0);
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
    public void A_forgotten_word_is_learned_again_when_used()
    {
        var (learning, index) = Start();
        var engine = new SuggestionEngine(WordDictionary.Empty, index, new SuggestionOptions { MinUsesToSuggest = 2 }, _time);
        learning.Learn("كيبوردات");
        learning.Learn("كيبوردات");

        learning.Remove("كيبوردات");                       // right-click on the bar
        engine.Suggest("كيب").ShouldBeEmpty();

        learning.Learn("كيبوردات");
        learning.Learn("كيبوردات");
        engine.Suggest("كيب").Select(s => s.Word).ShouldBe(["كيبوردات"]);
        index.IsBlocked("كيبوردات").ShouldBeFalse();
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
    public void A_sequence_typed_three_times_becomes_a_learned_phrase_and_is_saved()
    {
        var (learning, _) = Start();
        var changes = 0;
        learning.LearnedPhrasesChanged += () => changes++;

        learning.LearnPhrases(["جزاك الله خيرا"]);
        learning.LearnPhrases(["جزاك الله خيرًا"]);
        learning.LearnedPhrases().ShouldBeEmpty();

        learning.LearnPhrases(["جزاك الله خيرا"]);
        changes.ShouldBe(1);
        learning.Flush();

        var (reloaded, _) = Start();
        var phrase = reloaded.LearnedPhrases().ShouldHaveSingleItem();
        phrase.Text.ShouldBe("جزاك الله خيرا"); // tashkeel removed, the spelling typed last
        phrase.Count.ShouldBe(3);
    }

    [Fact]
    public void Nothing_is_counted_while_paused_turned_off_or_with_a_blocked_word()
    {
        var (learning, _) = Start();
        learning.SetBlocked("قلم", blocked: true);

        for (var i = 0; i < 3; i++)
        {
            learning.LearnPhrases(["سيارة جديدة", "قلم احمر", "hello world", "و الله", "ذهبت الى البيت الكبير اليوم مساء"]);
        }

        learning.IsPaused = true;
        learning.LearnPhrases(["كتاب جديد", "كتاب جديد", "كتاب جديد"]);
        learning.IsPaused = false;
        learning.LearnsPhrases = false;
        learning.LearnPhrases(["كتاب جديد", "كتاب جديد", "كتاب جديد"]);

        learning.LearnedPhrases().ShouldBeEmpty();
    }

    [Fact]
    public void A_shorter_phrase_inside_a_learned_one_is_left_out_unless_typed_more_on_its_own()
    {
        var (learning, _) = Start();
        for (var i = 0; i < 3; i++)
        {
            learning.LearnPhrases(["السلام عليكم", "عليكم ورحمة", "السلام عليكم ورحمة"]);
        }

        learning.LearnedPhrases().Select(p => p.Text).ShouldBe(["السلام عليكم ورحمة"]);
        learning.LearnedPhrases(all: true).Count.ShouldBe(3); // Settings lists every one

        for (var i = 0; i < 3; i++)
        {
            learning.LearnPhrases(["السلام عليكم"]);
        }

        learning.LearnedPhrases().Select(p => p.Text).ShouldBe(["السلام عليكم", "السلام عليكم ورحمة"]);
    }

    [Fact]
    public void A_sequence_not_learned_expires_after_thirty_days_but_a_learned_phrase_stays()
    {
        var (learning, _) = Start();
        learning.LearnPhrases(["كتاب جديد", "قلم احمر"]);
        learning.LearnPhrases(["قلم احمر", "قلم احمر"]);
        learning.Flush();

        _time.Now = Now.AddDays(31);
        var (reloaded, _) = Start();
        reloaded.LearnPhrases(["كتاب جديد", "كتاب جديد"]);

        reloaded.LearnedPhrases().Select(p => p.Text).ShouldBe(["قلم احمر"]); // «كتاب جديد» restarted at 0
    }

    [Fact]
    public void Forgetting_and_clearing_phrases_reach_the_disk()
    {
        var (learning, _) = Start();
        for (var i = 0; i < 3; i++)
        {
            learning.LearnPhrases(["كتاب جديد", "قلم احمر"]);
        }

        learning.ForgetPhrase("كتاب جديد").ShouldBeTrue();
        learning.Flush();
        Start().Learning.LearnedPhrases().Select(p => p.Text).ShouldBe(["قلم احمر"]);

        learning.ClearPhrases();
        Start().Learning.LearnedPhrases().ShouldBeEmpty();
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
