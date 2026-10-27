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

    public InputPipelineTests()
    {
        var pipeline = new InputPipeline(_channel.Reader, _context, new InputPipelineOptions { ProbeDelay = TimeSpan.FromMilliseconds(10) });
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
    public async Task Without_ui_automation_tracking_resumes_on_a_click()
    {
        Type("قديم ");
        Backspace();
        _context.Next = null; // unsupported app

        (await WaitForProbeAsync()).Probe.ShouldBe(ProbeOutcome.Unavailable);
        Type("تكملة");                            // continues an unseen word: not tracked
        Send(InputEvent.Of(InputEventKind.MouseClick));
        Type("كتاب ");
        await DrainAsync();

        Commits().ShouldBe(["قديم", "كتاب"]);
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

    private sealed class FakeCaretContext : ICaretContextSource
    {
        private int _calls;

        public string? Next { get; set; }

        public TimeSpan Delay { get; set; }

        public int Calls => Volatile.Read(ref _calls);

        public async Task<string?> ReadBeforeCaretAsync(int maxChars, CancellationToken cancellation)
        {
            Interlocked.Increment(ref _calls);
            if (Delay > TimeSpan.Zero)
            {
                await Task.Delay(Delay, cancellation);
            }

            return Next;
        }
    }
}
