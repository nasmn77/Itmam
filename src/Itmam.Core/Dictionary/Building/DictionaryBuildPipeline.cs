namespace Itmam.Core.Dictionary.Building;

/// <summary>One input frequency list with its merge settings.</summary>
/// <param name="Source">Attribution stored in the dictionary file.</param>
/// <param name="Words">Raw (token, count) pairs.</param>
/// <param name="Weight">Relative weight when merging sources.</param>
/// <param name="MinCount">Tokens seen fewer times are dropped (filters typos in large corpora).</param>
public sealed record SourceInput(DictionarySource Source, IEnumerable<(string Word, long Count)> Words, double Weight = 1.0, long MinCount = 1);

public sealed record BuildStatistics(int SourceTokens, int Accepted, int Rejected, int BelowMinCount, int Blocked, int Entries);

/// <summary>
/// Cleans each source, converts its counts to per-million, merges sources with their weights,
/// removes blocked words and de-duplicates by normalized key (most frequent spelling wins).
/// </summary>
public static class DictionaryBuildPipeline
{
    public static (WordDictionary Dictionary, BuildStatistics Statistics) Build(
        IReadOnlyList<SourceInput> inputs,
        Blocklist blocklist,
        int? maxEntries = null,
        bool dropBareAlefVariants = false)
    {
        int tokens = 0, accepted = 0, rejected = 0, belowMin = 0, blocked = 0;
        var merged = new Dictionary<string, double>(StringComparer.Ordinal);
        var totalWeight = inputs.Sum(i => i.Weight);

        foreach (var input in inputs)
        {
            // Counts per cleaned spelling; tashkeel variants of the same word collapse here.
            var counts = new Dictionary<string, long>(StringComparer.Ordinal);
            long total = 0;
            foreach (var (token, count) in input.Words)
            {
                tokens++;
                if (count < input.MinCount)
                {
                    belowMin++;
                    continue;
                }

                if (!WordFilter.TryClean(token, out var word))
                {
                    rejected++;
                    continue;
                }

                if (blocklist.IsBlocked(word))
                {
                    blocked++;
                    continue;
                }

                accepted++;
                counts[word] = counts.GetValueOrDefault(word) + count;
                total += count;
            }

            if (total == 0)
            {
                continue;
            }

            var factor = input.Weight / totalWeight * 1_000_000.0 / total;
            foreach (var (word, count) in counts)
            {
                merged[word] = merged.GetValueOrDefault(word) + count * factor;
            }
        }

        var dictionary = WordDictionary.Build(
            merged.Select(pair => new WeightedWord(pair.Key, pair.Value)),
            inputs.Select(i => i.Source).ToArray(),
            dropBareAlefVariants);

        if (maxEntries is { } max && dictionary.Count > max)
        {
            dictionary = KeepMostFrequent(dictionary, max, dropBareAlefVariants);
        }

        return (dictionary, new BuildStatistics(tokens, accepted, rejected, belowMin, blocked, dictionary.Count));
    }

    private static WordDictionary KeepMostFrequent(WordDictionary dictionary, int max, bool dropBareAlefVariants)
    {
        var top = Enumerable.Range(0, dictionary.Count)
            .OrderByDescending(dictionary.GetFrequency)
            .Take(max)
            .Select(i => new WeightedWord(dictionary.GetDisplay(i).ToString(), FrequencyScale.ToPerMillion(dictionary.GetFrequency(i))));
        return WordDictionary.Build(top, dictionary.Sources, dropBareAlefVariants);
    }
}
