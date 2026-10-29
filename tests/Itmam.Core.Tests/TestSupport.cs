using Itmam.Core.Dictionary;

namespace Itmam.Core.Tests;

internal sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;
}

internal static class TestDictionaries
{
    /// <summary>Builds a dictionary from (word, per-million) pairs.</summary>
    public static WordDictionary Of(params (string Word, double PerMillion)[] words) =>
        WordDictionary.Build(words.Select(w => new WeightedWord(w.Word, w.PerMillion)));

    public static string ShippedDictionaryPath => Path.Combine(RepositoryRoot, "data", "itmam-ar.dict");

    public static string ShippedBlocklistPath => Path.Combine(RepositoryRoot, "data", "blocklist.txt");

    public static string ShippedPhrasesPath => Path.Combine(RepositoryRoot, "data", "phrases.txt");

    /// <summary>Found by walking up from the test output folder to the folder holding Itmam.sln.</summary>
    private static string RepositoryRoot
    {
        get
        {
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "Itmam.sln")))
                {
                    return dir.FullName;
                }
            }

            throw new InvalidOperationException("Repository root (Itmam.sln) not found.");
        }
    }
}
