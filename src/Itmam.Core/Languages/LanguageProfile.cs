using Itmam.Core.Text;

namespace Itmam.Core.Languages;

/// <summary>
/// Everything that depends on the language being typed: which characters make a word, which marks are
/// ignored for matching, how a word becomes its matching key, and the language's word rules (clitics,
/// common phrase starts, blocklist forms). Every text path asks the profile instead of assuming Arabic.
/// </summary>
/// <remarks>
/// <see cref="Fold"/> maps one character to one character: the matching key of a word without ignorable
/// marks has the same length as the word, which <c>PrefixCandidateCache</c> relies on.
/// Instances are immutable and safe to share across threads.
/// </remarks>
public abstract class LanguageProfile
{
    /// <summary>Arabic: the shipped dictionaries, hamza and clitic rules.</summary>
    public static LanguageProfile Arabic { get; } = new ArabicLanguage();

    /// <summary>Short language tag ("ar", "en", "he" …); also stored with learned words.</summary>
    public abstract string Id { get; }

    /// <summary>The bar is laid out right to left and placed with its right edge at the caret.</summary>
    public abstract bool RightToLeft { get; }

    /// <summary>A letter that can be part of a word.</summary>
    public abstract bool IsLetter(char c);

    /// <summary>A mark kept in the typed text but ignored for matching (tashkeel, niqqud …).</summary>
    public abstract bool IsIgnorable(char c);

    /// <summary>The character of the matching key (one character for one, see the remarks).</summary>
    public virtual char Fold(char c) => c;

    /// <summary>A character that ends a word and commits it.</summary>
    public virtual bool IsWordBoundary(char c) => ArabicText.IsWordBoundary(c);

    /// <summary>Normalized clitics (attached prefixes), longest first: stripped, the stem completed, re-attached.</summary>
    public virtual IReadOnlyList<string> Clitics => [];

    /// <summary>Normalized words too common to start a phrase suggestion on their own.</summary>
    public virtual IReadOnlySet<string> CommonPhraseStarts { get; } = new HashSet<string>(StringComparer.Ordinal);

    public bool IsWordChar(char c) => IsLetter(c) || IsIgnorable(c);

    /// <summary>Removes the ignorable marks, keeping the original letters.</summary>
    public virtual string StripIgnorable(ReadOnlySpan<char> text)
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

    /// <summary>True when the text is non-empty and contains only letters of this language.</summary>
    public bool IsWord(ReadOnlySpan<char> text)
    {
        if (text.IsEmpty)
        {
            return false;
        }

        foreach (var c in text)
        {
            if (!IsLetter(c))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The matching key: ignorable marks removed, every other character folded.</summary>
    public virtual string Normalize(ReadOnlySpan<char> word)
    {
        Span<char> buffer = word.Length <= 128 ? stackalloc char[word.Length] : new char[word.Length];
        var length = 0;
        foreach (var c in word)
        {
            if (!IsIgnorable(c))
            {
                buffer[length++] = Fold(c);
            }
        }

        return new string(buffer[..length]);
    }

    /// <summary>Looser keys tried when the typed key finds too little (ranked below direct matches).</summary>
    public virtual IReadOnlyList<string> FallbackVariants(string normalizedKey) => [];

    /// <summary>
    /// After stripping <paramref name="clitic"/>, stems starting with this text are skipped (Arabic: ل + ال
    /// is written لل, so no ال stem follows a clitic ending in ل); null when none are.
    /// </summary>
    public virtual string? ExcludedStemStart(string clitic) => null;

    /// <summary>Blocklist key: looser than <see cref="Normalize"/> so no spelling escapes the list.</summary>
    public virtual string LooseKey(string word) => Normalize(word);

    /// <summary>Stems of a blocklist key that must be checked too (forms with an attached clitic).</summary>
    public virtual IEnumerable<string> BlocklistStems(string looseKey) => [];

    /// <summary>Strips the ignorable marks; null unless the result is a word of this language.</summary>
    public string? CleanWord(string word)
    {
        var cleaned = StripIgnorable(word);
        return IsWord(cleaned) ? cleaned : null;
    }

    public int CountLetters(ReadOnlySpan<char> text)
    {
        var count = 0;
        foreach (var c in text)
        {
            if (IsLetter(c))
            {
                count++;
            }
        }

        return count;
    }

    public override string ToString() => Id;
}
