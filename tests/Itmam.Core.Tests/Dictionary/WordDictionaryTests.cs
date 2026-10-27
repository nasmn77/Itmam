using Itmam.Core.Dictionary;

namespace Itmam.Core.Tests.Dictionary;

public class WordDictionaryTests
{
    private static readonly WordDictionary Sample = TestDictionaries.Of(
        ("كتاب", 100), ("كتب", 80), ("كتابة", 30), ("مكتب", 50), ("أحمد", 40), ("احمد", 5), ("كاتب", 20));

    private static string[] KeysInRange(WordDictionary dictionary, string prefix)
    {
        var (start, end) = dictionary.FindPrefixRange(prefix);
        return Enumerable.Range(start, end - start).Select(i => dictionary.GetKey(i).ToString()).ToArray();
    }

    [Fact]
    public void Entries_are_sorted_by_normalized_key()
    {
        var keys = Enumerable.Range(0, Sample.Count).Select(i => Sample.GetKey(i).ToString()).ToArray();
        keys.ShouldBe(keys.Order(StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void Prefix_range_contains_exactly_the_matching_keys()
    {
        KeysInRange(Sample, "كت").ShouldBe(["كتاب", "كتابه", "كتب"]);
        KeysInRange(Sample, "ك").Length.ShouldBe(4);
        KeysInRange(Sample, "احمد").ShouldBe(["احمد", "احمد"]);
        KeysInRange(Sample, "كتابه").ShouldBe(["كتابه"]);
        KeysInRange(Sample, "ز").ShouldBeEmpty();
        KeysInRange(Sample, "كتابات").ShouldBeEmpty();
    }

    [Fact]
    public void Distinct_spellings_of_one_key_are_kept_with_their_own_frequency_most_frequent_first()
    {
        var dictionary = TestDictionaries.Of(("إن", 300), ("أن", 1000), ("ان", 60));

        var (start, end) = dictionary.FindPrefixRange("ان");
        var entries = Enumerable.Range(start, end - start)
            .Select(i => (dictionary.GetDisplay(i).ToString(), FrequencyScale.ToPerMillion(dictionary.GetFrequency(i))))
            .ToArray();

        entries.ShouldBe([("أن", 1000.0), ("إن", 300.0), ("ان", 60.0)]);
    }

    [Fact]
    public void Spellings_under_5_percent_of_the_top_spelling_are_dropped()
    {
        var dictionary = TestDictionaries.Of(("أنا", 1000), ("انا", 49), ("أنى", 50));

        dictionary.IndexOfWord("أنا").ShouldBeGreaterThanOrEqualTo(0);
        dictionary.IndexOfWord("أنى").ShouldBeGreaterThanOrEqualTo(0);
        dictionary.IndexOfWord("انا").ShouldBe(-1);
    }

    [Fact]
    public void Bare_alef_variants_of_a_more_frequent_spelling_are_dropped_when_enabled()
    {
        WeightedWord[] words = [new("أن", 1000), new("إن", 100), new("ان", 300), new("على", 500), new("علي", 50), new("انتظار", 80), new("إنتظار", 5)];

        var dictionary = WordDictionary.Build(words, dropBareAlefVariants: true);

        dictionary.IndexOfWord("ان").ShouldBe(-1);
        dictionary.IndexOfWord("أن").ShouldBeGreaterThanOrEqualTo(0);
        dictionary.IndexOfWord("إن").ShouldBeGreaterThanOrEqualTo(0);
        dictionary.IndexOfWord("على").ShouldBeGreaterThanOrEqualTo(0);
        dictionary.IndexOfWord("علي").ShouldBeGreaterThanOrEqualTo(0);
        dictionary.IndexOfWord("انتظار").ShouldBeGreaterThanOrEqualTo(0);
        dictionary.IndexOfWord("إنتظار").ShouldBeGreaterThanOrEqualTo(0);
        WordDictionary.Build(words).IndexOfWord("ان").ShouldBeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public void At_most_four_spellings_are_kept_per_key()
    {
        var dictionary = TestDictionaries.Of(("أن", 100), ("إن", 90), ("ان", 80), ("آن", 70), ("ٱن", 60));

        dictionary.Count.ShouldBe(WordDictionary.MaxSpellingsPerKey);
        dictionary.IndexOfWord("ٱن").ShouldBe(-1);
    }

    [Fact]
    public void Identical_spellings_differing_only_in_tashkeel_are_summed()
    {
        var dictionary = TestDictionaries.Of(("كِتاب", 10), ("كتاب", 5));

        dictionary.Count.ShouldBe(1);
        FrequencyScale.ToPerMillion(dictionary.GetFrequency(0)).ShouldBe(15);
    }

    [Fact]
    public void Display_keeps_original_spelling_and_drops_tashkeel()
    {
        var dictionary = TestDictionaries.Of(("مَدْرَسَة", 10));
        dictionary.GetDisplay(0).ToString().ShouldBe("مدرسة");
        dictionary.GetKey(0).ToString().ShouldBe("مدرسه");
    }

    [Fact]
    public void Exact_word_lookup_requires_the_same_spelling()
    {
        Sample.IndexOfWord("أحمد").ShouldBeGreaterThanOrEqualTo(0);
        Sample.IndexOfWord("أَحمد").ShouldBeGreaterThanOrEqualTo(0);
        Sample.IndexOfWord("احمد").ShouldBeGreaterThanOrEqualTo(0);
        Sample.IndexOfWord("إحمد").ShouldBe(-1);
        Sample.IndexOfWord("قلم").ShouldBe(-1);
    }

    [Fact]
    public void File_round_trip_preserves_every_entry_and_source()
    {
        var sources = new[] { new DictionarySource("Test source", "CC0", "https://example.org") };
        var original = WordDictionary.Build(
            [new("كتاب", 100), new("أحمد", 40), new("مستشفى", 3.5), new("نادر", 0.0004)],
            sources);

        using var stream = new MemoryStream();
        DictionaryFile.Write(stream, original);
        stream.Position = 0;
        var loaded = DictionaryFile.Read(stream);

        loaded.Count.ShouldBe(original.Count);
        loaded.Sources.ShouldBe(sources);
        for (var i = 0; i < original.Count; i++)
        {
            loaded.GetKey(i).ToString().ShouldBe(original.GetKey(i).ToString());
            loaded.GetDisplay(i).ToString().ShouldBe(original.GetDisplay(i).ToString());
            loaded.GetFrequency(i).ShouldBe(original.GetFrequency(i));
        }
    }

    [Fact]
    public void Reading_a_file_with_wrong_magic_fails_cleanly()
    {
        using var stream = new MemoryStream("NOPE\u0001\u0000"u8.ToArray());
        Should.Throw<InvalidDataException>(() => DictionaryFile.Read(stream));
    }

    [Fact]
    public void Reading_a_truncated_file_fails_cleanly()
    {
        using var full = new MemoryStream();
        DictionaryFile.Write(full, Sample);
        using var truncated = new MemoryStream(full.ToArray()[..(int)(full.Length / 2)]);

        Should.Throw<Exception>(() => DictionaryFile.Read(truncated))
            .ShouldBeAssignableTo<Exception>()!.GetType().ShouldBeOneOf(typeof(InvalidDataException), typeof(EndOfStreamException));
    }
}
