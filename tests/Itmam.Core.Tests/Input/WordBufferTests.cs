using Itmam.Core.Input;

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
    [InlineData('3')]
    [InlineData('٣')]
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
