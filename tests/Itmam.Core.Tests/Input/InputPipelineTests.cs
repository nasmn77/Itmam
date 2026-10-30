using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Channels;
using Itmam.Core.Input;

namespace Itmam.Core.Tests.Input;

public sealed class InputPipelineTests : IAsyncDisposable
{
    private const nint Arabic = 0x04010401;
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private readonly Channel<InputRecord> _channel = Channel.CreateUnbounded<InputRecord>();
    private readonly FakeCaretContext _context = new();
    private readonly ConcurrentQueue<PipelineUpdate> _updates = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _run;
    private readonly FakeInjector _injector = new();
    private readonly FakeGuard _guard = new();

    public InputPipelineTests()
    {
        var pipeline = new InputPipeline(
            _channel.Reader,
            _context,
            new InputPipelineOptions { ProbeDelay = TimeSpan.FromMilliseconds(10) },
            _injector,
            _guard);
        pipeline.Updated += _updates.Enqueue;
        _run = Task.Run(() => pipeline.RunAsync(_stop.Token));
    }

    public async ValueTask DisposeAsync()
    {
        _channel.Writer.TryComplete();
        await _run.WaitAsync(Timeout);
        _stop.Dispose();
    }

    private void Send(InputEvent input) => _channel.Writer.TryWrite(new InputRecord(input, Arabic, Stopwatch.GetTimestamp()));

    private void Type(string text)
    {
        foreach (var c in text)
        {
            Send(InputEvent.Char(c));
        }
    }

    private void Backspace(int times = 1)
    {
        for (var i = 0; i < times; i++)
        {
            Send(InputEvent.Of(InputEventKind.Backspace));
        }
    }

    private async Task<PipelineUpdate> WaitForAsync(Func<PipelineUpdate, bool> predicate)
    {
        var deadline = Stopwatch.StartNew();
        while (deadline.Elapsed < Timeout)
        {
            var match = _updates.FirstOrDefault(predicate);
            if (match != default)
            {
                return match;
            }

            await Task.Delay(5);
        }

        throw new TimeoutException("Expected pipeline update did not arrive.");
    }

    private Task<PipelineUpdate> WaitForProbeAsync() => WaitForAsync(u => u.Probe is not null);

    private string[] Commits() =>
        _updates.Where(u => u.Result.Outcome == BufferOutcome.Committed).Select(u => u.Result.CommittedWord!).ToArray();

    private async Task DrainAsync()
    {
        var marker = InputEvent.Of(InputEventKind.Escape);
        var count = _updates.Count;
        Send(marker);
        await WaitForAsync(u => u.Input?.Input == marker && _updates.Count > count);
    }

    [Fact]
    public async Task Deleting_all_text_with_backspace_then_typing_a_word_commits_it()
    {
        Type("قديم ");
        Backspace(5);
        _context.Next = "";   // the field is now empty

        (await WaitForProbeAsync()).Probe.ShouldBe(ProbeOutcome.ResumedEmpty);
        Type("كتاب ");
        await DrainAsync();

        Commits().ShouldBe(["قديم", "كتاب"]);
    }

    [Fact]
    public async Task Turning_itmam_back_on_inside_a_word_resumes_that_word()
    {
        Type("ذهبت ");
        await DrainAsync();

        // Off and on again with the caret inside "مدر": the hooks raise the toggle with an unknown
        // layout (0), which must not be mistaken for a switch away from Arabic.
        _context.Next = "ذهبت إلى مدر";
        _channel.Writer.TryWrite(new InputRecord(InputEvent.Of(InputEventKind.ToggleEnabled), 0, Stopwatch.GetTimestamp()));

        var probe = await WaitForProbeAsync();
        probe.Probe.ShouldBe(ProbeOutcome.ResumedWithWord);
        probe.Word.ShouldBe("مدر");

        Type("سة");
        (await WaitForAsync(u => u.Word == "مدرسة")).IsDetached.ShouldBeFalse();
    }

