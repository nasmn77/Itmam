using Itmam.Core.Languages;

namespace Itmam.Core.Dictionary.Building;

/// <summary>
/// Words that must never be suggested. One word per line; blank lines and lines starting with
/// <c>#</c> are ignored.
/// </summary>
/// <remarks>
/// Matching uses the language's loose key (Arabic: the normalized form with ؤ/ئ treated as ء), so no
/// spelling escapes because of tashkeel, alef, ya, ta marbuta or hamza seat. Forms with an attached
/// clitic are also blocked, limited by word length to avoid false positives
/// (<see cref="LanguageProfile.BlocklistStems"/>).
/// </remarks>
public sealed class Blocklist
{
    private readonly HashSet<string> _keys;
    private readonly LanguageProfile _language;

    private Blocklist(HashSet<string> keys, LanguageProfile language) => (_keys, _language) = (keys, language);

    public static Blocklist Empty { get; } = new([], LanguageProfile.Arabic);

    public int Count => _keys.Count;

    public static Blocklist Read(TextReader reader, LanguageProfile? language = null)
    {
        language ??= LanguageProfile.Arabic;
        var keys = new HashSet<string>(StringComparer.Ordinal);
        while (reader.ReadLine() is { } line)
        {
            var word = line.Trim();
            if (word.Length > 0 && word[0] != '#')
            {
                keys.Add(language.LooseKey(word));
            }
        }

        return new Blocklist(keys, language);
    }

    public static Blocklist Load(string path, LanguageProfile? language = null)
    {
        using var reader = new StreamReader(path, System.Text.Encoding.UTF8);
        return Read(reader, language);
    }

    public bool IsBlocked(string word)
    {
        var key = _language.LooseKey(word);
        return _keys.Contains(key) || _language.BlocklistStems(key).Any(_keys.Contains);
    }

    /// <summary>The key a word is matched by (Arabic: normalized, every hamza seat ؤ ئ folded into ء).</summary>
    public static string LooseKey(string word, LanguageProfile? language = null) =>
        (language ?? LanguageProfile.Arabic).LooseKey(word);
}
