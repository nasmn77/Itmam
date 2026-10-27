using Itmam.Core.Text;

namespace Itmam.Core.Learning;

/// <summary>A word the user typed, added or blocked. Only the word and its usage are stored.</summary>
public sealed class UserWord
{
    internal UserWord(string word, int count, DateTimeOffset lastUsed, bool blocked)
    {
        Word = word;
        Key = ArabicNormalizer.Normalize(word);
        Count = count;
        LastUsed = lastUsed;
        IsBlocked = blocked;
    }

    public string Word { get; }

    /// <summary>Normalized matching key of <see cref="Word"/>.</summary>
    public string Key { get; }

    public int Count { get; internal set; }

    public DateTimeOffset LastUsed { get; internal set; }

    /// <summary>Blocked words are never suggested, even when they are in the dictionary.</summary>
    public bool IsBlocked { get; internal set; }
}

/// <summary>
/// In-memory index of the user's personal words, searchable by normalized key prefix.
/// Not thread-safe: owned by the suggestion worker thread. Persistence is handled by the caller.
/// </summary>
public sealed class UserWordIndex
{
    private static readonly Comparison<UserWord> ByKey = (a, b) =>
    {
        var comparison = string.CompareOrdinal(a.Key, b.Key);
        return comparison != 0 ? comparison : string.CompareOrdinal(a.Word, b.Word);
    };

    private readonly Dictionary<string, UserWord> _byWord = new(StringComparer.Ordinal);
    private readonly List<UserWord> _sortedByKey = [];

    public int Count => _byWord.Count;

    public IReadOnlyList<UserWord> All => _sortedByKey;

    /// <summary>Strips tashkeel/tatweel; returns null unless the result is an Arabic word.</summary>
    public static string? Clean(string word)
    {
        var cleaned = ArabicText.StripIgnorable(word);
        return ArabicText.IsArabicWord(cleaned) ? cleaned : null;
    }

    public UserWord? Get(string word) => Clean(word) is { } cleaned ? _byWord.GetValueOrDefault(cleaned) : null;

    public bool IsBlocked(ReadOnlySpan<char> word) =>
        _byWord.GetAlternateLookup<ReadOnlySpan<char>>().TryGetValue(word, out var entry) && entry.IsBlocked;

    /// <summary>Records one use of a committed or accepted word: adds it with count 1 or increments it.</summary>
    public UserWord? RecordUse(string word, DateTimeOffset when)
    {
        if (Clean(word) is not { } cleaned)
        {
            return null;
        }

        if (_byWord.TryGetValue(cleaned, out var entry))
        {
            entry.Count++;
            entry.LastUsed = when;
            return entry;
        }

        return Insert(new UserWord(cleaned, 1, when, blocked: false));
    }

    /// <summary>Adds or replaces a word with explicit values (manual add, import, loading from storage).</summary>
    public UserWord? Set(string word, int count, DateTimeOffset lastUsed, bool blocked = false)
    {
        if (Clean(word) is not { } cleaned)
        {
            return null;
        }

        Remove(cleaned);
        return Insert(new UserWord(cleaned, Math.Max(0, count), lastUsed, blocked));
    }

    public UserWord? SetBlocked(string word, bool blocked, DateTimeOffset when)
    {
        if (Clean(word) is not { } cleaned)
        {
            return null;
        }

        if (_byWord.TryGetValue(cleaned, out var entry))
        {
            entry.IsBlocked = blocked;
            return entry;
        }

        return Insert(new UserWord(cleaned, 0, when, blocked));
    }

    public bool Remove(string word)
    {
        if (Clean(word) is not { } cleaned || !_byWord.Remove(cleaned, out var entry))
        {
            return false;
        }

        var index = _sortedByKey.BinarySearch(entry, Comparer<UserWord>.Create(ByKey));
        _sortedByKey.RemoveAt(index);
        return true;
    }

    public void Clear()
    {
        _byWord.Clear();
        _sortedByKey.Clear();
    }

    /// <summary>All entries whose normalized key starts with <paramref name="keyPrefix"/>.</summary>
    public IEnumerable<UserWord> FindByPrefix(string keyPrefix)
    {
        var index = LowerBound(keyPrefix);
        for (; index < _sortedByKey.Count; index++)
        {
            var entry = _sortedByKey[index];
            if (!entry.Key.StartsWith(keyPrefix, StringComparison.Ordinal))
            {
                yield break;
            }

            yield return entry;
        }
    }

    private UserWord Insert(UserWord entry)
    {
        _byWord.Add(entry.Word, entry);
        var index = _sortedByKey.BinarySearch(entry, Comparer<UserWord>.Create(ByKey));
        _sortedByKey.Insert(~index, entry);
        return entry;
    }

    private int LowerBound(string keyPrefix)
    {
        int low = 0, high = _sortedByKey.Count;
        while (low < high)
        {
            var mid = (low + high) >>> 1;
            if (string.CompareOrdinal(_sortedByKey[mid].Key, keyPrefix) < 0)
            {
                low = mid + 1;
            }
            else
            {
                high = mid;
            }
        }

        return low;
    }
}
