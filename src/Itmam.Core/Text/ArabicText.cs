using System.Globalization;

namespace Itmam.Core.Text;

/// <summary>
/// Character classification for Arabic word tracking and matching.
/// </summary>
public static class ArabicText
{
    public const char Tatweel = 'ـ';

    /// <summary>
    /// Arabic letters that can be part of a word: U+0621–U+064A (minus tatweel) and the extended
    /// letters U+0671–U+06D3 (alef wasla, Persian/Urdu letters such as پ چ ژ گ ک ی).
    /// </summary>
    public static bool IsArabicLetter(char c) =>
        (c is >= 'ء' and <= 'ي' && c != Tatweel && char.IsLetter(c))
        || (c is >= 'ٱ' and <= 'ۓ' && char.IsLetter(c));

    /// <summary>Harakat and tanween U+064B–U+0652, plus the superscript (dagger) alef U+0670.</summary>
    public static bool IsTashkeel(char c) => c is (>= 'ً' and <= 'ْ') or 'ٰ';

    /// <summary>Marks that are kept in the typed text but ignored for matching.</summary>
    public static bool IsIgnorable(char c) => c == Tatweel || IsTashkeel(c);

    /// <summary>
    /// Characters that end a word and commit it: whitespace, digits (Latin and Arabic-Indic),
    /// punctuation (Latin and Arabic: ، ؛ ؟ ٪ …) and symbols.
    /// </summary>
    public static bool IsWordBoundary(char c)
    {
        if (char.IsWhiteSpace(c) || char.IsDigit(c))
        {
            return true;
        }

        return char.GetUnicodeCategory(c) switch
        {
            UnicodeCategory.ConnectorPunctuation
                or UnicodeCategory.DashPunctuation
                or UnicodeCategory.OpenPunctuation
                or UnicodeCategory.ClosePunctuation
                or UnicodeCategory.InitialQuotePunctuation
                or UnicodeCategory.FinalQuotePunctuation
                or UnicodeCategory.OtherPunctuation
                or UnicodeCategory.MathSymbol
                or UnicodeCategory.CurrencySymbol
                or UnicodeCategory.ModifierSymbol
                or UnicodeCategory.OtherSymbol
                or UnicodeCategory.Control => true,
            _ => false,
        };
    }

    /// <summary>Removes tashkeel and tatweel, keeping the original letters.</summary>
    public static string StripIgnorable(ReadOnlySpan<char> text)
    {
        Span<char> buffer = text.Length <= 128 ? stackalloc char[text.Length] : new char[text.Length];
        var length = 0;
        foreach (var c in text)
        {
            if (!IsIgnorable(c))
            {
                buffer[length++] = c;
            }
        }

        return length == text.Length && text.Length > 0 ? text.ToString() : new string(buffer[..length]);
    }

    /// <summary>True when the text is non-empty and contains only Arabic letters.</summary>
    public static bool IsArabicWord(ReadOnlySpan<char> text)
    {
        if (text.IsEmpty)
        {
            return false;
        }

        foreach (var c in text)
        {
            if (!IsArabicLetter(c))
            {
                return false;
            }
        }

        return true;
    }
}
