using Itmam.Core.Text;

namespace Itmam.Core.Suggestions;

/// <summary>
/// Common spelling mistakes the dictionary cannot fix by itself (data/corrections.txt, user decision
/// 1.1): «لاكن» → «لكن», «انشاءالله» → «إن شاء الله». When the word typed is exactly a known mistake, its
/// corrections come first in the bar; nothing is replaced without Tab. Hamza seats and ة/ه, ى/ي at the
/// end need no entry: the dictionary already puts the right spelling first.
/// </summary>
public sealed class Corrections
{
    private readonly Dictionary<string, string[]> _byKey = new(StringComparer.Ordinal);

    private Corrections()
    {
    }

    public static Corrections Empty { get; } = new();

    public int Count => _byKey.Count;

    /// <summary>Lines <c>wrong = right | another right</c>; <c>#</c> starts a comment; invalid lines are skipped.</summary>
    public static Corrections Read(TextReader reader)
    {
        var corrections = new Corrections();
        while (reader.ReadLine() is { } raw)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == '#' || line.Split('=', 2) is not [var wrong, var right])
            {
                continue;
            }

            var word = ArabicText.StripIgnorable(wrong.Trim());
            var fixes = right.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (ArabicText.IsArabicWord(word) && fixes.Length > 0)
            {
                corrections._byKey.TryAdd(word, fixes);
            }
        }

        return corrections;
    }

    /// <summary>
    /// The corrections of the word typed (exact spelling, tashkeel ignored: «انشاء» and «إنشاء» are
    /// different words, so hamza seats are not folded), or none.
    /// </summary>
    public IReadOnlyList<string> Find(string typed)
    {
        var literal = ArabicText.StripIgnorable(typed);
        return literal.Length > 0 && _byKey.TryGetValue(literal, out var fixes) ? fixes : [];
    }
}
