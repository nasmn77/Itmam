using System.Globalization;

namespace Itmam.Core.Languages;

/// <summary>
/// Any alphabetic language without a shipped dictionary (Hebrew, French, Russian …): only the user's
/// learned words and phrases are suggested. Letters are what Unicode calls letters, non-spacing marks
/// (niqqud, combining accents) are ignored for matching, and case is folded. The zero-width non-joiner
/// is part of Persian, Urdu and Kurdish words, so it counts as a letter.
/// </summary>
public sealed class LearningOnlyLanguage(string id, bool rightToLeft) : LanguageProfile
{
    private const char ZeroWidthNonJoiner = '‌';

    public override string Id { get; } = id;

    public override bool RightToLeft { get; } = rightToLeft;

    public override bool IsLetter(char c) => char.IsLetter(c) || c == ZeroWidthNonJoiner;

    public override bool IsIgnorable(char c) => CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark;

    public override char Fold(char c) => char.ToLowerInvariant(c);
}
