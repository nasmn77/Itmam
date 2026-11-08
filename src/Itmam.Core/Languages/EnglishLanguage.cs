using System.Globalization;
using System.Text;

namespace Itmam.Core.Languages;

/// <summary>
/// English (2.0): Latin letters, the apostrophe inside a word («don't»), letter case and accents folded
/// for matching («cafe» finds «café»), and suggestions in the case the user is typing («Lon» → «London»,
/// «LON» → «LONDON»).
/// </summary>
internal sealed class EnglishLanguage : LanguageProfile
{
    private const char Apostrophe = '\'';
    private const char RightSingleQuote = '’';

    // Lower case without diacritics for U+0000–U+024F (Latin, Latin-1, Extended-A and -B): one character for one.
    private static readonly char[] FoldTable = BuildFoldTable();

    private static readonly IReadOnlyDictionary<char, string> Neighbors =
        KeyboardNeighbors.Build(["qwertyuiop", "asdfghjkl", "zxcvbnm"], c => c);

    public override string KeyNeighbors(char keyLetter) => Neighbors.GetValueOrDefault(keyLetter, string.Empty);

    public override string KeyAlphabet => "abcdefghijklmnopqrstuvwxyz";

    public override string Id => "en";

    public override bool RightToLeft => false;

    public override IReadOnlySet<string> CommonPhraseStarts { get; } = new HashSet<string>(
        ["a", "an", "the", "i", "to", "of", "in", "on", "at", "and", "or", "but", "is", "it", "you", "we", "he", "she", "they",
         "my", "your", "for", "with", "this", "that", "so", "if", "not", "no", "be", "do", "as", "by", "from"],
        StringComparer.Ordinal);

    public override bool IsLetter(char c) => c < FoldTable.Length && char.IsLetter(c);

    public override bool IsIgnorable(char c) => CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark;

    public override bool IsInnerMark(char c) => c is Apostrophe or RightSingleQuote;

    // An apostrophe joins the letters around it; at the start of a word it is a quote (not a boundary
    // either: the word after it is tracked from its first letter).
    public override bool IsWordBoundary(char c) => !IsInnerMark(c) && base.IsWordBoundary(c);

    public override char Fold(char c) => c < FoldTable.Length ? FoldTable[c] : c == RightSingleQuote ? Apostrophe : char.ToLowerInvariant(c);

    public override string MatchCase(string suggestion, ReadOnlySpan<char> typed)
    {
        var letters = 0;
        var upper = 0;
        foreach (var c in typed)
        {
            if (char.IsLetter(c))
            {
                letters++;
                upper += char.IsUpper(c) ? 1 : 0;
            }
        }

        if (letters >= 2 && upper == letters)
        {
            return suggestion.ToUpperInvariant();
        }

        var first = typed.IndexOfAnyExcept(Apostrophe, RightSingleQuote);
        // A word with capitals of its own (iPhone, McDonald) keeps them.
        return first >= 0 && char.IsUpper(typed[first]) && suggestion.Length > 0 && !suggestion.Any(char.IsUpper)
            ? char.ToUpperInvariant(suggestion[0]) + suggestion[1..]
            : suggestion;
    }

    public override string DisplayKey(string word) => word.ToLowerInvariant();

    private static char[] BuildFoldTable()
    {
        var table = new char[0x250];
        for (var i = 0; i < table.Length; i++)
        {
            var c = char.ToLowerInvariant((char)i);
            var decomposed = c.ToString().Normalize(NormalizationForm.FormD);
            table[i] = decomposed.Length > 0 && decomposed[0] < 0x250 && char.IsLetter(decomposed[0]) ? decomposed[0] : c;
        }

        return table;
    }
}
