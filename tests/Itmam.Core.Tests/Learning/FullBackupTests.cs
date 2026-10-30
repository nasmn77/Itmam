using Itmam.Core.Dictionary.Building;
using Itmam.Core.Learning;
using Itmam.Core.Settings;
using Itmam.Core.Suggestions;

namespace Itmam.Core.Tests.Learning;

public sealed class FullBackupTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 30, 9, 0, 0, TimeSpan.Zero);
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "itmam-tests", Guid.NewGuid().ToString("N"));
    private readonly List<IDisposable> _open = [];

    public void Dispose()
    {
        _open.ForEach(d => d.Dispose());
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private LearningService Learning(string name)
    {
        var store = new SqliteUserWordStore(Path.Combine(_directory, name, "user.db"));
        var learning = new LearningService(new UserWordIndex(), store, Blocklist.Read(new StringReader("")),
            new LearningOptions { SaveDelay = TimeSpan.FromHours(1) }, new FixedTimeProvider(Now));
        learning.Load();
        _open.Add(learning);
        _open.Add(store);
        return learning;
    }

    [Fact]
    public void A_backup_restored_on_another_PC_replaces_everything_there()
    {
        var source = Learning("source");
        source.Learn("إتمام");
        source.Learn("إتمام");
        source.SetBlocked("قلم", blocked: true);
        source.LearnPhrases(["مرحبا بكم", "مرحبا بكم"]);
        source.RecordUsage(3, 21);
        var settings = new AppSettings
        {
            MaxSuggestions = 5,
            MyPhrases = ["مع خالص التحية"],
            TextShortcuts = [new TextShortcut("ع1", "السلام عليكم")],
            LongPhrases = [new LongPhrase("ترحيب", "عزيزي العميل،\nشكرًا لك.")],
        }.Normalized();

        using var file = new MemoryStream();
        FullBackup.Write(file, settings, source);
        file.Position = 0;
        var content = FullBackup.Read(file).ShouldNotBeNull();

        var target = Learning("target");
        target.Learn("قديم");
        target.LearnPhrases(["عبارة قديمة", "عبارة قديمة"]);
        FullBackup.RestoreLearning(content, target);

        content.Settings.MaxSuggestions.ShouldBe(5);
        content.Settings.MyPhrases.ShouldBe(["مع خالص التحية"]);
        content.Settings.TextShortcuts.ShouldBe([new TextShortcut("ع1", "السلام عليكم")]);
        content.Settings.LongPhrases.ShouldBe([new LongPhrase("ترحيب", "عزيزي العميل،\nشكرًا لك.")]);
        target.Snapshot().Select(w => (w.Word, w.Count, w.Blocked)).OrderBy(w => w.Word).ShouldBe([("إتمام", 2, false), ("قلم", 0, true)]);
        target.LearnedPhrases().Select(p => p.Text).ShouldBe(["مرحبا بكم"]);
        target.Usage.ShouldBe(new UsageStats(3, 21, Now));
    }

    [Fact]
    public void Another_file_is_not_taken_for_a_backup()
    {
        FullBackup.Read(new MemoryStream("not a zip"u8.ToArray())).ShouldBeNull();
    }
}
