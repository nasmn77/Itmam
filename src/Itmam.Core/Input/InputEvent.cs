namespace Itmam.Core.Input;

/// <summary>Abstract input events fed to <see cref="WordBuffer"/>. The Win32 layer translates real keys into these.</summary>
public enum InputEventKind
{
    /// <summary>A translated character (letter, mark, digit, punctuation, whitespace).</summary>
    Character,

    Backspace,

    /// <summary>Ctrl+Backspace (delete previous word).</summary>
    CtrlBackspace,

    /// <summary>Arrows, Home/End, PageUp/PageDown, Delete.</summary>
    Navigation,

    Escape,

    /// <summary>Any Ctrl/Alt/Win shortcut.</summary>
    Shortcut,

    Paste,

    FocusChanged,

    ForegroundChanged,

    MouseClick,

    LayoutChanged,

    /// <summary>A shown suggestion was accepted (Tab, Ctrl+N, Enter in navigation, or a click); <see cref="InputEvent.Text"/> is the word.</summary>
    AcceptSuggestion,

    /// <summary>Esc while the bar is shown: hide it until the current word ends.</summary>
    DismissSuggestions,

    /// <summary>Native navigation mode moved the selection; <see cref="InputEvent.Index"/> is the item, -1 when leaving the bar.</summary>
    NavigationSelect,

    /// <summary>The global enable/disable hotkey was pressed.</summary>
    ToggleEnabled,
}

/// <param name="Kind">What happened.</param>
/// <param name="Character">The character for <see cref="InputEventKind.Character"/>.</param>
/// <param name="Index">Suggestion index for accept/navigation events.</param>
/// <param name="Text">The accepted word for <see cref="InputEventKind.AcceptSuggestion"/> (exactly what was shown).</param>
public readonly record struct InputEvent(InputEventKind Kind, char Character = '\0', int Index = 0, string? Text = null)
{
    public static InputEvent Char(char c) => new(InputEventKind.Character, c);

    public static InputEvent Of(InputEventKind kind) => new(kind);

    public static InputEvent Accept(int index, string word) => new(InputEventKind.AcceptSuggestion, Index: index, Text: word);

    public static InputEvent Select(int index) => new(InputEventKind.NavigationSelect, Index: index);
}
