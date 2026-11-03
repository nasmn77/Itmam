using Itmam.Core.Languages;

namespace Itmam.Core.Tests.Languages;

public class LanguageRegistryTests
{
    [Theory]
    [InlineData(0x04010401, "ar", true)]   // Arabic (Saudi Arabia)
    [InlineData(0x14010401, "ar", true)]   // another Arabic keyboard
    [InlineData(0x04090409, "en", false)]
    [InlineData(0x040C040C, "fr", false)]
    [InlineData(0x040D040D, "he", true)]
    [InlineData(0x04290429, "fa", true)]
    [InlineData(0x04200420, "ur", true)]
    [InlineData(0x04190419, "ru", false)]
    public void A_layout_gives_its_language(long layout, string id, bool rightToLeft)
    {
        var language = LanguageRegistry.ForLayout((nint)layout).ShouldNotBeNull();

        language.Id.ShouldBe(id);
        language.RightToLeft.ShouldBe(rightToLeft);
    }

    [Theory]
    [InlineData(0x08040804)] // Chinese (IME)
    [InlineData(0x04110411)] // Japanese (IME)
    [InlineData(0x04120412)] // Korean (IME)
    [InlineData(0x041E041E)] // Thai (no spaces)
    [InlineData(0x04390439)] // Hindi (vowel signs are marks)
    [InlineData(0)]
    public void Input_method_and_complex_script_languages_are_not_supported(long layout)
    {
        LanguageRegistry.ForLayout((nint)layout).ShouldBeNull();
        LanguageRegistry.Support(LanguageRegistry.ForLayout((nint)layout)).ShouldBe(LanguageSupport.Unsupported);
    }

    [Fact]
    public void One_profile_per_language()
    {
        LanguageRegistry.ForTag("ar").ShouldBeSameAs(LanguageProfile.Arabic);
        LanguageRegistry.ForTag("he").ShouldBeSameAs(LanguageRegistry.ForLayout(0x040D040D));
        LanguageRegistry.Support(LanguageProfile.Arabic).ShouldBe(LanguageSupport.Full);
        LanguageRegistry.Support(LanguageRegistry.ForTag("he")).ShouldBe(LanguageSupport.LearningOnly);
    }

    [Fact]
    public void Text_of_no_particular_language_is_arabic_when_it_starts_arabic()
    {
        LanguageRegistry.ForText("  مَرحبا بكم").ShouldBeSameAs(LanguageProfile.Arabic);
        LanguageRegistry.ForText("good morning").ShouldBeSameAs(LanguageRegistry.AnyScript);
        LanguageRegistry.AnyScript.IsWord("مرحبا").ShouldBeTrue();
    }

    [Fact]
    public void The_zero_width_non_joiner_stays_inside_persian_words() =>
        LanguageRegistry.ForTag("fa").IsWord("می‌خواهم").ShouldBeTrue();
}
