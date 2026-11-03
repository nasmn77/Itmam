namespace Itmam.Core.Dictionary.Building;

/// <summary>
/// Puts the English contractions of FrequencyWords back together. Its tokenizer splits them at the
/// apostrophe («don't» is counted as «don» and «'t», «I'm» as «i» and «'m»), so the list has no «don't»
/// but has «don», «isn», «'t» … as words.
/// </summary>
/// <remarks>
/// A stem that only exists before n't («don», «isn», «didn» …) becomes its contraction with its own count.
/// The other suffixes are shared out between the words that take them in proportion to those words'
/// counts (an estimate: the list does not say which word each suffix followed); «can't» and «won't»
/// share what is left of n't. Tokens that start with an apostrophe are dropped; «i» is written «I».
/// </remarks>
public static class EnglishContractions
{
    // Stems that are not words on their own.
    private static readonly string[] NotStems =
        ["don", "isn", "didn", "doesn", "wasn", "aren", "weren", "haven", "hasn", "hadn", "wouldn", "couldn", "shouldn", "mustn", "needn", "ain"];

    // Suffix → the words it attaches to (lower case; «i» is capitalised afterwards).
    private static readonly (string Suffix, string[] Words)[] Shared =
    [
        ("'m", ["i"]),
        ("'re", ["you", "we", "they", "what", "who"]),
        ("'ll", ["i", "you", "he", "she", "it", "we", "they", "that", "there"]),
        ("'ve", ["i", "you", "we", "they", "could", "would", "should", "might", "must"]),
        ("'d", ["i", "you", "he", "she", "we", "they", "it"]),
        ("'s", ["it", "that", "he", "she", "what", "there", "here", "who", "where", "let", "how"]),
        ("'clock", ["o"]),
        ("'am", ["ma"]),
    ];

    public static IEnumerable<(string Word, long Count)> Rebuild(IEnumerable<(string Word, long Count)> words)
    {
        var counts = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var (word, count) in words)
        {
            counts[word] = counts.GetValueOrDefault(word) + count;
        }

        var result = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var (word, count) in counts)
        {
            if (word.Length > 0 && word[0] != '\'' && !NotStems.Contains(word))
            {
                result[word] = count;
            }
        }

        var negations = 0L;
        foreach (var stem in NotStems)
        {
            if (counts.TryGetValue(stem, out var count))
            {
                result[stem + "'t"] = result.GetValueOrDefault(stem + "'t") + count;
                negations += count;
            }
        }

        var restOfNot = Math.Max(0, counts.GetValueOrDefault("'t") - negations);
        foreach (var word in new[] { "can't", "won't" })
        {
            result[word] = result.GetValueOrDefault(word) + restOfNot / 2;
        }

        foreach (var (suffix, takers) in Shared)
        {
            var total = counts.GetValueOrDefault(suffix);
            var weights = takers.Select(w => counts.GetValueOrDefault(w)).ToArray();
            var sum = weights.Sum();
            for (var i = 0; i < takers.Length && sum > 0; i++)
            {
                var share = (long)(total * (double)weights[i] / sum);
                if (share > 0)
                {
                    result[takers[i] + suffix] = result.GetValueOrDefault(takers[i] + suffix) + share;
                }
            }
        }

        foreach (var (word, count) in result.OrderByDescending(p => p.Value).ThenBy(p => p.Key, StringComparer.Ordinal))
        {
            yield return (word == "i" || word.StartsWith("i'", StringComparison.Ordinal) ? "I" + word[1..] : word, count);
        }
    }
}