    [Fact]
    public async Task Backspace_to_a_space_resumes_with_an_empty_word()
    {
        Type("ذهبت ");
        Backspace();
        _context.Next = "ذهبت إلى ";

        (await WaitForProbeAsync()).Probe.ShouldBe(ProbeOutcome.ResumedEmpty);
        Type("قلم ");
        await DrainAsync();

        Commits().ShouldBe(["ذهبت", "قلم"]);
    }

    [Fact]
    public async Task Backspace_into_a_word_makes_it_the_current_word()
    {
        Type("مدرسة ");
        Backspace(3);
        _context.Next = "ذهبت إلى مدر";

        var probe = await WaitForProbeAsync();
        probe.Probe.ShouldBe(ProbeOutcome.ResumedWithWord);
        probe.Word.ShouldBe("مدر");

        Type("ب ");
        await DrainAsync();
        Commits().ShouldBe(["مدرسة", "مدرب"]);
    }

    [Fact]
    public async Task Without_ui_automation_a_click_resumes_with_an_empty_word()
    {
        Type("قديم ");
        _context.Next = null; // unsupported app
        Backspace();

        await WaitForAsync(_ => _updates.Count(u => u.Probe == ProbeOutcome.Unavailable) == 2);
        Type("تكملة");                            // continues an unseen word: not tracked
        Send(InputEvent.Of(InputEventKind.MouseClick));

        (await WaitForAsync(u => u.Probe == ProbeOutcome.AssumedEmpty)).IsDetached.ShouldBeFalse();
        Type("كتاب ");
        await DrainAsync();

        Commits().ShouldBe(["قديم", "كتاب"]);
    }

    [Fact]
    public async Task Clicking_after_an_existing_word_continues_it()
    {
        _context.Next = "نص سابق مد";             // the user clicked right after "مد"
        Send(InputEvent.Of(InputEventKind.MouseClick));

        var probe = await WaitForAsync(u => u.Probe == ProbeOutcome.ResumedWithWord);
        probe.Word.ShouldBe("مد");

        Type("رسة ");
        await DrainAsync();
        Commits().ShouldBe(["مدرسة"]);
    }

    [Theory]
    [InlineData(InputEventKind.Navigation)]
    [InlineData(InputEventKind.FocusChanged)]
    [InlineData(InputEventKind.ForegroundChanged)]
    public async Task Any_caret_move_reads_the_context(InputEventKind kind)
    {
        _context.Next = "ذهبت إلى المدر";
        Send(InputEvent.Of(kind));

        (await WaitForAsync(u => u.Probe == ProbeOutcome.ResumedWithWord)).Word.ShouldBe("المدر");
    }

    [Fact]
    public async Task Without_ui_automation_letters_typed_before_resuming_are_never_tracked()
    {
        _context.Next = null;
        _context.Delay = TimeSpan.FromMilliseconds(30);
        Send(InputEvent.Of(InputEventKind.MouseClick));
        await WaitForAsync(_ => _context.Calls == 1);
        Type("مد");                               // typed while the context is still unknown

        await WaitForAsync(_ => _updates.Count(u => u.Probe is ProbeOutcome.Unavailable) >= 2);
        await DrainAsync();

        _updates.ShouldNotContain(u => u.Probe == ProbeOutcome.AssumedEmpty);
        Type(" ");
        Type("قلم ");
        await DrainAsync();
        Commits().ShouldBe(["قلم"]);
    }

    [Fact]
    public async Task Holding_backspace_probes_once()
    {
        Type("كلمة ");
        _context.Next = "";
        Backspace(20);

        await WaitForProbeAsync();
        await Task.Delay(50, TestContext.Current.CancellationToken);

        _context.Calls.ShouldBe(1);
    }

    [Fact]
    public async Task Keys_typed_while_reading_discard_the_result()
    {
        Type("كلمة ");
        _context.Next = "";
        _context.Delay = TimeSpan.FromMilliseconds(100);
        Backspace();

        await WaitForAsync(_ => _context.Calls == 1);
        Type("ب");                                // arrives during the read

        (await WaitForProbeAsync()).Probe.ShouldBe(ProbeOutcome.Discarded);
        await DrainAsync();
        _updates.Last(u => u.Input is not null).IsDetached.ShouldBeFalse(); // Escape reset it
    }

