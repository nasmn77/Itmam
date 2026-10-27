using Itmam.Core.Dictionary.Building;

namespace Itmam.Core.Tests.Dictionary;

public class BlocklistTests
{
    // Neutral placeholder words stand in for real entries: سيارة (5 letters), بيت (3), من (2), مؤمن (hamza seat).
    private static readonly Blocklist List = Blocklist.Read(new StringReader("# comment\n\nسيارة\nبيت\nمن\nمؤمن\n"));

    [Theory]
    [InlineData("سيارة")]
    [InlineData("سياره")]
    [InlineData("سَيّارَة")]
    [InlineData("سيـارة")]
    [InlineData("بيت")]
    [InlineData("من")]
    public void Exact_word_is_blocked_in_every_spelling(string word) => List.IsBlocked(word).ShouldBeTrue();

    [Theory]
    [InlineData("مؤمن")]
    [InlineData("مئمن")]
    [InlineData("مءمن")]
    [InlineData("مُؤمِن")]
    public void Hamza_seat_variants_are_blocked(string word) => List.IsBlocked(word).ShouldBeTrue();

    [Theory]
    [InlineData("والسيارة")]
    [InlineData("بالسيارة")]
    [InlineData("للسيارة")]
    [InlineData("وسيارة")]
    [InlineData("فسيارة")]
    [InlineData("كسيارة")]
    [InlineData("لسيارة")]
    [InlineData("السيارة")]
    public void Long_words_are_blocked_with_any_clitic(string word) => List.IsBlocked(word).ShouldBeTrue();

    [Theory]
    [InlineData("البيت", true)]
    [InlineData("والبيت", true)]
    [InlineData("للبيت", true)]
    [InlineData("وبيت", false)]
    [InlineData("فبيت", false)]
    [InlineData("لبيت", false)]
    public void Three_letter_words_are_blocked_only_after_the_article(string word, bool blocked) =>
        List.IsBlocked(word).ShouldBe(blocked);

    [Theory]
    [InlineData("المن")]
    [InlineData("ومن")]
    [InlineData("فمن")]
    [InlineData("ثمن")]
    public void Two_letter_words_are_never_blocked_inside_other_words(string word) => List.IsBlocked(word).ShouldBeFalse();

    [Fact]
    public void Unrelated_words_are_not_blocked() => List.IsBlocked("كتاب").ShouldBeFalse();
}
