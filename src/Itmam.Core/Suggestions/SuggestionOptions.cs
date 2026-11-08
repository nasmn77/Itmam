namespace Itmam.Core.Suggestions;

public sealed record SuggestionOptions
{
    /// <summary>Number of suggestions shown (1–5).</summary>
    public int MaxSuggestions { get; init; } = 3;

    /// <summary>Letters typed before suggestions appear (1–4).</summary>
    public int MinChars { get; init; } = 2;

    /// <summary><c>w_user</c> in the ranking formula.</summary>
    public double UserWeight { get; init; } = 1.5;

    /// <summary>Days after which a learned word's boost is halved.</summary>
    public double RecencyHalfLifeDays { get; init; } = 30;

    /// <summary>Uses before a word that is not in the dictionary becomes suggestible (1–5).</summary>
    public int MinUsesToSuggest { get; init; } = 2;

    /// <summary>
    /// Score added to words that start with exactly the typed letters, so they rank above
    /// normalization-only matches unless those are much more frequent (2.3 ≈ 10× more frequent).
    /// Typing ا and wanting أحمد is common, so this is a bonus rather than an absolute rule;
    /// <see cref="double.PositiveInfinity"/> makes literal matches always rank first.
    /// </summary>
    public double LiteralMatchBonus { get; init; } = Math.Log(10);

    /// <summary>
    /// Score added to spellings whose normalized form equals what was typed: when the typed letters
    /// already form a word (ان), its correctly spelled forms (أن, إن) are the likeliest intent.
    /// </summary>
    public double CompleteWordBonus { get; init; } = Math.Log(10);

    /// <summary>Try stripping attached prefixes (و ف ب ك ل ال …) when direct matches are few.</summary>
    public bool CliticAware { get; init; } = true;

    /// <summary>
    /// When nothing matches, or only a rare entry equal to what was typed, try undoing one typing mistake («مزظ» → «موظف»).
    /// Only for words of <see cref="KeyTypoMinChars"/> to <see cref="KeyTypoMaxChars"/> letters.
    /// </summary>
    public bool FixKeyTypos { get; init; } = true;

    /// <summary>Fewer letters than this match almost anything after replacing one.</summary>
    public int KeyTypoMinChars { get; init; } = 3;

    /// <summary>Removing or adding a letter, or splitting in two, needs this many letters: short words match too much.</summary>
    public int KeyTypoEditMinChars { get; init; } = 4;

    /// <summary>Both words of a missing-space correction must be at least this common.</summary>
    public double KeyTypoSplitMinScore { get; init; } = 3;

    /// <summary>Bounds the look-ups made for one keystroke.</summary>
    public int KeyTypoMaxChars { get; init; } = 15;

    /// <summary>Corrections rarer than this are left out: the large dictionaries also hold misspellings and glued words.</summary>
    public double KeyTypoMinScore { get; init; } = 1;

    /// <summary>
    /// Dictionary frequency below which an entry is never a correction, whatever bonus it gets for being a
    /// complete word («كومز», «helloo»).
    /// </summary>
    public double KeyTypoMinBaseScore { get; init; } = 0.5;

    /// <summary>A typed word this common in the dictionary is correct: it is not corrected («معلن», «ظفر»).</summary>
    public double KeyTypoRareWordScore { get; init; } = 1;

    /// <summary>The score a correction needs to be shown for a rare dictionary entry.</summary>
    public double KeyTypoStrongScore { get; init; } = 4.5;

    /// <summary>Shortest stem looked up after stripping a clitic.</summary>
    public int MinStemChars { get; init; } = 2;

    /// <summary>Best dictionary candidates kept from a prefix range before user boosts are applied.</summary>
    public int CandidatePoolSize { get; init; } = 24;
}
