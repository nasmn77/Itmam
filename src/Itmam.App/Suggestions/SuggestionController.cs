using System.Diagnostics;
using System.Threading.Channels;
using Itmam.Core.Input;
using Itmam.Core.Suggestions;
using Itmam.Win32;

namespace Itmam.App.Suggestions;

/// <summary>What was shown for a keystroke (for the debug window; contains no typed text).</summary>
internal readonly record struct SuggestionShown(int Count, CaretSource Source, TimeSpan Latency);

/// <summary>
/// Connects the input pipeline to the suggestion bar: on every word change it asks the engine for
/// suggestions (on the pipeline thread), then locates the caret and shows or hides the bar.
/// Only the most recent request is processed, so fast typing never queues stale bars.
/// Phase 3 shows suggestions only; accepting them arrives in Phase 4.
/// </summary>
internal sealed class SuggestionController : IDisposable
{
    private readonly SuggestionEngine _engine;
    private readonly CaretLocator _caret;
    private readonly ExcludedApps _excludedApps;
    private readonly SuggestionPopup _popup;
    private readonly Channel<Request> _requests = Channel.CreateBounded<Request>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true, SingleWriter = true });
    private readonly CancellationTokenSource _stop = new();
    private long _sequence;

    public SuggestionController(SuggestionEngine engine, CaretLocator caret, ExcludedApps excludedApps, SuggestionPopup popup)
    {
        _engine = engine;
        _caret = caret;
        _excludedApps = excludedApps;
        _popup = popup;
        _ = Task.Run(() => ProcessAsync(_stop.Token));
    }

    /// <summary>Raised on a worker thread after the bar was shown.</summary>
    public event Action<SuggestionShown>? Shown;

    /// <summary>Call from the pipeline thread for every update.</summary>
    public void OnPipelineUpdated(PipelineUpdate update)
    {
        var sequence = Interlocked.Increment(ref _sequence);
        IReadOnlyList<string> words = [];
        if (update is { IsArabicLayout: true, IsDetached: false }
            && update.Letters >= _engine.Options.MinChars
            && !_excludedApps.IsExcluded(ProcessNames.ForegroundProcessName()))
        {
            words = _engine.Suggest(update.Word).Select(s => s.Word).ToArray();
        }

        var started = update.Input?.Timestamp ?? Stopwatch.GetTimestamp();
        _requests.Writer.TryWrite(new Request(sequence, words, started));
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

                var caret = await _caret.LocateAsync(rightToLeft: true, cancellation).ConfigureAwait(false);
                if (request.Sequence != Volatile.Read(ref _sequence))
                {
                    continue; // a newer keystroke is already waiting
                }

                OnUi(() =>
                {
                    if (request.Sequence == Volatile.Read(ref _sequence))
                    {
                        _popup.ShowSuggestions(request.Words, caret.Bounds, rightToLeft: true, belowPointer: caret.Source == CaretSource.Mouse);
                        Shown?.Invoke(new SuggestionShown(request.Words.Count, caret.Source, Stopwatch.GetElapsedTime(request.Started)));
                    }
                });
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void OnUi(Action action)
    {
        if (!_popup.IsDisposed && _popup.IsHandleCreated)
        {
            _popup.BeginInvoke(action);
        }
    }

    private readonly record struct Request(long Sequence, IReadOnlyList<string> Words, long Started);
}
