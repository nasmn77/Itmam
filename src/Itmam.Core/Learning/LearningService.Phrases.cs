using Itmam.Core.Languages;

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
/// never with a word on the blocklist or blocked by the user. Each language has its own phrases.
/// </remarks>
public sealed partial class LearningService
{
    /// <summary>The set of learned phrases changed (learned, forgotten, cleared). Raised outside the lock.</summary>
    public event Action? LearnedPhrasesChanged;

    /// <summary>"Learn phrases from my typing" setting (default on).</summary>
    public bool LearnsPhrases { get; set; } = true;

    /// <summary>Normalized key of a phrase: its words' keys, one space apart.</summary>
    public static string PhraseKey(string text, LanguageProfile? language = null)
    {
        language ??= LanguageProfile.Arabic;
        return string.Join(' ', text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(w => language.Normalize(language.StripIgnorable(w))));
    }

    /// <summary>Sequences that ended with a finished word: count each one.</summary>
    public void LearnPhrases(IEnumerable<string> sequences, LanguageProfile? language = null)
    {
        if (IsPaused || !LearnsPhrases)
        {
            return;
        }

        var learnedOne = false;
        lock (_lock)
        {
            var state = State(language);
            var now = _time.GetUtcNow();
            foreach (var sequence in sequences)
            {
                if (!IsLearnable(state, sequence))
                {
                    continue;
                }

                var key = PhraseKey(sequence, state.Language);
                if (!state.Phrases.TryGetValue(key, out var entry))
                {
                    state.Phrases[key] = entry = new PhraseEntry(sequence, 0, now);
                }

                entry.Text = sequence; // the spelling typed last
                entry.Count++;
                entry.LastUsed = now;
                learnedOne |= entry.Count == _options.PhraseMinUses;
                MarkPhraseChanged(state, key);
            }

            if (state.Phrases.Count > _options.MaxPhraseCandidates + _options.MaxLearnedPhrases)
            {
                learnedOne |= PrunePhrases(state, now);
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
    /// <param name="all">Every learned phrase, none left out.</param>
    /// <param name="language">Whose phrases.</param>
    public IReadOnlyList<StoredPhrase> LearnedPhrases(bool all = false, LanguageProfile? language = null)
    {
        lock (_lock)
        {
            var state = State(language);
            var learned = state.Phrases
                .Where(p => p.Value.Count >= _options.PhraseMinUses && !HasBlockedWord(state, p.Value.Text))
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

    /// <summary>
    /// Learned phrases from an exported file: each keeps the larger of its two counts. Returns how many
    /// were new. Respects blocked words like typing does.
    /// </summary>
    public int ImportPhrases(IEnumerable<(string Text, int Count)> phrases, LanguageProfile? language = null)
    {
        var added = 0;
        lock (_lock)
        {
            var state = State(language);
            var now = _time.GetUtcNow();
            foreach (var (text, count) in phrases)
            {
                if (!IsLearnable(state, text))
                {
                    continue;
                }

                var key = PhraseKey(text, state.Language);
                if (state.Phrases.TryGetValue(key, out var entry))
                {
                    entry.Count = Math.Max(entry.Count, count);
                }
                else
                {
                    state.Phrases[key] = new PhraseEntry(text, count, now);
                    added++;
                }

                MarkPhraseChanged(state, key);
            }
        }

        LearnedPhrasesChanged?.Invoke();
        return added;
    }

    /// <summary>A learned (suggested) phrase: the bar's menu offers to delete it.</summary>
    public bool IsLearnedPhrase(string text, LanguageProfile? language = null)
    {
        lock (_lock)
        {
            var state = State(language);
            return state.Phrases.TryGetValue(PhraseKey(text, state.Language), out var entry) && entry.Count >= _options.PhraseMinUses;
        }
    }

    /// <summary>Right-click on the bar, or "Delete" in Settings: the phrase is forgotten (learned again if typed again).</summary>
    /// <remarks>
    /// Its parts go with it unless they are typed much more on their own (see <see cref="LearnedPhrases"/>):
    /// otherwise deleting «مرحبا بكم في معرضنا» would only bring «مرحبا بكم» forward in its place.
    /// </remarks>
    public bool ForgetPhrase(string text, LanguageProfile? language = null)
    {
        lock (_lock)
        {
            var state = State(language);
            var key = PhraseKey(text, state.Language);
            if (!state.Phrases.Remove(key, out var entry))
            {
                return false;
            }

            MarkPhraseRemoved(state, key);
            var words = key.Split(' ');
            for (var length = 2; length < words.Length; length++)
            {
                for (var start = 0; start + length <= words.Length; start++)
                {
                    var part = string.Join(' ', words, start, length);
                    if (state.Phrases.TryGetValue(part, out var shorter) && entry.Count * 4 >= shorter.Count * 3)
                    {
                        RemovePhraseEntry(state, part);
                    }
                }
            }
        }

        LearnedPhrasesChanged?.Invoke();
        return true;
    }

    /// <summary>
    /// Deletes every learned phrase and every counted sequence of one language, or of all of them (null),
    /// in memory and on disk, now.
    /// </summary>
    public void ClearPhrases(LanguageProfile? language = null)
    {
        lock (_saveLock)
        {
            lock (_lock)
            {
                foreach (var state in language is null ? _languages.Values.ToArray() : [State(language)])
                {
                    state.Phrases.Clear();
                    state.ChangedPhrases.Clear();
                    state.RemovedPhrases.Clear();
                }
            }

            _store.ClearPhrases(language?.Id);
        }

        LearnedPhrasesChanged?.Invoke();
    }

    // Under _lock (Load).
    private void LoadPhrases(LanguageState state)
    {
        state.Phrases.Clear();
        foreach (var stored in _store.LoadPhrases(state.Language.Id))
        {
            state.Phrases[stored.Key] = new PhraseEntry(stored.Text, stored.Count, stored.LastUsed);
        }

        PrunePhrases(state, _time.GetUtcNow());
    }

    // Under _lock (Flush): the phrase changes to write, and the keys to delete.
    private (List<StoredPhrase> Changed, List<string> Removed) TakePhraseChanges(LanguageState state)
    {
        var now = _time.GetUtcNow();
        if (now - state.LastPrune > TimeSpan.FromDays(1))
        {
            PrunePhrases(state, now);
        }

        var changed = new List<StoredPhrase>(state.ChangedPhrases.Count);
        foreach (var key in state.ChangedPhrases)
        {
            if (state.Phrases.TryGetValue(key, out var entry))
            {
                changed.Add(new StoredPhrase(key, entry.Text, entry.Count, entry.LastUsed));
            }
        }

        List<string> removed = [.. state.RemovedPhrases];
        state.ChangedPhrases.Clear();
        state.RemovedPhrases.Clear();
        return (changed, removed);
    }

    // Drops sequences not learned and not typed for a while, then the oldest ones beyond the limits.
    // Returns true when learned phrases were dropped. Callers hold _lock.
    private bool PrunePhrases(LanguageState state, DateTimeOffset now)
    {
        state.LastPrune = now;
        var expired = now - _options.PhraseCandidateLifetime;
        var candidates = new List<KeyValuePair<string, PhraseEntry>>();
        var learned = new List<KeyValuePair<string, PhraseEntry>>();
        foreach (var pair in state.Phrases)
        {
            if (pair.Value.Count >= _options.PhraseMinUses)
            {
                learned.Add(pair);
            }
            else if (pair.Value.LastUsed < expired)
            {
                RemovePhraseEntry(state, pair.Key);
            }
            else
            {
                candidates.Add(pair);
            }
        }

        foreach (var pair in candidates.OrderBy(p => p.Value.LastUsed).Take(candidates.Count - _options.MaxPhraseCandidates))
        {
            RemovePhraseEntry(state, pair.Key);
        }

        var dropLearned = learned.Count - _options.MaxLearnedPhrases;
        foreach (var pair in learned.OrderBy(p => p.Value.LastUsed).Take(dropLearned))
        {
            RemovePhraseEntry(state, pair.Key);
        }

        return dropLearned > 0;
    }

    private void RemovePhraseEntry(LanguageState state, string key)
    {
        state.Phrases.Remove(key);
        MarkPhraseRemoved(state, key);
    }

    // 2–5 words, each long enough to be learned as a word, none of them blocked.
    private bool IsLearnable(LanguageState state, string sequence)
    {
        var words = sequence.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return words.Length >= 2 && words.Length <= _options.MaxPhraseWords
            && words.All(w => state.Language.CleanWord(w) is { } clean && state.Language.CountLetters(clean) >= _options.MinLetters)
            && !HasBlockedWord(state, sequence);
    }

    // Blocked by the user, or on the shipped blocklist and not kept by the user on purpose.
    private static bool HasBlockedWord(LanguageState state, string text) =>
        text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Any(word => state.Index.Get(word) is { } entry
            ? entry.IsBlocked
            : state.Blocklist.IsBlocked(word));

    private void MarkPhraseChanged(LanguageState state, string key)
    {
        state.RemovedPhrases.Remove(key);
        state.ChangedPhrases.Add(key);
        _saveTimer.Change(_options.SaveDelay, Timeout.InfiniteTimeSpan);
    }

    private void MarkPhraseRemoved(LanguageState state, string key)
    {
        state.ChangedPhrases.Remove(key);
        state.RemovedPhrases.Add(key);
        _saveTimer.Change(_options.SaveDelay, Timeout.InfiniteTimeSpan);
    }

    private sealed class PhraseEntry(string text, int count, DateTimeOffset lastUsed)
    {
        public string Text { get; set; } = text;

        public int Count { get; set; } = count;

        public DateTimeOffset LastUsed { get; set; } = lastUsed;
    }
}
