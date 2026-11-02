namespace Itmam.Core.Tests.Golden;

/// <summary>
/// Compares generated text with a file committed under tests/Itmam.Core.Tests/Golden. The files pin
/// the Arabic behaviour from before multi-language support (2.0): any difference is a regression.
/// Set ITMAM_UPDATE_GOLDEN=1 to rewrite them, only for a change that is meant to alter Arabic results.
/// </summary>
internal static class GoldenFile
{
    public static void Verify(string name, IEnumerable<string> lines)
    {
        var actual = lines.ToArray();
        var path = Path.Combine(TestDictionaries.RepositoryRoot, "tests", "Itmam.Core.Tests", "Golden", name);

        if (Environment.GetEnvironmentVariable("ITMAM_UPDATE_GOLDEN") == "1")
        {
            File.WriteAllText(path, string.Join('\n', actual) + "\n");
            return;
        }

        File.Exists(path).ShouldBeTrue($"{name} is missing; run the tests once with ITMAM_UPDATE_GOLDEN=1.");
        var expected = File.ReadAllText(path).ReplaceLineEndings("\n").TrimEnd('\n').Split('\n');

        var differences = Enumerable.Range(0, Math.Max(expected.Length, actual.Length))
            .Where(i => i >= expected.Length || i >= actual.Length || expected[i] != actual[i])
            .Take(10)
            .Select(i => $"line {i + 1}:\n  expected: {(i < expected.Length ? expected[i] : "<none>")}\n  actual:   {(i < actual.Length ? actual[i] : "<none>")}")
            .ToArray();

        differences.ShouldBeEmpty($"{name} differs from the golden file");
    }
}
