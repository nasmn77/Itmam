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
/// production). Implementations must never read password fields or excluded apps, and must not
/// keep the text.
/// </summary>
public interface ICaretContextSource
{
    Task<CaretContext> ReadBeforeCaretAsync(int maxChars, CancellationToken cancellation);
}

public enum CaretContextStatus
{
    /// <summary>The text before the caret was read.</summary>
    Text,

    /// <summary>Not readable: no UI Automation text support, excluded app, timeout.</summary>
    Unavailable,

    /// <summary>Text is selected: typing replaces it, so there is no word to continue.</summary>
    Selection,

    /// <summary>The focused field is a password field: nothing was read, and Itmam stays silent there.</summary>
    Password,
}

/// <summary>Why Itmam is silent (tracks, shows and learns nothing) in the current window.</summary>
public enum SilenceReason
{
    None,
    PasswordField,
    ElevatedApp,
    ExcludedApp,
    FullScreen,
}

/// <summary>
/// Decides whether Itmam may work in the current foreground window and focused field
/// (password fields, elevated apps, excluded apps, full-screen games and presentations).
/// Called on the pipeline thread whenever the context may have changed; must be fast.
/// </summary>
public interface IInputGuard
{
    SilenceReason Check();
}

/// <summary>Result of reading the caret context.</summary>
/// <param name="Status">Whether text was read.</param>
/// <param name="Text">Characters before the caret.</param>
/// <param name="CharAfter">The character after the caret, '\0' at the end of the text, null if not read.</param>
public readonly record struct CaretContext(CaretContextStatus Status, string Text = "", char? CharAfter = null)
{
    public static CaretContext Unavailable => new(CaretContextStatus.Unavailable);

    public static CaretContext Selection => new(CaretContextStatus.Selection);

    public static CaretContext Password => new(CaretContextStatus.Password);

    public static CaretContext Of(string text, char? charAfter = null) => new(CaretContextStatus.Text, text, charAfter);
}

/// <summary>Types an edit into the focused app (SendInput in production). Returns false if it failed.</summary>
public interface ITextInjector
{
    bool Send(TextEdit edit);
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

    /// <summary>Itmam is silent here (see <see cref="SilenceReason"/>): nothing was read.</summary>
    NotAllowed,

    /// <summary>
    /// After a caret move the context stayed unavailable and no letter was typed meanwhile:
    /// tracking resumed as if the caret were at the start of a word.
    /// </summary>
    AssumedEmpty,

    /// <summary>Text is selected (keyboard or mouse): nothing is tracked until the selection ends.</summary>
    SelectionActive,
}

/// <summary>State after an event or a context probe, raised on the pipeline's thread.</summary>
/// <param name="Input">The event, or null after a context probe.</param>
/// <param name="Result">What happened to the word (Committed for an accepted suggestion).</param>
/// <param name="Probe">Outcome of a caret-context read, if this update is one.</param>
/// <param name="Word">The current word as typed.</param>
/// <param name="Letters">Letters in the current word.</param>
/// <param name="IsArabicLayout">Arabic keyboard layout active.</param>
/// <param name="Layout">The HKL.</param>
/// <param name="IsDetached">Context unknown; nothing is tracked.</param>
/// <param name="SuggestionsSuppressed">Esc hid the bar for this word, or a suggestion was just accepted.</param>
/// <param name="Silenced">Why nothing is tracked here, or <see cref="SilenceReason.None"/>.</param>
/// <param name="WordToLearn">
/// A finished word that is now final and should be learned. A word ended by a boundary (or
/// accepted) stays pending until the next event: Backspace or Ctrl+Backspace right after it means
/// the user is correcting it, so it is dropped instead of learned.
/// </param>
/// <param name="RecentWords">
/// Normalized words typed in a row just before the current one (oldest first), to continue a known
/// phrase. In memory only; empty after any boundary other than a space or any caret move.
/// </param>
/// <param name="PhrasesToLearn">
/// Sequences of 2–5 words typed in a row that end with <paramref name="WordToLearn"/>, final with it
/// (and dropped with it when the word is corrected at once), for phrase learning.
/// </param>
public readonly record struct PipelineUpdate(
    InputRecord? Input,
    BufferResult Result,
    ProbeOutcome? Probe,
    string Word,
    int Letters,
    bool IsArabicLayout,
    nint Layout,
    bool IsDetached,
    bool SuggestionsSuppressed = false,
    SilenceReason Silenced = SilenceReason.None,
    string? WordToLearn = null,
    IReadOnlyList<string>? RecentWords = null,
    IReadOnlyList<string>? PhrasesToLearn = null);

public sealed record InputPipelineOptions
{
    /// <summary>Wait after a Backspace on an empty word so the app has applied it before we read.</summary>
    public TimeSpan ProbeDelay { get; init; } = TimeSpan.FromMilliseconds(40);

    /// <summary>
    /// One retry after an <see cref="ProbeOutcome.Unavailable"/> read: Chromium/Electron apps
    /// switch their accessibility tree on at the first UI Automation request and answer the second.
    /// </summary>
    public TimeSpan RetryUnavailableAfter { get; init; } = TimeSpan.FromMilliseconds(100);

