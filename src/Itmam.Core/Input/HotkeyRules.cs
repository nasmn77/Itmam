namespace Itmam.Core.Input;

/// <summary>The configurable shortcuts (Settings → Shortcuts).</summary>
public enum HotkeyAction
{
    AcceptFirst,
    Dismiss,
    ToggleEnabled,
    OpenLongPhrases,
}

/// <summary>Why a shortcut cannot be used.</summary>
public enum HotkeyProblem
{
    None,

    /// <summary>Already used by another Itmam action.</summary>
    Duplicate,

    /// <summary>Without Ctrl/Alt/Win the key types text (letters, digits, punctuation, space).</summary>
    TypesText,

    /// <summary>An editing or navigation key (arrows, Backspace, Delete, Home…) that must keep working.</summary>
    EditingKey,

    /// <summary>The global on/off shortcut is swallowed everywhere: it needs Ctrl or Alt.</summary>
    NeedsModifier,

    /// <summary>A common Windows/app shortcut (Ctrl+C, Alt+Tab, Alt+F4…).</summary>
    CommonShortcut,

    /// <summary>Same as "accept suggestion N" (the modifier held with a digit 1–9).</summary>
    AcceptsNthSuggestion,

    /// <summary>Modifiers alone: only the global toggle can be Ctrl+Alt without another key.</summary>
    ModifiersOnly,
}

/// <summary>
/// Checks a shortcut before it is saved, so a new shortcut never breaks typing, editing, common
/// shortcuts or another Itmam action. Pure logic, unit-tested.
/// </summary>
public static class HotkeyRules
{
    private const int Tab = 0x09;
    private const int Enter = 0x0D;
    private const int Escape = 0x1B;
    private const int Space = 0x20;
    private const int F4 = 0x73;

    // Ctrl + these letters are used by nearly every app (select all, copy, paste, cut, undo, redo, save…).
    private static readonly HashSet<int> CommonCtrlLetters = [.. "ACVXZYSOPNFWT".Select(c => (int)c)];

    /// <summary>Modifier key codes (Shift/Ctrl/Alt/Win, left/right): never a shortcut by themselves.</summary>
    public static bool IsModifierKey(int virtualKey) => ModifierOf(virtualKey) != KeyModifiers.None;

    /// <summary>The modifier a modifier key sets (Shift/Ctrl/Alt/Win, left or right); None for other keys.</summary>
    public static KeyModifiers ModifierOf(int virtualKey) => virtualKey switch
    {
        0x10 or 0xA0 or 0xA1 => KeyModifiers.Shift,
        0x11 or 0xA2 or 0xA3 => KeyModifiers.Ctrl,
        0x12 or 0xA4 or 0xA5 => KeyModifiers.Alt,
        0x5B or 0x5C => KeyModifiers.Win,
        _ => KeyModifiers.None,
    };

    /// <summary>Whether <paramref name="candidate"/> can be used for <paramref name="action"/>.</summary>
    public static HotkeyProblem Check(HotkeySettings settings, HotkeyAction action, Hotkey candidate)
    {
        var key = candidate.VirtualKey;
        var mods = candidate.Modifiers;
        var withCommand = (mods & (KeyModifiers.Ctrl | KeyModifiers.Alt | KeyModifiers.Win)) != 0;

        // Shortcuts that work everywhere need Ctrl or Alt, so they never collide with typing.
        if (action is HotkeyAction.ToggleEnabled or HotkeyAction.OpenLongPhrases && (mods & (KeyModifiers.Ctrl | KeyModifiers.Alt)) == 0)
        {
            return HotkeyProblem.NeedsModifier;
        }

        // Modifiers alone (pressed and released): only for the global toggle, and only Ctrl+Alt
        // (+Shift); Ctrl+Shift / Alt+Shift switch the keyboard layout, Win opens Start.
        if (key == 0)
        {
            if (action != HotkeyAction.ToggleEnabled)
            {
                return HotkeyProblem.ModifiersOnly;
            }

            return (mods & (KeyModifiers.Ctrl | KeyModifiers.Alt)) == (KeyModifiers.Ctrl | KeyModifiers.Alt) && !mods.HasFlag(KeyModifiers.Win)
                ? HotkeyProblem.None
                : HotkeyProblem.CommonShortcut;
        }

        if (!withCommand && TypesText(key))
        {
            return HotkeyProblem.TypesText;
        }

        if (!withCommand && IsEditingKey(key))
        {
            return HotkeyProblem.EditingKey;
        }

        if (IsCommonShortcut(key, mods))
        {
            return HotkeyProblem.CommonShortcut;
        }

        if (mods == settings.AcceptNthModifier && key is >= '1' and <= '9')
        {
            return HotkeyProblem.AcceptsNthSuggestion;
        }

        foreach (var (other, hotkey) in Assigned(settings))
        {
            if (other != action && hotkey == candidate)
            {
                return HotkeyProblem.Duplicate;
            }
        }

        return HotkeyProblem.None;
    }

