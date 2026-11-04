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
        if (element is null || isExcludedProcess(ProcessNames.Of((uint)element.CurrentProcessId)))
        {
            return CaretContext.Unavailable;
        }

        // Password field: read nothing, and tell the pipeline to stay silent in this field.
        if (element.CurrentIsPassword)
        {
            return CaretContext.Password;
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

        // Read-only text (a web page clicked outside its fields, a list item): Chromium reports a caret at
        // the click point there, and the page's words were taken for the one being typed.
        if (UiaText.IsReadOnly(caret))
        {
            return CaretContext.ReadOnly;
        }

        // Collapse a copy to the caret, then extend its start backwards by up to maxChars characters.
        var before = caret.Clone();
        before.MoveEndpointByRange(TextPatternRangeEndpoint.TextPatternRangeEndpoint_Start, caret, TextPatternRangeEndpoint.TextPatternRangeEndpoint_End);
        before.MoveEndpointByRange(TextPatternRangeEndpoint.TextPatternRangeEndpoint_End, caret, TextPatternRangeEndpoint.TextPatternRangeEndpoint_End);
        before.MoveEndpointByUnit(TextPatternRangeEndpoint.TextPatternRangeEndpoint_Start, TextUnit.TextUnit_Character, -maxChars);

        // Never past the start of the focused field: in web pages (Chromium) the range moves on into the
        // text before it, e.g. the chat messages above WhatsApp Web's message box, which were then taken
        // for the word being typed.
        var document = element.GetCurrentPattern(UIA_PATTERN_ID.UIA_TextPatternId) is IUIAutomationTextPattern field
            ? field.DocumentRange
            : null;
        if (document is not null
            && before.CompareEndpoints(TextPatternRangeEndpoint.TextPatternRangeEndpoint_Start, document, TextPatternRangeEndpoint.TextPatternRangeEndpoint_Start) < 0)
        {
            before.MoveEndpointByRange(TextPatternRangeEndpoint.TextPatternRangeEndpoint_Start, document, TextPatternRangeEndpoint.TextPatternRangeEndpoint_Start);
        }

        return CaretContext.Around(ReadText(before, maxChars), TextAfter(caret, document));
    }

    // Up to CaretContext.MaxTextAfter characters after the caret, never past the end of the field (empty at
    // the end of the text): the pipeline keeps only the rest of the word the caret is in (accepting
    // replaces the whole word) and the character after it (whether accepting adds a space).
    private static string TextAfter(IUIAutomationTextRange caret, IUIAutomationTextRange? document)
    {
        var after = caret.Clone();
        after.MoveEndpointByRange(TextPatternRangeEndpoint.TextPatternRangeEndpoint_Start, caret, TextPatternRangeEndpoint.TextPatternRangeEndpoint_End);
        after.MoveEndpointByRange(TextPatternRangeEndpoint.TextPatternRangeEndpoint_End, caret, TextPatternRangeEndpoint.TextPatternRangeEndpoint_End);
        if (after.MoveEndpointByUnit(TextPatternRangeEndpoint.TextPatternRangeEndpoint_End, TextUnit.TextUnit_Character, CaretContext.MaxTextAfter) == 0)
        {
            return string.Empty;
        }

        if (document is not null
            && after.CompareEndpoints(TextPatternRangeEndpoint.TextPatternRangeEndpoint_End, document, TextPatternRangeEndpoint.TextPatternRangeEndpoint_End) > 0)
        {
            after.MoveEndpointByRange(TextPatternRangeEndpoint.TextPatternRangeEndpoint_End, document, TextPatternRangeEndpoint.TextPatternRangeEndpoint_End);
        }

        return ReadText(after, CaretContext.MaxTextAfter);
    }

    private static unsafe string ReadText(IUIAutomationTextRange range, int maxChars)
    {
        var text = range.GetText(maxChars);
        try
        {
            return text.ToString() ?? string.Empty;
        }
        finally
        {
            Marshal.FreeBSTR((nint)text.Value);
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

    /// <summary>
    /// True only when the app states that the text at the caret cannot be edited. Mixed values, apps that
    /// do not support the attribute and errors count as editable, so Itmam never goes silent by mistake.
    /// </summary>
    public static bool IsReadOnly(IUIAutomationTextRange caret)
    {
        try
        {
            return caret.GetAttributeValue(UIA_TEXTATTRIBUTE_ID.UIA_IsReadOnlyAttributeId) is true;
        }
        catch (COMException)
        {
            return false;
        }
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
