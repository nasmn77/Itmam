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

    /// <summary>Try stripping attached prefixes (و ف ب ك ل ال …) when direct matches are few.</summary>
    public bool CliticAware { get; init; } = true;

    /// <summary>Shortest stem looked up after stripping a clitic.</summary>
    public int MinStemChars { get; init; } = 2;

    /// <summary>Best dictionary candidates kept from a prefix range before user boosts are applied.</summary>
    public int CandidatePoolSize { get; init; } = 24;
}
