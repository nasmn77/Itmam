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

    /// <summary>Path of the shipped dictionary, found by walking up to the repository root.</summary>
    public static string ShippedDictionaryPath
    {
        get
        {
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "Itmam.sln")))
                {
                    return Path.Combine(dir.FullName, "data", "itmam-ar.dict");
                }
            }

            throw new InvalidOperationException("Repository root (Itmam.sln) not found.");
        }
    }
}
