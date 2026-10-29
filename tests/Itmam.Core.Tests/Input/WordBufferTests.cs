using Itmam.Core.Input;
using Itmam.Core.Text;

namespace Itmam.Core.Tests.Input;

public class WordBufferTests
{
    private readonly WordBuffer _buffer = new();

    private BufferResult Type(string text)
    {
        var last = BufferResult.None;
        foreach (var c in text)
        {
            last = _buffer.Apply(InputEvent.Char(c));
        }

        return last;
    }

    private BufferResult Send(InputEventKind kind) => _buffer.Apply(InputEvent.Of(kind));

    [Fact]
    public void Digits_after_a_letter_belong_to_the_word_and_alone_are_a_boundary()
    {
        Type("ع1").Outcome.ShouldBe(BufferOutcome.Updated); // a text shortcut
        _buffer.Text.ShouldBe("ع1");
        _buffer.LetterCount.ShouldBe(1);
        Type(" ").CommittedWord.ShouldBe("ع1");

        Type("1").Outcome.ShouldBe(BufferOutcome.None);
        _buffer.IsEmpty.ShouldBeTrue();
    }

    [Fact]
    public void Arabic_letters_append()
    {
        Type("كتا").Outcome.ShouldBe(BufferOutcome.Updated);
        _buffer.Text.ShouldBe("كتا");
        _buffer.LetterCount.ShouldBe(3);
    }

    [Fact]
    public void Tashkeel_and_tatweel_append_but_do_not_count_as_letters()
    {
        Type("كَتـب");
        _buffer.Text.ShouldBe("كَتـب");
        _buffer.LetterCount.ShouldBe(3);
    }

    [Fact]
    public void Tashkeel_before_any_letter_is_ignored()
    {
        Type("َ").Outcome.ShouldBe(BufferOutcome.None);
        _buffer.IsEmpty.ShouldBeTrue();
    }

    [Theory]
    [InlineData(' ')]
    [InlineData('\r')]
    [InlineData('.')]
    [InlineData('،')]
    [InlineData('؛')]
    [InlineData('؟')]
    public void Boundary_commits_word_without_marks(char boundary)
    {
        Type("كِتاب");
        var result = _buffer.Apply(InputEvent.Char(boundary));

        result.ShouldBe(new BufferResult(BufferOutcome.Committed, "كتاب"));
        _buffer.IsEmpty.ShouldBeTrue();
    }

    [Fact]
    public void Boundary_on_empty_buffer_does_nothing() => Type(" ").ShouldBe(BufferResult.None);

    [Fact]
    public void Backspace_removes_last_character()
    {
        Type("كتب");
        Send(InputEventKind.Backspace).Outcome.ShouldBe(BufferOutcome.Updated);
        _buffer.Text.ShouldBe("كت");
        _buffer.LetterCount.ShouldBe(2);
    }

    [Fact]
    public void Backspace_over_a_mark_keeps_letter_count()
    {
        Type("كَ");
        Send(InputEventKind.Backspace);
        _buffer.Text.ShouldBe("ك");
        _buffer.LetterCount.ShouldBe(1);
    }

    [Fact]
    public void Backspace_to_empty_resets()
    {
        Type("ك");
        Send(InputEventKind.Backspace).Outcome.ShouldBe(BufferOutcome.Reset);
        _buffer.IsEmpty.ShouldBeTrue();
    }

    [Fact]
    public void Backspace_on_empty_buffer_does_nothing() => Send(InputEventKind.Backspace).ShouldBe(BufferResult.None);

    [Fact]
    public void Each_backspace_removes_one_character_and_only_the_last_one_resets()
    {
        Type("مدرسة");

        foreach (var expected in new[] { "مدرس", "مدر", "مد", "م" })
        {
            Send(InputEventKind.Backspace).Outcome.ShouldBe(BufferOutcome.Updated);
            _buffer.Text.ShouldBe(expected);
            _buffer.LetterCount.ShouldBe(expected.Length);
        }

        Send(InputEventKind.Backspace).Outcome.ShouldBe(BufferOutcome.Reset);
        _buffer.IsEmpty.ShouldBeTrue();
    }

    [Fact]
    public void Backspace_then_retyping_continues_the_same_word()
    {
        Type("كتب");
        Send(InputEventKind.Backspace);
        Type("ا").Outcome.ShouldBe(BufferOutcome.Updated);

        _buffer.Text.ShouldBe("كتا");
    }

