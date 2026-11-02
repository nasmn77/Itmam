using Itmam.Core.Settings;
using Itmam.Core.Suggestions;

namespace Itmam.Core.Tests.Suggestions;

public class TextShortcutsTests
{
    private static readonly TextShortcuts Shortcuts = new([
        new TextShortcut("ع1", "السلام عليكم ورحمة الله وبركاته"),
        new TextShortcut("ع2", "وعليكم السلام ورحمة الله وبركاته"),
        new TextShortcut("توقيع", "أخوكم ناصر\r\nإدارة التقنية"),
        new TextShortcut("1ع", "invalid key"),
        new TextShortcut("ع 3", "invalid key"),
    ]);

    [Fact]
    public void Only_valid_shortcuts_are_kept_and_the_text_is_one_line()
    {
        Shortcuts.Count.ShouldBe(3);
        Shortcuts.Find("توقيع").ShouldBe("أخوكم ناصر إدارة التقنية");
    }

    [Theory]
    [InlineData("ع1", "السلام عليكم ورحمة الله وبركاته")]
    [InlineData("ع١", "السلام عليكم ورحمة الله وبركاته")] // Arabic-Indic digit
    [InlineData("عَ2", "وعليكم السلام ورحمة الله وبركاته")] // tashkeel ignored
    [InlineData("ع", null)]                                     // exact match only
    [InlineData("ع12", null)]
    [InlineData("", null)]
    public void A_key_typed_exactly_finds_its_text(string typed, string? expected) =>
        Shortcuts.Find(typed).ShouldBe(expected);

    [Theory]
    [InlineData(" عَ1 ", "ع1")]
    [InlineData("سلام", "سلام")]
    [InlineData("1ع", null)]
    [InlineData("ع 1", null)]
    [InlineData("a1", "a1")]   // any language (2.0)
    [InlineData("brb", "brb")]
    [InlineData("1a", null)]
    [InlineData("", null)]
    public void Keys_are_cleaned_or_refused(string key, string? expected) =>
        TextShortcuts.CleanKey(key).ShouldBe(expected);

    [Fact]
    public void Settings_drop_invalid_and_duplicate_shortcuts()
    {
        var settings = new AppSettings
        {
            TextShortcuts = [new("ع1", "أولى"), new("ع1", "ثانية"), new("1x", "خطأ"), new("ع2", "  ")],
        }.Normalized();

        settings.TextShortcuts.ShouldBe([new TextShortcut("ع1", "أولى")]);
    }

    [Fact]
    public void A_key_in_another_language_ignores_letter_case()
    {
        var shortcuts = new TextShortcuts([new("brb", "be right back"), new("שלום1", "שלום וברכה")]);

        shortcuts.Find("BRB").ShouldBe("be right back");
        shortcuts.Find("Brb").ShouldBe("be right back");
        shortcuts.Find("שלום1").ShouldBe("שלום וברכה");
        shortcuts.Find("br").ShouldBeNull();
    }
}
