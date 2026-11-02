using Itmam.Core.Dictionary.Building;
using Itmam.Core.Input;
using Itmam.Core.Languages;
using Itmam.Core.Suggestions;

namespace Itmam.Core.Tests.Languages;

public class EnglishLanguageTests
{
    private static readonly LanguageProfile English = LanguageProfile.English;

    [Theory]
    [InlineData("Café", "cafe")]
    [InlineData("DON’T", "don't")]
    [InlineData("Ångström", "angstrom")]
    [InlineData("naïve", "naive")]
    public void Keys_ignore_case_accents_and_the_kind_of_apostrophe(string word, string key) =>
        English.Normalize(word).ShouldBe(key);

    [Theory]
    [InlineData("don't", true)]
    [InlineData("rock'n'roll", true)]
    [InlineData("'cause", false)]
    [InlineData("students'", false)]
    [InlineData("كتاب", false)]
    [InlineData("hello2", false)]
    public void An_apostrophe_belongs_between_letters(string text, bool word) =>
        English.IsWord(text).ShouldBe(word);

    [Fact]
    public void The_buffer_keeps_the_apostrophe_inside_a_word()
    {
        var buffer = new WordBuffer { Language = English };
        BufferResult last = default;
        foreach (var c in "don't 'cause")
        {
            last = buffer.Apply(InputEvent.Char(c));
            if (c == ' ')
            {
                last.CommittedWord.ShouldBe("don't");
            }
        }

        buffer.Text.ShouldBe("cause"); // a leading apostrophe is a quote, not part of the word
        buffer.LetterCount.ShouldBe(5);
    }

    [Theory]
    [InlineData("london", "lon", "london")]
    [InlineData("london", "Lon", "London")]
    [InlineData("london", "LON", "LONDON")]
    [InlineData("I'm", "i'", "I'm")]
    [InlineData("iPhone", "Ip", "iPhone")] // already has its own case
    [InlineData("hello", "H", "Hello")]
    public void Suggestions_follow_the_case_being_typed(string suggestion, string typed, string expected) =>
        English.MatchCase(suggestion, typed).ShouldBe(expected);

    [Fact]
    public void Contractions_split_by_the_source_are_put_back_together()
    {
        var rebuilt = EnglishContractions.Rebuild(
        [
            ("you", 1000), ("i", 900), ("don", 300), ("'t", 500), ("isn", 100), ("'m", 400), ("we", 200),
            ("'re", 300), ("can", 800), ("won", 50), ("'s", 20), ("it", 600),
        ]).ToDictionary(p => p.Word, p => p.Count);

        rebuilt["don't"].ShouldBe(300);
        rebuilt["isn't"].ShouldBe(100);
        rebuilt["can't"].ShouldBe(50);  // what is left of n't, shared with won't
        rebuilt["won't"].ShouldBe(50);
        rebuilt["I'm"].ShouldBe(400);
        rebuilt["I"].ShouldBe(900);
        rebuilt["you're"].ShouldBe(250); // 're shared by you (1000) and we (200)
        rebuilt["we're"].ShouldBe(50);
        rebuilt.ShouldContainKey("it's");
        rebuilt.Keys.ShouldNotContain("don");
        rebuilt.Keys.ShouldNotContain("'t");
        rebuilt["can"].ShouldBe(800);
    }

    [Fact]
    public void The_shipped_phrases_complete_in_the_case_being_typed()
    {
        PhraseIndex phrases;
        using (var reader = File.OpenText(Path.Combine(Path.GetDirectoryName(TestDictionaries.EnglishCorrectionsPath)!, "phrases.en.txt")))
        {
            phrases = PhraseIndex.Read(reader, English);
        }

        phrases.Count.ShouldBeGreaterThan(150);
        phrases.Suggest([], "Tha", 3)[0].ShouldStartWith("Thank you");
        phrases.Suggest([], "looki", 3)[0].ShouldStartWith("looking forward to"); // 60% of the first word, as in Arabic
        phrases.Suggest(["thank", "you"], "", 3).ShouldContain("very much");
        phrases.Suggest([], "the", 3).ShouldBeEmpty(); // a very common word alone never starts a phrase
        phrases.Suggest(["i"], "loo", 3)[0].ShouldBe("look forward to hearing from you");
    }

    [Fact]
    public void The_shipped_common_mistakes_are_read_as_english()
    {
        Corrections corrections;
        using (var reader = File.OpenText(TestDictionaries.EnglishCorrectionsPath))
        {
            corrections = Corrections.Read(reader, English);
        }

        corrections.Count.ShouldBeGreaterThan(50);
        corrections.Find("teh").ShouldBe(["the"]);
        corrections.Find("dont").ShouldBe(["don't"]);
        corrections.Find("cant").ShouldBe(["can't", "cant"]);
    }
}
