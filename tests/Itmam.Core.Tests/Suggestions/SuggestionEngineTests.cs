using Itmam.Core.Dictionary;
using Itmam.Core.Languages;
using Itmam.Core.Learning;
using Itmam.Core.Suggestions;

namespace Itmam.Core.Tests.Suggestions;

public class SuggestionEngineTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);

    private readonly FixedTimeProvider _time = new(Now);
    private readonly UserWordIndex _user = new();

    private SuggestionEngine Engine(WordDictionary dictionary, SuggestionOptions? options = null) =>
        new(dictionary, _user, options ?? new SuggestionOptions { MaxSuggestions = 3 }, _time);

    private static string[] Words(IReadOnlyList<Suggestion> suggestions) => suggestions.Select(s => s.Word).ToArray();

    [Fact]
    public void Suggestions_are_ordered_by_frequency()
    {
        var engine = Engine(TestDictionaries.Of(("كتاب", 100), ("كتب", 300), ("كتابة", 30), ("كتيب", 1)));

        Words(engine.Suggest("كت")).ShouldBe(["كتب", "كتاب", "كتابة"]);
    }

    private static readonly SuggestionOptions AnyFrequency = new() { MaxSuggestions = 3, KeyTypoMinScore = double.NegativeInfinity, KeyTypoMinBaseScore = double.NegativeInfinity };

    [Fact]
    public void A_key_next_to_the_right_one_still_finds_the_word()
    {
        var suggestions = Engine(TestDictionaries.Of(("موظف", 100)), AnyFrequency).Suggest("مزظ");

        Words(suggestions).ShouldBe(["موظف"]);
        suggestions[0].Kind.ShouldBe(MatchKind.KeyTypo);
    }

    [Fact]
    public void A_key_far_from_the_right_one_finds_nothing() =>
        Engine(TestDictionaries.Of(("موظف", 100)), AnyFrequency).Suggest("مقظ").ShouldBeEmpty();

    [Fact]
    public void A_whole_word_can_be_the_correction() =>
        Words(Engine(TestDictionaries.Of(("موظف", 100)), AnyFrequency).Suggest("مزظف")).ShouldBe(["موظف"]);

    [Fact]
    public void A_correct_word_with_no_longer_forms_is_not_corrected() =>
        Engine(TestDictionaries.Of(("موظف", 100), ("موزف", 50)), AnyFrequency).Suggest("موزف").ShouldBeEmpty();

    [Fact]
    public void A_learned_word_is_not_corrected()
    {
        _user.RecordUse("مزظو", Now);

        Engine(TestDictionaries.Of(("موظو", 100)), AnyFrequency).Suggest("مزظو").ShouldBeEmpty();
    }

    [Fact]
    public void Corrections_only_when_nothing_else_matches() =>
        Words(Engine(TestDictionaries.Of(("موظف", 100), ("مزظا", 1)), AnyFrequency).Suggest("مزظ")).ShouldBe(["مزظا"]);

    private static readonly SuggestionOptions RareMatches = new()
    {
        MaxSuggestions = 3,
        KeyTypoRareWordScore = double.PositiveInfinity,
        KeyTypoStrongScore = double.NegativeInfinity,
    };

    [Fact]
    public void A_rare_misspelling_in_the_dictionary_is_corrected() =>
        Words(Engine(TestDictionaries.Of(("مدرسة", 1000), ("مدرية", 1)), RareMatches).Suggest("مدرية")).ShouldContain("مدرسة");

    [Fact]
    public void The_start_of_a_longer_word_is_not_corrected() =>
        Words(Engine(TestDictionaries.Of(("موظف", 1000), ("مزظا", 1)), RareMatches).Suggest("مزظ")).ShouldBe(["مزظا"]);

    [Fact]
    public void A_common_typed_word_is_not_corrected() =>
        Engine(TestDictionaries.Of(("مدرسة", 1000), ("مدرية", 1)), RareMatches with { KeyTypoRareWordScore = double.NegativeInfinity }).Suggest("مدرية").ShouldBeEmpty();

    [Fact]
    public void Only_a_common_correction_replaces_rare_matches() =>
        Engine(TestDictionaries.Of(("مدرسة", 1000), ("مدرية", 1)), RareMatches with { KeyTypoStrongScore = double.PositiveInfinity }).Suggest("مدرية").ShouldBeEmpty();

    [Theory]
    [InlineData("مدسرة")] // two letters swapped
    [InlineData("مدرسسة")] // a letter too many
    [InlineData("مدسة")] // a letter missing
    public void One_typing_mistake_is_undone(string typed) =>
        Words(Engine(TestDictionaries.Of(("مدرسة", 100)), AnyFrequency).Suggest(typed)).ShouldBe(["مدرسة"]);

    [Theory]
    [InlineData("نضارة", "نظارة")]
    [InlineData("زهب", "ذهب")]
    [InlineData("سوب", "ثوب")]
    public void A_letter_that_sounds_alike_is_corrected(string typed, string word) =>
        Words(Engine(TestDictionaries.Of((word, 100)), AnyFrequency).Suggest(typed)).ShouldBe([word]);

    [Fact]
    public void Two_mistakes_are_not_undone() =>
        Engine(TestDictionaries.Of(("مدرسة", 100)), AnyFrequency).Suggest("مقسسة").ShouldBeEmpty();

    [Fact]
    public void A_missing_space_is_added()
    {
        var options = AnyFrequency with { KeyTypoSplitMinScore = double.NegativeInfinity };

        Words(Engine(TestDictionaries.Of(("في", 100), ("البيت", 100)), options).Suggest("فيالبيت")).ShouldBe(["في البيت"]);
    }

    [Fact]
    public void A_rare_word_in_the_dictionary_loses_no_letter_and_gets_no_space()
    {
        var options = RareMatches with { KeyTypoMinScore = double.NegativeInfinity, KeyTypoMinBaseScore = double.NegativeInfinity, KeyTypoSplitMinScore = double.NegativeInfinity };

        Engine(TestDictionaries.Of(("طاولة", 1000), ("مطاولة", 1)), options).Suggest("مطاولة").ShouldBeEmpty();
        Engine(TestDictionaries.Of(("شارع", 1000), ("ولدي", 1000), ("شارعولدي", 1)), options).Suggest("شارعولدي").ShouldBeEmpty();
    }

    [Fact]
    public void A_short_second_part_is_not_split_off() =>
        Engine(TestDictionaries.Of(("حملت", 100), ("ها", 100)), AnyFrequency with { KeyTypoSplitMinScore = double.NegativeInfinity })
            .Suggest("حملتها").ShouldBeEmpty();

    [Fact]
    public void A_missing_space_needs_two_common_words() =>
        Engine(TestDictionaries.Of(("في", 100), ("البيت", 100)), AnyFrequency with { KeyTypoSplitMinScore = double.PositiveInfinity })
            .Suggest("فيالبيت").ShouldBeEmpty();

    [Fact]
    public void A_blocked_word_is_not_used_for_a_correction()
    {
        _user.SetBlocked("مدرسة", blocked: true, Now);

        Engine(TestDictionaries.Of(("مدرسة", 100)), AnyFrequency).Suggest("مدسرة").ShouldBeEmpty();
    }

    [Fact]
    public void Two_letters_are_too_few_to_correct() =>
        Engine(TestDictionaries.Of(("موظف", 100)), AnyFrequency).Suggest("مز").ShouldBeEmpty();

    [Fact]
    public void Rare_corrections_are_left_out() =>
        Engine(TestDictionaries.Of(("موظف", 100)), AnyFrequency with { KeyTypoMinScore = double.PositiveInfinity }).Suggest("مزظ").ShouldBeEmpty();

    [Fact]
    public void Keyboard_neighbors_follow_the_standard_layouts()
    {
        LanguageProfile.Arabic.KeyNeighbors('ز').ShouldBe("كمظو", ignoreOrder: true);
        LanguageProfile.Arabic.KeyNeighbors('ه').ShouldContain('و'); // ة shares its key letter with ه
        LanguageProfile.English.KeyNeighbors('r').ShouldBe("etdf", ignoreOrder: true);
        LanguageProfile.Arabic.KeyNeighbors('x').ShouldBeEmpty();
    }

    [Fact]
    public void Nothing_is_suggested_below_min_chars()
    {
        var engine = Engine(TestDictionaries.Of(("كتاب", 100)), new SuggestionOptions { MinChars = 2 });

        engine.Suggest("ك").ShouldBeEmpty();
        engine.Suggest("كت").ShouldNotBeEmpty();
    }

    [Fact]
    public void Non_arabic_input_gives_nothing() =>
        Engine(TestDictionaries.Of(("كتاب", 100))).Suggest("ab").ShouldBeEmpty();

    [Fact]
    public void Literal_prefix_matches_rank_above_comparable_normalization_only_matches()
    {
        var engine = Engine(TestDictionaries.Of(("أمل", 30), ("امتحان", 10)));

        var suggestions = engine.Suggest("ام");

        Words(suggestions).ShouldBe(["امتحان", "أمل"]);
        suggestions[0].Kind.ShouldBe(MatchKind.Literal);
        suggestions[1].Kind.ShouldBe(MatchKind.Normalized);
    }

    [Fact]
    public void Much_more_frequent_normalized_match_can_outrank_a_literal_one() =>
        Words(Engine(TestDictionaries.Of(("أمل", 1000), ("امتحان", 5))).Suggest("ام")).ShouldBe(["أمل", "امتحان"]);

    [Fact]
    public void Infinite_literal_bonus_makes_literal_matches_always_first()
    {
        var engine = Engine(
            TestDictionaries.Of(("أمل", 1000), ("امتحان", 5)),
            new SuggestionOptions { LiteralMatchBonus = double.PositiveInfinity });

        Words(engine.Suggest("ام")).ShouldBe(["امتحان", "أمل"]);
    }

    [Fact]
    public void Spellings_of_the_typed_word_get_the_complete_word_bonus()
    {
        var dictionary = TestDictionaries.Of(("أن", 100), ("إن", 30), ("أنا", 200), ("أنت", 150));

        Words(Engine(dictionary).Suggest("ان")).ShouldBe(["أن", "إن", "أنا"]);
        Words(Engine(dictionary, new SuggestionOptions { CompleteWordBonus = 0 }).Suggest("ان"))
            .ShouldBe(["أنا", "أنت", "أن"]);
    }

    [Fact]
    public void Normalized_match_restores_hamza_and_ta_marbuta_spelling()
    {
        var engine = Engine(TestDictionaries.Of(("أحمد", 100), ("مدرسة", 50)));

        Words(engine.Suggest("احم")).ShouldBe(["أحمد"]);
        Words(engine.Suggest("مدرسه")).ShouldBe(["مدرسة"]);
    }

    [Fact]
    public void The_fully_typed_word_is_never_suggested()
    {
        var engine = Engine(TestDictionaries.Of(("كتاب", 100), ("كتابة", 10)));

        Words(engine.Suggest("كتاب")).ShouldBe(["كتابة"]);
        Words(engine.Suggest("كِتاب")).ShouldBe(["كتابة"]);
    }

    [Fact]
    public void Tashkeel_in_typed_text_is_ignored_for_matching() =>
        Words(Engine(TestDictionaries.Of(("كتاب", 100))).Suggest("كِتَا")).ShouldBe(["كتاب"]);

    [Fact]
    public void Hamza_seat_fallback_is_used_when_direct_matches_are_few()
    {
        var engine = Engine(TestDictionaries.Of(("مؤمن", 50)));

        var suggestion = engine.Suggest("مءم").ShouldHaveSingleItem();

        suggestion.Word.ShouldBe("مؤمن");
        suggestion.Kind.ShouldBe(MatchKind.HamzaFallback);
    }

    [Fact]
    public void Frequent_user_words_rise_above_dictionary_frequency()
    {
        var engine = Engine(TestDictionaries.Of(("كتب", 300), ("كتاب", 100)));
        for (var i = 0; i < 50; i++)
        {
            _user.RecordUse("كتاب", Now);
        }

        Words(engine.Suggest("كت")).ShouldBe(["كتاب", "كتب"]);
    }

    [Fact]
    public void User_boost_decays_with_time()
    {
        var engine = Engine(TestDictionaries.Of(("كتب", 300), ("كتاب", 100)));
        for (var i = 0; i < 50; i++)
        {
            _user.RecordUse("كتاب", Now);
        }

        _time.Now = Now.AddDays(365);

        Words(engine.Suggest("كت")).ShouldBe(["كتب", "كتاب"]);
    }

    [Fact]
    public void New_word_becomes_suggestible_after_the_threshold()
    {
        var engine = Engine(WordDictionary.Empty, new SuggestionOptions { MinUsesToSuggest = 2 });

        _user.RecordUse("إتمام", Now);
        engine.Suggest("إت").ShouldBeEmpty();

        _user.RecordUse("إتمام", Now);
        Words(engine.Suggest("إت")).ShouldBe(["إتمام"]);
    }

    [Fact]
    public void Threshold_of_one_suggests_after_first_use()
    {
        var engine = Engine(WordDictionary.Empty, new SuggestionOptions { MinUsesToSuggest = 1 });

        _user.RecordUse("إتمام", Now);

        Words(engine.Suggest("إت")).ShouldBe(["إتمام"]);
    }

    [Fact]
    public void Blocked_words_are_never_suggested()
    {
        var engine = Engine(TestDictionaries.Of(("كتب", 300), ("كتاب", 100)));
        _user.RecordUse("كتب", Now);
        _user.RecordUse("كتب", Now);

        _user.SetBlocked("كتب", blocked: true, Now);

        Words(engine.Suggest("كت")).ShouldBe(["كتاب"]);
    }

    [Fact]
    public void Clitic_prefix_is_stripped_and_reattached()
    {
        var engine = Engine(TestDictionaries.Of(("المدرسة", 100), ("مدرسة", 80)));

        var suggestions = engine.Suggest("والمد");

        Words(suggestions).ShouldContain("والمدرسة");
        suggestions.ShouldAllBe(s => s.Kind == MatchKind.Clitic);
    }

    [Theory]
    [InlineData("بالكت", "بالكتاب")]
    [InlineData("فكت", "فكتاب")]
    [InlineData("للكت", "للكتاب")]
    [InlineData("سيكت", "سيكتب")]
    public void Each_clitic_reattaches_its_typed_letters(string typed, string expected)
    {
        var engine = Engine(TestDictionaries.Of(("كتاب", 100), ("يكتب", 50)));

        Words(engine.Suggest(typed)).ShouldContain(expected);
    }

    [Fact]
    public void Clitic_ending_in_lam_never_produces_double_article()
    {
        var engine = Engine(TestDictionaries.Of(("الكتاب", 100), ("كتاب", 10)));

        var words = Words(engine.Suggest("لكت"));

        words.ShouldContain("لكتاب");
        words.ShouldNotContain("لالكتاب");
    }

    [Fact]
    public void Clitic_results_rank_below_direct_matches_and_only_fill_gaps()
    {
        var engine = Engine(TestDictionaries.Of(("وصل", 5), ("صلاة", 1000)), new SuggestionOptions { MaxSuggestions = 2 });

        var suggestions = engine.Suggest("وص");

        suggestions[0].ShouldBe(new Suggestion("وصل", MatchKind.Literal, suggestions[0].Score));
        suggestions.ShouldNotContain(s => s.Kind == MatchKind.Clitic && s.Word == "وصل");
    }

    [Fact]
    public void Clitic_lookup_can_be_disabled() =>
        Engine(TestDictionaries.Of(("مدرسة", 80)), new SuggestionOptions { CliticAware = false })
            .Suggest("والمد").ShouldBeEmpty();
}
