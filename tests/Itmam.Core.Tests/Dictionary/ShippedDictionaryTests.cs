using System.Diagnostics;
using Itmam.Core.Dictionary;
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
    public void Lookup_takes_under_2ms(string prefix)
    {
        var engine = new SuggestionEngine(Shipped.Value, new UserWordIndex());
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
}