    [Fact]
    public void Backspace_into_the_previous_word_stops_tracking_until_the_next_boundary()
    {
        Type("كتاب ");
        Send(InputEventKind.Backspace);   // caret is now at the end of كتاب
        _buffer.IsDetached.ShouldBeTrue();

        Type("ة").ShouldBe(BufferResult.None);
        _buffer.IsEmpty.ShouldBeTrue();
        Type(" ").ShouldBe(BufferResult.None); // the half-seen word is never committed

        _buffer.IsDetached.ShouldBeFalse();
        Type("قلم ").ShouldBe(new BufferResult(BufferOutcome.Committed, "قلم"));
    }

    [Theory]
    [InlineData(InputEventKind.MouseClick)]
    [InlineData(InputEventKind.Navigation)]
    [InlineData(InputEventKind.Shortcut)]      // e.g. Ctrl+A
    [InlineData(InputEventKind.Paste)]
    [InlineData(InputEventKind.CtrlBackspace)]
    [InlineData(InputEventKind.FocusChanged)]
    [InlineData(InputEventKind.ForegroundChanged)]
    [InlineData(InputEventKind.LayoutChanged)]
    public void Caret_moves_leave_the_context_unknown_until_it_is_resolved(InputEventKind kind)
    {
        Type("كتاب");
        Send(kind).ShouldBe(new BufferResult(BufferOutcome.Reset));
        _buffer.IsDetached.ShouldBeTrue();

        _buffer.ResumeEmpty().ShouldBe(new BufferResult(BufferOutcome.Reset));
        Type("قلم ").ShouldBe(new BufferResult(BufferOutcome.Committed, "قلم"));
    }

    [Fact]
    public void Click_after_an_existing_word_continues_it_once_the_context_is_read()
    {
        Send(InputEventKind.MouseClick);              // caret placed right after "مد"

        _buffer.ResumeFromContext("مد", maxRead: 49).Outcome.ShouldBe(BufferOutcome.Updated);
        Type("ر");

        _buffer.Text.ShouldBe("مدر");
    }

    [Fact]
    public void Empty_context_is_not_assumed_when_letters_were_typed_while_unknown()
    {
        Send(InputEventKind.MouseClick);
        Type("مد");                                    // typed before the context was known

        _buffer.ResumeEmpty().ShouldBe(BufferResult.None);
        _buffer.IsDetached.ShouldBeTrue();
        Type(" ").ShouldBe(BufferResult.None);        // the half-seen word is never committed
        Type("قلم ").ShouldBe(new BufferResult(BufferOutcome.Committed, "قلم"));
    }

    [Fact]
    public void Accepted_word_is_committed_and_the_next_word_starts_after_the_space()
    {
        Type("مدر");

        _buffer.AcceptWord("مدرسة", appendedSpace: true).ShouldBe(new BufferResult(BufferOutcome.Committed, "مدرسة"));
        _buffer.IsEmpty.ShouldBeTrue();
    }

    [Fact]
    public void Without_a_space_the_accepted_word_stays_the_current_word()
    {
        Type("مدر");
        _buffer.AcceptWord("مدرسة", appendedSpace: false);

        _buffer.Text.ShouldBe("مدرسة");
        Type("ي").Outcome.ShouldBe(BufferOutcome.Updated);
        _buffer.Text.ShouldBe("مدرسةي");
    }

    [Theory]
    [InlineData(InputEventKind.AcceptSuggestion)]
    [InlineData(InputEventKind.DismissSuggestions)]
    [InlineData(InputEventKind.NavigationSelect)]
    public void Bar_events_do_not_change_the_word(InputEventKind kind)
    {
        Type("مدر");

        Send(kind).ShouldBe(BufferResult.None);
        _buffer.Text.ShouldBe("مدر");
        _buffer.IsDetached.ShouldBeFalse();
    }

    [Fact]
    public void Escape_resets_without_making_the_context_unknown()
    {
        Type("كتا");
        Send(InputEventKind.Escape);

        _buffer.IsDetached.ShouldBeFalse();
        Type("ب").Outcome.ShouldBe(BufferOutcome.Updated);
    }

    [Theory]
    [InlineData("")]                 // all text deleted: start of the field
    [InlineData("ذهبت إلى ")]        // a space before the caret
    [InlineData("سطر\r")]            // a line break
    [InlineData("مرحبا، ")]
    public void Context_with_a_boundary_before_the_caret_resumes_with_an_empty_word(string before)
    {
        Type("كتاب ");
        Send(InputEventKind.Backspace);

        _buffer.ResumeFromContext(before, maxRead: 48).ShouldBe(new BufferResult(BufferOutcome.Reset));

        _buffer.IsDetached.ShouldBeFalse();
        Type("قلم ").ShouldBe(new BufferResult(BufferOutcome.Committed, "قلم"));
    }

