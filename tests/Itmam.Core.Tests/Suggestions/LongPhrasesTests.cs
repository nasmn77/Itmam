using Itmam.Core.Input;
using Itmam.Core.Presentation;
using Itmam.Core.Settings;
using Itmam.Core.Suggestions;

namespace Itmam.Core.Tests.Suggestions;

public class LongPhrasesTests
{
    private static readonly LongPhrase[] Phrases =
    [
        new("ترحيب بالعميل", "عزيزي العميل،\nشكرًا لتواصلك معنا."),
        new("إغلاق التذكرة", "تم حل المشكلة وإغلاق التذكرة.\nمع التحية"),
        new("طلب معلومات", "نرجو تزويدنا برقم الطلب للترحيب بخدمتك."),
    ];

    [Fact]
    public void A_text_keeps_its_lines_without_trailing_spaces_or_empty_ends() =>
        LongPhrases.CleanText("\r\n  مرحبًا   \r\nكيف أخدمك؟\r\n\r\n").ShouldBe("مرحبًا\nكيف أخدمك؟");

    [Theory]
    [InlineData("", null)]
    [InlineData("  ترحيب   بالعميل ", "ترحيب بالعميل")]
    public void Names_are_one_line_and_trimmed(string name, string? expected) =>
        LongPhrases.CleanName(name).ShouldBe(expected);

    [Fact]
    public void Too_long_is_refused()
    {
        LongPhrases.CleanName(new string('ب', LongPhrases.MaxNameLength + 1)).ShouldBeNull();
        LongPhrases.CleanText(new string('ب', LongPhrases.MaxTextLength + 1)).ShouldBeNull();
    }

    [Fact]
    public void Search_matches_names_first_then_texts_ignoring_hamza()
    {
        LongPhrases.Filter(Phrases, "ترحيب").Select(p => p.Name).ShouldBe(["ترحيب بالعميل", "طلب معلومات"]);
        LongPhrases.Filter(Phrases, "اغلاق").Select(p => p.Name).ShouldBe(["إغلاق التذكرة"]);
        LongPhrases.Filter(Phrases, "  ").Count.ShouldBe(3);
    }

    [Fact]
    public void The_preview_is_the_first_line()
    {
        LongPhrases.Preview("سطر أول\nسطر ثان").ShouldBe("سطر أول…");
        LongPhrases.Preview("سطر واحد").ShouldBe("سطر واحد");
    }

    [Fact]
    public void Files_keep_the_lines_and_an_existing_name_keeps_its_text()
    {
        var file = new StringWriter();
        UserListFiles.WriteLongPhrases(file, [Phrases[0], new LongPhrase("مسار", "C:\\new\\folder")]);

        var result = UserListFiles.ReadLongPhrases(new StringReader(file.ToString()), [new LongPhrase("ترحيب بالعميل", "قديم")]);

        result.Merged.ShouldBe([new LongPhrase("ترحيب بالعميل", "قديم"), new LongPhrase("مسار", "C:\\new\\folder")]);
        result.Added.ShouldBe(1);
        result.Skipped.ShouldBe(1);

        var lines = UserListFiles.ReadLongPhrases(new StringReader(file.ToString()), []);
        lines.Merged[0].Text.ShouldBe("عزيزي العميل،\nشكرًا لتواصلك معنا.");
    }

    [Fact]
    public void Settings_drop_invalid_and_duplicate_names() =>
        new AppSettings { LongPhrases = [new("ترحيب", "نص"), new("ترحيب", "آخر"), new(" ", "نص"), new("فارغ", " ")] }
            .Normalized().LongPhrases.ShouldBe([new LongPhrase("ترحيب", "نص")]);

    [Fact]
    public void The_default_shortcut_opens_the_list_anywhere_even_without_the_bar()
    {
        var interceptor = new HotkeyInterceptor(new HotkeySettings());

        interceptor.OnKeyDown(0x20, KeyModifiers.Ctrl | KeyModifiers.Shift, PopupSnapshot.Hidden)
            .ShouldBe(InterceptDecision.Swallow(InputEvent.Of(InputEventKind.OpenLongPhrases)));
        interceptor.OnKeyDown(0x20, KeyModifiers.None, PopupSnapshot.Hidden).ShouldBe(InterceptDecision.Pass);
    }

    [Fact]
    public void Its_shortcut_needs_Ctrl_or_Alt_and_cannot_repeat_another()
    {
        var settings = new HotkeySettings();

        HotkeyRules.Check(settings, HotkeyAction.OpenLongPhrases, new Hotkey(0x71, KeyModifiers.Shift)).ShouldBe(HotkeyProblem.NeedsModifier);
        HotkeyRules.Check(settings, HotkeyAction.OpenLongPhrases, settings.ToggleEnabled).ShouldNotBe(HotkeyProblem.None);
        HotkeyRules.Check(settings, HotkeyAction.OpenLongPhrases, new Hotkey(0x71, KeyModifiers.Ctrl | KeyModifiers.Shift)).ShouldBe(HotkeyProblem.None);
    }
}
