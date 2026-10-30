using Itmam.Core.Suggestions;

namespace Itmam.Core.Tests.Suggestions;

public class CorrectionsTests
{
    private static readonly Lazy<Corrections> Shipped = new(() =>
    {
        using var reader = File.OpenText(Path.Combine(Path.GetDirectoryName(TestDictionaries.ShippedPhrasesPath)!, "corrections.txt"));
        return Corrections.Read(reader);
    });

    [Theory]
    [InlineData("لاكن", new[] { "لكن" })]
    [InlineData("هاذا", new[] { "هذا" })]
    [InlineData("انشاءالله", new[] { "إن شاء الله" })]
    [InlineData("انشاء", new[] { "إن شاء الله", "إنشاء" })]   // also a real word: kept one Tab away
    [InlineData("اللذي", new[] { "الذي" })]
    [InlineData("شيئ", new[] { "شيء" })]
    public void A_known_mistake_typed_exactly_gets_its_corrections(string typed, string[] expected) =>
        Shipped.Value.Find(typed).ShouldBe(expected);

    [Theory]
    [InlineData("لكن")]
    [InlineData("إنشاء")]   // hamza seats are not folded: the right word is not "corrected"
    [InlineData("لاك")]     // exact word only, not a prefix
    [InlineData("")]
    public void Right_words_and_prefixes_are_left_alone(string typed) =>
        Shipped.Value.Find(typed).ShouldBeEmpty();

    [Fact]
    public void Every_line_of_the_shipped_list_is_read_and_never_corrects_a_word_into_itself()
    {
        var path = Path.Combine(Path.GetDirectoryName(TestDictionaries.ShippedPhrasesPath)!, "corrections.txt");
        var entries = File.ReadAllLines(path).Where(l => l.Contains('=') && !l.TrimStart().StartsWith('#')).ToArray();

        Shipped.Value.Count.ShouldBe(entries.Length);
        foreach (var line in entries)
        {
            var wrong = line.Split('=')[0].Trim();
            Shipped.Value.Find(wrong).ShouldNotContain(wrong);
        }
    }
}
