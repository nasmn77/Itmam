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
}

public readonly record struct InputEvent(InputEventKind Kind, char Character = '\0')
{
    public static InputEvent Char(char c) => new(InputEventKind.Character, c);

    public static InputEvent Of(InputEventKind kind) => new(kind);
}
