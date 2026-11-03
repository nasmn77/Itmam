using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Itmam.Core.Learning;

/// <summary>One stored learned word: the word and its usage only, nothing about where or with what it was typed.</summary>
public readonly record struct StoredWord(string Word, int Count, DateTimeOffset LastUsed, bool Blocked);

/// <summary>
/// One word sequence typed in a row (2–5 words, user decision 1.1): its normalized key, the spelling
/// typed last, how often and when it was last typed. Nothing about where it was typed.
/// </summary>
public readonly record struct StoredPhrase(string Key, string Text, int Count, DateTimeOffset LastUsed);

/// <summary>
/// Persistence for the user's learned words and phrases. From 2.0 every word and phrase belongs to a
/// language (its tag: "ar", "he" …); data from before 2.0 is Arabic.
/// </summary>
public interface IUserWordStore : IDisposable
{
    /// <summary>The languages that have words or phrases stored.</summary>
    IReadOnlyList<string> Languages();

    IReadOnlyList<StoredWord> LoadAll(string language = "ar");

    /// <summary>Upserts <paramref name="changed"/> and removes <paramref name="removed"/> in one transaction.</summary>
    void Save(IReadOnlyCollection<StoredWord> changed, IReadOnlyCollection<string> removed, string language = "ar");

    /// <summary>Deletes the words of one language, or of every language when null.</summary>
    void Clear(string? language = null);

    IReadOnlyList<StoredPhrase> LoadPhrases(string language = "ar");

    /// <summary>Upserts <paramref name="changed"/> and removes the keys in <paramref name="removed"/> in one transaction.</summary>
    void SavePhrases(IReadOnlyCollection<StoredPhrase> changed, IReadOnlyCollection<string> removed, string language = "ar");

    /// <summary>Deletes the phrases of one language, or of every language when null.</summary>
    void ClearPhrases(string? language = null);

    /// <summary>Named counters (statistics: numbers only).</summary>
    IReadOnlyDictionary<string, long> LoadStats();

    void SaveStats(IReadOnlyDictionary<string, long> stats);
}

/// <summary>
/// User words in an embedded SQLite database (<c>%LOCALAPPDATA%\Itmam\user.db</c>): one table of
/// word → count, last-used time, blocked flag, and (from 1.1, schema 2) one of word sequences →
/// spelling, count, last-used time; from 2.0 (schema 4) both are keyed by language too. WAL journaling
/// makes every committed transaction durable across crashes. A corrupt or unreadable file is moved
/// aside to <c>user.db.corrupt-&lt;timestamp&gt;</c> (kept as a backup) and a fresh database is started.
/// </summary>
/// <remarks>
/// A database from before 2.0 (no <c>lang</c> column) is copied to <c>user.db.v3-backup</c>, then
/// migrated in one transaction: every word and phrase becomes Arabic, the only language there was.
/// </remarks>
public sealed class SqliteUserWordStore : IUserWordStore
{
    private const int SchemaVersion = 4;
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

