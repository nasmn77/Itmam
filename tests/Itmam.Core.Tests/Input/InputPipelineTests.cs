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
    private volatile bool _foregroundExcluded;

    public InputPipelineTests()
    {
        var pipeline = new InputPipeline(
            _channel.Reader,
            _context,
            new InputPipelineOptions { ProbeDelay = TimeSpan.FromMilliseconds(10), CanReadContext = () => !_foregroundExcluded });
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

    [Fact]
    public async Task Context_is_never_read_in_an_excluded_app()
    {
        _foregroundExcluded = true;
        Type("كلمة ");
        _context.Next = "secret text";
        Backspace();

        var probe = await WaitForProbeAsync();
        await Task.Delay(30, TestContext.Current.CancellationToken);

        probe.Probe.ShouldBe(ProbeOutcome.NotAllowed);
        probe.IsDetached.ShouldBeTrue();
        _context.Calls.ShouldBe(0);
    }

    private sealed class FakeCaretContext : ICaretContextSource
    {
        private int _calls;

        public string? Next { get; set; }

        /// <summary>Response by call number (1-based); overrides <see cref="Next"/>.</summary>
        public Func<int, string?>? Responses { get; set; }

        public TimeSpan Delay { get; set; }

        public int Calls => Volatile.Read(ref _calls);

        public async Task<string?> ReadBeforeCaretAsync(int maxChars, CancellationToken cancellation)
        {
            var call = Interlocked.Increment(ref _calls);
            if (Delay > TimeSpan.Zero)
            {
                await Task.Delay(Delay, cancellation);
            }

            return Responses is { } responses ? responses(call) : Next;
        }
    }
}
