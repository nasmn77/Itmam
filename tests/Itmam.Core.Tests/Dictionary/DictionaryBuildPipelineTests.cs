using Itmam.Core.Dictionary;
using Itmam.Core.Dictionary.Building;

namespace Itmam.Core.Tests.Dictionary;

public class DictionaryBuildPipelineTests
{
    private static readonly DictionarySource SourceA = new("A", "CC0", "a");
    private static readonly DictionarySource SourceB = new("B", "CC0", "b");

    private static double PerMillion(WordDictionary dictionary, string word) =>
        FrequencyScale.ToPerMillion(dictionary.GetFrequency(dictionary.IndexOfWord(word)));

    [Theory]
    [InlineData("كتاب", true)]
    [InlineData("كِتَاب", true)]
    [InlineData("ك", false)]
    [InlineData("كتابkitab", false)]
    [InlineData("كتاب2", false)]
    [InlineData("،", false)]
    [InlineData("هههههه", false)]
    [InlineData("ممتاز", true)]
    public void Filter_keeps_only_clean_arabic_words(string token, bool accepted) =>
        WordFilter.TryClean(token, out _).ShouldBe(accepted);

    [Fact]
    public void Filter_rejects_words_longer_than_25_letters() =>
        WordFilter.TryClean(string.Concat(Enumerable.Repeat("كتب", 9)), out _).ShouldBeFalse();

    [Fact]
    public void Frequency_list_parser_reads_word_and_count()
    {
        using var reader = new StringReader("من 2965717\nفي 2420145\nbroken\nكلمة -3\nبدون\n");

        FrequencyList.Read(reader).ShouldBe([("من", 2965717L), ("في", 2420145L)]);
    }

    [Fact]
    public void Counts_become_per_million_of_each_source()
    {
        var (dictionary, _) = DictionaryBuildPipeline.Build(
            [new SourceInput(SourceA, [("كتاب", 750), ("قلم", 250)])],
            Blocklist.Empty);

        PerMillion(dictionary, "كتاب").ShouldBe(750_000);
        PerMillion(dictionary, "قلم").ShouldBe(250_000);
    }

    [Fact]
    public void Sources_merge_with_their_weights()
    {
        var (dictionary, _) = DictionaryBuildPipeline.Build(
            [
                new SourceInput(SourceA, [("كتاب", 100)], Weight: 3),
                new SourceInput(SourceB, [("قلم", 100)], Weight: 1),
            ],
            Blocklist.Empty);

        PerMillion(dictionary, "كتاب").ShouldBe(750_000);
        PerMillion(dictionary, "قلم").ShouldBe(250_000);
        dictionary.Sources.ShouldBe([SourceA, SourceB]);
    }

    [Fact]
    public void Min_count_drops_rare_tokens()
    {
        var (dictionary, stats) = DictionaryBuildPipeline.Build(
            [new SourceInput(SourceA, [("كتاب", 10), ("كتابب", 1)], MinCount: 2)],
            Blocklist.Empty);

        dictionary.Count.ShouldBe(1);
        stats.BelowMinCount.ShouldBe(1);
    }

    [Fact]
    public void Distinct_spellings_survive_and_rare_misspellings_are_dropped()
    {
        var (dictionary, _) = DictionaryBuildPipeline.Build(
            [new SourceInput(SourceA, [("إلى", 990), ("إِلَى", 10), ("إلي", 100), ("الى", 40)])],
            Blocklist.Empty);

        dictionary.Count.ShouldBe(2);
        PerMillion(dictionary, "إلى").ShouldBe(1_000_000.0 * 1000 / 1140, tolerance: 0.001);
        dictionary.IndexOfWord("إلي").ShouldBeGreaterThanOrEqualTo(0);
        dictionary.IndexOfWord("الى").ShouldBe(-1);
    }

    [Fact]
    public void Blocklisted_words_are_removed_in_every_spelling()
    {
        using var reader = new StringReader("# comment\n\nممنوعة\n");
        var blocklist = Blocklist.Read(reader);

        var (dictionary, stats) = DictionaryBuildPipeline.Build(
            [new SourceInput(SourceA, [("ممنوعة", 10), ("ممنوعه", 5), ("مسموح", 5)])],
            blocklist);

        dictionary.Count.ShouldBe(1);
        dictionary.GetDisplay(0).ToString().ShouldBe("مسموح");
        stats.Blocked.ShouldBe(2);
    }

    [Fact]
    public void Max_entries_keeps_the_most_frequent()
    {
        var (dictionary, _) = DictionaryBuildPipeline.Build(
            [new SourceInput(SourceA, [("كتاب", 30), ("قلم", 20), ("ورقة", 10)])],
            Blocklist.Empty,
            maxEntries: 2);

        dictionary.Count.ShouldBe(2);
        dictionary.IndexOfWord("ورقة").ShouldBe(-1);
    }
}