    [Theory]
    [InlineData("ذهبت إلى المدر", "المدر")]
    [InlineData("المدر", "المدر")]
    [InlineData("مُدَرِّ", "مُدَرِّ")]
    public void Context_ending_in_a_word_makes_it_the_current_word(string before, string expectedWord)
    {
        Type("كتاب ");
        Send(InputEventKind.Backspace);

        _buffer.ResumeFromContext(before, maxRead: 48).Outcome.ShouldBe(BufferOutcome.Updated);

        _buffer.Text.ShouldBe(expectedWord);
        Type("سة ").ShouldBe(new BufferResult(BufferOutcome.Committed, ArabicText.StripIgnorable(expectedWord) + "سة"));
    }

    [Theory]
    [InlineData("helloكت")]                  // glued to a Latin letter
    [InlineData("hello")]                    // caret after a non-Arabic word
    public void Unusable_context_keeps_the_buffer_detached(string before)
    {
        Send(InputEventKind.Backspace);

        _buffer.ResumeFromContext(before, maxRead: 48).ShouldBe(BufferResult.None);
        _buffer.IsDetached.ShouldBeTrue();
    }

    [Fact]
    public void Word_that_may_continue_beyond_the_read_window_keeps_the_buffer_detached()
    {
        Send(InputEventKind.Backspace);

        _buffer.ResumeFromContext("كتابات", maxRead: 6).ShouldBe(BufferResult.None);
        _buffer.IsDetached.ShouldBeTrue();
    }

    [Fact]
    public void Context_is_ignored_when_not_detached()
    {
        Type("كت");

        _buffer.ResumeFromContext("", maxRead: 48).ShouldBe(BufferResult.None);
        _buffer.Text.ShouldBe("كت");
    }

    [Theory]
    [InlineData(InputEventKind.CtrlBackspace)]
    [InlineData(InputEventKind.Navigation)]
    [InlineData(InputEventKind.Escape)]
    [InlineData(InputEventKind.Shortcut)]
    [InlineData(InputEventKind.Paste)]
    [InlineData(InputEventKind.FocusChanged)]
    [InlineData(InputEventKind.ForegroundChanged)]
    [InlineData(InputEventKind.MouseClick)]
    [InlineData(InputEventKind.LayoutChanged)]
    public void Context_changes_reset_without_committing(InputEventKind kind)
    {
        Type("كتاب");
        Send(kind).ShouldBe(new BufferResult(BufferOutcome.Reset));
        _buffer.IsEmpty.ShouldBeTrue();

        // The discarded word is not committed by a later boundary either.
        Type(" ").ShouldBe(BufferResult.None);
    }

    [Fact]
    public void Latin_letter_resets()
    {
        Type("كتa").Outcome.ShouldBe(BufferOutcome.Reset);
        _buffer.IsEmpty.ShouldBeTrue();
    }

    [Fact]
    public void Repeated_keys_behave_like_repeated_presses()
    {
        Type("ممم");
        _buffer.Text.ShouldBe("ممم");
        Send(InputEventKind.Backspace);
        Send(InputEventKind.Backspace);
        _buffer.Text.ShouldBe("م");
    }

    [Fact]
    public void Scripted_sequence_produces_expected_commits()
    {
        var commits = new List<string>();
        void Run(InputEvent e)
        {
            if (_buffer.Apply(e) is { Outcome: BufferOutcome.Committed, CommittedWord: { } word })
            {
                commits.Add(word);
            }
        }

        foreach (var c in "ذهبت ")
        {
            Run(InputEvent.Char(c));
        }

        foreach (var c in "إلى")
        {
            Run(InputEvent.Char(c));
        }

        Run(InputEvent.Of(InputEventKind.Backspace));
        Run(InputEvent.Char('ى'));
        Run(InputEvent.Char(' '));
        foreach (var c in "المد")
        {
            Run(InputEvent.Char(c));
        }

        Run(InputEvent.Of(InputEventKind.FocusChanged));
        _buffer.ResumeFromContext("", maxRead: 49);   // the newly focused field is empty
        foreach (var c in "البيت؟")
        {
            Run(InputEvent.Char(c));
        }

        commits.ShouldBe(["ذهبت", "إلى", "البيت"]);
    }

    [Fact]
    public void Overlong_run_is_not_tracked_until_next_boundary()
    {
        Type(new string('ب', WordBuffer.MaxLength) + "ت");
        _buffer.IsEmpty.ShouldBeTrue();
        Type("ث").ShouldBe(BufferResult.None);

        Type(" ");
        Type("كت");
        _buffer.Text.ShouldBe("كت");
    }
}
