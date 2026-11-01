using System.Globalization;
using Itmam.Core.Suggestions;

namespace Itmam.Core.Tests.Suggestions;

public class LongPhraseVariablesTests
{
    // Sunday 1 November 2026, 10:30 in the morning.
    private static readonly DateTime Morning = new(2026, 11, 1, 10, 30, 0);

    [Fact]
    public void Single_braces_are_asked_for_and_double_braces_are_automatic()
    {
        var parts = LongPhraseVariables.Parse("مرحبًا {اسم العميل}، اليوم {{التاريخ}}.");

        parts.ShouldBe(
        [
            new VariablePart.Literal("مرحبًا "),
            new VariablePart.Input("اسم العميل"),
            new VariablePart.Literal("، اليوم "),
            new VariablePart.Auto(AutoVariable.Date, "{{التاريخ}}"),
            new VariablePart.Literal("."),
        ]);
    }

    [Fact]
    public void A_name_asked_for_twice_is_asked_once_in_order()
    {
        LongPhraseVariables.InputNames("{الاسم} طلبك {رقم الطلب} يا {الاسم}").ShouldBe(["الاسم", "رقم الطلب"]);
    }

    [Fact]
    public void The_same_word_can_be_asked_for_or_automatic()
    {
        // «التاريخ» between single braces is a date the user types, not today's date.
        LongPhraseVariables.InputNames("موعدك {التاريخ}، وكُتب {{التاريخ}}").ShouldBe(["التاريخ"]);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{  }")]
    [InlineData("{ناقص")]
    [InlineData("سطر {أول\nثان}")]
    [InlineData("{{مزدوج ناقص}")]
    public void Braces_that_do_not_form_a_variable_stay_text(string text) =>
        LongPhraseVariables.Parse(text).ShouldAllBe(p => p is VariablePart.Literal);

    [Fact]
    public void A_name_longer_than_30_characters_is_not_a_variable()
    {
        var name = new string('ب', LongPhraseVariables.MaxNameLength + 1);
        LongPhraseVariables.Parse("{" + name + "}").ShouldAllBe(p => p is VariablePart.Literal);
        LongPhraseVariables.IsValidName(new string('ب', LongPhraseVariables.MaxNameLength)).ShouldBeTrue();
    }

    [Theory]
    [InlineData("{{{الوقت}}}")]
    [InlineData("{الاسم}}")]
    public void Extra_braces_around_a_variable_leave_it_as_text(string text) =>
        LongPhraseVariables.Fill(text, null, Morning).ShouldBe(text);

    [Fact]
    public void An_unknown_automatic_name_is_reported_and_kept_as_text()
    {
        LongPhraseVariables.UnknownAutoVariables("رقم {{رقم الطلب}} و{{التاريخ}}").ShouldBe(["{{رقم الطلب}}"]);
        LongPhraseVariables.Fill("رقم {{رقم الطلب}}", null, Morning).ShouldBe("رقم {{رقم الطلب}}");
    }

    [Theory]
    [InlineData("{{date}}")]
    [InlineData("{{Date}}")]
    [InlineData("{{ التاريخ }}")]
    public void English_names_and_extra_spaces_are_accepted(string token) =>
        LongPhraseVariables.Parse(token).ShouldHaveSingleItem().ShouldBeOfType<VariablePart.Auto>().Kind.ShouldBe(AutoVariable.Date);

    [Fact]
    public void Values_replace_the_variables_and_missing_ones_are_empty()
    {
        var text = "مرحبًا {الاسم}، طلبك {رقم الطلب}.";
        var values = new Dictionary<string, string> { ["الاسم"] = "أحمد" };

        LongPhraseVariables.Fill(text, values, Morning).ShouldBe("مرحبًا أحمد، طلبك .");
    }

    [Fact]
    public void The_preview_keeps_the_variables_asked_for()
    {
        LongPhraseVariables.Fill("مرحبًا {الاسم}، {{اليوم}}", null, Morning).ShouldBe("مرحبًا {الاسم}، الأحد");
    }

    [Fact]
    public void An_arabic_phrase_gets_arabic_values()
    {
        var text = "التاريخ {{التاريخ}}، {{اليوم}}، الساعة {{الوقت}}";
        LongPhraseVariables.Fill(text, null, Morning).ShouldBe("التاريخ 1 نوفمبر 2026، الأحد، الساعة 10:30 ص");
    }

    [Fact]
    public void An_english_phrase_gets_english_values_even_with_arabic_variable_names()
    {
        var text = "Date: {{التاريخ}}, {{day}}, {{time}}";
        LongPhraseVariables.Fill(text, null, Morning).ShouldBe("Date: November 1, 2026, Sunday, 10:30 AM");
    }

    [Theory]
    [InlineData(0, 5, "12:05 ص", "12:05 AM")]
    [InlineData(12, 0, "12:00 م", "12:00 PM")]
    [InlineData(23, 59, "11:59 م", "11:59 PM")]
    public void Times_use_twelve_hours(int hour, int minute, string arabic, string english)
    {
        var time = new DateTime(2026, 11, 1, hour, minute, 0);
        LongPhraseVariables.Format(AutoVariable.Time, time, arabic: true).ShouldBe(arabic);
        LongPhraseVariables.Format(AutoVariable.Time, time, arabic: false).ShouldBe(english);
    }

    [Fact]
    public void The_hijri_date_follows_umm_al_qura()
    {
        var calendar = new UmAlQuraCalendar();
        var day = calendar.GetDayOfMonth(Morning);
        var year = calendar.GetYear(Morning);

        var arabic = LongPhraseVariables.Format(AutoVariable.HijriDate, Morning, arabic: true);
        var english = LongPhraseVariables.Format(AutoVariable.HijriDate, Morning, arabic: false);

        arabic.ShouldStartWith(day.ToString(CultureInfo.InvariantCulture) + " ");
        arabic.ShouldEndWith(year.ToString(CultureInfo.InvariantCulture) + " هـ");
        english.ShouldEndWith(year.ToString(CultureInfo.InvariantCulture) + " AH");
    }

    [Fact]
    public void Tokens_round_trip_through_the_parser()
    {
        foreach (var kind in LongPhraseVariables.All)
        {
            foreach (var arabic in new[] { true, false })
            {
                LongPhraseVariables.Parse(LongPhraseVariables.AutoToken(kind, arabic))
                    .ShouldHaveSingleItem().ShouldBeOfType<VariablePart.Auto>().Kind.ShouldBe(kind);
            }
        }

        LongPhraseVariables.Parse(LongPhraseVariables.InputToken("  اسم   العميل ")).ShouldBe([new VariablePart.Input("اسم العميل")]);
    }

    [Fact]
    public void A_phrase_without_variables_is_unchanged()
    {
        var text = "شكرًا لتواصلك.\nمع التحية";
        LongPhraseVariables.HasInputs(text).ShouldBeFalse();
        LongPhraseVariables.Fill(text, null, Morning).ShouldBe(text);
    }
}
