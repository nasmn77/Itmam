using Itmam.Core.Learning;

namespace Itmam.Core.Tests.Learning;

public class UserWordIndexTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);

    private readonly UserWordIndex _index = new();

    [Fact]
    public void First_use_adds_word_with_count_one()
    {
        var entry = _index.RecordUse("إتمام", Now).ShouldNotBeNull();

        entry.Count.ShouldBe(1);
        entry.LastUsed.ShouldBe(Now);
        _index.Count.ShouldBe(1);
    }

    [Fact]
    public void Later_use_increments_count_and_last_used()
    {
        _index.RecordUse("إتمام", Now);
        var entry = _index.RecordUse("إتمام", Now.AddHours(1)).ShouldNotBeNull();

        entry.Count.ShouldBe(2);
        entry.LastUsed.ShouldBe(Now.AddHours(1));
    }

    [Fact]
    public void Tashkeel_variants_are_the_same_word()
    {
        _index.RecordUse("كَتَب", Now);
        _index.RecordUse("كتب", Now);

        _index.Get("كتب").ShouldNotBeNull().Count.ShouldBe(2);
    }

    [Theory]
    [InlineData("")]
    [InlineData("hello")]
    [InlineData("كتب2")]
    public void Non_arabic_words_are_not_learned(string word) => _index.RecordUse(word, Now).ShouldBeNull();

    [Fact]
    public void Prefix_search_uses_normalized_keys()
    {
        _index.RecordUse("أحمد", Now);
        _index.RecordUse("احتفال", Now);
        _index.RecordUse("كتاب", Now);

        _index.FindByPrefix("اح").Select(w => w.Word).ShouldBe(["احتفال", "أحمد"]);
    }

    [Fact]
    public void Remove_deletes_from_both_lookups()
    {
        _index.RecordUse("كتاب", Now);
        _index.Remove("كتاب").ShouldBeTrue();

        _index.Get("كتاب").ShouldBeNull();
        _index.FindByPrefix("كت").ShouldBeEmpty();
    }

    [Fact]
    public void Blocking_an_unknown_word_creates_a_blocked_entry()
    {
        _index.SetBlocked("كلمة", blocked: true, Now);

        _index.IsBlocked("كلمة").ShouldBeTrue();
        _index.Get("كلمة").ShouldNotBeNull().Count.ShouldBe(0);
    }
}
