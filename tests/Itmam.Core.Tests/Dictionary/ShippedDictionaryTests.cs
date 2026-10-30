using System.Diagnostics;
using Itmam.Core.Dictionary;
using Itmam.Core.Dictionary.Building;
using Itmam.Core.Learning;
using Itmam.Core.Suggestions;

namespace Itmam.Core.Tests.Dictionary;

/// <summary>Size, quality and performance checks against the real shipped dictionary file.</summary>
public class ShippedDictionaryTests(ITestOutputHelper output)
{
    private static readonly Lazy<WordDictionary> Shipped = new(() => DictionaryFile.Load(TestDictionaries.ShippedDictionaryPath));

    [Fact]
    public void Has_at_least_300k_words_with_attribution()
    {
        Shipped.Value.Count.ShouldBeGreaterThanOrEqualTo(300_000);
        Shipped.Value.Sources.ShouldNotBeEmpty();
        Shipped.Value.Sources.ShouldAllBe(s => s.License.Length > 0 && s.Url.Length > 0);
    }

    [Fact]
    public void Loads_within_500ms_and_80MB()
    {
        // Warm up JIT so the measurement reflects the loader, not first-call compilation.
        DictionaryFile.Load(TestDictionaries.ShippedDictionaryPath);

        var before = GC.GetTotalMemory(forceFullCollection: true);
        var stopwatch = Stopwatch.StartNew();
        var dictionary = DictionaryFile.Load(TestDictionaries.ShippedDictionaryPath);
        stopwatch.Stop();
        var retained = GC.GetTotalMemory(forceFullCollection: true) - before;
        GC.KeepAlive(dictionary);

        output.WriteLine($"Load: {stopwatch.ElapsedMilliseconds} ms, retained: {retained / 1024 / 1024} MB, words: {dictionary.Count:N0}");
        stopwatch.ElapsedMilliseconds.ShouldBeLessThan(500);
        retained.ShouldBeLessThan(80L * 1024 * 1024);
    }

    [Theory]
    [InlineData("ال")]
    [InlineData("وا")]
    [InlineData("مت")]
    [InlineData("است")]
    [InlineData("والمد")]
    [InlineData("بالت")]
    [InlineData("المس")]
    [InlineData("المت")]
    [InlineData("استخ")]
    [InlineData("ا")]
    public void Lookup_takes_under_2ms(string prefix)
    {
        var engine = new SuggestionEngine(Shipped.Value, new UserWordIndex(), new SuggestionOptions { MinChars = 1 });
        for (var i = 0; i < 20; i++)
        {
            engine.Suggest(prefix);
        }

        const int runs = 200;
        var timings = new double[runs];
        for (var i = 0; i < runs; i++)
        {
            var start = Stopwatch.GetTimestamp();
            engine.Suggest(prefix);
            timings[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        }

        Array.Sort(timings);
        var median = timings[runs / 2];
        output.WriteLine($"{prefix}: median {median:F3} ms, p95 {timings[runs * 95 / 100]:F3} ms");
        median.ShouldBeLessThan(2.0);
    }

    [Fact]
    public void Prefix_cache_gives_exactly_the_same_suggestions_as_a_full_scan()
    {
        var dictionary = Shipped.Value;
        var cached = new SuggestionEngine(dictionary, new UserWordIndex(), new SuggestionOptions { MinChars = 1, MaxSuggestions = 5 });

        // A pool larger than the cache disables it, forcing a full range scan; the top 5 must not change.
        var scanned = new SuggestionEngine(
            dictionary,
            new UserWordIndex(),
            new SuggestionOptions { MinChars = 1, MaxSuggestions = 5, CandidatePoolSize = PrefixCandidateCache.CandidatesPerPrefix + 1 });

        // Every distinct literal prefix of 1–3 letters that occurs in the dictionary.
        var prefixes = Enumerable.Range(0, dictionary.Count)
            .SelectMany(i => Enumerable.Range(1, Math.Min(3, dictionary.GetDisplay(i).Length)).Select(n => dictionary.GetDisplay(i)[..n].ToString()))
            .Distinct()
            .ToArray();

        var mismatches = prefixes
            .Where(p => !cached.Suggest(p).Select(s => s.Word).SequenceEqual(scanned.Suggest(p).Select(s => s.Word)))
            .Take(10)
            .ToArray();

        output.WriteLine($"Compared {prefixes.Length:N0} prefixes; cache: {dictionary.Cache.PrefixCount:N0} prefixes, {dictionary.Cache.CandidateCount:N0} candidates");
        mismatches.ShouldBeEmpty();
    }

    [Fact]
    public void No_blocked_word_is_in_the_dictionary()
    {
        var blocklist = Blocklist.Load(TestDictionaries.ShippedBlocklistPath);
        var dictionary = Shipped.Value;

        var leaked = Enumerable.Range(0, dictionary.Count)
            .Select(i => dictionary.GetDisplay(i).ToString())
            .Where(blocklist.IsBlocked)
            .ToArray();

        blocklist.Count.ShouldBeGreaterThan(0);
        leaked.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("أمك")]
    [InlineData("أختك")]
    [InlineData("فخرا")]
    [InlineData("كسكس")]
    [InlineData("آلامك")]
    [InlineData("قضيب")]
    [InlineData("اغتصاب")]
    [InlineData("لواط")]
    [InlineData("سحاق")]
    public void Ordinary_words_near_blocked_ones_stay_in_the_dictionary(string word) =>
        Shipped.Value.IndexOfWord(word).ShouldBeGreaterThanOrEqualTo(0);

    [Theory]
    [InlineData("أن")]
    [InlineData("إن")]
    [InlineData("على")]
    [InlineData("علي")]
    [InlineData("إلى")]
    public void Distinct_words_sharing_a_normalized_form_are_all_present(string word) =>
        Shipped.Value.IndexOfWord(word).ShouldBeGreaterThanOrEqualTo(0);

    [Fact]
    public void Typing_an_suggests_both_an_and_in()
    {
        var engine = new SuggestionEngine(Shipped.Value, new UserWordIndex());

        var words = engine.Suggest("ان").Select(s => s.Word).ToArray();

        output.WriteLine($"ان: {string.Join(" | ", words)}");
        words.ShouldContain("أن");
        words.ShouldContain("إن");
    }

    [Theory]
    [InlineData("مرح", "مرحبا")]
    [InlineData("شكر", "شكرا")]
    [InlineData("اعتق", "أعتقد")]
    [InlineData("مدرس", "مدرسة")]
    public void Common_words_are_suggested(string prefix, string expected)
    {
        var engine = new SuggestionEngine(Shipped.Value, new UserWordIndex(), new SuggestionOptions { MaxSuggestions = 5 });

        var words = engine.Suggest(prefix).Select(s => s.Word).ToArray();

        output.WriteLine($"{prefix}: {string.Join(" | ", words)}");
        words.ShouldContain(expected);
    }

    [Theory]
    [InlineData("مدرسه", "مدرسة")]
    [InlineData("الى", "إلى")]
    [InlineData("انا", "أنا")]
    [InlineData("اذا", "إذا")]
    [InlineData("مكتبه", "مكتبة")]
    public void A_common_misspelling_gets_the_right_spelling_first(string typed, string expected) =>
        new SuggestionEngine(Shipped.Value, new UserWordIndex()).Suggest(typed)[0].Word.ShouldBe(expected);
}
