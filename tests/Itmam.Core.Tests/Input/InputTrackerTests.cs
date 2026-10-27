using Itmam.Core.Input;

namespace Itmam.Core.Tests.Input;

public class InputTrackerTests
{
    private const nint ArabicSaudi = 0x04010401;
    private const nint ArabicEgypt = 0x0C010C01;
    private const nint EnglishUs = 0x04090409;

    private readonly InputTracker _tracker = new();

    private BufferResult Type(string text, nint layout)
    {
        var last = BufferResult.None;
        foreach (var c in text)
        {
            last = _tracker.Process(InputEvent.Char(c), layout);
        }

        return last;
    }

    [Theory]
    [InlineData(0x04010401L, true)]
    [InlineData(0x0C010C01L, true)]
    [InlineData(0x14010401L, true)]
    [InlineData(0x04090409L, false)]
    [InlineData(0x040C040CL, false)]
    [InlineData(0L, false)]
    public void Arabic_layouts_are_detected_by_primary_language(long hkl, bool arabic) =>
        KeyboardLayoutId.IsArabic((nint)hkl).ShouldBe(arabic);

    [Fact]
    public void Arabic_layout_tracks_the_word()
    {
        Type("كتا", ArabicSaudi);

        _tracker.IsArabicLayout.ShouldBeTrue();
        _tracker.Buffer.Text.ShouldBe("كتا");
    }

    [Fact]
    public void English_layout_is_never_tracked()
    {
        Type("hello", EnglishUs).ShouldBe(BufferResult.None);
        Type(" ", EnglishUs).ShouldBe(BufferResult.None);

        _tracker.IsArabicLayout.ShouldBeFalse();
        _tracker.Buffer.IsEmpty.ShouldBeTrue();
    }

    [Fact]
    public void Switching_layout_mid_word_resets_without_committing()
    {
        Type("كتا", ArabicSaudi);

        _tracker.Process(InputEvent.Char('b'), EnglishUs).ShouldBe(new BufferResult(BufferOutcome.Reset));
        _tracker.Buffer.IsEmpty.ShouldBeTrue();
    }

    [Fact]
    public void Switching_between_two_arabic_layouts_also_resets()
    {
        Type("كتا", ArabicSaudi);

        _tracker.Process(InputEvent.Char('ب'), ArabicEgypt).ShouldBe(new BufferResult(BufferOutcome.Reset));
        _tracker.Buffer.IsEmpty.ShouldBeTrue();
        _tracker.Buffer.IsDetached.ShouldBeTrue(); // the caret may be mid-word: the context is read next
    }

    [Fact]
    public void Returning_to_arabic_waits_for_the_context_then_tracks()
    {
        Type("كتا", ArabicSaudi);
        Type("ab", EnglishUs);
        _tracker.Process(InputEvent.Of(InputEventKind.MouseClick), ArabicSaudi);
        _tracker.Buffer.ResumeEmpty();

        Type("قلم ", ArabicSaudi).ShouldBe(new BufferResult(BufferOutcome.Committed, "قلم"));
    }
}
