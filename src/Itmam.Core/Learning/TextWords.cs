using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Itmam.Core.Languages;

namespace Itmam.Core.Learning;

/// <summary>
/// "My words" → Import from any text file (user decision 2.0): the words of one language are taken from the
/// text with how often they occur; the text itself is never kept. A file in the Export format (or a plain list,
/// one word per line) is recognised and imported as such.
/// </summary>
public static partial class TextWords
{
    /// <summary>Largest file read (a book is a few MB).</summary>
    public const int MaxBytes = 64 * 1024 * 1024;

    /// <summary>
    /// The text of a file: UTF-8 or UTF-16 with a byte order mark, UTF-8 without one, otherwise the Windows
    /// code page of <paramref name="language"/> (older Notepad files: Windows-1256 for Arabic and Persian …).
    /// </summary>
    public static string Decode(byte[] bytes, LanguageProfile language)
    {
        if (bytes is [0xEF, 0xBB, 0xBF, ..] or [0xFF, 0xFE, ..] or [0xFE, 0xFF, ..])
        {
            using var reader = new StreamReader(new MemoryStream(bytes), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            return reader.ReadToEnd();
        }

        try
        {
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            var codePage = 1252;
            try
            {
                codePage = CultureInfo.GetCultureInfo(language.Id).TextInfo.ANSICodePage;
            }
            catch (CultureNotFoundException)
            {
            }

            return Encoding.GetEncoding(codePage).GetString(bytes);
        }
    }

    /// <summary>
    /// True for the Export format or a plain word list: every line (comments aside) is <c>word</c>,
    /// <c>word count</c> or <c>!word</c>. Anything else is ordinary text.
    /// </summary>
    public static bool IsWordList(string text)
    {
        var lines = 0;
        foreach (var raw in text.AsSpan().EnumerateLines())
        {
            var line = raw.Trim();
            if (line.IsEmpty || line[0] == '#')
            {
                continue;
            }

            if (!ListLine().IsMatch(line))
            {
                return false;
            }

            lines++;
        }

        return lines > 0;
    }

    /// <summary>
    /// The words of <paramref name="language"/> in <paramref name="text"/> (marks removed, at least
    /// <paramref name="minLetters"/> letters) with how many times each occurs. In a cased language a word
    /// capitalised only where a sentence starts («The») is counted with its lower-case form when the text has it.
    /// </summary>
    public static Dictionary<string, int> Count(string text, LanguageProfile language, int minLetters = 2)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        var start = -1;
        for (var i = 0; i <= text.Length; i++)
        {
            if (i < text.Length && language.IsWordChar(text[i]))
            {
                if (start < 0)
                {
                    start = i;
                }

                continue;
            }

            if (start >= 0)
            {
                Add(text.AsSpan(start, i - start));
                start = -1;
            }
        }

        foreach (var word in counts.Keys.ToArray())
        {
            if (char.IsUpper(word[0]) && !word.AsSpan(1).ContainsAnyInRange('A', 'Z')
                && word.ToLower(CultureInfo.InvariantCulture) is var lower && lower != word && counts.ContainsKey(lower))
            {
                counts[lower] += counts[word];
                counts.Remove(word);
            }
        }

        return counts;

        void Add(ReadOnlySpan<char> run)
        {
            // An apostrophe or a mark at either end is not part of the word ('quoted', students').
            while (!run.IsEmpty && !language.IsLetter(run[0]))
            {
                run = run[1..];
            }

            while (!run.IsEmpty && !language.IsLetter(run[^1]) && !language.IsIgnorable(run[^1]))
            {
                run = run[..^1];
            }

            if (language.CleanWord(run.ToString()) is { } word && language.CountLetters(word) >= minLetters)
            {
                counts[word] = counts.GetValueOrDefault(word) + 1;
            }
        }
    }

    [GeneratedRegex(@"^!?\S+$|^\S+[\t ]+\d+$")]
    private static partial Regex ListLine();
}
