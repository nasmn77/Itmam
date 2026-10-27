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

    /// <summary>
    /// After a caret move the context stayed unavailable and no letter was typed meanwhile:
    /// tracking resumed as if the caret were at the start of a word.
    /// </summary>
    AssumedEmpty,
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

    /// <summary>
    /// One retry after an <see cref="ProbeOutcome.Unavailable"/> read: Chromium/Electron apps
    /// switch their accessibility tree on at the first UI Automation request and answer the second.
    /// </summary>
    public TimeSpan RetryUnavailableAfter { get; init; } = TimeSpan.FromMilliseconds(100);

    /// <summary>Characters read before the caret: enough for any word plus the boundary before it.</summary>
    public int ProbeChars { get; init; } = WordBuffer.MaxLength + 1;

    /// <summary>
    /// Checked right before every read of the caret context; false (e.g. the foreground app is
    /// excluded) means nothing is read from the app.
    /// </summary>
    public Func<bool> CanReadContext { get; init; } = static () => true;
}

/// <summary>
/// Single worker that applies input events to an <see cref="InputTracker"/>. Whenever the text
/// before the caret becomes unknown (Backspace into existing text, a click, arrows, a focus,
/// foreground or layout change, paste, a shortcut), the buffer is detached; once the app has
/// processed the event, the characters before the caret are read and tracking resumes with the
/// word the caret is in. Without that context: after a caret move, tracking resumes with an empty
/// word if nothing was typed meanwhile; otherwise it waits for the next boundary.
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
        var retried = false;
        var detachedByBackspace = false;
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
                        var outcome = await ProbeAsync(isRetry: retried, afterCaretMove: !detachedByBackspace, cancellation).ConfigureAwait(false);
                        if (outcome == ProbeOutcome.Discarded)
                        {
                            // Keys arrived: read again once they settle (the text will include them).
                            probeDue = DateTime.UtcNow.Ticks + _options.ProbeDelay.Ticks;
                        }
                        else if (outcome == ProbeOutcome.Unavailable && !retried)
                        {
                            retried = true;
                            probeDue = DateTime.UtcNow.Ticks + _options.RetryUnavailableAfter.Ticks;
                        }

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

                // Every non-character event that leaves the context unknown (Backspace into a word,
                // click, arrows, focus/foreground/layout change, paste, shortcut) restarts the delay
                // and the retry allowance, so holding a key probes once.
                if (Tracker.Buffer.IsDetached && Tracker.IsArabicLayout && record.Input.Kind != InputEventKind.Character)
                {
                    probeDue = DateTime.UtcNow.Ticks + _options.ProbeDelay.Ticks;
                    retried = false;
                    detachedByBackspace = record.Input.Kind == InputEventKind.Backspace;
                }
                else if (!Tracker.Buffer.IsDetached)
                {
                    probeDue = null;
                }
            }
        }
    }

    /// <param name="isRetry">This is the last attempt (after an Unavailable one).</param>
    /// <param name="afterCaretMove">
    /// Detached by a click, arrow, focus change… rather than Backspace: if the context stays
    /// unavailable, assume an empty word (as if the caret were at a word start), unless letters were
    /// typed meanwhile. After Backspace the caret is known to be inside a word, so tracking waits.
    /// </param>
    /// <param name="cancellation">Stops the read.</param>
    private async Task<ProbeOutcome?> ProbeAsync(bool isRetry, bool afterCaretMove, CancellationToken cancellation)
    {
        if (!Tracker.Buffer.IsDetached)
        {
            return null;
        }

        if (HasPendingInput())
        {
            return ProbeOutcome.Discarded;
        }

        if (!_options.CanReadContext())
        {
            Raise(null, BufferResult.None, ProbeOutcome.NotAllowed);
            return ProbeOutcome.NotAllowed;
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
            if (isRetry && afterCaretMove)
            {
                result = Tracker.Buffer.ResumeEmpty();
                if (result.Outcome == BufferOutcome.Reset)
                {
                    outcome = ProbeOutcome.AssumedEmpty;
                }
            }
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
        return outcome;
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
