using System.Diagnostics;
using System.Threading.Channels;
using Itmam.Core.Input;
using Itmam.Core.Languages;
using Itmam.Core.Suggestions;
using Itmam.Win32;

namespace Itmam.App.Suggestions;

/// <summary>What was shown for a keystroke (for the debug window; contains no typed text).</summary>
internal readonly record struct SuggestionShown(int Count, CaretSource Source, TimeSpan Latency);

/// <summary>What one language suggests from: its engine (dictionary + learned words), phrases and common mistakes.</summary>
internal sealed record LanguageSuggestions(SuggestionEngine Engine, PhraseIndex Phrases, Corrections Corrections)
{
    public LanguageProfile Language => Engine.Language;
}

/// <summary>
/// Connects the input pipeline to the suggestion bar: on every word change it asks the engine for
/// suggestions (on the pipeline thread), then locates the caret and shows or hides the bar.
/// Only the most recent request is processed, so fast typing never queues stale bars.
/// Accepting (Tab, Ctrl+N, click) is done by the pipeline; afterwards the word is empty or
/// suppressed, so the bar hides by itself.
/// </summary>
internal sealed class SuggestionController : IDisposable
{
    private readonly Func<LanguageProfile, LanguageSuggestions> _forLanguage;
    private readonly CaretLocator _caret;
    private readonly SuggestionPopup _popup;
    private readonly Func<bool> _isEnabled;
    private readonly Lock _userWordsLock;

