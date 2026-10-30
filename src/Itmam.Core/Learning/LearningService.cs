using System.Globalization;
using Itmam.Core.Dictionary.Building;
using Itmam.Core.Text;

namespace Itmam.Core.Learning;

public sealed record LearningOptions
{
    /// <summary>Shortest word learned automatically (user decision: 2, like the dictionary).</summary>
    public int MinLetters { get; init; } = 2;

    /// <summary>Changes are written to disk this long after the last one (and always on exit).</summary>
    public TimeSpan SaveDelay { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>Times a sequence must be typed before it is suggested as a phrase (user decision: 2, was 3).</summary>
    public int PhraseMinUses { get; init; } = 2;

    /// <summary>Longest sequence learned (user decision: 2–5 words).</summary>
    public int MaxPhraseWords { get; init; } = 5;

    /// <summary>A sequence not yet learned is deleted after this long without being typed (user decision: 30 days).</summary>
    public TimeSpan PhraseCandidateLifetime { get; init; } = TimeSpan.FromDays(30);

    /// <summary>Most sequences counted but not yet learned; the least recently typed go first.</summary>
    public int MaxPhraseCandidates { get; init; } = 20_000;

    /// <summary>Most learned phrases; the least recently typed go first.</summary>
    public int MaxLearnedPhrases { get; init; } = 2_000;
}

public enum LearnOutcome
{
    Learned,

    /// <summary>Learning is paused by the user.</summary>
    Paused,

    /// <summary>On the shipped blocklist and not added by the user: never learned automatically.</summary>
    Blocked,

    /// <summary>Not an Arabic word of at least <see cref="LearningOptions.MinLetters"/> letters.</summary>
    NotAWord,
}

public readonly record struct ImportResult(int Added, int Updated, int Skipped);

/// <summary>Statistics: suggestions and long phrases inserted, characters typed for the user, since when.</summary>
public readonly record struct UsageStats(long Completions, long CharactersSaved, DateTimeOffset Since);

/// <summary>
/// Learns the user's words (word → count and last use only) and, from 1.1, repeated word sequences
/// (see LearningService.Phrases.cs), and keeps them on disk.
/// </summary>
/// <remarks>
/// <see cref="Learn"/> is called for each finished word (typed and ended by a boundary, or accepted
/// from the bar). A word not yet known is saved with count 1; known words are incremented. Words on
/// the shipped blocklist are never learned automatically, but a word the user added or kept on
/// purpose is respected. Changes are saved in the background <see cref="LearningOptions.SaveDelay"/>
/// after the last one, and by <see cref="Flush"/> on exit.
/// All members are thread-safe. Changes to the <see cref="UserWordIndex"/> happen under
/// <see cref="SyncRoot"/>: whoever reads the index directly (the suggestion engine) must hold it too,
/// because "My words" in Settings changes words from the UI thread.
/// </remarks>
public sealed partial class LearningService : IDisposable
{
    private readonly UserWordIndex _index;
    private readonly IUserWordStore _store;
    private readonly Blocklist _blocklist;
    private readonly LearningOptions _options;
    private readonly TimeProvider _time;
    private readonly Lock _lock = new();
    private readonly Lock _saveLock = new();
    private readonly HashSet<string> _changed = new(StringComparer.Ordinal);
    private readonly HashSet<string> _removed = new(StringComparer.Ordinal);
    private readonly ITimer _saveTimer;
    private UsageStats _usage = new(0, 0, DateTimeOffset.UnixEpoch);
    private bool _usageChanged;

