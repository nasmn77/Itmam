namespace Itmam.Core.Suggestions;

/// <summary>Result of importing a list: the merged list, how many entries were new and how many were skipped.</summary>
public readonly record struct ListImport<T>(IReadOnlyList<T> Merged, int Added, int Skipped);

/// <summary>
/// Plain UTF-8 text files for "My phrases" (one phrase per line, then the learned phrases as
/// <c>*phrase&lt;TAB&gt;count</c>) and text shortcuts
/// (<c>key&lt;TAB&gt;text</c>), to back them up or move them to another PC. Lines starting with
/// <c>#</c> are comments. Importing adds to the existing list and never replaces an entry
/// (user decision: an existing shortcut key keeps its text).
/// </summary>
public static class UserListFiles
{
    public static void WritePhrases(TextWriter writer, IEnumerable<string> phrases, IEnumerable<(string Text, int Count)>? learned = null)
    {
        writer.WriteLine("# Itmam (إتمام) — My phrases, one per line; learned phrases: *phrase<TAB>count");
        foreach (var phrase in phrases)
        {
            writer.WriteLine(phrase);
        }

        foreach (var (text, count) in learned ?? [])
        {
            writer.WriteLine($"*{text}\t{count.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
        }
    }

    /// <summary>"My phrases" merged with the file's; the file's learned phrases go to <paramref name="learned"/>.</summary>
    public static ListImport<string> ReadPhrases(TextReader reader, IReadOnlyList<string> existing, List<(string Text, int Count)>? learned = null)
    {
        var merged = existing.ToList();
        var keys = existing.Select(PhraseKey).ToHashSet(StringComparer.Ordinal);
        int added = 0, skipped = 0;
        foreach (var line in Lines(reader))
        {
            if (line[0] == '*')
            {
                var parts = line[1..].Split('\t', 2);
                if (learned is not null && PhraseIndex.Clean(parts[0]) is { } text
                    && parts.Length == 2 && int.TryParse(parts[1], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var count) && count > 0)
                {
                    learned.Add((text, count));
                }
                else
                {
                    skipped++;
                }
            }
            else if (PhraseIndex.Clean(line) is { } phrase && keys.Add(PhraseKey(phrase)))
            {
                merged.Add(phrase);
                added++;
            }
            else
            {
                skipped++;
            }
        }

        return new ListImport<string>(merged, added, skipped);
    }

    public static void WriteShortcuts(TextWriter writer, IEnumerable<TextShortcut> shortcuts)
    {
        writer.WriteLine("# Itmam (إتمام) — text shortcuts: shortcut<TAB>text");
        foreach (var shortcut in shortcuts)
        {
            writer.WriteLine($"{shortcut.Key}\t{shortcut.Text}");
        }
    }

    public static ListImport<TextShortcut> ReadShortcuts(TextReader reader, IReadOnlyList<TextShortcut> existing)
    {
        var merged = existing.ToList();
        var keys = existing.Select(s => s.Key).ToHashSet(StringComparer.Ordinal);
        int added = 0, skipped = 0;
        foreach (var line in Lines(reader))
        {
            var parts = line.Split('\t', 2);
            if (parts.Length == 2
                && TextShortcuts.CleanKey(parts[0]) is { } key
                && TextShortcuts.CleanText(parts[1]) is { } text
                && keys.Add(key))
            {
                merged.Add(new TextShortcut(key, text));
                added++;
            }
            else
            {
                skipped++;
            }
        }

        return new ListImport<TextShortcut>(merged, added, skipped);
    }

    // Non-empty lines that are not comments.
    private static IEnumerable<string> Lines(TextReader reader)
    {
        while (reader.ReadLine() is { } raw)
        {
            var line = raw.Trim();
            if (line.Length > 0 && line[0] != '#')
            {
                yield return line;
            }
        }
    }

    private static string PhraseKey(string phrase) => Learning.LearningService.PhraseKey(phrase);
}
