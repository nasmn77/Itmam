using Itmam.Core.Dictionary.Building;
using Itmam.Core.Languages;
using Itmam.Core.Learning;
using Itmam.Core.Settings;
using Microsoft.Data.Sqlite;

namespace Itmam.Core.Tests.Learning;

/// <summary>Words and phrases of several languages (2.0): storage, migration from 1.x, learning, backups.</summary>
public sealed class MultiLanguageLearningTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);
    private static readonly LanguageProfile Hebrew = LanguageRegistry.ForTag("he");
    private static readonly LanguageProfile French = LanguageRegistry.ForTag("fr");

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "itmam-tests", Guid.NewGuid().ToString("N"));
    private readonly List<IDisposable> _open = [];

    public void Dispose()
    {
        _open.ForEach(d => d.Dispose());
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private string DatabasePath(string name = "pc") => Path.Combine(_directory, name, "user.db");

    private LearningService Start(string name = "pc")
    {
        var store = new SqliteUserWordStore(DatabasePath(name));
        var learning = new LearningService(new UserWordIndex(), store, Blocklist.Empty,
            new LearningOptions { SaveDelay = TimeSpan.FromHours(1), PhraseMinUses = 2 }, new FixedTimeProvider(Now));
        learning.Load();
        _open.Add(learning);
        _open.Add(store);
        return learning;
    }

    [Fact]
    public void A_database_from_1_x_becomes_arabic_and_is_backed_up_first()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath())!);
        using (var old = new SqliteConnection($"Data Source={DatabasePath()};Pooling=False"))
        {
            old.Open();
            using var command = old.CreateCommand();
            command.CommandText =
                "CREATE TABLE words(word TEXT PRIMARY KEY NOT NULL, count INTEGER NOT NULL, last_used INTEGER NOT NULL, blocked INTEGER NOT NULL DEFAULT 0) WITHOUT ROWID;" +
                "CREATE TABLE phrases(key TEXT PRIMARY KEY NOT NULL, text TEXT NOT NULL, count INTEGER NOT NULL, last_used INTEGER NOT NULL) WITHOUT ROWID;" +
                "CREATE TABLE stats(name TEXT PRIMARY KEY NOT NULL, value INTEGER NOT NULL) WITHOUT ROWID;" +
                $"INSERT INTO words VALUES('إتمام', 4, {Now.ToUnixTimeMilliseconds()}, 0), ('قلم', 0, {Now.ToUnixTimeMilliseconds()}, 1);" +
                $"INSERT INTO phrases VALUES('مرحبا بكم', 'مرحبا بكم', 3, {Now.ToUnixTimeMilliseconds()});" +
                "INSERT INTO stats VALUES('completions', 7);" +
                "PRAGMA user_version = 3;";
            command.ExecuteNonQuery();
        }

        using (var store = new SqliteUserWordStore(DatabasePath()))
        {
            store.Languages().ShouldBe(["ar"]);
            store.LoadAll("ar").OrderBy(w => w.Word, StringComparer.Ordinal).ShouldBe(
                [new StoredWord("إتمام", 4, Now, false), new StoredWord("قلم", 0, Now, true)]);
            store.LoadPhrases("ar").ShouldBe([new StoredPhrase("مرحبا بكم", "مرحبا بكم", 3, Now)]);
            store.LoadStats()["completions"].ShouldBe(7);
            store.Save([new StoredWord("שלום", 1, Now, false)], [], "he"); // the new schema works at once
        }

        File.Exists(DatabasePath() + ".v3-backup").ShouldBeTrue();
        using var reopened = new SqliteUserWordStore(DatabasePath()); // already migrated: nothing happens again
        reopened.LoadAll("ar").Count.ShouldBe(2);
        reopened.LoadAll("he").ShouldBe([new StoredWord("שלום", 1, Now, false)]);
    }

    [Fact]
    public void The_store_keeps_languages_apart()
    {
        using var store = new SqliteUserWordStore(DatabasePath());
        store.Save([new StoredWord("taxi", 2, Now, false)], [], "fr");
        store.Save([new StoredWord("taxi", 5, Now, true)], [], "de");
        store.SavePhrases([new StoredPhrase("bon matin", "bon matin", 2, Now)], [], "fr");

        store.Languages().Order(StringComparer.Ordinal).ShouldBe(["de", "fr"]);
        store.LoadAll("fr").ShouldBe([new StoredWord("taxi", 2, Now, false)]);
        store.LoadAll("ar").ShouldBeEmpty();

        store.Save([], ["taxi"], "de");
        store.LoadAll("de").ShouldBeEmpty();
        store.LoadAll("fr").Count.ShouldBe(1);

        store.Clear("fr");
        store.ClearPhrases("fr");
        store.Languages().ShouldBeEmpty();
    }

    [Fact]
    public void A_language_with_only_phrases_not_learned_yet_has_nothing_to_show()
    {
        var learning = Start();
        learning.LearnPhrases(["בוקר טוב"], Hebrew); // typed once: counted, not learned
        learning.Languages.Select(l => l.Id).ShouldBe(["ar", "he"]); // kept by backups
        learning.LanguagesWithLearned.Select(l => l.Id).ShouldBe(["ar"]);

        learning.LearnPhrases(["בוקר טוב"], Hebrew);
        learning.LanguagesWithLearned.Select(l => l.Id).ShouldBe(["ar", "he"]);
    }

    [Fact]
    public void Each_language_learns_and_reloads_its_own_words_and_phrases()
    {
        var learning = Start();
        learning.Learn("שלום", Hebrew).ShouldBe(LearnOutcome.Learned);
        learning.Learn("כתבה", Hebrew);
        learning.Learn("שלום", Hebrew);
        learning.Learn("إتمام").ShouldBe(LearnOutcome.Learned);
        learning.Learn("שלום").ShouldBe(LearnOutcome.NotAWord); // not an Arabic word
        learning.LearnPhrases(["בוקר טוב", "בוקר טוב"], Hebrew);
        learning.Flush();

        var reloaded = Start();
        reloaded.Languages.Select(l => l.Id).ShouldBe(["ar", "he"]);
        reloaded.IndexFor(Hebrew).Get("שלום").ShouldNotBeNull().Count.ShouldBe(2);
        reloaded.Snapshot().Select(w => w.Word).ShouldBe(["إتمام"]);
        reloaded.LearnedPhrases(language: Hebrew).Select(p => p.Text).ShouldBe(["בוקר טוב"]);
        reloaded.LearnedPhrases().ShouldBeEmpty();
        reloaded.IsLearnedWord("שלום", Hebrew).ShouldBeTrue();
        reloaded.IsLearnedWord("שלום").ShouldBeFalse();
    }

    [Fact]
    public void Clearing_one_language_keeps_the_others()
    {
        var learning = Start();
        learning.Learn("إتمام");
        learning.Learn("bonjour", French);

        learning.ClearAll(French);
        learning.Flush();

        var reloaded = Start();
        reloaded.Snapshot().Count.ShouldBe(1);
        reloaded.Snapshot(French).ShouldBeEmpty();
    }

    [Fact]
    public void Export_puts_other_languages_after_a_language_line_and_import_reads_it_back()
    {
        var learning = Start();
        learning.Learn("إتمام");
        learning.Learn("bonjour", French);
        learning.SetBlocked("merde", blocked: true, French);

        var exported = new StringWriter();
        learning.Export(exported);
        var lines = exported.ToString().ReplaceLineEndings("\n").TrimEnd().Split('\n');
        lines.ShouldBe(["# Itmam (إتمام) personal dictionary — word<TAB>count; !word = blocked", "إتمام\t1", "# language: fr", "bonjour\t1", "!merde"]);

        var other = Start("other");
        other.Import(new StringReader(exported.ToString())).ShouldBe(new ImportResult(3, 0, 0));
        other.Snapshot().Select(w => w.Word).ShouldBe(["إتمام"]);
        other.Snapshot(French).Select(w => (w.Word, w.Blocked)).ShouldBe([("bonjour", false), ("merde", true)]);
    }

    [Fact]
    public void A_full_backup_carries_every_language()
    {
        var source = Start("source");
        source.Learn("إتمام");
        source.LearnPhrases(["مرحبا بكم", "مرحبا بكم"]);
        source.Learn("שלום", Hebrew);
        source.LearnPhrases(["בוקר טוב", "בוקר טוב"], Hebrew);

        using var file = new MemoryStream();
        FullBackup.Write(file, new AppSettings().Normalized(), source);
        file.Position = 0;
        var content = FullBackup.Read(file).ShouldNotBeNull();
        content.LearnedPhrases.ShouldBe([("مرحبا بكم", 2, "ar"), ("בוקר טוב", 2, "he")], ignoreOrder: true);

        var target = Start("target");
        target.Learn("bonjour", French);
        FullBackup.RestoreLearning(content, target);

        target.Snapshot().Select(w => w.Word).ShouldBe(["إتمام"]);
        target.Snapshot(Hebrew).Select(w => w.Word).ShouldBe(["שלום"]);
        target.Snapshot(French).ShouldBeEmpty(); // a restore replaces everything
        target.LearnedPhrases(language: Hebrew).Select(p => p.Text).ShouldBe(["בוקר טוב"]);
        target.LearnedPhrases().Select(p => p.Text).ShouldBe(["مرحبا بكم"]);
    }
}
