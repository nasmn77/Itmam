using Itmam.Core.Text;

namespace Itmam.Core.Suggestions;

/// <summary>A long phrase («ترحيب بالعميل» → a reply of several lines), added by the user in Settings.</summary>
public sealed record LongPhrase(string Name, string Text);

/// <summary>
/// Long phrases (user decision 1.1, for technical support replies): a shortcut (default
/// Ctrl+Shift+Space) opens a list of their names, and the chosen one is pasted into the app (the
/// clipboard is restored afterwards). Kept in settings.json; never learned from.
/// </summary>
public static class LongPhrases
{
    /// <summary>Longest name, in characters.</summary>
    public const int MaxNameLength = 60;

    /// <summary>Longest text, in characters.</summary>
    public const int MaxTextLength = 4000;

    /// <summary>The name on one line and trimmed, or null when empty or too long.</summary>
    public static string? CleanName(string name)
    {
        var cleaned = string.Join(' ', name.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return cleaned.Length is > 0 and <= MaxNameLength ? cleaned : null;
    }

    /// <summary>
    /// The text with its lines kept (line breaks as <c>\n</c>, trailing spaces of each line removed, no
    /// empty lines at the start or end), or null when empty or too long.
    /// </summary>
    public static string? CleanText(string text)
    {
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n').Select(l => l.TrimEnd());
        var cleaned = string.Join('\n', lines).Trim('\n', ' ');
        return cleaned.Length is > 0 and <= MaxTextLength ? cleaned : null;
    }

    /// <summary>The first line of a text, shortened with «…» (the list in Settings).</summary>
    public static string Preview(string text, int maxLength = 80)
    {
        var firstLine = text.Split('\n', 2)[0];
        return firstLine.Length > maxLength || firstLine.Length < text.Length
            ? firstLine[..Math.Min(firstLine.Length, maxLength)].TrimEnd() + "…"
            : text;
    }

    /// <summary>
    /// The phrases whose name or text contains every word of <paramref name="query"/> (tashkeel and
    /// hamza seats ignored), names matching first; all of them for an empty query.
    /// </summary>
    public static IReadOnlyList<LongPhrase> Filter(IReadOnlyList<LongPhrase> phrases, string query)
    {
        var words = query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Select(Key).ToArray();
        if (words.Length == 0)
        {
            return phrases;
        }

        bool Matches(string text)
        {
            var key = Key(text);
            return words.All(w => key.Contains(w, StringComparison.OrdinalIgnoreCase));
        }

        return [.. phrases.Where(p => Matches(p.Name)), .. phrases.Where(p => !Matches(p.Name) && Matches(p.Text))];
    }

    private static string Key(string text) => ArabicNormalizer.Normalize(text);
}