    public LearningService(UserWordIndex index, IUserWordStore store, Blocklist blocklist, LearningOptions? options = null, TimeProvider? time = null)
    {
        _index = index;
        _store = store;
        _blocklist = blocklist;
        _options = options ?? new LearningOptions();
        _time = time ?? TimeProvider.System;
        _saveTimer = _time.CreateTimer(_ => Flush(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    /// <summary>"Pause learning" setting: nothing new is learned while true.</summary>
    public bool IsPaused { get; set; }

    /// <summary>Held by every change to the index; hold it to read the index directly (reentrant).</summary>
    public Lock SyncRoot => _lock;

    /// <summary>A copy of every stored word ("My words" list), taken under the lock.</summary>
    public IReadOnlyList<StoredWord> Snapshot()
    {
        lock (_lock)
        {
            return [.. _index.All.Select(w => new StoredWord(w.Word, w.Count, w.LastUsed, w.IsBlocked))];
        }
    }

    /// <summary>A word the user typed or added (not blocked): the bar's menu offers to delete it.</summary>
    public bool IsLearnedWord(string word)
    {
        lock (_lock)
        {
            return _index.Get(word) is { IsBlocked: false };
        }
    }

    /// <summary>Statistics (user decision 1.1): numbers only, never what was typed.</summary>
    public UsageStats Usage
    {
        get
        {
            lock (_lock)
            {
                return _usage;
            }
        }
    }

    /// <summary>A suggestion or long phrase was inserted (or an insert undone: negative numbers).</summary>
    public void RecordUsage(int completions, int characters)
    {
        lock (_lock)
        {
            _usage = _usage with
            {
                Completions = Math.Max(0, _usage.Completions + completions),
                CharactersSaved = Math.Max(0, _usage.CharactersSaved + characters),
            };
            _usageChanged = true;
            _saveTimer.Change(_options.SaveDelay, Timeout.InfiniteTimeSpan);
        }
    }

    /// <summary>The statistics from a full backup.</summary>
    public void RestoreUsage(UsageStats usage)
    {
        lock (_lock)
        {
            _usage = usage;
            _usageChanged = true;
            _saveTimer.Change(_options.SaveDelay, Timeout.InfiniteTimeSpan);
        }
    }

    /// <summary>Starts the statistics again from now.</summary>
    public void ResetUsage()
    {
        lock (_lock)
        {
            _usage = new UsageStats(0, 0, _time.GetUtcNow());
            _usageChanged = true;
            _saveTimer.Change(_options.SaveDelay, Timeout.InfiniteTimeSpan);
        }
    }

    /// <summary>Loads the stored words into the index (call once at start-up).</summary>
    public void Load()
    {
        lock (_lock)
        {
            _index.Clear();
            foreach (var stored in _store.LoadAll())
            {
                _index.Set(stored.Word, stored.Count, stored.LastUsed, stored.Blocked);
            }

            LoadPhrases();
            var stats = _store.LoadStats();
            _usage = new UsageStats(
                stats.GetValueOrDefault("completions"),
                stats.GetValueOrDefault("characters"),
                stats.TryGetValue("since", out var since) ? DateTimeOffset.FromUnixTimeMilliseconds(since) : _time.GetUtcNow());
            _usageChanged = !stats.ContainsKey("since"); // remember when counting started
        }
    }

    /// <summary>A finished word: learn it (new words are saved with count 1).</summary>
    public LearnOutcome Learn(string word)
    {
        if (IsPaused)
        {
            return LearnOutcome.Paused;
        }

        if (UserWordIndex.Clean(word) is not { } clean || clean.Count(ArabicText.IsArabicLetter) < _options.MinLetters)
        {
            return LearnOutcome.NotAWord;
        }

        lock (_lock)
        {
            if (_index.Get(clean) is null && _blocklist.IsBlocked(clean))
            {
                return LearnOutcome.Blocked;
            }

            _index.RecordUse(clean, _time.GetUtcNow());
            MarkChanged(clean);
        }

        return LearnOutcome.Learned;
    }

    /// <summary>"My words" → Add: the user's choice is respected even for blocklisted words.</summary>
    /// <param name="word">The word (tashkeel is removed).</param>
    /// <param name="count">Uses to record; pass the suggestion threshold to make it suggestible at once.</param>
    public bool AddManually(string word, int count)
    {
        lock (_lock)
        {
            var existing = UserWordIndex.Clean(word) is { } clean ? _index.Get(clean) : null;
            var entry = _index.Set(word, Math.Max(count, existing?.Count ?? 0), _time.GetUtcNow());
            if (entry is null)
            {
                return false;
            }

            MarkChanged(entry.Word);
            return true;
        }
    }

    /// <summary>"My words" → Edit: replaces a word's spelling, keeping its usage.</summary>
    public bool Rename(string word, string newWord)
    {
        lock (_lock)
        {
            if (_index.Get(word) is not { } entry || UserWordIndex.Clean(newWord) is not { } clean)
            {
                return false;
            }

            var (count, lastUsed, blocked) = (entry.Count, entry.LastUsed, entry.IsBlocked);
            _index.Remove(entry.Word);
            MarkRemoved(entry.Word);
            var existing = _index.Get(clean);
            _index.Set(clean, Math.Max(count, existing?.Count ?? 0), lastUsed, blocked);
            MarkChanged(clean);
            return true;
        }
    }

    public bool Remove(string word)
    {
        lock (_lock)
        {
            if (_index.Get(word) is not { } entry)
            {
                return false;
            }

            _index.Remove(entry.Word);
            MarkRemoved(entry.Word);
            return true;
        }
    }

    /// <summary>Block (never suggest again) or unblock a word, learned or from the dictionary.</summary>
    public bool SetBlocked(string word, bool blocked)
    {
        lock (_lock)
        {
            var entry = _index.SetBlocked(word, blocked, _time.GetUtcNow());
            if (entry is null)
            {
                return false;
            }

            MarkChanged(entry.Word);
            return true;
        }
    }

    /// <summary>"Clear all learned data": empties memory and disk immediately.</summary>
    public void ClearAll()
    {
        lock (_saveLock)
        {
            lock (_lock)
            {
                _index.Clear();
                _changed.Clear();
                _removed.Clear();
            }

            _store.Clear();
        }
    }

    /// <summary>
    /// UTF-8 text, one word per line: <c>word&lt;TAB&gt;count</c>; blocked words as <c>!word</c>;
    /// lines starting with <c>#</c> are comments.
    /// </summary>
    public void Export(TextWriter writer)
    {
        List<(string Word, int Count, bool Blocked)> words;
        lock (_lock)
        {
            words = _index.All.Select(w => (w.Word, w.Count, w.IsBlocked)).ToList();
        }

        writer.WriteLine("# Itmam (إتمام) personal dictionary — word<TAB>count; !word = blocked");
        foreach (var (word, count, blocked) in words.OrderBy(w => w.Word, StringComparer.Ordinal))
        {
            writer.WriteLine(blocked ? $"!{word}" : $"{word}\t{count.ToString(CultureInfo.InvariantCulture)}");
        }
    }

    /// <summary>Reads the <see cref="Export"/> format (a plain word list works too: count 1). Counts are merged by maximum.</summary>
    public ImportResult Import(TextReader reader)
    {
        int added = 0, updated = 0, skipped = 0;
        var now = _time.GetUtcNow();
        lock (_lock)
        {
            while (reader.ReadLine() is { } raw)
            {
                var line = raw.Trim();
                if (line.Length == 0 || line[0] == '#')
                {
                    continue;
                }

                var blocked = line[0] == '!';
                var parts = (blocked ? line[1..] : line).Split(['\t', ' '], 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                var count = blocked ? 0 : 1; // a blocked word was not used, only blocked
                if (parts.Length == 0
                    || UserWordIndex.Clean(parts[0]) is not { } clean
                    || (parts.Length == 2 && (!int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out count) || count < 1)))
                {
                    skipped++;
                    continue;
                }

                var existing = _index.Get(clean);
                _index.Set(clean, Math.Max(count, existing?.Count ?? 0), existing?.LastUsed ?? now, blocked || (existing?.IsBlocked ?? false));
                MarkChanged(clean);
                if (existing is null)
                {
                    added++;
                }
                else
                {
                    updated++;
                }
            }
        }

        return new ImportResult(added, updated, skipped);
    }

    /// <summary>Writes pending changes to disk now (also called on exit and log-off).</summary>
    public void Flush()
    {
        lock (_saveLock)
        {
            List<StoredWord> changed;
            List<string> removed;
            (List<StoredPhrase> Changed, List<string> Removed) phrases;
            Dictionary<string, long>? stats = null;
            lock (_lock)
            {
                changed = _changed
                    .Select(word => _index.Get(word))
                    .OfType<UserWord>()
                    .Select(w => new StoredWord(w.Word, w.Count, w.LastUsed, w.IsBlocked))
                    .ToList();
                removed = [.. _removed];
                _changed.Clear();
                _removed.Clear();
                phrases = TakePhraseChanges();
                if (_usageChanged)
                {
                    stats = new Dictionary<string, long>(StringComparer.Ordinal)
                    {
                        ["completions"] = _usage.Completions,
                        ["characters"] = _usage.CharactersSaved,
                        ["since"] = _usage.Since.ToUnixTimeMilliseconds(),
                    };
                    _usageChanged = false;
                }
            }

            _store.Save(changed, removed);
            _store.SavePhrases(phrases.Changed, phrases.Removed);
            if (stats is not null)
            {
                _store.SaveStats(stats);
            }
        }
    }

    public void Dispose()
    {
        _saveTimer.Dispose();
        Flush();
    }

    // Callers hold _lock.
    private void MarkChanged(string word)
    {
        _removed.Remove(word);
        _changed.Add(word);
        _saveTimer.Change(_options.SaveDelay, Timeout.InfiniteTimeSpan);
    }

    private void MarkRemoved(string word)
    {
        _changed.Remove(word);
        _removed.Add(word);
        _saveTimer.Change(_options.SaveDelay, Timeout.InfiniteTimeSpan);
    }
}