    public IReadOnlyList<string> Languages()
    {
        lock (_lock)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = "SELECT lang FROM words UNION SELECT lang FROM phrases";
            using var reader = command.ExecuteReader();
            var languages = new List<string>();
            while (reader.Read())
            {
                languages.Add(reader.GetString(0));
            }

            return languages;
        }
    }

    public IReadOnlyList<StoredWord> LoadAll(string language = "ar")
    {
        lock (_lock)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = "SELECT word, count, last_used, blocked FROM words WHERE lang = $l";
            command.Parameters.AddWithValue("$l", language);
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

    public void Save(IReadOnlyCollection<StoredWord> changed, IReadOnlyCollection<string> removed, string language = "ar")
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
                    "INSERT INTO words(lang, word, count, last_used, blocked) VALUES($l, $w, $c, $t, $b) " +
                    "ON CONFLICT(lang, word) DO UPDATE SET count = excluded.count, last_used = excluded.last_used, blocked = excluded.blocked";
                upsert.Parameters.AddWithValue("$l", language);
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
                delete.CommandText = "DELETE FROM words WHERE lang = $l AND word = $w";
                delete.Parameters.AddWithValue("$l", language);
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

    public void Clear(string? language = null)
    {
        lock (_lock)
        {
            Delete("words", language);
        }
    }

    public IReadOnlyList<StoredPhrase> LoadPhrases(string language = "ar")
    {
        lock (_lock)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = "SELECT key, text, count, last_used FROM phrases WHERE lang = $l";
            command.Parameters.AddWithValue("$l", language);
            using var reader = command.ExecuteReader();
            var phrases = new List<StoredPhrase>();
            while (reader.Read())
            {
                phrases.Add(new StoredPhrase(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetInt32(2),
                    DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(3))));
            }

            return phrases;
        }
    }

    public void SavePhrases(IReadOnlyCollection<StoredPhrase> changed, IReadOnlyCollection<string> removed, string language = "ar")
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
                    "INSERT INTO phrases(lang, key, text, count, last_used) VALUES($l, $k, $x, $c, $t) " +
                    "ON CONFLICT(lang, key) DO UPDATE SET text = excluded.text, count = excluded.count, last_used = excluded.last_used";
                upsert.Parameters.AddWithValue("$l", language);
                var key = upsert.Parameters.Add("$k", SqliteType.Text);
                var text = upsert.Parameters.Add("$x", SqliteType.Text);
                var count = upsert.Parameters.Add("$c", SqliteType.Integer);
                var lastUsed = upsert.Parameters.Add("$t", SqliteType.Integer);
                foreach (var entry in changed)
                {
                    key.Value = entry.Key;
                    text.Value = entry.Text;
                    count.Value = entry.Count;
                    lastUsed.Value = entry.LastUsed.ToUnixTimeMilliseconds();
                    upsert.ExecuteNonQuery();
                }
            }

            using (var delete = _connection.CreateCommand())
            {
                delete.Transaction = transaction;
                delete.CommandText = "DELETE FROM phrases WHERE lang = $l AND key = $k";
                delete.Parameters.AddWithValue("$l", language);
                var key = delete.Parameters.Add("$k", SqliteType.Text);
                foreach (var entry in removed)
                {
                    key.Value = entry;
                    delete.ExecuteNonQuery();
                }
            }

            transaction.Commit();
        }
    }

    public IReadOnlyDictionary<string, long> LoadStats()
    {
        lock (_lock)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = "SELECT name, value FROM stats";
            using var reader = command.ExecuteReader();
            var stats = new Dictionary<string, long>(StringComparer.Ordinal);
            while (reader.Read())
            {
                stats[reader.GetString(0)] = reader.GetInt64(1);
            }

            return stats;
        }
    }

    public void SaveStats(IReadOnlyDictionary<string, long> stats)
    {
        lock (_lock)
        {
            using var transaction = _connection.BeginTransaction();
            using var upsert = _connection.CreateCommand();
            upsert.Transaction = transaction;
            upsert.CommandText = "INSERT INTO stats(name, value) VALUES($n, $v) ON CONFLICT(name) DO UPDATE SET value = excluded.value";
            var name = upsert.Parameters.Add("$n", SqliteType.Text);
            var value = upsert.Parameters.Add("$v", SqliteType.Integer);
            foreach (var (key, number) in stats)
            {
                name.Value = key;
                value.Value = number;
                upsert.ExecuteNonQuery();
            }

            transaction.Commit();
        }
    }

    public void ClearPhrases(string? language = null)
    {
        lock (_lock)
        {
            Delete("phrases", language);
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _connection.Dispose();
        }
    }

    // Callers hold _lock. The table name is one of the two constants above, never user input.
    private void Delete(string table, string? language)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = language is null ? $"DELETE FROM {table}" : $"DELETE FROM {table} WHERE lang = $l";
        if (language is not null)
        {
            command.Parameters.AddWithValue("$l", language);
        }

        command.ExecuteNonQuery();
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

            if (HasTable(connection, "words") && !HasColumn(connection, "words", "lang"))
            {
                MigrateToLanguages(connection, path);
            }

            Execute(connection,
                "CREATE TABLE IF NOT EXISTS words(" +
                "lang TEXT NOT NULL, word TEXT NOT NULL, count INTEGER NOT NULL, last_used INTEGER NOT NULL, blocked INTEGER NOT NULL DEFAULT 0, " +
                "PRIMARY KEY(lang, word)) WITHOUT ROWID");
            Execute(connection,
                "CREATE TABLE IF NOT EXISTS phrases(" +
                "lang TEXT NOT NULL, key TEXT NOT NULL, text TEXT NOT NULL, count INTEGER NOT NULL, last_used INTEGER NOT NULL, " +
                "PRIMARY KEY(lang, key)) WITHOUT ROWID");
            Execute(connection, "CREATE TABLE IF NOT EXISTS stats(name TEXT PRIMARY KEY NOT NULL, value INTEGER NOT NULL) WITHOUT ROWID");
            Execute(connection, $"PRAGMA user_version = {SchemaVersion.ToString(CultureInfo.InvariantCulture)}");
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    // Schema 1–3 (before 2.0): a copy of the file first, then words and phrases become Arabic in one transaction.
    private static void MigrateToLanguages(SqliteConnection connection, string path)
    {
        var backup = path + ".v3-backup";
        File.Delete(backup);
        using (var copy = connection.CreateCommand())
        {
            copy.CommandText = "VACUUM INTO $p";
            copy.Parameters.AddWithValue("$p", backup);
            copy.ExecuteNonQuery();
        }

        using var transaction = connection.BeginTransaction();
        void Run(string sql)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }

        Run("CREATE TABLE words_v4(lang TEXT NOT NULL, word TEXT NOT NULL, count INTEGER NOT NULL, last_used INTEGER NOT NULL, " +
            "blocked INTEGER NOT NULL DEFAULT 0, PRIMARY KEY(lang, word)) WITHOUT ROWID");
        Run("INSERT INTO words_v4(lang, word, count, last_used, blocked) SELECT 'ar', word, count, last_used, blocked FROM words");
        Run("DROP TABLE words");
        Run("ALTER TABLE words_v4 RENAME TO words");
        if (HasTable(connection, "phrases", transaction))
        {
            Run("CREATE TABLE phrases_v4(lang TEXT NOT NULL, key TEXT NOT NULL, text TEXT NOT NULL, count INTEGER NOT NULL, " +
                "last_used INTEGER NOT NULL, PRIMARY KEY(lang, key)) WITHOUT ROWID");
            Run("INSERT INTO phrases_v4(lang, key, text, count, last_used) SELECT 'ar', key, text, count, last_used FROM phrases");
            Run("DROP TABLE phrases");
            Run("ALTER TABLE phrases_v4 RENAME TO phrases");
        }

        Run($"PRAGMA user_version = {SchemaVersion.ToString(CultureInfo.InvariantCulture)}");
        transaction.Commit();
    }

    private static bool HasTable(SqliteConnection connection, string table, SqliteTransaction? transaction = null)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT count(*) FROM sqlite_master WHERE type = 'table' AND name = $n";
        command.Parameters.AddWithValue("$n", table);
        return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture) > 0;
    }

    private static bool HasColumn(SqliteConnection connection, string table, string column)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM pragma_table_info($t) WHERE name = $c";
        command.Parameters.AddWithValue("$t", table);
        command.Parameters.AddWithValue("$c", column);
        return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture) > 0;
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
