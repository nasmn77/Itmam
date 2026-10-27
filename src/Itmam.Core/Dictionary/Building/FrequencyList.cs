using System.Globalization;

namespace Itmam.Core.Dictionary.Building;

/// <summary>Reads "word count" lines (FrequencyWords format: word, space, number of occurrences).</summary>
public static class FrequencyList
{
    public static IEnumerable<(string Word, long Count)> Read(TextReader reader)
    {
        while (reader.ReadLine() is { } line)
        {
            var separator = line.LastIndexOfAny([' ', '\t']);
            if (separator <= 0)
            {
                continue;
            }

            var word = line.AsSpan(0, separator).Trim();
            if (word.IsEmpty
                || !long.TryParse(line.AsSpan(separator + 1), NumberStyles.None, CultureInfo.InvariantCulture, out var count)
                || count <= 0)
            {
                continue;
            }

            yield return (word.ToString(), count);
        }
    }
}
