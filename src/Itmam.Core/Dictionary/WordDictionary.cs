using Itmam.Core.Suggestions;
using Itmam.Core.Text;

namespace Itmam.Core.Dictionary;

/// <summary>A word and its frequency (per million words) before it is added to a dictionary.</summary>
public readonly record struct WeightedWord(string Word, double PerMillion);

/// <summary>Attribution for one source used to build the dictionary (shown in the About window).</summary>
public sealed record DictionarySource(string Name, string License, string Url);

/// <summary>
/// Read-only dictionary held in flat arrays sorted by normalized key, so a prefix maps to one
/// contiguous index range found with two binary searches. One entry per normalized key; the entry
/// keeps the most frequent original spelling for display.
/// </summary>
/// <remarks>
/// Flat char pools plus offset arrays instead of an object trie keep 500k words in roughly 25 MB
/// and avoid allocating per lookup. Instances are immutable and safe to share across threads.
/// </remarks>
public sealed class WordDictionary
{
    private readonly char[] _keyChars;
    private readonly int[] _keyOffsets;
    private readonly char[] _displayChars;
    private readonly int[] _displayOffsets;
    private readonly uint[] _frequencies;
    private readonly float[] _baseScores;

    internal WordDictionary(
        char[] keyChars,
        int[] keyOffsets,
        char[] displayChars,
        int[] displayOffsets,
        uint[] frequencies,
        IReadOnlyList<DictionarySource> sources)
    {
        _keyChars = keyChars;
        _keyOffsets = keyOffsets;
        _displayChars = displayChars;
        _displayOffsets = displayOffsets;
        _frequencies = frequencies;
        Sources = sources;

        _baseScores = new float[frequencies.Length];
        for (var i = 0; i < frequencies.Length; i++)
        {
            _baseScores[i] = (float)Ranking.BaseScore(FrequencyScale.ToPerMillion(frequencies[i]));
        }
    }

    public static WordDictionary Empty { get; } = new([], [0], [], [0], [], []);

    public int Count => _frequencies.Length;

    public IReadOnlyList<DictionarySource> Sources { get; }

    public ReadOnlySpan<char> GetKey(int index) =>
        _keyChars.AsSpan(_keyOffsets[index], _keyOffsets[index + 1] - _keyOffsets[index]);

    public ReadOnlySpan<char> GetDisplay(int index) =>
        _displayChars.AsSpan(_displayOffsets[index], _displayOffsets[index + 1] - _displayOffsets[index]);

    /// <summary>Stored frequency in <see cref="FrequencyScale"/> units.</summary>
    public uint GetFrequency(int index) => _frequencies[index];

    /// <summary>Precomputed <c>log(perMillion + 1)</c>.</summary>
    public float GetBaseScore(int index) => _baseScores[index];

    /// <summary>
    /// Builds a dictionary from words with their frequencies: words are normalized, grouped by key
    /// (frequencies summed) and each group keeps its most frequent spelling.
    /// </summary>
    public static WordDictionary Build(IEnumerable<WeightedWord> words, IReadOnlyList<DictionarySource>? sources = null)
    {
        var groups = new Dictionary<string, (string Display, double DisplayFrequency, double Total)>(StringComparer.Ordinal);
        foreach (var (word, perMillion) in words)
        {
            var display = ArabicText.StripIgnorable(word);
            if (display.Length == 0 || perMillion <= 0)
            {
                continue;
            }

            var key = ArabicNormalizer.Normalize(display);
            if (groups.TryGetValue(key, out var group))
            {
                var keepDisplay = perMillion > group.DisplayFrequency
                    || (perMillion == group.DisplayFrequency && string.CompareOrdinal(display, group.Display) < 0);
                groups[key] = keepDisplay
                    ? (display, perMillion, group.Total + perMillion)
                    : (group.Display, group.DisplayFrequency, group.Total + perMillion);
            }
            else
            {
                groups[key] = (display, perMillion, perMillion);
            }
        }

        var keys = groups.Keys.ToArray();
        Array.Sort(keys, StringComparer.Ordinal);

        var builder = new Builder(keys.Length);
        foreach (var key in keys)
        {
            var group = groups[key];
            builder.Add(key, group.Display, FrequencyScale.FromPerMillion(group.Total));
        }

        return builder.ToDictionary(sources ?? []);
    }

    /// <summary>Index range [start, end) of all entries whose key starts with <paramref name="keyPrefix"/>.</summary>
    public (int Start, int End) FindPrefixRange(ReadOnlySpan<char> keyPrefix)
    {
        var start = LowerBound(keyPrefix, matchCountsAsGreater: true);
        var end = LowerBound(keyPrefix, matchCountsAsGreater: false);
        return (start, end);
    }

    /// <summary>Index of the entry whose key equals <paramref name="key"/>, or -1.</summary>
    public int IndexOfKey(ReadOnlySpan<char> key)
    {
        var (start, end) = FindPrefixRange(key);
        return start < end && GetKey(start).SequenceEqual(key) ? start : -1;
    }

    /// <summary>Index of the entry with exactly this original spelling (tashkeel ignored), or -1.</summary>
    public int IndexOfWord(ReadOnlySpan<char> word)
    {
        var display = ArabicText.StripIgnorable(word);
        var index = IndexOfKey(ArabicNormalizer.Normalize(display));
        return index >= 0 && GetDisplay(index).SequenceEqual(display) ? index : -1;
    }

    // First index whose key is >= prefix (matchCountsAsGreater) or > every key starting with prefix.
    private int LowerBound(ReadOnlySpan<char> prefix, bool matchCountsAsGreater)
    {
        int low = 0, high = Count;
        while (low < high)
        {
            var mid = (int)((uint)(low + high) >> 1);
            var comparison = CompareKeyToPrefix(GetKey(mid), prefix);
            var goRight = comparison < 0 || (comparison == 0 && !matchCountsAsGreater);
            if (goRight)
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

    // < 0: key sorts before every key with this prefix; 0: key starts with prefix; > 0: sorts after.
    private static int CompareKeyToPrefix(ReadOnlySpan<char> key, ReadOnlySpan<char> prefix)
    {
        var common = Math.Min(key.Length, prefix.Length);
        var comparison = key[..common].SequenceCompareTo(prefix[..common]);
        if (comparison != 0)
        {
            return comparison;
        }

        return key.Length < prefix.Length ? -1 : 0;
    }

    /// <summary>Accumulates entries in key order; used by <see cref="Build"/> and the file reader.</summary>
    internal sealed class Builder(int capacity)
    {
        private readonly List<char> _keyChars = new(capacity * 8);
        private readonly List<int> _keyOffsets = new(capacity + 1) { 0 };
        private readonly List<char> _displayChars = new(capacity * 8);
        private readonly List<int> _displayOffsets = new(capacity + 1) { 0 };
        private readonly List<uint> _frequencies = new(capacity);

        public void Add(ReadOnlySpan<char> key, ReadOnlySpan<char> display, uint frequency)
        {
            foreach (var c in key)
            {
                _keyChars.Add(c);
            }

            foreach (var c in display)
            {
                _displayChars.Add(c);
            }

            _keyOffsets.Add(_keyChars.Count);
            _displayOffsets.Add(_displayChars.Count);
            _frequencies.Add(Math.Max(frequency, 1u));
        }

        public WordDictionary ToDictionary(IReadOnlyList<DictionarySource> sources) => new(
            [.. _keyChars],
            [.. _keyOffsets],
            [.. _displayChars],
            [.. _displayOffsets],
            [.. _frequencies],
            sources);
    }
}
