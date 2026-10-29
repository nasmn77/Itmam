using Itmam.Core.Text;

namespace Itmam.Core.Learning;

/// <summary>
/// Phrase learning (user decision 1.1, on by default): word sequences typed in a row are counted, and a
/// sequence typed <see cref="LearningOptions.PhraseMinUses"/> times becomes a suggested phrase.
/// </summary>
/// <remarks>
/// Counts are kept in user.db so a phrase repeated over several days is learned too (user decision);
/// a sequence not yet learned is deleted once it has not been typed for
/// <see cref="LearningOptions.PhraseCandidateLifetime"/>. Never in password fields, excluded or
/// elevated apps or full screen (the pipeline sees nothing there), never while learning is paused, and
/// never with a word on the blocklist or blocked by the user.
/// </remarks>
public sealed partial class LearningService
{
    private readonly Dictionary<string, PhraseEntry> _phrases = new(StringComparer.Ordinal);
    private readonly HashSet<string> _changedPhrases = new(StringComparer.Ordinal);
    private readonly HashSet<string> _removedPhrases = new(StringComparer.Ordinal);
    private DateTimeOffset _lastPrune;

    /// <summary>The set of learned phrases changed (learned, forgotten, cleared). Raised outside the lock.</summary>
    public event Action? LearnedPhrasesChanged;

    /// <summary>"Learn phrases from my typing" setting (default on).</summary>
    public bool LearnsPhrases { get; set; } = true;

    /// <summary>Normalized key of a phrase: its words' keys, one space apart.</summary>
    public static string PhraseKey(string text) =>
        string.Join(' ', text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(w => ArabicNormalizer.Normalize(ArabicText.StripIgnorable(w))));

    /// <summary>Sequences that ended with a finished word: count each one.</summary>
    public void LearnPhrases(IEnumerable<string> sequences)
    {
        if (IsPaused || !LearnsPhrases)
        {
            return;
        }

        var learnedOne = false;
        lock (_lock)
        {
            var now = _time.GetUtcNow();
            foreach (var sequence in sequences)
            {
                if (!IsLearnable(sequence))
                {
                    continue;
                }

                var key = PhraseKey(sequence);
                if (!_phrases.TryGetValue(key, out var entry))
                {
                    _phrases[key] = entry = new PhraseEntry(sequence, 0, now);
                }

                entry.Text = sequence; // the spelling typed last
                entry.Count++;
                entry.LastUsed = now;
                learnedOne |= entry.Count == _options.PhraseMinUses;
                MarkPhraseChanged(key);
            }

            if (_phrases.Count > _options.MaxPhraseCandidates + _options.MaxLearnedPhrases)
            {
                learnedOne |= PrunePhrases(now);
            }
        }

        if (learnedOne)
        {
            LearnedPhrasesChanged?.Invoke();
        }
    }

    /// <summary>
    /// The learned phrases to suggest, most used first. A phrase is left out when a longer learned one
    /// contains it and is typed nearly as often: after «السلام عليكم ورحمة الله» is learned, «عليكم ورحمة»
    /// alone is only noise, unless it is also typed elsewhere.
    /// </summary>
    /// <param name="all">Every learned phrase, none left out (the list in Settings).</param>
    public IReadOnlyList<StoredPhrase> LearnedPhrases(bool all = false)
    {
        lock (_lock)
        {
            var learned = _phrases
                .Where(p => p.Value.Count >= _options.PhraseMinUses && !HasBlockedWord(p.Value.Text))
                .ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
            var covered = new HashSet<string>(StringComparer.Ordinal);
            foreach (var (key, entry) in all ? [] : learned)
            {
                var words = key.Split(' ');
                for (var length = 2; length < words.Length; length++)
                {
                    for (var start = 0; start + length <= words.Length; start++)
                    {
                        var part = string.Join(' ', words, start, length);
                        if (learned.TryGetValue(part, out var shorter) && entry.Count * 4 >= shorter.Count * 3)
                        {
                            covered.Add(part);
                        }
                    }
                }
            }

            return [.. learned
                .Where(p => !covered.Contains(p.Key))
                .Select(p => new StoredPhrase(p.Key, p.Value.Text, p.Value.Count, p.Value.LastUsed))
                .OrderByDescending(p => p.Count)
                .ThenByDescending(p => p.Key.Count(c => c == ' '))
                .ThenByDescending(p => p.LastUsed)];
        }
    }

    /// <summary>A learned (suggested) phrase: the bar's menu offers to delete it.</summary>
    public bool IsLearnedPhrase(string text)
    {
        lock (_lock)
        {
            return _phrases.TryGetValue(PhraseKey(text), out var entry) && entry.Count >= _options.PhraseMinUses;
        }
    }

