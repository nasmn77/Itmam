using Itmam.Core.Input;
using Itmam.Core.Suggestions;
using Itmam.Core.Text;

namespace Itmam.Core.Tests.Suggestions;

public class PhraseIndexTests
{
    private static readonly PhraseIndex Index = PhraseIndex.Read(new StringReader("""
        # comment
        بسم الله الرحمن الرحيم
        جزاك الله خيرا
        السلام عليكم ورحمة الله وبركاته
        الله أكبر
        الله يحفظك
        على سبيل المثال
        في أمان الله
        كلمة
        """));

    private static string[] Context(params string[] words) => [.. words.Select(w => ArabicNormalizer.Normalize(w))];

    [Fact]
    public void Comments_and_single_words_are_skipped() =>
        Index.Count.ShouldBe(7);

    [Fact]
    public void Typing_a_distinctive_first_word_offers_the_whole_phrase_once() =>
        Index.Suggest([], "بسم", 3).ShouldBe(["بسم الله الرحمن الرحيم"]);

    [Fact]
    public void Missing_hamza_and_partial_words_still_match() =>
        Index.Suggest([], "جزا", 3).ShouldBe(["جزاك الله خيرا"]);

    [Fact]
    public void Too_few_letters_offer_nothing() =>
        Index.Suggest([], "جز", 3).ShouldBeEmpty();

    [Fact]
    public void Right_after_a_space_the_rest_and_the_next_word_are_offered() =>
        Index.Suggest(Context("بسم"), "", 3).ShouldBe(["الله الرحمن الرحيم", "الله"]);

    [Fact]
    public void Word_by_word_continues_through_the_phrase()
    {
        Index.Suggest(Context("بسم", "الله"), "", 3).ShouldBe(["الرحمن الرحيم", "الرحمن"]);
        Index.Suggest(Context("بسم", "الله", "الرحمن"), "", 3).ShouldBe(["الرحيم"]);
        Index.Suggest(Context("بسم", "الله", "الرحمن", "الرحيم"), "", 3).ShouldBeEmpty();
    }

    [Fact]
    public void Typed_letters_filter_the_next_word() =>
        Index.Suggest(Context("السلام"), "ع", 3).ShouldBe(["عليكم ورحمة الله وبركاته", "عليكم"]);

    [Fact]
    public void A_word_that_does_not_continue_the_phrase_offers_nothing() =>
        Index.Suggest(Context("بسم"), "ك", 3).ShouldBeEmpty();

    [Fact]
    public void Several_phrases_with_the_same_start_offer_one_whole_phrase_so_dictionary_words_keep_their_place() =>
        Index.Suggest([], "الله", 3).ShouldBe(["الله أكبر"]);

    [Fact]
    public void Inside_a_phrase_up_to_two_endings_then_next_words() =>
        Index.Suggest(Context("الله"), "", 4).ShouldBe(["أكبر", "يحفظك"]); // both endings are one word: no duplicates

    [Fact]
    public void Common_short_words_never_start_a_phrase_or_open_the_bar_alone()
    {
        Index.Suggest([], "على", 3).ShouldBeEmpty();     // Tab after «على» still inserts «على»
        Index.Suggest(Context("على"), "", 3).ShouldBeEmpty(); // no bar after every «على »
        Index.Suggest(Context("في"), "", 3).ShouldBeEmpty();
    }

    [Fact]
    public void After_a_common_word_the_first_letter_of_the_next_word_brings_the_phrase() =>
        Index.Suggest(Context("على"), "س", 3).ShouldBe(["سبيل المثال", "سبيل"]);

    [Fact]
    public void User_phrases_come_first_and_duplicates_are_merged()
    {
        var index = Index.With(["بسم الله الرحمن الرحيم", "بسم الكريم أبدأ"]);

        index.Count.ShouldBe(8);
        index.Phrases[0].Text.ShouldBe("بسم الله الرحمن الرحيم");
        index.Phrases[1].Text.ShouldBe("بسم الكريم أبدأ");
    }

    [Theory]
    [InlineData("  جزاك   اللهُ خيرًا ", "جزاك الله خيرا")]
    [InlineData("كلمة", null)]
    [InlineData("hello world", null)]
    [InlineData("مرحبا 123", null)]
    public void User_phrases_are_cleaned_or_refused(string input, string? expected) =>
        PhraseIndex.Clean(input).ShouldBe(expected);

    [Fact]
    public void The_shipped_list_loads_and_contains_the_common_phrases()
    {
        using var reader = File.OpenText(TestDictionaries.ShippedPhrasesPath);
        var shipped = PhraseIndex.Read(reader);

        shipped.Count.ShouldBeGreaterThan(200);
        string[] expected = ["بسم الله الرحمن الرحيم", "السلام عليكم ورحمة الله وبركاته", "جزاك الله خيرا", "إن شاء الله", "كل عام وأنتم بخير"];
        foreach (var phrase in expected)
        {
            shipped.Phrases.ShouldContain(p => p.Text == phrase);
        }
    }
}

public class RecentWordsTests
{
    [Fact]
    public void Keeps_normalized_words_and_the_last_eight_only()
    {
        var recent = new RecentWords();
        recent.Push("أهلا");
        recent.Snapshot.ShouldBe(["اهلا"]);

        for (var i = 0; i < 10; i++)
        {
            recent.Push("كلمة");
        }

        recent.Snapshot.Count.ShouldBe(RecentWords.Max);
    }

    [Fact]
    public void An_accepted_phrase_adds_each_word_and_anything_else_clears()
    {
        var recent = new RecentWords();
        recent.Push("بسم الله");
        recent.Snapshot.Count.ShouldBe(2);

        recent.Push("hello");
        recent.Snapshot.ShouldBeEmpty();
    }

    [Fact]
    public void Sequences_ending_with_a_word_keep_the_spelling_and_stop_at_five_words()
    {
        var recent = new RecentWords();
        recent.Push("السلام عليكم ورحمة الله");

        recent.SequencesEndingWith("وبركاتُه").ShouldBe([
            "الله وبركاته",
            "ورحمة الله وبركاته",
            "عليكم ورحمة الله وبركاته",
            "السلام عليكم ورحمة الله وبركاته",
        ]);

        recent.Push("وبركاته");
        recent.SequencesEndingWith("اخي").Last().ShouldBe("عليكم ورحمة الله وبركاته اخي");
        recent.SequencesEndingWith("hello").ShouldBeEmpty();
    }
}
