using Itmam.Core.Dictionary;
using Itmam.Core.Input;
using Itmam.Core.Languages;
using Itmam.Core.Learning;
using Itmam.Core.Suggestions;

namespace Itmam.Core.Tests.Languages;

public class LanguageProfileTests
{
    private static readonly LanguageProfile Hebrew = new LearningOnlyLanguage("he", rightToLeft: true);
    private static readonly LanguageProfile French = new LearningOnlyLanguage("fr", rightToLeft: false);

    [Fact]
    public void Arabic_keeps_its_rules()
    {
        var arabic = LanguageProfile.Arabic;

        arabic.Id.ShouldBe("ar");
        arabic.RightToLeft.ShouldBeTrue();
        arabic.Normalize("أُمّة").ShouldBe("امه");
        arabic.IsWord("كتاب").ShouldBeTrue();
        arabic.IsWord("book").ShouldBeFalse();
        arabic.Clitics.ShouldContain("ال");
        arabic.ExcludedStemStart("لل").ShouldBe("ال");
        arabic.ExcludedStemStart("ب").ShouldBeNull();
        arabic.FallbackVariants("سءال").ShouldContain("سؤال");
    }

    [Fact]
    public void A_learning_only_language_folds_case_and_ignores_marks()
    {
        French.IsWord("École").ShouldBeTrue();
        French.Normalize("École").ShouldBe("école");
        French.Normalize("école").ShouldBe("ecole"); // a combining accent is ignored for matching
        French.CleanWord("école").ShouldBe("ecole");
        French.IsWord("l'école").ShouldBeFalse();

        Hebrew.RightToLeft.ShouldBeTrue();
        Hebrew.Normalize("שָׁלוֹם").ShouldBe("שלום"); // niqqud
        Hebrew.IsWord("שלום").ShouldBeTrue();
        Hebrew.Clitics.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("École")]
    [InlineData("STRASSE")]
    [InlineData("Привет")]
    [InlineData("שלום")]
    public void Folding_keeps_one_character_for_one(string word) =>
        French.Normalize(word).Length.ShouldBe(word.Length);

    [Fact]
    public void The_word_buffer_tracks_the_letters_of_its_language()
    {
        var buffer = new WordBuffer { Language = Hebrew };
        foreach (var c in "שלום עולם")
        {
            buffer.Apply(InputEvent.Char(c));
        }

        buffer.Text.ShouldBe("עולם");
        buffer.LetterCount.ShouldBe(4);
        buffer.Apply(InputEvent.Char('.')).CommittedWord.ShouldBe("עולם");
    }

    [Fact]
    public void Learned_words_of_a_learning_only_language_are_suggested()
    {
        var user = new UserWordIndex(French);
        var now = DateTimeOffset.UtcNow;
        user.Set("Bonjour", 3, now);
        user.Set("bonsoir", 2, now);
        var engine = new SuggestionEngine(WordDictionary.Build([], language: French), user);

        engine.Suggest("bon").Select(s => s.Word).ShouldBe(["bonsoir", "Bonjour"]); // the literal match wins
        engine.Suggest("Bon").Select(s => s.Word).ShouldBe(["Bonjour", "bonsoir"]);
        engine.Suggest("BON").Select(s => s.Word).ShouldBe(["Bonjour", "bonsoir"]);
    }

    [Fact]
    public void Phrases_and_corrections_use_their_language()
    {
        var phrases = PhraseIndex.Read(new StringReader("merci beaucoup"), French);
        phrases.Suggest(["merci"], "", 3).ShouldBe(["beaucoup"]);
        PhraseIndex.Clean("merci  beaucoup", French).ShouldBe("merci beaucoup");
        PhraseIndex.Clean("merci beaucoup").ShouldBeNull(); // Arabic by default

        var corrections = Corrections.Read(new StringReader("pluis = plus"), French);
        corrections.Find("pluis").ShouldBe(["plus"]);
    }

    /// <summary>Text paths ask a <see cref="LanguageProfile"/>; only these files may use the Arabic helpers directly.</summary>
    [Fact]
    public void Only_the_language_layer_uses_the_arabic_helpers_directly()
    {
        var core = Path.Combine(TestDictionaries.RepositoryRoot, "src", "Itmam.Core");
        string[] allowed =
        [
            Path.Combine("Languages", "ArabicLanguage.cs"),
            Path.Combine("Languages", "LanguageProfile.cs"), // the shared word boundary rule
            Path.Combine("Text", "ArabicNormalizer.cs"),
            Path.Combine("Text", "ArabicText.cs"),
            Path.Combine("Suggestions", "LongPhrases.cs"), // searching names in any language
            Path.Combine("Suggestions", "LongPhraseVariables.cs"), // Arabic text gets Arabic dates
        ];

        var offenders = Directory.EnumerateFiles(core, "*.cs", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(core, path))
            .Where(path => !path.StartsWith("obj", StringComparison.Ordinal) && !path.StartsWith("bin", StringComparison.Ordinal))
            .Where(path => !allowed.Contains(path))
            .Where(path => File.ReadAllText(Path.Combine(core, path)) is var text
                && (text.Contains("ArabicText.", StringComparison.Ordinal) || text.Contains("ArabicNormalizer.", StringComparison.Ordinal)))
            .ToArray();

        offenders.ShouldBeEmpty();
    }
}