    [Fact]
    public async Task Unavailable_context_is_retried_once_like_chromium_waking_up()
    {
        Type("كلمة ");
        _context.Responses = call => call == 1 ? null : "ذهبت إلى ";
        Backspace();

        var resumed = await WaitForAsync(u => u.Probe == ProbeOutcome.ResumedEmpty);

        resumed.IsDetached.ShouldBeFalse();
        _context.Calls.ShouldBe(2);
        _updates.Count(u => u.Probe == ProbeOutcome.Unavailable).ShouldBe(1);
    }

    [Fact]
    public async Task Context_that_stays_unavailable_is_retried_only_once()
    {
        Type("كلمة ");
        _context.Next = null;
        Backspace();

        await WaitForAsync(_ => _updates.Count(u => u.Probe == ProbeOutcome.Unavailable) == 2);
        await Task.Delay(250, TestContext.Current.CancellationToken);

        _context.Calls.ShouldBe(2);
    }

    [Theory]
    [InlineData(SilenceReason.ExcludedApp)]
    [InlineData(SilenceReason.PasswordField)]
    [InlineData(SilenceReason.ElevatedApp)]
    [InlineData(SilenceReason.FullScreen)]
    public async Task Silenced_windows_track_read_and_learn_nothing(SilenceReason reason)
    {
        _guard.Reason = reason;
        _context.Next = "secret text";
        Send(InputEvent.Of(InputEventKind.FocusChanged));
        Type("كلمة ");
        Backspace();
        Type("سر ");
        await DrainAsync();
        await Task.Delay(50, TestContext.Current.CancellationToken);

        _context.Calls.ShouldBe(0);
        Commits().ShouldBeEmpty();
        _updates.ShouldNotContain(u => u.WordToLearn != null);
        _updates.Where(u => u.Input?.Input.Kind == InputEventKind.Character).ShouldAllBe(u => u.Silenced == reason && u.Letters == 0);
    }

    [Fact]
    public async Task Password_field_found_while_reading_silences_until_the_focus_moves()
    {
        _context.Password = true;
        Send(InputEvent.Of(InputEventKind.FocusChanged));
        (await WaitForProbeAsync()).Silenced.ShouldBe(SilenceReason.PasswordField);

        Type("سرية ");
        await DrainAsync();
        Commits().ShouldBeEmpty();

        _context.Password = false;
        _context.Next = "";
        Send(InputEvent.Of(InputEventKind.FocusChanged));   // moved to an ordinary field
        await WaitForAsync(u => u.Probe == ProbeOutcome.ResumedEmpty);
        Type("كلمة ");
        await DrainAsync();
        Commits().ShouldBe(["كلمة"]);
    }

    [Fact]
    public async Task A_finished_word_is_learned_when_the_next_event_arrives()
    {
        Type("كلمة ");
        await DrainAsync(); // Escape after the space finalizes it

        _updates.Select(u => u.WordToLearn).OfType<string>().ShouldBe(["كلمة"]);
    }

    [Fact]
    public async Task Deleting_a_word_right_after_finishing_it_does_not_learn_it()
    {
        _context.Next = "كتبا";
        Type("كتبا ");
        Backspace();                                          // the user is correcting it
        await WaitForAsync(u => u.Probe == ProbeOutcome.ResumedWithWord);
        Backspace(2);
        Type("اب ");
        await DrainAsync();

        _updates.Select(u => u.WordToLearn).OfType<string>().ShouldBe(["كتاب"]);
    }

    [Fact]
    public async Task An_accepted_suggestion_deleted_at_once_is_not_learned()
    {
        Type("مدر");
        Send(InputEvent.Accept(0, "مدرسة"));
        Send(InputEvent.Of(InputEventKind.CtrlBackspace));     // wrong suggestion: removed at once
        await DrainAsync();

        _updates.ShouldNotContain(u => u.WordToLearn != null);
    }

