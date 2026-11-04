using System.Globalization;
using Itmam.Core.Dictionary.Building;
using Itmam.Core.Languages;

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

    /// <summary>Most sequences counted but not yet learned (per language); the least recently typed go first.</summary>
    public int MaxPhraseCandidates { get; init; } = 20_000;

    /// <summary>Most learned phrases (per language); the least recently typed go first.</summary>
    public int MaxLearnedPhrases { get; init; } = 2_000;
}

public enum LearnOutcome
{
    Learned,

    /// <summary>Learning is paused by the user.</summary>
    Paused,

    /// <summary>On the shipped blocklist and not added by the user: never learned automatically.</summary>
    Blocked,

    /// <summary>Not a word of the language with at least <see cref="LearningOptions.MinLetters"/> letters.</summary>
    NotAWord,
}

public readonly record struct ImportResult(int Added, int Updated, int Skipped);

/// <summary>Statistics: suggestions and long phrases inserted, characters typed for the user, since when.</summary>
public readonly record struct UsageStats(long Completions, long CharactersSaved, DateTimeOffset Since);

/// <summary>
/// Learns the user's words (word → count and last use only) and, from 1.1, repeated word sequences
/// (see LearningService.Phrases.cs), and keeps them on disk. From 2.0 every word and phrase belongs to a
/// language: each language has its own <see cref="UserWordIndex"/>, phrases and blocklist.
/// </summary>
/// <remarks>
/// <see cref="Learn"/> is called for each finished word (typed and ended by a boundary, or accepted
/// from the bar). A word not yet known is saved with count 1; known words are incremented. Words on
/// the shipped blocklist are never learned automatically, but a word the user added or kept on
/// purpose is respected. Changes are saved in the background <see cref="LearningOptions.SaveDelay"/>
/// after the last one, and by <see cref="Flush"/> on exit.
/// Members that take a language use the <see cref="PrimaryLanguage"/> when it is null.
/// All members are thread-safe. Changes to the indexes happen under <see cref="SyncRoot"/>: whoever
/// reads an index directly (the suggestion engine) must hold it too, because "My words" in Settings
/// changes words from the UI thread.
/// </remarks>
public sealed partial class LearningService : IDisposable
{
    private const string LanguageHeader = "# language: ";

    private readonly Dictionary<string, LanguageState> _languages = new(StringComparer.Ordinal);
    private readonly LanguageState _primary;
    private readonly IUserWordStore _store;
    private readonly LearningOptions _options;
    private readonly TimeProvider _time;
    private readonly Lock _lock = new();
    private readonly Lock _saveLock = new();
    private readonly ITimer _saveTimer;
    private UsageStats _usage = new(0, 0, DateTimeOffset.UnixEpoch);
    private bool _usageChanged;