    /// <summary>Right-click on the bar, or "Delete" in Settings: the phrase is forgotten (learned again if typed again).</summary>
    public bool ForgetPhrase(string text)
    {
        lock (_lock)
        {
            var key = PhraseKey(text);
            if (!_phrases.Remove(key))
            {
                return false;
            }

            MarkPhraseRemoved(key);
        }

        LearnedPhrasesChanged?.Invoke();
        return true;
    }

    /// <summary>Deletes every learned phrase and every counted sequence, in memory and on disk, now.</summary>
    public void ClearPhrases()
    {
        lock (_saveLock)
        {
            lock (_lock)
            {
                _phrases.Clear();
                _changedPhrases.Clear();
                _removedPhrases.Clear();
            }

            _store.ClearPhrases();
        }

        LearnedPhrasesChanged?.Invoke();
    }

    // Under _lock (Load).
    private void LoadPhrases()
    {
        _phrases.Clear();
        foreach (var stored in _store.LoadPhrases())
        {
            _phrases[stored.Key] = new PhraseEntry(stored.Text, stored.Count, stored.LastUsed);
        }

        PrunePhrases(_time.GetUtcNow());
    }

    // Under _lock (Flush): the phrase changes to write, and the keys to delete.
    private (List<StoredPhrase> Changed, List<string> Removed) TakePhraseChanges()
    {
        var now = _time.GetUtcNow();
        if (now - _lastPrune > TimeSpan.FromDays(1))
        {
            PrunePhrases(now);
        }

        var changed = new List<StoredPhrase>(_changedPhrases.Count);
        foreach (var key in _changedPhrases)
        {
            if (_phrases.TryGetValue(key, out var entry))
            {
                changed.Add(new StoredPhrase(key, entry.Text, entry.Count, entry.LastUsed));
            }
        }

        List<string> removed = [.. _removedPhrases];
        _changedPhrases.Clear();
        _removedPhrases.Clear();
        return (changed, removed);
    }

    // Drops sequences not learned and not typed for a while, then the oldest ones beyond the limits.
    // Returns true when learned phrases were dropped. Callers hold _lock.
    private bool PrunePhrases(DateTimeOffset now)
    {
        _lastPrune = now;
        var expired = now - _options.PhraseCandidateLifetime;
        var candidates = new List<KeyValuePair<string, PhraseEntry>>();
        var learned = new List<KeyValuePair<string, PhraseEntry>>();
        foreach (var pair in _phrases)
        {
            if (pair.Value.Count >= _options.PhraseMinUses)
            {
                learned.Add(pair);
            }
            else if (pair.Value.LastUsed < expired)
            {
                RemovePhraseEntry(pair.Key);
            }
            else
            {
                candidates.Add(pair);
            }
        }

        foreach (var pair in candidates.OrderBy(p => p.Value.LastUsed).Take(candidates.Count - _options.MaxPhraseCandidates))
        {
            RemovePhraseEntry(pair.Key);
        }

        var dropLearned = learned.Count - _options.MaxLearnedPhrases;
        foreach (var pair in learned.OrderBy(p => p.Value.LastUsed).Take(dropLearned))
        {
            RemovePhraseEntry(pair.Key);
        }

        return dropLearned > 0;
    }

    private void RemovePhraseEntry(string key)
    {
        _phrases.Remove(key);
        MarkPhraseRemoved(key);
    }

    // 2–5 Arabic words, each long enough to be learned as a word, none of them blocked.
    private bool IsLearnable(string sequence)
    {
        var words = sequence.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return words.Length >= 2 && words.Length <= _options.MaxPhraseWords
            && words.All(w => UserWordIndex.Clean(w) is { } clean && clean.Count(ArabicText.IsArabicLetter) >= _options.MinLetters)
            && !HasBlockedWord(sequence);
    }

    // Blocked by the user, or on the shipped blocklist and not kept by the user on purpose.
    private bool HasBlockedWord(string text) =>
        text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Any(word => _index.Get(word) is { } entry
            ? entry.IsBlocked
            : _blocklist.IsBlocked(word));

    private void MarkPhraseChanged(string key)
    {
        _removedPhrases.Remove(key);
        _changedPhrases.Add(key);
        _saveTimer.Change(_options.SaveDelay, Timeout.InfiniteTimeSpan);
    }

    private void MarkPhraseRemoved(string key)
    {
        _changedPhrases.Remove(key);
        _removedPhrases.Add(key);
        _saveTimer.Change(_options.SaveDelay, Timeout.InfiniteTimeSpan);
    }

    private sealed class PhraseEntry(string text, int count, DateTimeOffset lastUsed)
    {
        public string Text { get; set; } = text;

        public int Count { get; set; } = count;

        public DateTimeOffset LastUsed { get; set; } = lastUsed;
    }
}
