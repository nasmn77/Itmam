using Itmam.Core.Input;

namespace Itmam.Core.Tests.Input;

public class TextEditTests
{
    [Fact]
    public void Suggestion_extending_typed_text_sends_only_the_suffix() =>
        TextEdit.Compute("مدر", "مدرسة").ShouldBe(new TextEdit(0, "سة"));

    [Fact]
    public void Different_first_letter_replaces_the_whole_prefix() =>
        TextEdit.Compute("احم", "أحمد").ShouldBe(new TextEdit(3, "أحمد"));

    [Fact]
    public void Only_the_differing_tail_is_replaced() =>
        TextEdit.Compute("مدرسه", "مدرسة").ShouldBe(new TextEdit(1, "ة"));

    [Fact]
    public void Typed_tashkeel_is_deleted_one_unit_per_backspace() =>
        TextEdit.Compute("مدَر", "مدرسة").ShouldBe(new TextEdit(2, "رسة"));

    [Fact]
    public void Trailing_space_is_appended_when_requested() =>
        TextEdit.Compute("مدر", "مدرسة", appendSpace: true).ShouldBe(new TextEdit(0, "سة "));
}
