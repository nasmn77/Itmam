using Itmam.Core.Text;

namespace Itmam.Core.Tests.Text;

public class ArabicTextTests
{
    [Theory]
    [InlineData('ء')]
    [InlineData('ا')]
    [InlineData('ي')]
    [InlineData('ٱ')]
    [InlineData('پ')]
    [InlineData('ک')]
    [InlineData('ی')]
    public void Arabic_letters_are_letters(char c) => ArabicText.IsArabicLetter(c).ShouldBeTrue();

    [Theory]
    [InlineData('ـ')]
    [InlineData('َ')]
    [InlineData('a')]
    [InlineData('،')]
    [InlineData('٣')]
    public void Non_letters_are_not_letters(char c) => ArabicText.IsArabicLetter(c).ShouldBeFalse();

    [Theory]
    [InlineData(' ')]
    [InlineData('\r')]
    [InlineData('\t')]
    [InlineData('.')]
    [InlineData(',')]
    [InlineData('!')]
    [InlineData('،')]
    [InlineData('؛')]
    [InlineData('؟')]
    [InlineData('٪')]
    [InlineData('5')]
    [InlineData('٥')]
    [InlineData('۵')]
    [InlineData('"')]
    [InlineData('«')]
    public void Whitespace_punctuation_and_digits_are_boundaries(char c) => ArabicText.IsWordBoundary(c).ShouldBeTrue();

    [Theory]
    [InlineData('ب')]
    [InlineData('ً')]
    [InlineData('ـ')]
    [InlineData('x')]
    public void Letters_and_marks_are_not_boundaries(char c) => ArabicText.IsWordBoundary(c).ShouldBeFalse();

    [Fact]
    public void Strip_keeps_letters_and_spelling() => ArabicText.StripIgnorable("أَحْمَـد").ShouldBe("أحمد");
}
