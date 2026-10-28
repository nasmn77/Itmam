using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Itmam.Core.Learning;

/// <summary>One stored learned word: the word and its usage only, nothing about where or with what it was typed.</summary>
public readonly record struct StoredWord(string Word, int Count, DateTimeOffset LastUsed, bool Blocked);

/// <summary>Persistence for the user's learned words.</summary>
public interface IUserWordStore : IDisposable
{
    IReadOnlyList<StoredWord> LoadAll();

    /// <summary>Upserts <paramref name="changed"/> and removes <paramref name="removed"/> in one transaction.</summary>
    void Save(IReadOnlyCollection<StoredWord> changed, IReadOnlyCollection<string> removed);

    void Clear();
}

/// <summary>
/// User words in an embedded SQLite database (<c>%LOCALAPPDATA%\Itmam\user.db</c>): one table of
/// word → count, last-used time, blocked flag. WAL journaling makes every committed transaction
/// durable across crashes. A corrupt or unreadable file is moved aside to
/// <c>user.db.corrupt-&lt;timestamp&gt;</c> (kept as a backup) and a fresh database is started.
/// </summary>
public sealed class SqliteUserWordStore : IUserWordStore
{
    private const int SchemaVersion = 1;
    private readonly Lock _lock = new();
    private readonly SqliteConnection _connection;

    public SqliteUserWordStore(string path)
    {
        Path = path;
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path))!);
        try
        {
            _connection = Open(path);
        }
        catch (SqliteException)
        {
            RecoveredBackupPath = MoveAside(path);
            _connection = Open(path);
        }
    }

    public string Path { get; }

    /// <summary>Set when the database was corrupt at start-up: where the damaged file was kept.</summary>
    public string? RecoveredBackupPath { get; }

    public IReadOnlyList<StoredWord> LoadAll()
    {
        lock (_lock)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = "SELECT word, count, last_used, blocked FROM words";
            using var reader = command.ExecuteReader();
            var words = new List<StoredWord>();
            while (reader.Read())
            {
                words.Add(new StoredWord(
                    reader.GetString(0),
                    reader.GetInt32(1),
                    DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(2)),
                    reader.GetInt64(3) != 0));
            }

            return words;
        }
    }

    public void Save(IReadOnlyCollection<StoredWord> changed, IReadOnlyCollection<string> removed)
    {
        if (changed.Count == 0 && removed.Count == 0)
        {
            return;
        }

        lock (_lock)
        {
            using var transaction = _connection.BeginTransaction();

            using (var upsert = _connection.CreateCommand())
            {
                upsert.Transaction = transaction;
                upsert.CommandText =
                    "INSERT INTO words(word, count, last_used, blocked) VALUES($w, $c, $t, $b) " +
                    "ON CONFLICT(word) DO UPDATE SET count = excluded.count, last_used = excluded.last_used, blocked = excluded.blocked";
                var word = upsert.Parameters.Add("$w", SqliteType.Text);
                var count = upsert.Parameters.Add("$c", SqliteType.Integer);
                var lastUsed = upsert.Parameters.Add("$t", SqliteType.Integer);
                var blocked = upsert.Parameters.Add("$b", SqliteType.Integer);
                foreach (var entry in changed)
                {
                    word.Value = entry.Word;
                    count.Value = entry.Count;
                    lastUsed.Value = entry.LastUsed.ToUnixTimeMilliseconds();
                    blocked.Value = entry.Blocked ? 1 : 0;
                    upsert.ExecuteNonQuery();
                }
            }

            using (var delete = _connection.CreateCommand())
            {
                delete.Transaction = transaction;
                delete.CommandText = "DELETE FROM words WHERE word = $w";
                var word = delete.Parameters.Add("$w", SqliteType.Text);
                foreach (var entry in removed)
                {
                    word.Value = entry;
                    delete.ExecuteNonQuery();
                }
            }

            transaction.Commit();
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            Execute(_connection, "DELETE FROM words");
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _connection.Dispose();
        }
    }

    private static SqliteConnection Open(string path)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false, // the file can be moved aside and closed deterministically
        }.ToString());

        try
        {
            connection.Open();
            Execute(connection, "PRAGMA journal_mode = WAL");
            Execute(connection, "PRAGMA synchronous = NORMAL");

            // quick_check reads every page: a damaged file fails here instead of later while typing.
            using (var check = connection.CreateCommand())
            {
                check.CommandText = "PRAGMA quick_check";
                if (check.ExecuteScalar() is not string result || result != "ok")
                {
                    throw new SqliteException("Database integrity check failed.", 11); // SQLITE_CORRUPT
                }
            }

            Execute(connection,
                "CREATE TABLE IF NOT EXISTS words(" +
                "word TEXT PRIMARY KEY NOT NULL, count INTEGER NOT NULL, last_used INTEGER NOT NULL, blocked INTEGER NOT NULL DEFAULT 0" +
                ") WITHOUT ROWID");
            Execute(connection, $"PRAGMA user_version = {SchemaVersion.ToString(CultureInfo.InvariantCulture)}");
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    // Keeps the damaged database (and its WAL/SHM files) as a backup and frees the path.
    private static string MoveAside(string path)
    {
        SqliteConnection.ClearAllPools();
        var backup = $"{path}.corrupt-{DateTime.Now:yyyyMMdd-HHmmss}";
        File.Move(path, backup, overwrite: true);
        foreach (var suffix in new[] { "-wal", "-shm" })
        {
            if (File.Exists(path + suffix))
            {
                File.Move(path + suffix, backup + suffix, overwrite: true);
            }
        }

        return backup;
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
