using Itmam.Core.Suggestions;
using Itmam.Core.Text;

namespace Itmam.Core.Dictionary;

/// <summary>A word and its frequency (per million words) before it is added to a dictionary.</summary>
public readonly record struct WeightedWord(string Word, double PerMillion);

/// <summary>Attribution for one source used to build the dictionary (shown in the About window).</summary>
public sealed record DictionarySource(string Name, string License, string Url);

/// <summary>
/// Read-only dictionary held in flat arrays sorted by normalized key, so a prefix maps to one
/// contiguous index range found with two binary searches. A key can have several entries, one per
/// original spelling (أن / إن, على / علي), each with its own frequency; within a key, entries are
/// ordered by descending frequency.
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

    /// <summary>Most spellings kept for one normalized key.</summary>
    public const int MaxSpellingsPerKey = 4;

    /// <summary>
    /// A spelling less frequent than this share of its key's most frequent spelling is dropped:
    /// this removes common misspellings (انا for أنا) while keeping real distinct words (إن beside أن).
    /// </summary>
    public const double MinSpellingShare = 0.05;

    /// <summary>
    /// Builds a dictionary from words with their frequencies. Tashkeel is stripped (identical
    /// spellings are summed), spellings are grouped by normalized key, and each key keeps up to
    /// <see cref="MaxSpellingsPerKey"/> spellings whose frequency is at least
    /// <see cref="MinSpellingShare"/> of the key's most frequent spelling.
    /// </summary>
    /// <param name="words">Words with their frequencies.</param>
    /// <param name="sources">Attribution stored with the dictionary.</param>
    /// <param name="dropBareAlefVariants">
    /// Also drop a spelling that is exactly a more frequent spelling of the same key with the hamza
    /// left off its alef (ان for أن, انا for أنا, الى for إلى). Distinct words such as إن / أن or
    /// على / علي are unaffected because they do not differ only by a missing hamza.
    /// </param>
    public static WordDictionary Build(
        IEnumerable<WeightedWord> words,
        IReadOnlyList<DictionarySource>? sources = null,
        bool dropBareAlefVariants = false)
    {
        var spellings = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var (word, perMillion) in words)
        {
            var display = ArabicText.StripIgnorable(word);
            if (display.Length > 0 && perMillion > 0)
            {
                spellings[display] = spellings.GetValueOrDefault(display) + perMillion;
            }
        }

        var groups = spellings
            .GroupBy(pair => ArabicNormalizer.Normalize(pair.Key), StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal);

        var builder = new Builder(spellings.Count);
        foreach (var group in groups)
        {
            var ranked = group
                .OrderByDescending(pair => pair.Value)
                .ThenBy(pair => pair.Key, StringComparer.Ordinal)
                .ToArray();
            var threshold = ranked[0].Value * MinSpellingShare;
            var kept = new List<string>(MaxSpellingsPerKey);
            foreach (var (display, perMillion) in ranked)
            {
                if (kept.Count == MaxSpellingsPerKey || perMillion < threshold)
                {
                    break;
                }

                if (dropBareAlefVariants && kept.Any(better => IsBareAlefVariant(display, better)))
                {
                    continue;
                }

                kept.Add(display);
                builder.Add(group.Key, display, FrequencyScale.FromPerMillion(perMillion));
            }
        }

        return builder.ToDictionary(sources ?? []);
    }

    // True when candidate equals spelling except that some أ/إ/آ in spelling are a bare ا in candidate.
    private static bool IsBareAlefVariant(string candidate, string spelling)
    {
        if (candidate.Length != spelling.Length || candidate == spelling)
        {
            return false;
        }

        for (var i = 0; i < candidate.Length; i++)
        {
            if (candidate[i] != spelling[i] && !(candidate[i] == 'ا' && spelling[i] is 'أ' or 'إ' or 'آ'))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Index range [start, end) of all entries whose key starts with <paramref name="keyPrefix"/>.</summary>
    public (int Start, int End) FindPrefixRange(ReadOnlySpan<char> keyPrefix)
    {
        var start = LowerBound(keyPrefix, matchCountsAsGreater: true);
        var end = LowerBound(keyPrefix, matchCountsAsGreater: false);
        return (start, end);
    }

    /// <summary>Index of the first (most frequent) entry whose key equals <paramref name="key"/>, or -1.</summary>
    public int IndexOfKey(ReadOnlySpan<char> key)
    {
        var (start, end) = FindPrefixRange(key);
        return start < end && GetKey(start).SequenceEqual(key) ? start : -1;
    }

    /// <summary>Index of the entry with exactly this original spelling (tashkeel ignored), or -1.</summary>
    public int IndexOfWord(ReadOnlySpan<char> word)
    {
        var display = ArabicText.StripIgnorable(word);
        var key = ArabicNormalizer.Normalize(display);
        var index = IndexOfKey(key);
        if (index < 0)
        {
            return -1;
        }

        for (; index < Count && GetKey(index).SequenceEqual(key); index++)
        {
            if (GetDisplay(index).SequenceEqual(display))
            {
                return index;
            }
        }

        return -1;
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
