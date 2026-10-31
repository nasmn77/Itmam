using System.Diagnostics;
using Itmam.Core.Dictionary;
using Itmam.Core.Dictionary.Building;

namespace Itmam.Core.Tests.Dictionary;

/// <summary>Checks against the second shipped dictionary, data/itmam-ar-wikipedia.dict (Settings → General).</summary>
public class WikipediaDictionaryTests(ITestOutputHelper output)
{
    private static readonly Lazy<WordDictionary> Wikipedia = new(() => DictionaryFile.Load(TestDictionaries.WikipediaDictionaryPath));

    [Fact]
    public void Is_larger_than_the_basic_one_and_credits_both_sources()
    {
        Wikipedia.Value.Count.ShouldBeGreaterThan(DictionaryFile.Load(TestDictionaries.ShippedDictionaryPath).Count);
        Wikipedia.Value.Sources.Select(s => s.Url).ShouldContain("https://dumps.wikimedia.org/arwiki/");
        Wikipedia.Value.Sources.Select(s => s.Url).ShouldContain("https://github.com/hermitdave/FrequencyWords");
        Wikipedia.Value.Sources.ShouldAllBe(s => s.License == "CC-BY-SA-4.0");
    }

    [Fact]
    public void Loads_within_1s_and_80MB()
    {
        DictionaryFile.Load(TestDictionaries.WikipediaDictionaryPath);

        var before = GC.GetTotalMemory(forceFullCollection: true);
        var stopwatch = Stopwatch.StartNew();
        var dictionary = DictionaryFile.Load(TestDictionaries.WikipediaDictionaryPath);
        stopwatch.Stop();
        var retained = GC.GetTotalMemory(forceFullCollection: true) - before;
        GC.KeepAlive(dictionary);

        output.WriteLine($"Load: {stopwatch.ElapsedMilliseconds} ms, retained: {retained / 1024 / 1024} MB, words: {dictionary.Count:N0}");
        stopwatch.ElapsedMilliseconds.ShouldBeLessThan(1000);
        retained.ShouldBeLessThan(80L * 1024 * 1024);
    }

    [Fact]
    public void No_blocked_word_is_in_the_dictionary()
    {
        var blocklist = Blocklist.Load(TestDictionaries.ShippedBlocklistPath);
        var dictionary = Wikipedia.Value;

        var leaked = Enumerable.Range(0, dictionary.Count)
            .Select(i => dictionary.GetDisplay(i).ToString())
            .Where(blocklist.IsBlocked)
            .ToArray();

        leaked.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("أن")]
    [InlineData("إن")]
    [InlineData("على")]
    [InlineData("علي")]
    [InlineData("إلى")]
    [InlineData("الاستقلال")]
    [InlineData("الخوارزمية")]
    public void Has_the_distinct_spellings_and_formal_words(string word) =>
        Wikipedia.Value.IndexOfWord(word).ShouldBeGreaterThanOrEqualTo(0);
}
