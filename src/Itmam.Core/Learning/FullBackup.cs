using System.Globalization;
using System.IO.Compression;
using System.Text;
using Itmam.Core.Settings;
using Itmam.Core.Suggestions;

namespace Itmam.Core.Learning;

/// <summary>What a full backup holds.</summary>
public sealed record BackupContent(AppSettings Settings, string Words, IReadOnlyList<(string Text, int Count)> LearnedPhrases, UsageStats? Usage);

/// <summary>
/// The full backup (user decision 1.1): one <c>.itmam</c> file (a ZIP) with everything needed to move
/// Itmam to another PC — the settings (with My phrases, text shortcuts, long phrases), the user's words,
/// the learned phrases and the statistics. Restoring replaces all of it (after the user confirmed).
/// The file stays where the user saves it; Itmam sends it nowhere.
/// </summary>
public static class FullBackup
{
    private const string SettingsEntry = "settings.json";
    private const string WordsEntry = "words.txt";
    private const string PhrasesEntry = "learned-phrases.txt";
    private const string StatsEntry = "statistics.txt";
    private const string MarkerEntry = "itmam-backup.txt";

    public static void Write(Stream output, AppSettings settings, LearningService learning)
    {
        using var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
        WriteText(zip, MarkerEntry, "Itmam (إتمام) full backup, format 1");

        using (var stream = zip.CreateEntry(SettingsEntry).Open())
        {
            SettingsStore.Write(stream, settings);
        }

        var words = new StringWriter(CultureInfo.InvariantCulture);
        learning.Export(words);
        WriteText(zip, WordsEntry, words.ToString());

        var phrases = new StringWriter(CultureInfo.InvariantCulture);
        UserListFiles.WritePhrases(phrases, [], learning.LearnedPhrases(all: true).Select(p => (p.Text, p.Count)));
        WriteText(zip, PhrasesEntry, phrases.ToString());

        var usage = learning.Usage;
        WriteText(zip, StatsEntry, string.Join('\n',
            usage.Completions.ToString(CultureInfo.InvariantCulture),
            usage.CharactersSaved.ToString(CultureInfo.InvariantCulture),
            usage.Since.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture)));
    }

    /// <summary>The backup's content, or null when the file is not an Itmam backup.</summary>
    public static BackupContent? Read(Stream input)
    {
        try
        {
            using var zip = new ZipArchive(input, ZipArchiveMode.Read, leaveOpen: true);
            if (zip.GetEntry(MarkerEntry) is null || zip.GetEntry(SettingsEntry) is not { } settingsEntry)
            {
                return null;
            }

            AppSettings? settings;
            using (var stream = settingsEntry.Open())
            {
                settings = SettingsStore.Read(stream);
            }

            if (settings is null)
            {
                return null;
            }

            var learned = new List<(string Text, int Count)>();
            UserListFiles.ReadPhrases(new StringReader(ReadText(zip, PhrasesEntry)), [], learned);
            return new BackupContent(settings, ReadText(zip, WordsEntry), learned, ReadUsage(ReadText(zip, StatsEntry)));
        }
        catch (InvalidDataException)
        {
            return null; // not a ZIP file
        }
    }

    /// <summary>Replaces the user's words, learned phrases and statistics with the backup's.</summary>
    public static void RestoreLearning(BackupContent content, LearningService learning)
    {
        learning.ClearAll();
        learning.Import(new StringReader(content.Words));
        learning.ClearPhrases();
        learning.ImportPhrases(content.LearnedPhrases);
        if (content.Usage is { } usage)
        {
            learning.RestoreUsage(usage);
        }
    }

    private static UsageStats? ReadUsage(string text)
    {
        var parts = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length == 3
            && long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var completions)
            && long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var characters)
            && long.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var since)
            ? new UsageStats(completions, characters, DateTimeOffset.FromUnixTimeMilliseconds(since))
            : null;
    }

    private static void WriteText(ZipArchive zip, string name, string text)
    {
        using var writer = new StreamWriter(zip.CreateEntry(name).Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.Write(text);
    }

    private static string ReadText(ZipArchive zip, string name)
    {
        if (zip.GetEntry(name) is not { } entry)
        {
            return string.Empty;
        }

        using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
