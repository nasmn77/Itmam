using System.Diagnostics;
using Itmam.Core.Dictionary;
using Itmam.Core.Dictionary.Building;
using Itmam.Core.Languages;
using Itmam.Core.Learning;
using Itmam.Core.Suggestions;

namespace Itmam.Core.Tests.Dictionary;

/// <summary>Size, quality and performance checks against the shipped English dictionary (2.0).</summary>
public class EnglishDictionaryTests(ITestOutputHelper output)
{
    private static readonly Lazy<WordDictionary> Shipped = new(() => DictionaryFile.Load(TestDictionaries.EnglishDictionaryPath));

    private static SuggestionEngine Engine(int max = 3) =>
        new(Shipped.Value, new UserWordIndex(LanguageProfile.English), new SuggestionOptions { MaxSuggestions = max });

    private string[] Suggest(string typed, int max = 3)
    {
        var words = Engine(max).Suggest(typed).Select(s => s.Word).ToArray();
        output.WriteLine($"{typed}: {string.Join(" | ", words)}");
        return words;
    }

    [Fact]
    public void Is_english_with_at_least_250k_words_and_attribution()
    {
        Shipped.Value.Language.ShouldBeSameAs(LanguageProfile.English);
        Shipped.Value.Count.ShouldBeGreaterThanOrEqualTo(250_000);
        Shipped.Value.Sources.ShouldAllBe(s => s.License == "CC-BY-SA-4.0" && s.Url.Length > 0);
    }

    [Fact]
    public void Loads_within_500ms_and_40MB()
    {
        DictionaryFile.Load(TestDictionaries.EnglishDictionaryPath);

        var before = GC.GetTotalMemory(forceFullCollection: true);
        var stopwatch = Stopwatch.StartNew();
        var dictionary = DictionaryFile.Load(TestDictionaries.EnglishDictionaryPath);
        stopwatch.Stop();
        var retained = GC.GetTotalMemory(forceFullCollection: true) - before;
        GC.KeepAlive(dictionary);

        output.WriteLine($"Load: {stopwatch.ElapsedMilliseconds} ms, retained: {retained / 1024 / 1024} MB, words: {dictionary.Count:N0}");
        stopwatch.ElapsedMilliseconds.ShouldBeLessThan(500);
        retained.ShouldBeLessThan(40L * 1024 * 1024);
    }

    [Theory]
    [InlineData("t")]
    [InlineData("th")]
    [InlineData("co")]
    [InlineData("pre")]
    [InlineData("inter")]
    public void Lookup_takes_under_2ms(string prefix)
    {
        var engine = new SuggestionEngine(Shipped.Value, new UserWordIndex(LanguageProfile.English), new SuggestionOptions { MinChars = 1 });
        for (var i = 0; i < 20; i++)
        {
            engine.Suggest(prefix);
        }

        var timings = Enumerable.Range(0, 200).Select(_ =>
        {
            var start = Stopwatch.GetTimestamp();
            engine.Suggest(prefix);
            return Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        }).Order().ToArray();
        timings[100].ShouldBeLessThan(2.0);
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
    public void No_blocked_word_is_in_the_dictionary()
    {
        var blocklist = Blocklist.Load(TestDictionaries.EnglishBlocklistPath, LanguageProfile.English);
        var dictionary = Shipped.Value;

        blocklist.Count.ShouldBeGreaterThan(100);
        Enumerable.Range(0, dictionary.Count).Select(i => dictionary.GetDisplay(i).ToString()).Where(blocklist.IsBlocked).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("don't")]
    [InlineData("can't")]
    [InlineData("won't")]
    [InlineData("I'm")]
    [InlineData("it's")]
    [InlineData("you're")]
    [InlineData("o'clock")]
    [InlineData("café")]
    [InlineData("sex")]   // ordinary words next to blocked ones stay (user decision)
    [InlineData("dick")]
    public void Words_and_contractions_are_present(string word) =>
        Shipped.Value.IndexOfWord(word).ShouldBeGreaterThanOrEqualTo(0);

    [Theory]
    [InlineData("don")]
    [InlineData("isn")]
    [InlineData("didn")]
    [InlineData("'s")]
    [InlineData("i")]
    public void Split_contraction_parts_are_not_words(string word) =>
        Shipped.Value.IndexOfWord(word).ShouldBe(-1);

    [Theory]
    [InlineData("hel", "hello")]
    [InlineData("tha", "thanks")]
    [InlineData("beca", "because")]
    [InlineData("don'", "don't")]
    [InlineData("I'", "I'm")]
    [InlineData("i'", "I'm")]
    [InlineData("cafe", "café")]
    [InlineData("Lon", "London")]
    [InlineData("LON", "LONDON")]
    public void Common_words_are_suggested(string typed, string expected) =>
        Suggest(typed, max: 5).ShouldContain(expected);
}
