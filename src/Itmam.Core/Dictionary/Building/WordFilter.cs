using Itmam.Core.Text;

namespace Itmam.Core.Dictionary.Building;

/// <summary>Rejects tokens that should never be in the shipped dictionary.</summary>
public static class WordFilter
{
    public const int MinLength = 2;
    public const int MaxLength = 25;

    /// <summary>
    /// Strips tashkeel/tatweel and accepts the result only if it is Arabic letters only (no mixed
    /// scripts, digits or punctuation), 2–25 letters long and has no letter repeated 3+ times in a row.
    /// </summary>
    public static bool TryClean(string token, out string word)
    {
        word = ArabicText.StripIgnorable(token);
        return word.Length is >= MinLength and <= MaxLength
            && ArabicText.IsArabicWord(word)
            && !HasTripleRepeat(word);
    }

    private static bool HasTripleRepeat(string word)
    {
        for (var i = 2; i < word.Length; i++)
        {
            if (word[i] == word[i - 1] && word[i] == word[i - 2])
            {
                return true;
            }
        }

        return false;
    }
}
