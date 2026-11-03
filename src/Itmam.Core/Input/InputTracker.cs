using Itmam.Core.Languages;

namespace Itmam.Core.Input;

/// <summary>
/// Feeds input events into a <see cref="WordBuffer"/> while honouring the keyboard layout: a layout
/// switch resets the word, the buffer follows the layout's language, and nothing is tracked while the
/// layout's language is unsupported (input method editors …) or not turned on.
/// Not thread-safe; owned by the single input worker (<see cref="EnabledLanguages"/> may be replaced from any thread).
/// </summary>
public sealed class InputTracker
{
    private nint? _layout;
    private LanguageProfile? _layoutLanguage;
    private volatile IReadOnlySet<string> _enabledLanguages = new HashSet<string>(StringComparer.Ordinal) { LanguageProfile.Arabic.Id };

    public WordBuffer Buffer { get; } = new();

    /// <summary>Layout of the most recent event.</summary>
    public nint Layout => _layout ?? 0;

    /// <summary>The language being typed and tracked, or null when nothing is tracked with this layout.</summary>
    public LanguageProfile? Language => _layoutLanguage is { } language && _enabledLanguages.Contains(language.Id) ? language : null;

    /// <summary>Tags of the languages turned on in Settings (default: Arabic only).</summary>
    public IReadOnlySet<string> EnabledLanguages
    {
        get => _enabledLanguages;
        set => _enabledLanguages = value;
    }

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
            _layoutLanguage = LanguageRegistry.ForLayout(keyboardLayout);
            Buffer.Language = _layoutLanguage ?? LanguageProfile.Arabic;
        }

        // Silent with an unsupported or turned-off language: its typing is never tracked.
        var result = Language is not null ? Buffer.Apply(input) : Buffer.Reset();
        return result.Outcome == BufferOutcome.None ? layoutReset : result;
    }
}