    [Fact]
    public void Pending_word_can_be_taken_on_exit()
    {
        var pipeline = new InputPipeline(Channel.CreateUnbounded<InputRecord>().Reader, _context);

        pipeline.TakePendingWord().ShouldBeNull();
    }

    [Fact]
    public async Task Accepting_types_the_minimal_edit_and_commits_the_word()
    {
        Type("مدرسه");
        Send(InputEvent.Accept(0, "مدرسة"));
        await DrainAsync();

        _injector.Edits.ShouldBe([new TextEdit(1, "ة ")]);  // fix the last letter, add a space
        Commits().ShouldBe(["مدرسة"]);
        _updates.Last(u => u.Result.Outcome == BufferOutcome.Committed).Word.ShouldBeEmpty();
    }

    private Task<PipelineUpdate> AfterCharacterAsync(char c) =>
        WaitForAsync(u => u.Input?.Input is { Kind: InputEventKind.Character } input && input.Character == c);

    [Fact]
    public async Task Words_ended_by_a_space_are_remembered_for_phrases()
    {
        Type("بسم ");
        (await AfterCharacterAsync(' ')).RecentWords.ShouldBe(["بسم"]);

        Type("الله ");
        (await WaitForAsync(u => u.RecentWords is { Count: 2 })).RecentWords.ShouldBe(["بسم", "الله"]);
    }

