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

    public Task<string?> ReadBeforeCaretAsync(int maxChars, CancellationToken cancellation) =>
        worker.RunAsync(automation => Read(automation, maxChars), null, _timeout, cancellation);

    private string? Read(IUIAutomation automation, int maxChars)
    {
        var element = automation.GetFocusedElement();
        if (element is null
            || isExcludedProcess(ProcessNames.Of((uint)element.CurrentProcessId))
            || element.CurrentIsPassword)
        {
            return null;
        }

        var caret = UiaText.CaretRange(element);
        if (caret is null)
        {
            return null;
        }

        // Collapse a copy to the caret, then extend its start backwards by up to maxChars characters.
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