    /// <summary>Type a space after an accepted suggestion (setting, default on).</summary>
    public bool AppendSpaceOnAccept { get; init; } = true;

    /// <summary>Characters read before the caret: enough for any word plus the boundary before it.</summary>
    public int ProbeChars { get; init; } = WordBuffer.MaxLength + 1;
}

/// <summary>
/// Single worker that applies input events to an <see cref="InputTracker"/>. Whenever the text
/// before the caret becomes unknown (Backspace into existing text, a click, arrows, a focus,
/// foreground or layout change, paste, a shortcut), the buffer is detached; once the app has
/// processed the event, the characters before the caret are read and tracking resumes with the
/// word the caret is in. Without that context: after a caret move, tracking resumes with an empty
/// word if nothing was typed meanwhile; otherwise it waits for the next boundary.
/// Where <see cref="IInputGuard"/> (or a password field found while reading) says so, nothing is
/// tracked, read, shown or learned until the context changes.
/// </summary>
public sealed class InputPipeline(
    ChannelReader<InputRecord> input,
    ICaretContextSource context,
    InputPipelineOptions? options = null,
    ITextInjector? injector = null,
    IInputGuard? guard = null)
{
    private volatile InputPipelineOptions _options = options ?? new InputPipelineOptions();
    private SilenceReason _silence;
    private bool _guardChecked;
    private string? _pendingWord;
    private string[]? _pendingPhrases;

    // Sequences finished by the event being processed (they become pending with its committed word).
    private List<string>? _finishedPhrases;

    private enum Suppression
    {
        None,

        /// <summary>Esc: until the word ends (boundary, reset, caret move).</summary>
        UntilWordEnds,

        /// <summary>Just accepted without a trailing space: until the next typed letter.</summary>
        UntilNextLetter,
    }

    private Suppression _suppression;

    // The character after the caret, read with the caret context ('\0' = end of text, null = unknown).
    // Typing, Backspace and accepting insert/delete before it, so it stays valid until the caret moves.
    private char? _charAfterCaret;

    // The words typed in a row just before the current one, for phrase suggestions (memory only).
    private readonly RecentWords _recent = new();

    public InputTracker Tracker { get; } = new();

    /// <summary>Replaced live from Settings (e.g. the space after accepting).</summary>
    public InputPipelineOptions Options
    {
        get => _options;
        set => _options = value;
    }

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
                _finishedPhrases = null;
                var result = Process(record);
                var (wordToLearn, phrasesToLearn) = AdvancePendingWord(record.Input.Kind, result);
                Raise(record, result, probe: null, wordToLearn, phrasesToLearn);

                // Keyboard selection or a silenced window: nothing to read.
                if (record.Input.Kind == InputEventKind.Selection || _silence != SilenceReason.None)
                {
                    probeDue = null;
                    continue;
                }

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

    /// <summary>
    /// The word finished last, not yet learned: it becomes final with the next event, unless that
    /// event is Backspace/Ctrl+Backspace (the user is correcting it). Call on exit to learn it.
    /// </summary>
    public string? TakePendingWord() => Interlocked.Exchange(ref _pendingWord, null);

    /// <summary>The sequences ending with the pending word (see <see cref="TakePendingWord"/>).</summary>
    public IReadOnlyList<string>? TakePendingPhrases() => Interlocked.Exchange(ref _pendingPhrases, null);

    // Returns the word (and its sequences) that just became final, to learn, if any.
    private (string? Word, IReadOnlyList<string>? Phrases) AdvancePendingWord(InputEventKind kind, BufferResult result)
    {
        string? final = null;
        IReadOnlyList<string>? phrases = null;
        if (_pendingWord is not null || _pendingPhrases is not null)
        {
            if (kind is InputEventKind.Backspace or InputEventKind.CtrlBackspace)
            {
                // Deleting right after finishing the word: do not learn it, nor the sequences it ends.
                _pendingWord = null;
                _pendingPhrases = null;
            }
            else if (result.Outcome != BufferOutcome.Committed)
            {
                final = TakePendingWord();
                phrases = TakePendingPhrases();
            }
        }

        if (result is { Outcome: BufferOutcome.Committed, CommittedWord: { } committed })
        {
            final ??= TakePendingWord();
            phrases ??= TakePendingPhrases();
            _pendingWord = committed;
            _pendingPhrases = _finishedPhrases is { Count: > 0 } finished ? [.. finished] : null;
        }

        return (final, phrases);
    }

    private BufferResult Process(InputRecord record)
    {
        var buffer = Tracker.Buffer;
        var kind = record.Input.Kind;
        var contextMayChange = kind is not (InputEventKind.Character or InputEventKind.Backspace or InputEventKind.Escape
            or InputEventKind.NavigationSelect or InputEventKind.AcceptSuggestion or InputEventKind.DismissSuggestions or InputEventKind.ForgetSuggestion);

        // Re-check where we are whenever the window, field or caret may have changed.
        if (contextMayChange || !_guardChecked)
        {
            _silence = guard?.Check() ?? SilenceReason.None;
            _guardChecked = true;
        }

        if (_silence != SilenceReason.None)
        {
            // Password field, elevated or excluded app, full-screen game: track nothing at all.
            buffer.Reset();
            _recent.Clear();
            _suppression = Suppression.None;
            _charAfterCaret = null;
            return BufferResult.None;
        }

        switch (kind)
        {
            case InputEventKind.AcceptSuggestion:
                return Accept(record);
            case InputEventKind.DismissSuggestions:
                _suppression = Suppression.UntilWordEnds;
                return BufferResult.None;
        }

        if (contextMayChange)
        {
            _charAfterCaret = null; // the caret may have moved: what follows it is unknown
        }

        var result = Tracker.Process(record.Input, record.KeyboardLayout);
        TrackRecentWords(record.Input, result);
        if (result.Outcome is BufferOutcome.Committed or BufferOutcome.Reset || buffer.IsDetached || buffer.IsEmpty)
        {
            _suppression = Suppression.None;
        }
        else if (_suppression == Suppression.UntilNextLetter && result.Outcome == BufferOutcome.Updated)
        {
            _suppression = Suppression.None;
        }

        return result;
    }

    // Phrase context: a word ended by a plain space continues it; any other boundary, a reset, or any
    // non-character event (Backspace, caret move, focus or layout change…) forgets it.
    private void TrackRecentWords(InputEvent input, BufferResult result)
    {
        // A finished word ends sequences with the words typed in a row before it, whatever ends it.
        if (result is { Outcome: BufferOutcome.Committed, CommittedWord: { } finished })
        {
            CollectPhrases(finished);
        }

        if (input.Kind != InputEventKind.Character)
        {
            _recent.Clear();
        }
        else if (result.Outcome == BufferOutcome.Committed)
        {
            if (input.Character == ' ' && result.CommittedWord is { } word)
            {
                _recent.Push(word);
            }
            else
            {
                _recent.Clear();
            }
        }
        else if (result.Outcome == BufferOutcome.Reset)
        {
            _recent.Clear();
        }
    }

    // Replaces the typed prefix with the accepted word (or the rest of a phrase) in the app, then updates
    // the buffer. The buffer may be empty: right after a space the bar can offer a phrase's next word.
    private BufferResult Accept(InputRecord record)
    {
        var buffer = Tracker.Buffer;
        if (record.Input.Text is not { Length: > 0 } word || buffer.IsDetached || injector is null)
        {
            return BufferResult.None;
        }

        var edit = TextEdit.Compute(buffer.Text, word, _options.AppendSpaceOnAccept, _charAfterCaret);
        if (!injector.Send(edit))
        {
            return BufferResult.None;
        }

        // Accepted words join the phrase context; without a trailing space the last one is still being typed.
        foreach (var accepted in (edit.EndsWithSpace ? word : word[..Math.Max(0, word.LastIndexOf(' '))]).Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            CollectPhrases(accepted);
            _recent.Push(accepted);
        }

        _suppression = edit.EndsWithSpace ? Suppression.None : Suppression.UntilNextLetter;
        return buffer.AcceptWord(word, edit.EndsWithSpace);
    }

    private void CollectPhrases(string word)
    {
        foreach (var sequence in _recent.SequencesEndingWith(word))
        {
            (_finishedPhrases ??= []).Add(sequence);
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

        // The foreground may have changed without an event we saw (e.g. a game went full screen).
        _silence = guard?.Check() ?? SilenceReason.None;
        if (_silence != SilenceReason.None)
        {
            Raise(null, BufferResult.None, ProbeOutcome.NotAllowed);
            return ProbeOutcome.NotAllowed;
        }

        var caret = await context.ReadBeforeCaretAsync(_options.ProbeChars, cancellation).ConfigureAwait(false);

        ProbeOutcome outcome;
        var result = BufferResult.None;
        if (HasPendingInput())
        {
            outcome = ProbeOutcome.Discarded;
        }
        else if (caret.Status == CaretContextStatus.Password)
        {
            // Found while reading (UI Automation IsPassword): silent until the focus moves elsewhere.
            _silence = SilenceReason.PasswordField;
            Tracker.Buffer.Reset();
            outcome = ProbeOutcome.NotAllowed;
        }
        else if (caret.Status == CaretContextStatus.Selection)
        {
            outcome = ProbeOutcome.SelectionActive; // e.g. selected by dragging the mouse
        }
        else if (caret.Status == CaretContextStatus.Unavailable)
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
            result = Tracker.Buffer.ResumeFromContext(caret.Text, _options.ProbeChars);
            _charAfterCaret = caret.CharAfter;
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

    private void Raise(InputRecord? record, BufferResult result, ProbeOutcome? probe, string? wordToLearn = null, IReadOnlyList<string>? phrasesToLearn = null)
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
            buffer.IsDetached,
            _suppression != Suppression.None,
            _silence,
            wordToLearn,
            _recent.Snapshot,
            phrasesToLearn));
    }
}
