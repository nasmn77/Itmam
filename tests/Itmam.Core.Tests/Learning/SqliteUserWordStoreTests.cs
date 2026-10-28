using Itmam.Core.Learning;

namespace Itmam.Core.Tests.Learning;

public sealed class SqliteUserWordStoreTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 28, 9, 0, 0, TimeSpan.Zero);
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "itmam-tests", Guid.NewGuid().ToString("N"));

    private string DatabasePath => Path.Combine(_directory, "user.db");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void Saved_words_survive_reopening_the_database()
    {
        using (var store = new SqliteUserWordStore(DatabasePath))
        {
            store.Save([new StoredWord("إتمام", 3, Now, false), new StoredWord("كلمة", 1, Now, true)], []);
        }

        using var reopened = new SqliteUserWordStore(DatabasePath);
        reopened.LoadAll().OrderBy(w => w.Word, StringComparer.Ordinal).ShouldBe(
            [new StoredWord("إتمام", 3, Now, false), new StoredWord("كلمة", 1, Now, true)]);
    }

    [Fact]
    public void Saving_updates_existing_words_and_removes_deleted_ones()
    {
        using var store = new SqliteUserWordStore(DatabasePath);
        store.Save([new StoredWord("إتمام", 1, Now, false), new StoredWord("كلمة", 1, Now, false)], []);

        store.Save([new StoredWord("إتمام", 5, Now.AddDays(1), false)], ["كلمة"]);

        store.LoadAll().ShouldBe([new StoredWord("إتمام", 5, Now.AddDays(1), false)]);
    }

    [Fact]
    public void Clear_removes_everything()
    {
        using var store = new SqliteUserWordStore(DatabasePath);
        store.Save([new StoredWord("إتمام", 1, Now, false)], []);

        store.Clear();

        store.LoadAll().ShouldBeEmpty();
    }

    [Fact]
    public void Corrupt_database_is_kept_as_a_backup_and_a_fresh_one_is_started()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllBytes(DatabasePath, [.. "this is not a SQLite database at all, just garbage"u8, .. new byte[4096]]);

        using var store = new SqliteUserWordStore(DatabasePath);

        store.RecoveredBackupPath.ShouldNotBeNull();
        File.Exists(store.RecoveredBackupPath).ShouldBeTrue();
        store.LoadAll().ShouldBeEmpty();
        store.Save([new StoredWord("إتمام", 1, Now, false)], []);
        store.LoadAll().Count.ShouldBe(1);
    }

    [Fact]
    public void Healthy_database_reports_no_recovery()
    {
        using var store = new SqliteUserWordStore(DatabasePath);

        store.RecoveredBackupPath.ShouldBeNull();
    }
}
