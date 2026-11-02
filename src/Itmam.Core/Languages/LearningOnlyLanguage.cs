using System.Globalization;

namespace Itmam.Core.Languages;

/// <summary>
/// Any alphabetic language without a shipped dictionary (Hebrew, French, Russian …): only the user's
/// learned words and phrases are suggested. Letters are what Unicode calls letters, non-spacing marks
/// (niqqud, combining accents) are ignored for matching, and case is folded.
/// </summary>
public sealed class LearningOnlyLanguage(string id, bool rightToLeft) : LanguageProfile
{
    public override string Id { get; } = id;

    public override bool RightToLeft { get; } = rightToLeft;

    public override bool IsLetter(char c) => char.IsLetter(c);

    public override bool IsIgnorable(char c) => CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark;

    public override char Fold(char c) => char.ToLowerInvariant(c);
}
