using System.Security.Cryptography;
using Itmam.Core.Dictionary;

namespace Itmam.App;

/// <summary>
/// The optional Wikipedia dictionary (user decision 1.1): a larger dictionary built from the shipped
/// word list and Arabic Wikipedia, downloaded only when the user presses "Download" in Settings →
/// General. This and the Store update check are Itmam's only network use; the request asks GitHub
/// for one fixed file and sends nothing about the user or their typing.
/// </summary>
/// <remarks>
/// The file is pinned by size and SHA-256 (set when the dictionary is published): any other file is
/// refused, so a changed download can never replace the dictionary.
/// </remarks>
internal static class WikipediaDictionary
{
    /// <summary>A release of this repository holds the file.</summary>
    public const string Url = "https://github.com/nasmn77/Itmam/releases/download/dictionary-wikipedia-1/itmam-ar-wikipedia.dict";

    /// <summary>Expected SHA-256 of the file (hex); empty until the dictionary is published.</summary>
    public const string Sha256 = "F27DDA554804A260DA08F51DC785F4D11A8D38EDBDAE6E91F06461C0B9E73FD2";

    /// <summary>Expected size in bytes (shown before downloading).</summary>
    public const long Size = 2_968_954;

    private const string FileName = "itmam-ar-wikipedia.dict";

    /// <summary>The file can be downloaded (it has been published and pinned).</summary>
    public static bool IsPublished => Sha256.Length == 64 && Size > 0;

    public static string PathIn(string dataFolder) => Path.Combine(dataFolder, FileName);

    /// <summary>The downloaded dictionary, or null when it is missing or unreadable.</summary>
    public static WordDictionary? TryLoad(string dataFolder)
    {
        var path = PathIn(dataFolder);
        try
        {
            return File.Exists(path) ? DictionaryFile.Load(path) : null;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Downloads the file, checks its size and hash, and moves it into place. Returns the loaded
    /// dictionary, or null when the download failed or the file is not the published one.
    /// </summary>
    public static async Task<WordDictionary?> DownloadAsync(string dataFolder, IProgress<double> progress, CancellationToken cancellation)
    {
        if (!IsPublished)
        {
            return null;
        }

        Directory.CreateDirectory(dataFolder);
        var target = PathIn(dataFolder);
        var temporary = target + ".download";
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
            using var response = await http.GetAsync(Url, HttpCompletionOption.ResponseHeadersRead, cancellation).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using (var source = await response.Content.ReadAsStreamAsync(cancellation).ConfigureAwait(false))
            await using (var file = File.Create(temporary))
            {
                var buffer = new byte[81920];
                long total = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, cancellation).ConfigureAwait(false)) > 0)
                {
                    total += read;
                    if (total > Size)
                    {
                        return null; // not the published file
                    }

                    hash.AppendData(buffer, 0, read);
                    await file.WriteAsync(buffer.AsMemory(0, read), cancellation).ConfigureAwait(false);
                    progress.Report((double)total / Size);
                }
            }

            if (!Convert.ToHexString(hash.GetHashAndReset()).Equals(Sha256, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var dictionary = DictionaryFile.Load(temporary);
            File.Move(temporary, target, overwrite: true);
            return dictionary;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or UnauthorizedAccessException or TaskCanceledException)
        {
            return null;
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    public static void Delete(string dataFolder)
    {
        try
        {
            File.Delete(PathIn(dataFolder));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Still in use or protected: it is simply not loaded at the next start.
        }
    }
}
