namespace Itmam.Core.Input;

/// <summary>
/// Feeds input events into a <see cref="WordBuffer"/> while honouring the keyboard layout:
/// a layout switch resets the word, and nothing is tracked while the layout is not Arabic.
/// Not thread-safe; owned by the single input worker.
/// </summary>
public sealed class InputTracker
{
    private nint? _layout;

    public WordBuffer Buffer { get; } = new();

    /// <summary>Layout of the most recent event.</summary>
    public nint Layout => _layout ?? 0;

    public bool IsArabicLayout { get; private set; }

    /// <param name="input">The event.</param>
    /// <param name="keyboardLayout">
    /// Layout (HKL) of the foreground window when the event happened; 0 = unknown (events raised
    /// outside typing, e.g. Itmam turned back on), which keeps the current layout.
    /// </param>
    public BufferResult Process(InputEvent input, nint keyboardLayout)
    {
        var layoutReset = BufferResult.None;
        if (keyboardLayout != 0 && _layout != keyboardLayout)
        {
            if (_layout is not null)
            {
                layoutReset = Buffer.Apply(InputEvent.Of(InputEventKind.LayoutChanged));
            }

            _layout = keyboardLayout;
            IsArabicLayout = KeyboardLayoutId.IsArabic(keyboardLayout);
        }

        // Silent with any other layout: English typing is never tracked.
        var result = IsArabicLayout ? Buffer.Apply(input) : Buffer.Reset();
        return result.Outcome == BufferOutcome.None ? layoutReset : result;
    }
}
