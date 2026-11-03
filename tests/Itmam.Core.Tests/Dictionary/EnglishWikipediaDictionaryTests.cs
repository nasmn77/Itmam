using System.Diagnostics;
using Itmam.Core.Dictionary;
using Itmam.Core.Dictionary.Building;
using Itmam.Core.Languages;
using Itmam.Core.Learning;
using Itmam.Core.Suggestions;

namespace Itmam.Core.Tests.Dictionary;

/// <summary>The English Wikipedia dictionary (2.0, the default for English): the basic one plus Wikipedia's vocabulary.</summary>
[Collection(TimingCollection.Name)]
public class EnglishWikipediaDictionaryTests(ITestOutputHelper output)
{
    private static readonly Lazy<WordDictionary> Shipped = new(() => DictionaryFile.Load(TestDictionaries.EnglishWikipediaDictionaryPath));

    private static string[] Suggest(string typed) =>
        [.. new SuggestionEngine(Shipped.Value, new UserWordIndex(LanguageProfile.English), new SuggestionOptions { MaxSuggestions = 5 })
            .Suggest(typed).Select(s => s.Word)];

    [Fact]
    public void Is_larger_than_the_basic_one_and_credits_both_sources()
    {
        var basic = DictionaryFile.Load(TestDictionaries.EnglishDictionaryPath);

        Shipped.Value.Language.ShouldBeSameAs(LanguageProfile.English);
        Shipped.Value.Count.ShouldBeGreaterThan(basic.Count * 3 / 2);
        Shipped.Value.Sources.Select(s => s.Url).ShouldBe(["https://github.com/hermitdave/FrequencyWords", "https://dumps.wikimedia.org/enwiki/"], ignoreOrder: true);
        Shipped.Value.Sources.ShouldAllBe(s => s.License == "CC-BY-SA-4.0");
    }

    [Fact]
    public void Loads_within_1s_and_60MB()
    {
        DictionaryFile.Load(TestDictionaries.EnglishWikipediaDictionaryPath);

        var before = GC.GetTotalMemory(forceFullCollection: true);
        var stopwatch = Stopwatch.StartNew();
        var dictionary = DictionaryFile.Load(TestDictionaries.EnglishWikipediaDictionaryPath);
        stopwatch.Stop();
        var retained = GC.GetTotalMemory(forceFullCollection: true) - before;
        GC.KeepAlive(dictionary);

        output.WriteLine($"Load: {stopwatch.ElapsedMilliseconds} ms, retained: {retained / 1024 / 1024} MB, words: {dictionary.Count:N0}");
        stopwatch.ElapsedMilliseconds.ShouldBeLessThan(1000);
        retained.ShouldBeLessThan(60L * 1024 * 1024);
    }

    [Fact]
    public void Prefix_cache_gives_exactly_the_same_suggestions_as_a_full_scan()
    {
        var dictionary = Shipped.Value;
        var cached = new SuggestionEngine(dictionary, new UserWordIndex(LanguageProfile.English), new SuggestionOptions { MinChars = 1, MaxSuggestions = 5 });
        var scanned = new SuggestionEngine(dictionary, new UserWordIndex(LanguageProfile.English),
            new SuggestionOptions { MinChars = 1, MaxSuggestions = 5, CandidatePoolSize = PrefixCandidateCache.CandidatesPerPrefix + 1 });

        var prefixes = Enumerable.Range(0, dictionary.Count)
            .SelectMany(i => Enumerable.Range(1, Math.Min(3, dictionary.GetDisplay(i).Length)).Select(n => dictionary.GetDisplay(i)[..n].ToString()))
            .Distinct()
            .ToArray();

        prefixes.Where(p => !cached.Suggest(p).Select(s => s.Word).SequenceEqual(scanned.Suggest(p).Select(s => s.Word))).Take(10).ShouldBeEmpty();
    }

    [Fact]
    public void No_blocked_word_and_no_possessive_is_in_the_dictionary()
    {
        var blocklist = Blocklist.Load(TestDictionaries.EnglishBlocklistPath, LanguageProfile.English);
        var words = Enumerable.Range(0, Shipped.Value.Count).Select(i => Shipped.Value.GetDisplay(i).ToString()).ToArray();

        words.Where(blocklist.IsBlocked).ShouldBeEmpty();
        words.ShouldNotContain("London's");
        words.ShouldContain("it's");
    }

    [Theory]
    [InlineData("Mic", "Microsoft")]
    [InlineData("Lon", "London")]
    [InlineData("procu", "procurement")]
    [InlineData("tha", "thanks")]
    [InlineData("don'", "don't")]
    [InlineData("I'", "I'm")]
    public void Names_formal_words_and_everyday_words_are_suggested(string typed, string expected)
    {
        var words = Suggest(typed);
        output.WriteLine($"{typed}: {string.Join(" | ", words)}");
        words.ShouldContain(expected);
    }

    [Fact]
    public void Everyday_words_keep_their_rank() =>
        Suggest("th")[..3].ShouldBe(["the", "that", "this"]);
}
