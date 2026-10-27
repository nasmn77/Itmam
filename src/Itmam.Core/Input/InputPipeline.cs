using System.Diagnostics;
using System.Threading.Channels;

namespace Itmam.Core.Input;

/// <summary>An input event with the foreground keyboard layout at that moment.</summary>
/// <param name="Input">What the event means for the word being typed. Never logged.</param>
/// <param name="KeyboardLayout">HKL of the window receiving keyboard input.</param>
/// <param name="Timestamp"><see cref="Stopwatch.GetTimestamp"/> when the event was captured.</param>
public readonly record struct InputRecord(InputEvent Input, nint KeyboardLayout, long Timestamp);

/// <summary>
/// Reads the few characters immediately before the caret in the focused app (UI Automation in
/// production). Implementations must return null for password fields, unsupported apps and
/// timeouts, and must not keep the text.
/// </summary>
public interface ICaretContextSource
{
    Task<string?> ReadBeforeCaretAsync(int maxChars, CancellationToken cancellation);
}

public enum ProbeOutcome
{
    /// <summary>Tracking resumed with an empty word (start of text or a boundary before the caret).</summary>
    ResumedEmpty,

    /// <summary>The word before the caret became the current word.</summary>
    ResumedWithWord,

    /// <summary>The context could not be used; tracking stays paused until a boundary or context change.</summary>
    Unusable,

    /// <summary>The app does not expose its text (or it is a password field, or the read timed out).</summary>
    Unavailable,

    /// <summary>Keys arrived while reading, so the text may already include them; result discarded.</summary>
    Discarded,

    /// <summary>The foreground app is excluded (or reading is not allowed): nothing was read.</summary>
    NotAllowed,
}

/// <summary>State after an event or a context probe, raised on the pipeline's thread.</summary>
public readonly record struct PipelineUpdate(
    InputRecord? Input,
    BufferResult Result,
    ProbeOutcome? Probe,
    string Word,
    int Letters,
    bool IsArabicLayout,
    nint Layout,
    bool IsDetached);

public sealed record InputPipelineOptions
{
    /// <summary>Wait after a Backspace on an empty word so the app has applied it before we read.</summary>
    public TimeSpan ProbeDelay { get; init; } = TimeSpan.FromMilliseconds(40);

    /// <summary>Characters read before the caret: enough for any word plus the boundary before it.</summary>
    public int ProbeChars { get; init; } = WordBuffer.MaxLength + 1;

    /// <summary>
    /// Checked right before every read of the caret context; false (e.g. the foreground app is
    /// excluded) means nothing is read from the app.
    /// </summary>
    public Func<bool> CanReadContext { get; init; } = static () => true;
}

/// <summary>
/// Single worker that applies input events to an <see cref="InputTracker"/>. When a Backspace on
/// an empty word leaves the buffer detached (the caret moved into text we did not see typed), it
/// reads the characters before the caret once the app has processed the key, and resumes tracking
/// from them. Without that context, tracking resumes at the next boundary or context change.
/// </summary>
public sealed class InputPipeline(ChannelReader<InputRecord> input, ICaretContextSource context, InputPipelineOptions? options = null)
{
    private readonly InputPipelineOptions _options = options ?? new InputPipelineOptions();

    public InputTracker Tracker { get; } = new();

    /// <summary>Raised on the pipeline thread after every event and probe.</summary>
    public event Action<PipelineUpdate>? Updated;

    public async Task RunAsync(CancellationToken cancellation)
    {
        long? probeDue = null;
        Task<bool>? waitForInput = null;
        while (!cancellation.IsCancellationRequested)
        {
            bool hasInput;
            try
            {
                if (probeDue is { } due)
                {
                    // Race the next input against the probe delay; only here is a Task allocated.
                    waitForInput ??= input.WaitToReadAsync(cancellation).AsTask();
                    using var delayCancel = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
                    var remaining = TimeSpan.FromTicks(Math.Max(0, due - DateTime.UtcNow.Ticks));
                    var delay = Task.Delay(remaining, delayCancel.Token);
                    if (await Task.WhenAny(waitForInput, delay).ConfigureAwait(false) == delay)
                    {
                        probeDue = null;
                        await ProbeAsync(cancellation).ConfigureAwait(false);
                        continue;
                    }

                    delayCancel.Cancel(); // free the timer now
                    hasInput = await waitForInput.ConfigureAwait(false);
                    waitForInput = null;
                }
                else if (waitForInput is not null)
                {
                    hasInput = await waitForInput.ConfigureAwait(false);
                    waitForInput = null;
                }
                else
                {
                    hasInput = await input.WaitToReadAsync(cancellation).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (!hasInput)
            {
                return; // channel completed
            }

            while (input.TryRead(out var record))
            {
                var result = Tracker.Process(record.Input, record.KeyboardLayout);
                Raise(record, result, probe: null);

                // Every Backspace while detached restarts the delay, so holding Backspace probes once.
                probeDue = Tracker.Buffer.IsDetached && Tracker.IsArabicLayout && record.Input.Kind == InputEventKind.Backspace
                    ? DateTime.UtcNow.Ticks + _options.ProbeDelay.Ticks
                    : Tracker.Buffer.IsDetached ? probeDue : null;
            }
        }
    }

    private async Task ProbeAsync(CancellationToken cancellation)
    {
        if (!Tracker.Buffer.IsDetached || HasPendingInput())
        {
            return;
        }

        if (!_options.CanReadContext())
        {
            Raise(null, BufferResult.None, ProbeOutcome.NotAllowed);
            return;
        }

        var text = await context.ReadBeforeCaretAsync(_options.ProbeChars, cancellation).ConfigureAwait(false);

        ProbeOutcome outcome;
        var result = BufferResult.None;
        if (HasPendingInput())
        {
            outcome = ProbeOutcome.Discarded;
        }
        else if (text is null)
        {
            outcome = ProbeOutcome.Unavailable;
        }
        else
        {
            result = Tracker.Buffer.ResumeFromContext(text, _options.ProbeChars);
            outcome = result.Outcome switch
            {
                BufferOutcome.Updated => ProbeOutcome.ResumedWithWord,
                BufferOutcome.Reset => ProbeOutcome.ResumedEmpty,
                _ => ProbeOutcome.Unusable,
            };
        }

        Raise(null, result, outcome);
    }

    private bool HasPendingInput() => input.CanCount ? input.Count > 0 : input.TryPeek(out _);

    private void Raise(InputRecord? record, BufferResult result, ProbeOutcome? probe)
    {
        var buffer = Tracker.Buffer;
        Updated?.Invoke(new PipelineUpdate(
            record,
            result,
            probe,
            buffer.Text,
            buffer.LetterCount,
            Tracker.IsArabicLayout,
            Tracker.Layout,
            buffer.IsDetached));
    }
}
