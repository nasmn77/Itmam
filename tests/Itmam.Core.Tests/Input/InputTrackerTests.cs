using Itmam.Core.Input;
using Itmam.Core.Languages;

namespace Itmam.Core.Tests.Input;

public class InputTrackerTests
{
    private const nint ArabicSaudi = 0x04010401;
    private const nint ArabicEgypt = 0x0C010C01;
    private const nint EnglishUs = 0x04090409;
    private const nint Hebrew = 0x040D040D;
    private const nint Japanese = 0x04110411;

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
    public void Arabic_layouts_are_detected_by_primary_language(long hkl, bool arabic) =>
        (LanguageRegistry.ForLayout((nint)hkl) == LanguageProfile.Arabic).ShouldBe(arabic);

    [Fact]
    public void An_unknown_layout_keeps_the_current_one()
    {
        Type("كتا", ArabicSaudi);

        _tracker.Process(InputEvent.Of(InputEventKind.ToggleEnabled), 0);

        _tracker.Language.ShouldBe(LanguageProfile.Arabic);
        _tracker.Buffer.IsDetached.ShouldBeTrue(); // the caret may have moved while Itmam was off
        Type("ب", ArabicSaudi);
        _tracker.Buffer.IsDetached.ShouldBeTrue(); // no layout switch: still waiting for the context read
    }

    [Fact]
    public void Arabic_layout_tracks_the_word()
    {
        Type("كتا", ArabicSaudi);

        _tracker.Language.ShouldBe(LanguageProfile.Arabic);
        _tracker.Buffer.Text.ShouldBe("كتا");
    }

    [Fact]
    public void A_language_that_is_not_turned_on_is_never_tracked()
    {
        Type("hello", EnglishUs).ShouldBe(BufferResult.None);
        Type(" ", EnglishUs).ShouldBe(BufferResult.None);

        _tracker.Language.ShouldBeNull();
        _tracker.Buffer.IsEmpty.ShouldBeTrue();
    }

    [Fact]
    public void A_language_turned_on_is_tracked_with_its_letters()
    {
        _tracker.EnabledLanguages = new HashSet<string> { "ar", "en", "he" };

        Type("hello", EnglishUs);
        _tracker.Language.ShouldNotBeNull().Id.ShouldBe("en");
        _tracker.Buffer.Text.ShouldBe("hello");
        Type(" ", EnglishUs).CommittedWord.ShouldBe("hello");

        _tracker.Process(InputEvent.Of(InputEventKind.MouseClick), Hebrew);
        _tracker.Buffer.ResumeEmpty();
        Type("שלום", Hebrew);
        _tracker.Language.ShouldNotBeNull().RightToLeft.ShouldBeTrue();
        _tracker.Buffer.Text.ShouldBe("שלום");
    }

    [Fact]
    public void Input_method_languages_are_silent_even_when_turned_on()
    {
        _tracker.EnabledLanguages = new HashSet<string> { "ar", "ja" };

        Type("nihon", Japanese).ShouldBe(BufferResult.None);

        _tracker.Language.ShouldBeNull();
        _tracker.Buffer.IsEmpty.ShouldBeTrue();
    }

    [Fact]
    public void Turning_a_language_off_stops_tracking_at_once()
    {
        _tracker.EnabledLanguages = new HashSet<string> { "ar", "en" };
        Type("hel", EnglishUs);

        _tracker.EnabledLanguages = new HashSet<string> { "ar" };

        _tracker.Process(InputEvent.Char('l'), EnglishUs).ShouldBe(new BufferResult(BufferOutcome.Reset));
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