    /// <param name="index">The words of the primary language (Arabic in the app).</param>
    /// <param name="store">Where words, phrases and statistics are kept.</param>
    /// <param name="blocklist">The primary language's shipped blocklist.</param>
    /// <param name="options">Learning limits.</param>
    /// <param name="time">The clock.</param>
    public LearningService(UserWordIndex index, IUserWordStore store, Blocklist blocklist, LearningOptions? options = null, TimeProvider? time = null)
    {
        _primary = new LanguageState(index, blocklist);
        _languages[index.Language.Id] = _primary;
        _store = store;
        _options = options ?? new LearningOptions();
        _time = time ?? TimeProvider.System;
        _saveTimer = _time.CreateTimer(_ => Flush(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    /// <summary>"Pause learning" setting: nothing new is learned while true.</summary>
    public bool IsPaused { get; set; }

    /// <summary>Held by every change to the indexes; hold it to read an index directly (reentrant).</summary>
    public Lock SyncRoot => _lock;

    /// <summary>The language used when a member is given none.</summary>
    public LanguageProfile PrimaryLanguage => _primary.Language;

    /// <summary>The languages that have learned words or phrases (the primary one always), primary first.</summary>
    public IReadOnlyList<LanguageProfile> Languages
    {
        get
        {
            lock (_lock)
            {
                return [_primary.Language, .. _languages.Values
                    .Where(s => s != _primary && (s.Index.Count > 0 || s.Phrases.Count > 0))
                    .Select(s => s.Language)
                    .OrderBy(l => l.Id, StringComparer.Ordinal)];
            }
        }
    }

    /// <summary>
    /// The languages with something to show in Settings (the primary one always), primary first: learned
    /// words, or phrases typed at least <see cref="LearningOptions.PhraseMinUses"/> times. Unlike
    /// <see cref="Languages"/>, sequences counted but not learned yet do not count (they are never shown).
    /// </summary>
    public IReadOnlyList<LanguageProfile> LanguagesWithLearned
    {
        get
        {
            lock (_lock)
            {
                return [_primary.Language, .. _languages.Values
                    .Where(s => s != _primary && (s.Index.Count > 0 || s.Phrases.Values.Any(p => p.Count >= _options.PhraseMinUses)))
                    .Select(s => s.Language)
                    .OrderBy(l => l.Id, StringComparer.Ordinal)];
            }
        }
    }

    /// <summary>
    /// The words of a language, read by its suggestion engine (under <see cref="SyncRoot"/>); created empty
    /// the first time. A blocklist given here replaces the language's (none by default).
    /// </summary>
    public UserWordIndex IndexFor(LanguageProfile language, Blocklist? blocklist = null)
    {
        lock (_lock)
        {
            var state = State(language);
            if (blocklist is not null)
            {
                state.Blocklist = blocklist;
            }

            return state.Index;
        }
    }

    /// <summary>A copy of every stored word of a language ("My words" list), taken under the lock.</summary>
    public IReadOnlyList<StoredWord> Snapshot(LanguageProfile? language = null)
    {
        lock (_lock)
        {
            return [.. State(language).Index.All.Select(w => new StoredWord(w.Word, w.Count, w.LastUsed, w.IsBlocked))];
        }
    }

    /// <summary>A word the user typed or added (not blocked): the bar's menu offers to delete it.</summary>
    public bool IsLearnedWord(string word, LanguageProfile? language = null)
    {
        lock (_lock)
        {
            return State(language).Index.Get(word) is { IsBlocked: false };
        }
    }

    /// <summary>Statistics (user decision 1.1): numbers only, never what was typed. All languages together.</summary>
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

    /// <summary>Loads the stored words and phrases of every language (call once at start-up).</summary>
    public void Load()
    {
        lock (_lock)
        {
            foreach (var tag in _store.Languages().Append(_primary.Language.Id).Distinct(StringComparer.Ordinal))
            {
                var state = State(LanguageRegistry.ForTag(tag));
                state.Index.Clear();
                foreach (var stored in _store.LoadAll(tag))
                {
                    state.Index.Set(stored.Word, stored.Count, stored.LastUsed, stored.Blocked);
                }

                LoadPhrases(state);
            }

            var stats = _store.LoadStats();
            _usage = new UsageStats(
                stats.GetValueOrDefault("completions"),
                stats.GetValueOrDefault("characters"),
                stats.TryGetValue("since", out var since) ? DateTimeOffset.FromUnixTimeMilliseconds(since) : _time.GetUtcNow());
            _usageChanged = !stats.ContainsKey("since"); // remember when counting started
        }
    }

    /// <summary>A finished word: learn it (new words are saved with count 1).</summary>
    public LearnOutcome Learn(string word, LanguageProfile? language = null)
    {
        if (IsPaused)
        {
            return LearnOutcome.Paused;
        }

        lock (_lock)
        {
            var state = State(language);
            if (state.Language.CleanWord(word) is not { } clean || state.Language.CountLetters(clean) < _options.MinLetters)
            {
                return LearnOutcome.NotAWord;
            }

            if (state.Index.Get(clean) is null && state.Blocklist.IsBlocked(clean))
            {
                return LearnOutcome.Blocked;
            }

            state.Index.RecordUse(clean, _time.GetUtcNow());
            MarkChanged(state, clean);
        }

        return LearnOutcome.Learned;
    }

    /// <summary>"My words" → Add: the user's choice is respected even for blocklisted words.</summary>
    /// <param name="word">The word (tashkeel is removed).</param>
    /// <param name="count">Uses to record; pass the suggestion threshold to make it suggestible at once.</param>
    /// <param name="language">The word's language.</param>
    public bool AddManually(string word, int count, LanguageProfile? language = null)
    {
        lock (_lock)
        {
            var state = State(language);
            var existing = state.Language.CleanWord(word) is { } clean ? state.Index.Get(clean) : null;
            var entry = state.Index.Set(word, Math.Max(count, existing?.Count ?? 0), _time.GetUtcNow());
            if (entry is null)
            {
                return false;
            }

            MarkChanged(state, entry.Word);
            return true;
        }
    }

    /// <summary>"My words" → Edit: replaces a word's spelling, keeping its usage.</summary>
    public bool Rename(string word, string newWord, LanguageProfile? language = null)
    {
        lock (_lock)
        {
            var state = State(language);
            if (state.Index.Get(word) is not { } entry || state.Language.CleanWord(newWord) is not { } clean)
            {
                return false;
            }

            var (count, lastUsed, blocked) = (entry.Count, entry.LastUsed, entry.IsBlocked);
            state.Index.Remove(entry.Word);
            MarkRemoved(state, entry.Word);
            var existing = state.Index.Get(clean);
            state.Index.Set(clean, Math.Max(count, existing?.Count ?? 0), lastUsed, blocked);
            MarkChanged(state, clean);
            return true;
        }
    }

    public bool Remove(string word, LanguageProfile? language = null)
    {
        lock (_lock)
        {
            var state = State(language);
            if (state.Index.Get(word) is not { } entry)
            {
                return false;
            }

            state.Index.Remove(entry.Word);
            MarkRemoved(state, entry.Word);
            return true;
        }
    }

    /// <summary>Block (never suggest again) or unblock a word, learned or from the dictionary.</summary>
    public bool SetBlocked(string word, bool blocked, LanguageProfile? language = null)
    {
        lock (_lock)
        {
            var state = State(language);
            var entry = state.Index.SetBlocked(word, blocked, _time.GetUtcNow());
            if (entry is null)
            {
                return false;
            }

            MarkChanged(state, entry.Word);
            return true;
        }
    }

    /// <summary>"Clear all learned data": empties memory and disk immediately, for one language or all of them (null).</summary>
    public void ClearAll(LanguageProfile? language = null)
    {
        lock (_saveLock)
        {
            lock (_lock)
            {
                foreach (var state in language is null ? _languages.Values.ToArray() : [State(language)])
                {
                    state.Index.Clear();
                    state.Changed.Clear();
                    state.Removed.Clear();
                }
            }

            _store.Clear(language?.Id);
        }
    }

    /// <summary>
    /// UTF-8 text, one word per line: <c>word&lt;TAB&gt;count</c>; blocked words as <c>!word</c>;
    /// lines starting with <c>#</c> are comments. The primary language's words come first; every other
    /// language follows a <c># language: tag</c> line (older versions read it as a comment and skip its
    /// words, which are not Arabic). With a language given, only its words are written.
    /// </summary>
    public void Export(TextWriter writer, LanguageProfile? language = null)
    {
        List<(LanguageProfile Language, List<(string Word, int Count, bool Blocked)> Words)> languages;
        lock (_lock)
        {
            languages = [.. (language is null ? _languages.Values.ToArray() : [State(language)])
                .OrderBy(s => s == _primary ? 0 : 1)
                .ThenBy(s => s.Language.Id, StringComparer.Ordinal)
                .Select(s => (s.Language, s.Index.All.Select(w => (w.Word, w.Count, w.IsBlocked)).ToList()))
                .Where(l => l.Item2.Count > 0 || l.Language == (language ?? _primary.Language))];
        }

        writer.WriteLine("# Itmam (إتمام) personal dictionary — word<TAB>count; !word = blocked");
        foreach (var (wordsLanguage, words) in languages)
        {
            if (wordsLanguage != _primary.Language)
            {
                writer.WriteLine(LanguageHeader + wordsLanguage.Id);
            }

            foreach (var (word, count, blocked) in words.OrderBy(w => w.Word, StringComparer.Ordinal))
            {
                writer.WriteLine(blocked ? $"!{word}" : $"{word}\t{count.ToString(CultureInfo.InvariantCulture)}");
            }
        }
    }

    /// <summary>
    /// Reads the <see cref="Export"/> format (a plain word list works too: each word gets
    /// <paramref name="plainCount"/> uses). Counts are merged by maximum. Words before any <c># language:</c>
    /// line belong to <paramref name="language"/>.
    /// </summary>
    public ImportResult Import(TextReader reader, LanguageProfile? language = null, int plainCount = 1)
    {
        int added = 0, updated = 0, skipped = 0;
        var now = _time.GetUtcNow();
        lock (_lock)
        {
            var state = State(language);
            while (reader.ReadLine() is { } raw)
            {
                var line = raw.Trim();
                if (line.StartsWith(LanguageHeader, StringComparison.Ordinal) && line[LanguageHeader.Length..].Trim() is { Length: > 0 } tag)
                {
                    state = State(LanguageRegistry.ForTag(tag));
                    continue;
                }

                if (line.Length == 0 || line[0] == '#')
                {
                    continue;
                }

                var blocked = line[0] == '!';
                var parts = (blocked ? line[1..] : line).Split(['\t', ' '], 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                var count = blocked ? 0 : plainCount; // a blocked word was not used, only blocked
                if (parts.Length == 0
                    || state.Language.CleanWord(parts[0]) is not { } clean
                    || (parts.Length == 2 && (!int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out count) || count < 1)))
                {
                    skipped++;
                    continue;
                }

                var existing = state.Index.Get(clean);
                state.Index.Set(clean, Math.Max(count, existing?.Count ?? 0), existing?.LastUsed ?? now, blocked || (existing?.IsBlocked ?? false));
                MarkChanged(state, clean);
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

    /// <summary>
    /// Words found in a text the user chose to import (<see cref="TextWords.Count"/>), with how often each occurs:
    /// every one is kept with at least <paramref name="minCount"/> uses, so it is suggested at once (user decision
    /// 2.0). Words on the blocklist are skipped, as when typing; a word the user blocked stays blocked.
    /// </summary>
    public ImportResult ImportCounts(IReadOnlyDictionary<string, int> words, int minCount, LanguageProfile? language = null)
    {
        int added = 0, updated = 0, skipped = 0;
        var now = _time.GetUtcNow();
        lock (_lock)
        {
            var state = State(language);
            foreach (var (word, occurrences) in words)
            {
                var existing = state.Index.Get(word);
                if (existing is null && state.Blocklist.IsBlocked(word))
                {
                    skipped++;
                    continue;
                }

                var entry = state.Index.Set(word, Math.Max(Math.Max(occurrences, minCount), existing?.Count ?? 0), existing?.LastUsed ?? now, existing?.IsBlocked ?? false);
                if (entry is null)
                {
                    skipped++;
                    continue;
                }

                MarkChanged(state, entry.Word);
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
            var changes = new List<(string Language, List<StoredWord> Changed, List<string> Removed, (List<StoredPhrase> Changed, List<string> Removed) Phrases)>();
            Dictionary<string, long>? stats = null;
            lock (_lock)
            {
                foreach (var state in _languages.Values)
                {
                    var changed = state.Changed
                        .Select(word => state.Index.Get(word))
                        .OfType<UserWord>()
                        .Select(w => new StoredWord(w.Word, w.Count, w.LastUsed, w.IsBlocked))
                        .ToList();
                    List<string> removed = [.. state.Removed];
                    state.Changed.Clear();
                    state.Removed.Clear();
                    var phrases = TakePhraseChanges(state);
                    if (changed.Count > 0 || removed.Count > 0 || phrases.Changed.Count > 0 || phrases.Removed.Count > 0)
                    {
                        changes.Add((state.Language.Id, changed, removed, phrases));
                    }
                }

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

            foreach (var (language, changed, removed, phrases) in changes)
            {
                _store.Save(changed, removed, language);
                _store.SavePhrases(phrases.Changed, phrases.Removed, language);
            }

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

    // Callers hold _lock. The primary language when none is given; a new language starts empty.
    private LanguageState State(LanguageProfile? language)
    {
        if (language is null)
        {
            return _primary;
        }

        if (!_languages.TryGetValue(language.Id, out var state))
        {
            _languages[language.Id] = state = new LanguageState(new UserWordIndex(language), Blocklist.Empty);
        }

        return state;
    }

    // Callers hold _lock.
    private void MarkChanged(LanguageState state, string word)
    {
        state.Removed.Remove(word);
        state.Changed.Add(word);
        _saveTimer.Change(_options.SaveDelay, Timeout.InfiniteTimeSpan);
    }

    private void MarkRemoved(LanguageState state, string word)
    {
        state.Changed.Remove(word);
        state.Removed.Add(word);
        _saveTimer.Change(_options.SaveDelay, Timeout.InfiniteTimeSpan);
    }

    // One language's words, phrases and pending changes. Accessed under _lock.
    private sealed class LanguageState(UserWordIndex index, Blocklist blocklist)
    {
        public UserWordIndex Index { get; } = index;

        public LanguageProfile Language => Index.Language;

        public Blocklist Blocklist { get; set; } = blocklist;

        public HashSet<string> Changed { get; } = new(StringComparer.Ordinal);

        public HashSet<string> Removed { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, PhraseEntry> Phrases { get; } = new(StringComparer.Ordinal);

        public HashSet<string> ChangedPhrases { get; } = new(StringComparer.Ordinal);

        public HashSet<string> RemovedPhrases { get; } = new(StringComparer.Ordinal);

        public DateTimeOffset LastPrune { get; set; }
    }
}
