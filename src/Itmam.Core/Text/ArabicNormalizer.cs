namespace Itmam.Core.Text;

/// <summary>
/// Builds the matching key of a word. The key is used only for lookups; the displayed and
/// inserted word always keeps its original spelling.
/// </summary>
/// <remarks>
/// Rules: أ إ آ ٱ → ا, ى → ي, ة → ه (ta marbuta only occurs word-final, so this makes ة and ه equal
/// at the end of a word), tashkeel and tatweel are removed. ء ؤ ئ are kept; the looser
/// ؤ/ئ ↔ ء fallback is produced separately by <see cref="HamzaFallbackVariants"/>.
/// </remarks>
public static class ArabicNormalizer
{
    public static string Normalize(ReadOnlySpan<char> word)
    {
        Span<char> buffer = word.Length <= 128 ? stackalloc char[word.Length] : new char[word.Length];
        var length = 0;
        foreach (var c in word)
        {
            if (ArabicText.IsIgnorable(c))
            {
                continue;
            }

            buffer[length++] = NormalizeChar(c);
        }

        return new string(buffer[..length]);
    }

    public static char NormalizeChar(char c) => c switch
    {
        'أ' or 'إ' or 'آ' or 'ٱ' => 'ا',
        'ى' => 'ي',
        'ة' => 'ه',
        _ => c,
    };

    /// <summary>
    /// Returns the keys obtained by swapping each hamza seat (ء ؤ ئ) for the other two, so a user who
    /// types ء still finds words spelled with ؤ/ئ and vice versa (ranked below direct matches).
    /// Only the first <paramref name="maxHamzas"/> hamzas are expanded to bound the work (≤ 8 variants).
    /// </summary>
    public static IReadOnlyList<string> HamzaFallbackVariants(string normalizedKey, int maxHamzas = 2)
    {
        var positions = new List<int>();
        for (var i = 0; i < normalizedKey.Length && positions.Count < maxHamzas; i++)
        {
            if (IsHamzaSeat(normalizedKey[i]))
            {
                positions.Add(i);
            }
        }

        if (positions.Count == 0)
        {
            return [];
        }

        ReadOnlySpan<char> seats = ['ء', 'ؤ', 'ئ'];
        var combinations = (int)Math.Pow(seats.Length, positions.Count);
        var variants = new List<string>(combinations - 1);
        var chars = normalizedKey.ToCharArray();
        for (var combo = 0; combo < combinations; combo++)
        {
            var value = combo;
            foreach (var position in positions)
            {
                chars[position] = seats[value % seats.Length];
                value /= seats.Length;
            }

            var variant = new string(chars);
            if (variant != normalizedKey)
            {
                variants.Add(variant);
            }
        }

        return variants;
    }

    private static bool IsHamzaSeat(char c) => c is 'ء' or 'ؤ' or 'ئ';
}