    [Fact]
    public async Task Any_other_boundary_or_a_caret_move_forgets_them()
    {
        Type("بسم،");
        (await AfterCharacterAsync('،')).RecentWords.ShouldBeEmpty();

        Type("بسم ");
        await AfterCharacterAsync(' ');
        Send(InputEvent.Of(InputEventKind.MouseClick));
        (await WaitForAsync(u => u.Input?.Input.Kind == InputEventKind.MouseClick)).RecentWords.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_next_word_of_a_phrase_can_be_accepted_before_typing_any_letter()
    {
        Type("بسم ");
        await AfterCharacterAsync(' ');
        Send(InputEvent.Accept(0, "الله الرحمن الرحيم"));

        var accepted = await WaitForAsync(u => u.Input?.Input.Kind == InputEventKind.AcceptSuggestion);
        _injector.Edits.ShouldBe([new TextEdit(0, "الله الرحمن الرحيم ")]);
        accepted.RecentWords.ShouldBe(["بسم", "الله", "الرحمن", "الرحيم"]);
    }

    private string[] PhrasesLearned() => [.. _updates.SelectMany(u => u.PhrasesToLearn ?? [])];

    [Fact]
    public async Task Words_typed_in_a_row_become_sequences_to_learn_when_the_last_word_is_final()
    {
        Type("جزاك الله خيرا.");
        await AfterCharacterAsync('.');
        PhrasesLearned().ShouldBe(["جزاك الله"]); // «خيرا» is still pending: it could be corrected

        await DrainAsync();
        PhrasesLearned().ShouldBe(["جزاك الله", "الله خيرا", "جزاك الله خيرا"]);
    }

    [Fact]
    public async Task Correcting_the_last_word_at_once_drops_its_sequences()
    {
        Type("جزاك الله ");
        await AfterCharacterAsync(' ');
        Backspace();
        await DrainAsync();

        PhrasesLearned().ShouldBeEmpty();
    }

    [Fact]
    public async Task Words_before_the_caret_join_the_phrase_when_the_context_is_read()
    {
        // A focus event (some editors raise one after Enter) detaches; «صباح » is already typed when the context is read.
        _context.Next = "سطر\rصباح ال";
        Send(InputEvent.Of(InputEventKind.FocusChanged));
        (await WaitForAsync(u => u.Probe == ProbeOutcome.ResumedWithWord)).RecentWords.ShouldBe(["صباح"]);

        Type("خير ");
        await DrainAsync();

        PhrasesLearned().ShouldBe(["صباح الخير"]);
    }

    [Fact]
    public async Task A_word_finished_before_the_context_is_read_is_recovered_from_it()
    {
        // A focus or window change, then «ارجو لكم» typed at once: the space comes before the read.
        _context.Next = "سطر\nارجو لكم";
        Send(InputEvent.Of(InputEventKind.FocusChanged));
        Type("ارجو لكم");

        (await WaitForAsync(u => u.Probe == ProbeOutcome.ResumedWithWord)).Word.ShouldBe("لكم");
        Type(" ");
        await DrainAsync();

        _updates.Select(u => u.WordToLearn).OfType<string>().ShouldBe(["ارجو", "لكم"]);
        PhrasesLearned().ShouldBe(["ارجو لكم"]);
    }

    [Fact]
    public async Task Typing_that_does_not_match_the_text_read_is_not_learned()
    {
        _context.Next = "شيء آخر تماما";
        Send(InputEvent.Of(InputEventKind.FocusChanged));
        Type("ارجو لكم");
        await WaitForAsync(u => u.Probe is not null && u.Probe != ProbeOutcome.Discarded);
        await DrainAsync();

        _updates.ShouldNotContain(u => u.WordToLearn == "ارجو");
    }

    [Fact]
    public async Task Without_ui_automation_words_after_the_first_boundary_typed_meanwhile_are_kept()
    {
        _context.Next = null;
        Send(InputEvent.Of(InputEventKind.MouseClick));
        Type("ب ارجو لكم");                       // «ب» may end a word we never saw: not learned

        (await WaitForAsync(u => u.Probe == ProbeOutcome.AssumedEmpty)).Word.ShouldBe("لكم");
        await DrainAsync();

        _updates.Select(u => u.WordToLearn).OfType<string>().ShouldBe(["ارجو"]);
    }

    [Fact]
    public async Task Pasted_words_never_join_the_phrase_chain()
    {
        _context.Next = "أرجو لكم يوما سعيدا";
        Send(InputEvent.Of(InputEventKind.Paste));
        (await WaitForAsync(u => u.Probe == ProbeOutcome.ResumedWithWord)).RecentWords.ShouldBeEmpty();

        Type(" ");
        await DrainAsync();

        PhrasesLearned().ShouldBeEmpty();
    }

    [Fact]
    public async Task Enter_after_an_accepted_word_and_its_space_still_ends_the_chain()
    {
        Type("يوما ");
        await AfterCharacterAsync(' ');
        Send(InputEvent.Accept(0, "سعيدا"));      // inserts «سعيدا »
        Type((char)13 + "ارجو ");
        await DrainAsync();

        PhrasesLearned().ShouldBe(["يوما سعيدا"]);
    }

    [Fact]
    public async Task Choosing_words_with_the_arrows_keeps_the_chain()
    {
        Type("ارجو ");
        await AfterCharacterAsync(' ');
        Send(InputEvent.Select(0));                // ↑ into the bar
        Send(InputEvent.Accept(0, "ان"));
        Send(InputEvent.Select(0));
        Send(InputEvent.Select(1));
        Send(InputEvent.Accept(1, "تكون"));
        Type("ب");
        await DrainAsync();

        PhrasesLearned().ShouldBe(["ارجو ان", "ان تكون", "ارجو ان تكون"]);
    }

    [Fact]
    public async Task An_accepted_phrase_counts_as_typed()
    {
        Type("بسم ");
        await AfterCharacterAsync(' ');
        Send(InputEvent.Accept(0, "الله الرحمن"));
        await DrainAsync();

        PhrasesLearned().ShouldBe(["بسم الله", "الله الرحمن", "بسم الله الرحمن"]);
    }

    [Fact]
    public async Task Backspace_right_after_an_accept_puts_the_typed_text_back_and_learns_nothing()
    {
        Type("احم");
        Send(InputEvent.Accept(0, "أحمد"));
        Send(InputEvent.Of(InputEventKind.UndoAccept));
        var undone = await WaitForAsync(u => u.Input?.Input.Kind == InputEventKind.UndoAccept);
        await DrainAsync();

        _injector.Edits.ShouldBe([new TextEdit(3, "أحمد "), new TextEdit(5, "احم")]);
        undone.Word.ShouldBe("احم");
        _updates.ShouldNotContain(u => u.WordToLearn != null);
    }

    [Fact]
    public async Task An_accept_counts_what_it_typed_and_its_undo_takes_it_back()
    {
        Type("احم");
        Send(InputEvent.Accept(0, "أحمد"));         // types «أحمد » for one key: 4 saved
        Send(InputEvent.Of(InputEventKind.UndoAccept));
        await DrainAsync();

        _updates.Select(u => u.Usage).OfType<UsageDelta>().ShouldBe([new UsageDelta(1, 4), new UsageDelta(-1, -4)]);
    }

    [Fact]
    public async Task With_nothing_to_undo_the_swallowed_backspace_is_sent_as_is()
    {
        Type("احم");
        Send(InputEvent.Accept(0, "أحمد"));
        Type("ب");                                // another key: the accept can no longer be undone
        Send(InputEvent.Of(InputEventKind.UndoAccept));
        await DrainAsync();

        _injector.Edits.Last().ShouldBe(new TextEdit(1, ""));
    }

    [Fact]
    public async Task Accepting_a_hamza_spelling_replaces_the_whole_prefix()
    {
        Type("احم");
        Send(InputEvent.Accept(0, "أحمد"));
        await DrainAsync();

        _injector.Edits.ShouldBe([new TextEdit(3, "أحمد ")]);
    }

    [Fact]
    public async Task Accept_is_ignored_when_the_context_is_unknown()
    {
        Type("كلمة ");
        Send(InputEvent.Of(InputEventKind.MouseClick));
        Send(InputEvent.Accept(0, "مدرسة"));
        await DrainAsync();

        _injector.Edits.ShouldBeEmpty();
    }

    [Fact]
    public async Task Failed_injection_leaves_the_word_unchanged()
    {
        _injector.Succeeds = false;
        Type("مدر");
        Send(InputEvent.Accept(0, "مدرسة"));
        await DrainAsync();

        Commits().ShouldBeEmpty();
        _updates.Last(u => u.Input?.Input.Kind == InputEventKind.AcceptSuggestion).Word.ShouldBe("مدر");
    }

    [Fact]
    public async Task Esc_suppresses_suggestions_until_the_word_ends()
    {
        Type("مدر");
        Send(InputEvent.Of(InputEventKind.DismissSuggestions));
        Type("س");
        await DrainAsync();
        _updates.Last(u => u.Input?.Input.Kind == InputEventKind.Character).SuggestionsSuppressed.ShouldBeTrue();

        Type(" قل");
        await DrainAsync();
        _updates.Last(u => u.Input?.Input.Kind == InputEventKind.Character).SuggestionsSuppressed.ShouldBeFalse();
    }

    [Theory]
    [InlineData(' ', 1, "سة ")]   // space after the caret: replaced, never doubled
    [InlineData('،', 0, "سة")]    // punctuation: no space before it
    [InlineData('\0', 0, "سة ")]  // end of the text: space added
    public async Task Accepting_mid_line_respects_the_character_after_the_caret(char after, int deleteForward, string typed)
    {
        _context.Next = "ذهبت إلى ";
        _context.CharAfter = after;
        Send(InputEvent.Of(InputEventKind.MouseClick));     // clicked in the middle of a line
        await WaitForAsync(u => u.Probe == ProbeOutcome.ResumedEmpty);

        Type("مدر");
        Send(InputEvent.Accept(0, "مدرسة"));
        await DrainAsync();

        _injector.Edits.ShouldBe([new TextEdit(0, typed, deleteForward)]);
        Commits().ShouldBe(["مدرسة"]);
    }

    [Fact]
    public async Task What_follows_the_caret_stays_known_across_words_until_the_caret_moves()
    {
        _context.Next = "";
        _context.CharAfter = ' ';
        Send(InputEvent.Of(InputEventKind.MouseClick));
        await WaitForAsync(u => u.Probe == ProbeOutcome.ResumedEmpty);

        Type("ذهبت مدر");                                   // typing inserts before the same space
        Send(InputEvent.Accept(0, "مدرسة"));
        Send(InputEvent.Of(InputEventKind.Escape));
        await DrainAsync();

        _injector.Edits.Single().DeleteForward.ShouldBe(1);
    }

    [Fact]
    public async Task Keyboard_selection_hides_suggestions_without_reading_the_context()
    {
        _context.Next = "ذهبت إلى المدرسة";
        Type("مدر");
        Send(InputEvent.Of(InputEventKind.Selection));   // Shift+Left
        await DrainAsync();
        await Task.Delay(50, TestContext.Current.CancellationToken);

        _updates.Last(u => u.Input?.Input.Kind == InputEventKind.Selection).IsDetached.ShouldBeTrue();
        _context.Calls.ShouldBe(0);
        _updates.ShouldNotContain(u => u.Probe != null);
    }

    [Fact]
    public async Task Plain_caret_move_after_a_selection_reads_the_context_again()
    {
        _context.Next = "ذهبت إلى المدر";
        Send(InputEvent.Of(InputEventKind.Selection));
        Send(InputEvent.Of(InputEventKind.Navigation));  // Right without Shift: selection collapsed

        (await WaitForAsync(u => u.Probe == ProbeOutcome.ResumedWithWord)).Word.ShouldBe("المدر");
    }

    [Fact]
    public async Task Mouse_selection_keeps_suggestions_hidden_and_never_assumes_an_empty_word()
    {
        _context.HasSelection = true;                    // text dragged with the mouse
        Send(InputEvent.Of(InputEventKind.MouseClick));

        var probe = await WaitForProbeAsync();
        await Task.Delay(150, TestContext.Current.CancellationToken);

        probe.Probe.ShouldBe(ProbeOutcome.SelectionActive);
        probe.IsDetached.ShouldBeTrue();
        _context.Calls.ShouldBe(1);                       // no retry
        _updates.ShouldNotContain(u => u.Probe == ProbeOutcome.AssumedEmpty);
    }

    private sealed class FakeGuard : IInputGuard
    {
        public volatile SilenceReason Reason;

        public SilenceReason Check() => Reason;
    }

    private sealed class FakeInjector : ITextInjector
    {
        public List<TextEdit> Edits { get; } = [];

        public bool Succeeds { get; set; } = true;

        public bool Send(TextEdit edit)
        {
            if (Succeeds)
            {
                Edits.Add(edit);
            }

            return Succeeds;
        }
    }

    private sealed class FakeCaretContext : ICaretContextSource
    {
        private int _calls;

        public string? Next { get; set; }

        /// <summary>Response by call number (1-based); overrides <see cref="Next"/>.</summary>
        public Func<int, string?>? Responses { get; set; }

        public TimeSpan Delay { get; set; }

        public int Calls => Volatile.Read(ref _calls);

        /// <summary>Text is selected in the app (overrides <see cref="Next"/>).</summary>
        public bool HasSelection { get; set; }

        /// <summary>The focused field is a password field.</summary>
        public volatile bool Password;

        public async Task<CaretContext> ReadBeforeCaretAsync(int maxChars, CancellationToken cancellation)
        {
            var call = Interlocked.Increment(ref _calls);
            if (Delay > TimeSpan.Zero)
            {
                await Task.Delay(Delay, cancellation);
            }

            if (Password)
            {
                return CaretContext.Password;
            }

            if (HasSelection)
            {
                return CaretContext.Selection;
            }

            var text = Responses is { } responses ? responses(call) : Next;
            return text is null ? CaretContext.Unavailable : CaretContext.Of(text, CharAfter);
        }

        /// <summary>The character after the caret returned with the text.</summary>
        public char? CharAfter { get; set; }
    }
}
