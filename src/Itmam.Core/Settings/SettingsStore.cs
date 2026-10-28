using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Itmam.Core.Settings;

/// <summary>
/// Loads and saves <see cref="AppSettings"/> as JSON. Saving writes a temporary file and then
/// replaces the old one, so a crash never leaves a half-written file. An unreadable file is kept
/// as <c>settings.json.corrupt-&lt;timestamp&gt;</c> and defaults are used.
/// </summary>
public sealed class SettingsStore(string path)
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // keep Arabic readable in the file
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly Lock _lock = new();

    public string Path { get; } = path;

    /// <summary>Set when the file could not be read at the last <see cref="Load"/>: where it was kept.</summary>
    public string? RecoveredBackupPath { get; private set; }

    public AppSettings Load()
    {
        lock (_lock)
        {
            if (!File.Exists(Path))
            {
                return new AppSettings();
            }

            try
            {
                using var stream = File.OpenRead(Path);
                return (JsonSerializer.Deserialize<AppSettings>(stream, Options) ?? new AppSettings()).Normalized();
            }
            catch (Exception ex) when (ex is JsonException or NotSupportedException)
            {
                RecoveredBackupPath = $"{Path}.corrupt-{DateTime.Now:yyyyMMdd-HHmmss}";
                File.Move(Path, RecoveredBackupPath, overwrite: true);
                return new AppSettings();
            }
        }
    }

    public void Save(AppSettings settings)
    {
        lock (_lock)
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(Path))!);
            var temporary = Path + ".tmp";
            using (var stream = File.Create(temporary))
            {
                JsonSerializer.Serialize(stream, settings, Options);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporary, Path, overwrite: true);
        }
    }
}
