using Itmam.Core.Text;

namespace Itmam.Core.Dictionary.Building;

/// <summary>
/// Words that must never be suggested. One word per line; blank lines and lines starting with
/// <c>#</c> are ignored.
/// </summary>
/// <remarks>
/// Matching uses a loose key (the normalized form with ؤ/ئ treated as ء), so no spelling escapes
/// because of tashkeel, alef, ya, ta marbuta or hamza seat. Forms with an attached clitic are also
/// blocked, limited by word length to avoid false positives: words of 4+ letters with any clitic
/// (و ف ب ك ل ال وال بال فال كال لل), 3-letter words only after the article forms (ال وال بال فال كال لل),
/// and 2-letter words never (كس must not block ألكس or كسكس; خرا must not block فخرا).
/// </remarks>
public sealed class Blocklist
{
    private static readonly string[] Clitics = ["وال", "بال", "فال", "كال", "لل", "ال", "و", "ف", "ب", "ك", "ل"];
    private static readonly string[] ArticleClitics = ["وال", "بال", "فال", "كال", "لل", "ال"];

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
                keys.Add(LooseKey(word));
            }
        }

        return new Blocklist(keys);
    }

    public static Blocklist Load(string path)
    {
        using var reader = new StreamReader(path, System.Text.Encoding.UTF8);
        return Read(reader);
    }

    public bool IsBlocked(string word)
    {
        var key = LooseKey(word);
        if (_keys.Contains(key))
        {
            return true;
        }

        foreach (var clitic in Clitics)
        {
            if (key.Length <= clitic.Length || !key.StartsWith(clitic, StringComparison.Ordinal))
            {
                continue;
            }

            var stem = key[clitic.Length..];
            var allowed = stem.Length >= 4 || (stem.Length == 3 && ArticleClitics.Contains(clitic));
            if (allowed && _keys.Contains(stem))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Normalized form with every hamza seat (ؤ ئ) folded into ء.</summary>
    public static string LooseKey(string word) =>
        ArabicNormalizer.Normalize(word).Replace('ؤ', 'ء').Replace('ئ', 'ء');
}
