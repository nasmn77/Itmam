using Itmam.Core.Text;

namespace Itmam.Core.Dictionary.Building;

/// <summary>
/// Words that must never be suggested. One word per line; blank lines and lines starting with
/// <c>#</c> are ignored. Matching uses the normalized key, so every spelling variant is blocked.
/// </summary>
public sealed class Blocklist
{
    private readonly HashSet<string> _keys;

    private Blocklist(HashSet<string> keys) => _keys = keys;

    public static Blocklist Empty { get; } = new([]);

    public int Count => _keys.Count;

    public static Blocklist Read(TextReader reader)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        while (reader.ReadLine() is { } line)
        {
            var word = line.Trim();
            if (word.Length > 0 && word[0] != '#')
            {
                keys.Add(ArabicNormalizer.Normalize(word));
            }
        }

        return new Blocklist(keys);
    }

    public bool IsBlocked(string word) => _keys.Contains(ArabicNormalizer.Normalize(word));
}
