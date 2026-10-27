using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Itmam.Core.Input;
using Windows.Win32.UI.Accessibility;

namespace Itmam.Win32;

/// <summary>
/// Reads the characters immediately before the caret of the focused element with UI Automation
/// (TextPattern2 caret range, or the TextPattern selection). Used only to resume word tracking
/// after Backspace moves the caret into text we did not see typed.
/// </summary>
/// <remarks>
/// Privacy: at most <c>maxChars</c> characters are read, nothing is read from password fields,
/// and the text is returned to the caller without being stored or logged here.
/// UI Automation calls are cross-process and can hang on unresponsive apps, so they run on a
/// dedicated MTA thread, one request at a time, and the caller gets null after the timeout.
/// </remarks>
public sealed class UiaCaretContextSource : ICaretContextSource, IDisposable
{
    private readonly BlockingCollection<(int MaxChars, TaskCompletionSource<string?> Result)> _requests = new(boundedCapacity: 1);
    private readonly Thread _thread;
    private readonly TimeSpan _timeout;
    private readonly Func<string?, bool> _isExcludedProcess;
    private int _busy;

    /// <param name="isExcludedProcess">
    /// Checked against the process that owns the focused element right before reading; excluded
    /// apps are never read even if the foreground changed since the caller's own check.
    /// </param>
    /// <param name="timeout">How long the caller waits for UI Automation.</param>
    public UiaCaretContextSource(Func<string?, bool> isExcludedProcess, TimeSpan? timeout = null)
    {
        _isExcludedProcess = isExcludedProcess;
        _timeout = timeout ?? TimeSpan.FromMilliseconds(250);
        _thread = new Thread(Run) { IsBackground = true, Name = "Itmam UI Automation" };
        _thread.SetApartmentState(ApartmentState.MTA);
        _thread.Start();
    }

    public async Task<string?> ReadBeforeCaretAsync(int maxChars, CancellationToken cancellation)
    {
        // A previous request is still stuck in an unresponsive app: do not queue behind it.
        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
        {
            return null;
        }

        var result = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_requests.TryAdd((maxChars, result)))
        {
            Volatile.Write(ref _busy, 0);
            return null;
        }

        try
        {
            return await result.Task.WaitAsync(_timeout, cancellation).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            return null;
        }
    }

    public void Dispose() => _requests.CompleteAdding();

    private void Run()
    {
        IUIAutomation? automation = null;
        foreach (var (maxChars, result) in _requests.GetConsumingEnumerable())
        {
            try
            {
                automation ??= (IUIAutomation)new CUIAutomation8();
                result.TrySetResult(Read(automation, maxChars, _isExcludedProcess));
            }
            catch (Exception ex) when (ex is COMException or InvalidCastException or UnauthorizedAccessException)
            {
                result.TrySetResult(null); // app without UI Automation support, elevated app, element gone…
            }
            finally
            {
                Volatile.Write(ref _busy, 0);
            }
        }
    }

    private static unsafe string? Read(IUIAutomation automation, int maxChars, Func<string?, bool> isExcludedProcess)
    {
        var element = automation.GetFocusedElement();
        if (element is null
            || isExcludedProcess(ProcessNames.Of((uint)element.CurrentProcessId))
            || element.CurrentIsPassword)
        {
            return null;
        }

        var caret = CaretRange(element);
        if (caret is null)
        {
            return null;
        }

        // Extend the start of the (degenerate) caret range backwards by up to maxChars characters.
        var before = caret.Clone();
        before.MoveEndpointByRange(TextPatternRangeEndpoint.TextPatternRangeEndpoint_Start, caret, TextPatternRangeEndpoint.TextPatternRangeEndpoint_End);
        before.MoveEndpointByRange(TextPatternRangeEndpoint.TextPatternRangeEndpoint_End, caret, TextPatternRangeEndpoint.TextPatternRangeEndpoint_End);
        before.MoveEndpointByUnit(TextPatternRangeEndpoint.TextPatternRangeEndpoint_Start, TextUnit.TextUnit_Character, -maxChars);

        var text = before.GetText(maxChars);
        try
        {
            return text.ToString() ?? string.Empty;
        }
        finally
        {
            Marshal.FreeBSTR((nint)text.Value);
        }
    }

    private static unsafe IUIAutomationTextRange? CaretRange(IUIAutomationElement element)
    {
        if (element.GetCurrentPattern(UIA_PATTERN_ID.UIA_TextPattern2Id) is IUIAutomationTextPattern2 pattern2)
        {
            Windows.Win32.Foundation.BOOL isActive;
            var range = pattern2.GetCaretRange(&isActive);
            if (range is not null)
            {
                return range;
            }
        }

        // Fallback: the selection; after a Backspace it is empty, i.e. exactly the caret.
        if (element.GetCurrentPattern(UIA_PATTERN_ID.UIA_TextPatternId) is IUIAutomationTextPattern pattern)
        {
            var selection = pattern.GetSelection();
            if (selection is not null && selection.Length > 0)
            {
                return selection.GetElement(0);
            }
        }

        return null;
    }
}
