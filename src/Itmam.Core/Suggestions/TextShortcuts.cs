using Itmam.Core.Text;

namespace Itmam.Core.Suggestions;

/// <summary>A text shortcut ("ع1" → "السلام عليكم ورحمة الله وبركاته"), added by the user in Settings.</summary>
public sealed record TextShortcut(string Key, string Text);

/// <summary>
/// The user's text shortcuts (user decision 1.1): typing a key exactly shows its text first in the bar,
/// and Tab inserts it in place of the key. Nothing is replaced without Tab.
/// </summary>
/// <remarks>
/// A key is one Arabic word, optionally with digits after its first letter ("ع1", "سلام"): the word
/// buffer keeps digits typed after a letter, so the key is seen whole. Matching ignores tashkeel and
/// hamza seats (<see cref="ArabicNormalizer"/>), and Arabic-Indic digits equal Western ones.
/// </remarks>
public sealed class TextShortcuts
{
    /// <summary>Longest key, in characters.</summary>
    public const int MaxKeyLength = 20;

    /// <summary>Longest text, in characters.</summary>
    public const int MaxTextLength = 500;

    private readonly Dictionary<string, string> _byKey = new(StringComparer.Ordinal);

    public TextShortcuts(IEnumerable<TextShortcut> shortcuts)
    {
        foreach (var shortcut in shortcuts)
        {
            if (CleanKey(shortcut.Key) is { } key && CleanText(shortcut.Text) is { } text)
            {
                _byKey.TryAdd(MatchKey(key), text);
            }
        }
    }

    public static TextShortcuts Empty { get; } = new([]);

    public int Count => _byKey.Count;

    /// <summary>The text of the shortcut typed exactly, or null.</summary>
    public string? Find(string typed) =>
        typed.Length > 0 && typed.Length <= MaxKeyLength * 2 && _byKey.TryGetValue(MatchKey(typed), out var text) ? text : null;

    /// <summary>
    /// A key cleaned for storage (tashkeel removed), or null unless it is an Arabic letter followed by
    /// Arabic letters or digits, without spaces.
    /// </summary>
    public static string? CleanKey(string key)
    {
        var cleaned = ArabicText.StripIgnorable(key.Trim());
        return cleaned.Length is > 0 and <= MaxKeyLength
            && ArabicText.IsArabicLetter(cleaned[0])
            && cleaned.All(c => ArabicText.IsArabicLetter(c) || char.IsDigit(c))
            ? cleaned
            : null;
    }

    /// <summary>The text on one line (line breaks become spaces), or null when empty or too long.</summary>
    public static string? CleanText(string text)
    {
        var cleaned = string.Join(' ', text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return cleaned.Length is > 0 and <= MaxTextLength ? cleaned : null;
    }

    // Normalized letters and Western digits: «ع١» equals «ع1».
    private static string MatchKey(string key) =>
        new([.. ArabicNormalizer.Normalize(key).Select(c => char.IsDigit(c) ? (char)('0' + (int)char.GetNumericValue(c)) : c)]);
}
