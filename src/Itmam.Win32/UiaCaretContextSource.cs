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
/// Privacy: at most <c>maxChars</c> characters are read, nothing is read from password fields or
/// excluded apps, and the text is returned to the caller without being stored or logged here.
/// </remarks>
/// <param name="worker">The shared UI Automation thread.</param>
/// <param name="isExcludedProcess">
/// Checked against the process that owns the focused element right before reading; excluded
/// apps are never read even if the foreground changed since the caller's own check.
/// </param>
/// <param name="timeout">How long the caller waits for UI Automation.</param>
public sealed class UiaCaretContextSource(UiaWorker worker, Func<string?, bool> isExcludedProcess, TimeSpan? timeout = null)
    : ICaretContextSource
{
    private readonly TimeSpan _timeout = timeout ?? TimeSpan.FromMilliseconds(250);

    public Task<CaretContext> ReadBeforeCaretAsync(int maxChars, CancellationToken cancellation) =>
        worker.RunAsync(automation => Read(automation, maxChars), CaretContext.Unavailable, _timeout, cancellation);

    private CaretContext Read(IUIAutomation automation, int maxChars)
    {
        var element = automation.GetFocusedElement();
        if (element is null
            || isExcludedProcess(ProcessNames.Of((uint)element.CurrentProcessId))
            || element.CurrentIsPassword)
        {
            return CaretContext.Unavailable;
        }

        // Selected text (e.g. dragged with the mouse) is replaced by typing: no word to continue.
        if (UiaText.HasSelection(element))
        {
            return CaretContext.Selection;
        }

        var caret = UiaText.CaretRange(element);
        if (caret is null)
        {
            return CaretContext.Unavailable;
        }

        // Collapse a copy to the caret, then extend its start backwards by up to maxChars characters.
        var before = caret.Clone();
        before.MoveEndpointByRange(TextPatternRangeEndpoint.TextPatternRangeEndpoint_Start, caret, TextPatternRangeEndpoint.TextPatternRangeEndpoint_End);
        before.MoveEndpointByRange(TextPatternRangeEndpoint.TextPatternRangeEndpoint_End, caret, TextPatternRangeEndpoint.TextPatternRangeEndpoint_End);
        before.MoveEndpointByUnit(TextPatternRangeEndpoint.TextPatternRangeEndpoint_Start, TextUnit.TextUnit_Character, -maxChars);

        var text = before.GetText(maxChars);
        try
        {
            return CaretContext.Of(text.ToString() ?? string.Empty);
        }
        finally
        {
            unsafe
            {
                Marshal.FreeBSTR((nint)text.Value);
            }
        }
    }
}

/// <summary>Shared UI Automation text helpers.</summary>
internal static class UiaText
{
    /// <summary>True when the focused text has a non-empty selection.</summary>
    public static bool HasSelection(IUIAutomationElement element)
    {
        if (element.GetCurrentPattern(UIA_PATTERN_ID.UIA_TextPatternId) is not IUIAutomationTextPattern pattern)
        {
            return false;
        }

        var selection = pattern.GetSelection();
        if (selection is null || selection.Length == 0)
        {
            return false;
        }

        var range = selection.GetElement(0);
        return range.CompareEndpoints(TextPatternRangeEndpoint.TextPatternRangeEndpoint_Start, range, TextPatternRangeEndpoint.TextPatternRangeEndpoint_End) != 0;
    }

    /// <summary>Degenerate range at the caret: TextPattern2 caret range, or the TextPattern selection.</summary>
    public static unsafe IUIAutomationTextRange? CaretRange(IUIAutomationElement element)
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
