namespace Itmam.Core.Input;

[Flags]
public enum KeyModifiers
{
    None = 0,
    Shift = 1,
    Ctrl = 2,
    Alt = 4,
    Win = 8,
}

public enum KeyAction
{
    /// <summary>Modifier or lock key: no effect on the word.</summary>
    Ignore,

    /// <summary>Translate with the keyboard layout; a produced character becomes a Character event.</summary>
    Translate,

    /// <summary>Emit <see cref="KeyClassification.Kind"/> directly.</summary>
    Event,
}

public readonly record struct KeyClassification(KeyAction Action, InputEventKind Kind = InputEventKind.Character);

/// <summary>
/// Maps a key-down (virtual-key code + modifiers) to what it means for the word being typed.
/// Pure logic so the hook callback stays trivial and the rules are unit-tested.
/// </summary>
public static class KeyClassifier
{
    private const int Backspace = 0x08;
    private const int Escape = 0x1B;
    private const int PageUp = 0x21;
    private const int Down = 0x28;
    private const int Insert = 0x2D;
    private const int Delete = 0x2E;
    private const int KeyV = 0x56;
    private const int KeyA = 0x41;
    private const int ProcessKey = 0xE5; // IME is processing the key
    private const int Packet = 0xE7;     // Unicode character injected as a key

    public static KeyClassification Classify(int virtualKey, KeyModifiers modifiers)
    {
        if (IsModifierOrLock(virtualKey))
        {
            return new(KeyAction.Ignore);
        }

        var ctrl = modifiers.HasFlag(KeyModifiers.Ctrl);
        var alt = modifiers.HasFlag(KeyModifiers.Alt);

        // Ctrl+Alt is AltGr on many layouts and can type characters.
        var altGr = ctrl && alt;

        if (virtualKey == Backspace)
        {
            return ctrl && !alt ? Event(InputEventKind.CtrlBackspace)
                : alt && !ctrl ? Event(InputEventKind.Shortcut) // Alt+Backspace is undo
                : Event(InputEventKind.Backspace);
        }

        if ((ctrl && !alt && virtualKey == KeyV) || (modifiers == KeyModifiers.Shift && virtualKey == Insert))
        {
            return Event(InputEventKind.Paste);
        }

        // Selecting text: typing would replace the selection, so no word is tracked or suggested.
        var shiftOrCtrlShift = modifiers is KeyModifiers.Shift or (KeyModifiers.Shift | KeyModifiers.Ctrl);
        if ((shiftOrCtrlShift && virtualKey is >= PageUp and <= Down) || (modifiers == KeyModifiers.Ctrl && virtualKey == KeyA))
        {
            return Event(InputEventKind.Selection);
        }

        if (modifiers.HasFlag(KeyModifiers.Win) || ((ctrl || alt) && !altGr))
        {
            return Event(InputEventKind.Shortcut);
        }

        if (virtualKey == Escape)
        {
            return Event(InputEventKind.Escape);
        }

        if (virtualKey is >= PageUp and <= Down or Insert or Delete or ProcessKey or Packet)
        {
            return Event(InputEventKind.Navigation);
        }

        return new(KeyAction.Translate);
    }

    private static KeyClassification Event(InputEventKind kind) => new(KeyAction.Event, kind);

    private static bool IsModifierOrLock(int vk) => vk is
        0x10 or 0x11 or 0x12            // Shift, Ctrl, Alt
        or (>= 0xA0 and <= 0xA5)        // left/right Shift, Ctrl, Alt
        or 0x5B or 0x5C                 // Windows keys
        or 0x14 or 0x90 or 0x91;        // Caps, Num, Scroll lock
}