    /// <summary>Whether the "accept suggestion N" modifier clashes with an assigned shortcut.</summary>
    public static HotkeyProblem CheckAcceptNthModifier(HotkeySettings settings, KeyModifiers modifier)
    {
        if ((modifier & (KeyModifiers.Ctrl | KeyModifiers.Alt)) == 0)
        {
            return HotkeyProblem.TypesText; // Shift+digit or a bare digit types text
        }

        return Assigned(settings).Any(a => a.Hotkey.Modifiers == modifier && a.Hotkey.VirtualKey is >= '1' and <= '9')
            ? HotkeyProblem.Duplicate
            : HotkeyProblem.None;
    }

    public static Hotkey Get(HotkeySettings settings, HotkeyAction action) => action switch
    {
        HotkeyAction.AcceptFirst => settings.AcceptFirst,
        HotkeyAction.Dismiss => settings.Dismiss,
        HotkeyAction.OpenLongPhrases => settings.OpenLongPhrases,
        _ => settings.ToggleEnabled,
    };

    public static HotkeySettings With(HotkeySettings settings, HotkeyAction action, Hotkey hotkey) => action switch
    {
        HotkeyAction.AcceptFirst => settings with { AcceptFirst = hotkey },
        HotkeyAction.Dismiss => settings with { Dismiss = hotkey },
        HotkeyAction.OpenLongPhrases => settings with { OpenLongPhrases = hotkey },
        _ => settings with { ToggleEnabled = hotkey },
    };

    private static IEnumerable<(HotkeyAction Action, Hotkey Hotkey)> Assigned(HotkeySettings settings) =>
    [
        (HotkeyAction.AcceptFirst, settings.AcceptFirst),
        (HotkeyAction.Dismiss, settings.Dismiss),
        (HotkeyAction.ToggleEnabled, settings.ToggleEnabled),
        (HotkeyAction.OpenLongPhrases, settings.OpenLongPhrases),
    ];

    // Letters, digits, space, OEM punctuation, numpad digits/operators (Shift alone still types), and
    // VK_PROCESSKEY (IME) / VK_PACKET (Unicode text sent by tools), which carry text, not a key.
    private static bool TypesText(int key) =>
        key is Space or 0xE5 or 0xE7 or (>= '0' and <= '9') or (>= 'A' and <= 'Z') or (>= 0x60 and <= 0x6F) or (>= 0xBA and <= 0xC0) or (>= 0xDB and <= 0xDF) or 0xE2;

    // Backspace, Page Up/Down, End, Home, arrows, Insert, Delete.
    private static bool IsEditingKey(int key) => key is 0x08 or (>= 0x21 and <= 0x28) or 0x2D or 0x2E;

    private static bool IsCommonShortcut(int key, KeyModifiers mods) =>
        (mods == KeyModifiers.Ctrl && CommonCtrlLetters.Contains(key))
        || (mods.HasFlag(KeyModifiers.Alt) && key is Tab or F4 or Escape)
        || (mods.HasFlag(KeyModifiers.Ctrl) && key == Escape)
        || ((mods & KeyModifiers.Win) != 0)
        || (mods == KeyModifiers.Shift && key is Tab or Enter);
}