    /// <summary>Time for the app to insert the typed letter before the caret is read again.</summary>
    private static readonly TimeSpan SettleDelay = TimeSpan.FromMilliseconds(60);
    private readonly Channel<Request> _requests = Channel.CreateBounded<Request>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true, SingleWriter = true });
    private readonly CancellationTokenSource _stop = new();
    private long _sequence;

    /// <param name="forLanguage">What the language being typed suggests from (called on the pipeline thread).</param>
    /// <param name="caret">Finds the caret.</param>
    /// <param name="popup">The bar.</param>
    /// <param name="isEnabled">Whether Itmam is on.</param>
    /// <param name="userWordsLock">
    /// Held while the engine reads the user's words: "My words" in Settings changes them from the UI thread.
    /// </param>
    public SuggestionController(Func<LanguageProfile, LanguageSuggestions> forLanguage, CaretLocator caret, SuggestionPopup popup, Func<bool> isEnabled, Lock userWordsLock)
    {
        _forLanguage = forLanguage;
        _userWordsLock = userWordsLock;
        _caret = caret;
        _popup = popup;
        _isEnabled = isEnabled;
        _ = Task.Run(() => ProcessAsync(_stop.Token));
    }

    /// <summary>Raised on a worker thread after the bar was shown.</summary>
    public event Action<SuggestionShown>? Shown;

    /// <summary>Call from the pipeline thread for every update.</summary>
    public void OnPipelineUpdated(PipelineUpdate update)
    {
        // Native navigation only moves the selection; the words stay the same.
        if (update.Input?.Input is { Kind: InputEventKind.NavigationSelect } select)
        {
            OnUi(() => _popup.SetSelected(select.Index));
            return;
        }

        var sequence = Interlocked.Increment(ref _sequence);
        IReadOnlyList<string> words = [];
        var sources = new Dictionary<string, string>(StringComparer.Ordinal);
        // Silenced windows (password field, elevated/excluded app, full screen) never show the bar.
        var language = update.Language ?? LanguageProfile.Arabic;
        if (update is { Language: not null, IsDetached: false, SuggestionsSuppressed: false, Silenced: SilenceReason.None }
            && _isEnabled())
        {
            words = Suggest(_forLanguage(language), update, sources);
        }

        var started = update.Input?.Timestamp ?? Stopwatch.GetTimestamp();
        _requests.Writer.TryWrite(new Request(sequence, words, started, sources, language));
    }

    public void Dispose()
    {
        _stop.Cancel();
        _requests.Writer.TryComplete();
    }

    private async Task ProcessAsync(CancellationToken cancellation)
    {
        try
        {
            await foreach (var request in _requests.Reader.ReadAllAsync(cancellation))
            {
                if (request.Words.Count == 0)
                {
                    OnUi(_popup.HideSuggestions);
                    continue;
                }

                var rightToLeft = request.Language.RightToLeft;
                var caret = await _caret.LocateAsync(rightToLeft, cancellation).ConfigureAwait(false);
                if (request.Sequence != Volatile.Read(ref _sequence))
                {
                    continue; // a newer keystroke is already waiting
                }

                OnUi(() =>
                {
                    if (request.Sequence == Volatile.Read(ref _sequence))
                    {
                        _popup.ShowSuggestions(request.Words, caret.Bounds, rightToLeft, belowPointer: caret.Source == CaretSource.Mouse);
                        PhraseSources = request.Sources;
                        ShownLanguage = request.Language;
                        Shown?.Invoke(new SuggestionShown(request.Words.Count, caret.Source, Stopwatch.GetElapsedTime(request.Started)));
                    }
                });

                // The hook sees a key before the app inserts the letter, so the caret was read one step early.
                // Usually that is one character, but an empty field with automatic direction (Google search)
                // turns right-to-left on the first Arabic letter and its caret jumps to the other side. If no
                // newer keystroke arrives meanwhile, read the caret again once the app has settled and move the bar.
                if (await QuietForAsync(SettleDelay, cancellation).ConfigureAwait(false))
                {
                    var settled = await _caret.LocateAsync(rightToLeft, cancellation).ConfigureAwait(false);
                    if (Moved(caret.Bounds, settled.Bounds))
                    {
                        OnUi(() =>
                        {
                            if (request.Sequence == Volatile.Read(ref _sequence))
                            {
                                _popup.ShowSuggestions(request.Words, settled.Bounds, rightToLeft, belowPointer: settled.Source == CaretSource.Mouse);
                            }
                        });
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    /// <summary>
    /// For the bar on screen: the whole phrase each phrase suggestion comes from (it shows only the rest).
    /// UI thread.
    /// </summary>
    public IReadOnlyDictionary<string, string> PhraseSources { get; private set; } = new Dictionary<string, string>();

    /// <summary>For the bar on screen: the language of its suggestions. UI thread.</summary>
    public LanguageProfile ShownLanguage { get; private set; } = LanguageProfile.Arabic;

    /// <summary>The user's text shortcuts («ع1» → its text).</summary>
    public TextShortcuts Shortcuts { get; set; } = TextShortcuts.Empty;

    /// <summary>Phrases after the dictionary words (setting <c>PhrasesLast</c>).</summary>
    public bool PhrasesLast { get; set; }

    // A text shortcut typed exactly comes first, then a phrase the user is typing (Tab inserts the whole
    // rest of it), then dictionary words. Phrases can show the bar right after a space, before any
    // letter: the next word of «بسم …»; shortcuts show whatever the minimum letters setting. With
    // PhrasesLast the same suggestions are shown, the phrases moved after the words.
    private IReadOnlyList<string> Suggest(LanguageSuggestions language, PipelineUpdate update, Dictionary<string, string> sources)
    {
        var suggestions = SuggestInOrder(language, update, sources, out var phraseSuggestions);
        if (!PhrasesLast || phraseSuggestions.Count == 0)
        {
            return suggestions;
        }

        var phrases = phraseSuggestions.ToHashSet(StringComparer.Ordinal);
        return [.. suggestions.Where(s => !phrases.Contains(s)), .. suggestions.Where(phrases.Contains)];
    }

    private IReadOnlyList<string> SuggestInOrder(LanguageSuggestions language, PipelineUpdate update, Dictionary<string, string> sources, out IReadOnlyList<string> phraseSuggestions)
    {
        var engine = language.Engine;
        var max = engine.Options.MaxSuggestions;
        IReadOnlyList<string> phrases = language.Phrases.Suggest(update.RecentWords ?? [], update.Word, max, sources);
        phraseSuggestions = phrases;
        // A known mistake typed exactly: its corrections first (after the user's own shortcut).
        if (language.Corrections.Find(update.Word) is { Count: > 0 } fixes)
        {
            phrases = [.. fixes.Concat(phrases).Distinct().Take(max)];
        }

        if (Shortcuts.Find(update.Word) is { } shortcut)
        {
            phrases = [shortcut, .. phrases.Where(p => p != shortcut).Take(max - 1)];
        }

        if (update.Letters < engine.Options.MinChars)
        {
            return phrases;
        }

        string[] dictionary;
        lock (_userWordsLock)
        {
            dictionary = [.. engine.Suggest(update.Word).Select(s => s.Word)];
        }

        return [.. phrases.Concat(dictionary).Distinct().Take(max)];
    }

    // True when no new request arrived within <paramref name="delay"/>.
    private async Task<bool> QuietForAsync(TimeSpan delay, CancellationToken cancellation)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(delay);
        try
        {
            await _requests.Reader.WaitToReadAsync(timeout.Token).ConfigureAwait(false);
            return false;
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
        {
            return true;
        }
    }

    // A few pixels are rounding; a real move is the caret on another character or side.
    private static bool Moved(Rectangle before, Rectangle after) =>
        Math.Abs(before.X - after.X) > 4 || Math.Abs(before.Y - after.Y) > 4;

    private void OnUi(Action action)
    {
        if (!_popup.IsDisposed && _popup.IsHandleCreated)
        {
            _popup.BeginInvoke(action);
        }
    }

    private readonly record struct Request(long Sequence, IReadOnlyList<string> Words, long Started, IReadOnlyDictionary<string, string> Sources, LanguageProfile Language);
}
