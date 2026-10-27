namespace Itmam.Core.Dictionary;

/// <summary>
/// Best entries, by base frequency, for short prefixes whose dictionary range is large
/// (e.g. ال covers ~40% of the dictionary), precomputed once when the dictionary loads so a
/// keystroke never scans hundreds of thousands of entries.
/// </summary>
/// <remarks>
/// For each normalized key prefix of 1–<see cref="MaxPrefixLength"/> letters with at least
/// <see cref="MinRangeSize"/> entries, two lists are kept: the top entries of the whole key range,
/// and the top entries per literal (original-spelling) prefix inside that range. The suggestion
/// engine adds score bonuses only for literal matches and for entries whose key equals the typed
/// key; candidates from these two lists plus the equal-key entries therefore contain the exact
/// top-N of a full scan, as long as N plus the filtered-out entries (blocked words, the typed
/// word itself) stays within <see cref="CandidatesPerPrefix"/>.
/// </remarks>
internal sealed class PrefixCandidateCache
{
    public const int MaxPrefixLength = 3;
    public const int MinRangeSize = 512;
    public const int CandidatesPerPrefix = 64;

    private readonly Dictionary<string, int[]> _byKeyPrefix = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int[]> _byLiteralPrefix = new(StringComparer.Ordinal);

    private PrefixCandidateCache()
    {
    }

    public int PrefixCount => _byKeyPrefix.Count + _byLiteralPrefix.Count;

    public long CandidateCount => _byKeyPrefix.Values.Sum(v => (long)v.Length) + _byLiteralPrefix.Values.Sum(v => (long)v.Length);

    public static PrefixCandidateCache Build(WordDictionary dictionary)
    {
        var cache = new PrefixCandidateCache();
        for (var length = 1; length <= MaxPrefixLength; length++)
        {
            var index = 0;
            while (index < dictionary.Count)
            {
                var key = dictionary.GetKey(index);
                if (key.Length < length)
                {
                    index++;
                    continue;
                }

                // Keys are sorted, so the range of this prefix starts here.
                var prefix = key[..length];
                var (_, end) = dictionary.FindPrefixRange(prefix);
                if (end - index >= MinRangeSize)
                {
                    cache.AddRange(dictionary, prefix.ToString(), index, end, length);
                }

                index = end;
            }
        }

        return cache;
    }

    /// <summary>
    /// Candidate indices for a typed prefix, or false when the prefix is not cached (its range is
    /// small enough to scan). <paramref name="byLiteral"/> is empty when no entry starts with the
    /// typed letters literally.
    /// </summary>
    public bool TryGet(string keyPrefix, string literalPrefix, out int[] byKey, out int[] byLiteral)
    {
        byLiteral = [];
        if (keyPrefix.Length > MaxPrefixLength || !_byKeyPrefix.TryGetValue(keyPrefix, out byKey!))
        {
            byKey = [];
            return false;
        }

        if (_byLiteralPrefix.TryGetValue(literalPrefix, out var literal))
        {
            byLiteral = literal;
        }

        return true;
    }

    private void AddRange(WordDictionary dictionary, string keyPrefix, int start, int end, int length)
    {
        var byKey = new TopEntries();
        var byLiteral = new Dictionary<string, TopEntries>(StringComparer.Ordinal);
        var literalLookup = byLiteral.GetAlternateLookup<ReadOnlySpan<char>>();
        for (var i = start; i < end; i++)
        {
            var score = dictionary.GetBaseScore(i);
            byKey.Offer(i, score);

            // Normalization maps characters 1:1, so the display is at least as long as the key.
            var literal = dictionary.GetDisplay(i)[..length];
            if (!literalLookup.TryGetValue(literal, out var top))
            {
                top = new TopEntries();
                literalLookup[literal] = top;
            }

            top.Offer(i, score);
        }

        _byKeyPrefix[keyPrefix] = byKey.ToArray();
        foreach (var (literal, top) in byLiteral)
        {
            _byLiteralPrefix[literal] = top.ToArray();
        }
    }

    /// <summary>Keeps the <see cref="CandidatesPerPrefix"/> highest-scoring indices.</summary>
    private sealed class TopEntries
    {
        private readonly PriorityQueue<int, float> _heap = new(CandidatesPerPrefix + 1);

        public void Offer(int index, float score)
        {
            if (_heap.Count < CandidatesPerPrefix)
            {
                _heap.Enqueue(index, score);
            }
            else if (_heap.TryPeek(out _, out var lowest) && score > lowest)
            {
                _heap.EnqueueDequeue(index, score);
            }
        }

        public int[] ToArray() => _heap.UnorderedItems.Select(item => item.Element).Order().ToArray();
    }
}
