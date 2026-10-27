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
        KeysInRange(Sample, "كتابه").ShouldBe(["كتابه"]);
        KeysInRange(Sample, "ز").ShouldBeEmpty();
        KeysInRange(Sample, "كتابات").ShouldBeEmpty();
    }

    [Fact]
    public void Spelling_variants_merge_and_keep_the_most_frequent_spelling()
    {
        var index = Sample.IndexOfKey("احمد");

        index.ShouldBeGreaterThanOrEqualTo(0);
        Sample.GetDisplay(index).ToString().ShouldBe("أحمد");
        FrequencyScale.ToPerMillion(Sample.GetFrequency(index)).ShouldBe(45);
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
        Sample.IndexOfWord("احمد").ShouldBe(-1);
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
