using Itmam.Core.Text;

namespace Itmam.Core.Tests.Text;

public class ArabicNormalizerTests
{
    [Theory]
    [InlineData("أحمد", "احمد")]
    [InlineData("إسلام", "اسلام")]
    [InlineData("آمن", "امن")]
    [InlineData("ٱلله", "الله")]
    [InlineData("سأل", "سال")]
    public void Alef_forms_become_bare_alef(string word, string expected) =>
        ArabicNormalizer.Normalize(word).ShouldBe(expected);

    [Theory]
    [InlineData("على", "علي")]
    [InlineData("مستشفى", "مستشفي")]
    public void Alef_maqsura_becomes_ya(string word, string expected) =>
        ArabicNormalizer.Normalize(word).ShouldBe(expected);

    [Fact]
    public void Ta_marbuta_and_ha_are_equal_at_word_end() =>
        ArabicNormalizer.Normalize("مدرسة").ShouldBe(ArabicNormalizer.Normalize("مدرسه"));

    [Theory]
    [InlineData("مُحَمَّدٌ", "محمد")]
    [InlineData("كِتَابًا", "كتابا")]
    [InlineData("سْ", "س")]
    [InlineData("هٰذا", "هذا")]
    public void Tashkeel_is_removed(string word, string expected) =>
        ArabicNormalizer.Normalize(word).ShouldBe(expected);

    [Fact]
    public void Tatweel_is_removed() => ArabicNormalizer.Normalize("مـــحمد").ShouldBe("محمد");

    [Theory]
    [InlineData("سماء")]
    [InlineData("مؤمن")]
    [InlineData("سائل")]
    public void Hamza_seats_are_kept(string word) => ArabicNormalizer.Normalize(word).ShouldBe(word);

    [Fact]
    public void All_rules_combine() => ArabicNormalizer.Normalize("أُسْتَاذَةـ").ShouldBe("استاذه");

    [Fact]
    public void Hamza_fallback_swaps_each_seat()
    {
        ArabicNormalizer.HamzaFallbackVariants("مءمن").ShouldBe(["مؤمن", "مئمن"], ignoreOrder: true);
        ArabicNormalizer.HamzaFallbackVariants("مؤمن").ShouldBe(["مءمن", "مئمن"], ignoreOrder: true);
    }

    [Fact]
    public void Hamza_fallback_expands_two_hamzas_into_eight_variants() =>
        ArabicNormalizer.HamzaFallbackVariants("ءاء").Count.ShouldBe(8);

    [Fact]
    public void Hamza_fallback_is_empty_without_hamza() =>
        ArabicNormalizer.HamzaFallbackVariants("كتاب").ShouldBeEmpty();
}
