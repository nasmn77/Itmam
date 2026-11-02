using System.Collections.Concurrent;
using System.Globalization;

namespace Itmam.Core.Languages;

/// <summary>How much Itmam does for a language.</summary>
public enum LanguageSupport
{
    /// <summary>A shipped dictionary and tuned rules (Arabic).</summary>
    Full,

    /// <summary>No dictionary: the user's learned words and phrases, text shortcuts and My phrases.</summary>
    LearningOnly,

    /// <summary>
    /// Typed through an input method editor (Chinese, Japanese, Korean …) or written without spaces or with
    /// marks that make up the letters (Thai, Devanagari …): Itmam stays silent.
    /// </summary>
    Unsupported,
}

/// <summary>
/// Maps a keyboard layout to the language being typed, and a stored language tag back to its profile.
/// There is one profile instance per language, so profiles can be compared by reference.
/// </summary>
public static class LanguageRegistry
{
    private const int PrimaryLanguageMask = 0x3FF;
    private const int LangArabic = 0x01;

    // Primary language IDs Itmam does not track: input method editors (zh, ja, ko, am, ti, vi), scripts
    // without spaces between words (th, lo, km, my, bo), and Indic scripts whose vowel signs are marks.
    private static readonly HashSet<int> UnsupportedPrimaryIds =
    [
        0x04, 0x11, 0x12, 0x5E, 0x73, 0x2A,
        0x1E, 0x54, 0x53, 0x55, 0x51,
        0x39, 0x45, 0x46, 0x47, 0x48, 0x49, 0x4A, 0x4B, 0x4C, 0x4D, 0x4E, 0x4F, 0x57, 0x5B, 0x61,
    ];

    private static readonly ConcurrentDictionary<string, LanguageProfile> ByTag = new(StringComparer.Ordinal)
    {
        [LanguageProfile.Arabic.Id] = LanguageProfile.Arabic,
    };

    /// <summary>
    /// Accepts the letters of every script: for text that belongs to no particular language (My phrases,
    /// text shortcut keys) when it is not Arabic.
    /// </summary>
    public static LanguageProfile AnyScript { get; } = new LearningOnlyLanguage("*", rightToLeft: false);

    /// <summary>The language of a keyboard layout (HKL), or null when Itmam stays silent with it.</summary>
    public static LanguageProfile? ForLayout(nint keyboardLayout) => ForLanguageId((int)(keyboardLayout & 0xFFFF));

    /// <summary>The language of a Windows language ID (LANGID), or null when it is not supported.</summary>
    public static LanguageProfile? ForLanguageId(int languageId)
    {
        var primary = languageId & PrimaryLanguageMask;
        if (primary == LangArabic)
        {
            return LanguageProfile.Arabic;
        }

        if (UnsupportedPrimaryIds.Contains(primary) || languageId == 0)
        {
            return null;
        }

        try
        {
            return ForTag(CultureInfo.GetCultureInfo(languageId).TwoLetterISOLanguageName);
        }
        catch (CultureNotFoundException)
        {
            return ForTag($"x{primary:x2}");
        }
    }

    /// <summary>The profile of a stored language tag ("ar", "he" …); unknown tags are learning-only.</summary>
    public static LanguageProfile ForTag(string tag) =>
        ByTag.GetOrAdd(tag, static t => new LearningOnlyLanguage(t, IsRightToLeft(t)));

    public static LanguageSupport Support(LanguageProfile? language) => language switch
    {
        null => LanguageSupport.Unsupported,
        ArabicLanguage => LanguageSupport.Full,
        _ => LanguageSupport.LearningOnly,
    };

    /// <summary>
    /// Arabic when the text's first word is Arabic, otherwise <see cref="AnyScript"/>: how text that
    /// belongs to no particular language is cleaned and compared.
    /// </summary>
    public static LanguageProfile ForText(string text)
    {
        var first = text.AsSpan().Trim();
        var space = first.IndexOfAny(' ', '\t');
        if (space >= 0)
        {
            first = first[..space];
        }

        return LanguageProfile.Arabic.IsWord(LanguageProfile.Arabic.StripIgnorable(first)) ? LanguageProfile.Arabic : AnyScript;
    }

    private static bool IsRightToLeft(string tag)
    {
        try
        {
            return CultureInfo.GetCultureInfo(tag).TextInfo.IsRightToLeft;
        }
        catch (CultureNotFoundException)
        {
            return false;
        }
    }
}
