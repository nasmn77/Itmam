using Itmam.Core.Text;

namespace Itmam.Core.Languages;

/// <summary>Arabic, through <see cref="ArabicText"/> and <see cref="ArabicNormalizer"/> (the rules from before 2.0).</summary>
internal sealed class ArabicLanguage : LanguageProfile
{
    // Already normalized. Longest first; every matching clitic is tried and results are merged.
    private static readonly string[] SuggestionClitics = ["وال", "بال", "فال", "كال", "لل", "ال", "و", "ف", "ب", "ك", "ل", "س"];

    // Blocklist forms: words of 4+ letters with any of these, 3-letter words only after the article forms,
    // 2-letter words never (كس must not block ألكس or كسكس; خرا must not block فخرا).
    private static readonly string[] BlocklistClitics = ["وال", "بال", "فال", "كال", "لل", "ال", "و", "ف", "ب", "ك", "ل"];
    private static readonly string[] ArticleClitics = ["وال", "بال", "فال", "كال", "لل", "ال"];

    // The Arabic (101) keyboard; the key of the ligature لا is skipped.
    private static readonly IReadOnlyDictionary<char, string> Neighbors = KeyboardNeighbors.Build(
        ["ضصثقفغعهخحجد", "شسيبلاتنمكط", "ئءؤر\0ىةوزظ"],
        ArabicNormalizer.NormalizeChar);

    public override string KeyNeighbors(char keyLetter) => Neighbors.GetValueOrDefault(keyLetter, string.Empty);

    public override string SoundAlikes(char keyLetter) => keyLetter switch
    {
        'ض' => "ظ",
        'ظ' => "ضز",
        'ذ' => "زد",
        'ز' => "ذظ",
        'د' => "ذ",
        'ث' => "ست",
        'س' => "ثص",
        'ص' => "س",
        'ت' => "طث",
        'ط' => "ت",
        'ق' => "غ",
        'غ' => "ق",
        'ح' => "ه",
        'ه' => "ح",
        _ => string.Empty,
    };

    public override string KeyAlphabet => "ابتثجحخدذرزسشصضطظعغفقكلمنهويءؤئ";

    public override string Id => "ar";

    public override bool RightToLeft => true;

    public override IReadOnlyList<string> Clitics => SuggestionClitics;

    public override IReadOnlySet<string> CommonPhraseStarts { get; } = new HashSet<string>(
        ["في", "علي", "من", "الي", "عن", "مع", "يا", "لا", "ما", "ان", "او", "ثم", "بل", "كل", "هذا", "هذه", "ذلك", "تلك", "قد", "لم", "لن", "هل", "و"],
        StringComparer.Ordinal);

    public override bool IsLetter(char c) => ArabicText.IsArabicLetter(c);

    public override bool IsIgnorable(char c) => ArabicText.IsIgnorable(c);

    public override char Fold(char c) => ArabicNormalizer.NormalizeChar(c);

    public override string StripIgnorable(ReadOnlySpan<char> text) => ArabicText.StripIgnorable(text);

    public override string Normalize(ReadOnlySpan<char> word) => ArabicNormalizer.Normalize(word);

    public override IReadOnlyList<string> FallbackVariants(string normalizedKey) => ArabicNormalizer.HamzaFallbackVariants(normalizedKey);

    public override string? ExcludedStemStart(string clitic) => clitic[^1] == 'ل' ? "ال" : null;

    /// <summary>Normalized form with every hamza seat (ؤ ئ) folded into ء.</summary>
    public override string LooseKey(string word) => Normalize(word).Replace('ؤ', 'ء').Replace('ئ', 'ء');

    public override IEnumerable<string> BlocklistStems(string looseKey)
    {
        foreach (var clitic in BlocklistClitics)
        {
            if (looseKey.Length <= clitic.Length || !looseKey.StartsWith(clitic, StringComparison.Ordinal))
            {
                continue;
            }

            var stem = looseKey[clitic.Length..];
            if (stem.Length >= 4 || (stem.Length == 3 && ArticleClitics.Contains(clitic)))
            {
                yield return stem;
            }
        }
    }
}
