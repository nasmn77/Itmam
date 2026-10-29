using Itmam.Core.Presentation;

namespace Itmam.Core.Input;

/// <summary>A shortcut. <see cref="VirtualKey"/> 0 means the modifiers alone, pressed and released (e.g. Ctrl+Alt).</summary>
public readonly record struct Hotkey(int VirtualKey, KeyModifiers Modifiers)
{
    public bool IsModifiersOnly => VirtualKey == 0 && Modifiers != KeyModifiers.None;
}

/// <summary>Keys that act on the suggestion bar (configurable in Settings, Phase 6).</summary>
public sealed record HotkeySettings
{
    /// <summary>Accept the first (best) suggestion. Default Tab.</summary>
    public Hotkey AcceptFirst { get; init; } = new(0x09, KeyModifiers.None);

    /// <summary>Held with a digit 1–9 to accept suggestion N. Default Ctrl.</summary>
    public KeyModifiers AcceptNthModifier { get; init; } = KeyModifiers.Ctrl;

    /// <summary>Hide the bar until the current word ends. Default Esc.</summary>
    public Hotkey Dismiss { get; init; } = new(0x1B, KeyModifiers.None);

    /// <summary>
    /// Enable/disable Itmam from anywhere. Default Ctrl+Alt pressed and released on its own (user
    /// decision: no letter, so it works with any keyboard layout; Ctrl+Alt+P did not work with Arabic).
    /// </summary>
    public Hotkey ToggleEnabled { get; init; } = new(0, KeyModifiers.Ctrl | KeyModifiers.Alt);

    /// <summary>
    /// On by default (user decision, like the native Windows suggestions for English). While the
    /// bar is shown, Up moves into it, Left/Right select, Enter inserts, Esc/Down leave.
    /// </summary>
    public bool NativeNavigation { get; init; } = true;
}

public enum InterceptAction
{
    /// <summary>Let the key reach the app (normal processing continues).</summary>
    Pass,

    /// <summary>Swallow the key: the app never sees it.</summary>
    Swallow,
}

/// <param name="Action">Whether the key reaches the app.</param>
/// <param name="Command">Event to publish for the pipeline, if any (also with <see cref="InterceptAction.Pass"/>).</param>
public readonly record struct InterceptDecision(InterceptAction Action, InputEvent? Command = null)
{
    public static InterceptDecision Pass => new(InterceptAction.Pass);

    public static InterceptDecision Swallow(InputEvent command) => new(InterceptAction.Swallow, command);
}

/// <summary>
/// Decides, inside the keyboard hook, whether a key-down is a suggestion-bar hotkey. Keys are only
/// intercepted while the bar is visible (except the global toggle); otherwise they pass untouched.
/// Pure logic plus the native-navigation selection state, owned by the hook thread.
/// </summary>
public sealed class HotkeyInterceptor(HotkeySettings settings)
{
    private const int Backspace = 0x08;
    private const int Enter = 0x0D;
    private const int Escape = 0x1B;
    private const int Left = 0x25;
    private const int Up = 0x26;
    private const int Right = 0x27;
    private const int Down = 0x28;
    private const int Delete = 0x2E;
    private const int Digit1 = 0x31;
    private const int Digit9 = 0x39;

    private PopupSnapshot? _navigatedSnapshot;
    private bool _chordArmed;

    public HotkeySettings Settings { get; } = settings;

    /// <summary>Selected item in native navigation mode, or -1 when not navigating.</summary>
    public int Selected { get; private set; } = -1;

    /// <param name="virtualKey">The key pressed.</param>
    /// <param name="modifiers">Modifiers held.</param>
    /// <param name="popup">What the bar shows now.</param>
    /// <param name="canUndoAccept">A suggestion was accepted just before: Backspace undoes it.</param>
    public InterceptDecision OnKeyDown(int virtualKey, KeyModifiers modifiers, PopupSnapshot popup, bool canUndoAccept = false)
    {
        // Modifier keys never act on the bar (Ctrl+Delete while navigating starts with Ctrl);
        // they only arm a modifiers-only toggle once exactly its modifiers are held.
        var own = HotkeyRules.ModifierOf(virtualKey);
        if (own != KeyModifiers.None)
        {
            var held = modifiers | own;
            var toggle = Settings.ToggleEnabled;
            if (!toggle.IsModifiersOnly || (held & ~toggle.Modifiers) != 0)
            {
                _chordArmed = false;
            }
            else if (held == toggle.Modifiers)
            {
                _chordArmed = true;
            }

            return InterceptDecision.Pass;
        }

        _chordArmed = false; // Ctrl+Alt+Del, Ctrl+Alt+arrows, AltGr+letter… are not the toggle

        if (Matches(Settings.ToggleEnabled, virtualKey, modifiers))
        {
            Selected = -1;
            return InterceptDecision.Swallow(InputEvent.Of(InputEventKind.ToggleEnabled));
        }

        if (canUndoAccept && virtualKey == Backspace && modifiers == KeyModifiers.None)
        {
            Selected = -1;
            return InterceptDecision.Swallow(InputEvent.Of(InputEventKind.UndoAccept));
        }

        // Navigation belongs to one bar: a new or hidden bar ends it.
        if (!popup.IsVisible || !ReferenceEquals(popup, _navigatedSnapshot))
        {
            Selected = -1;
        }

        if (!popup.IsVisible)
        {
            return InterceptDecision.Pass;
        }

        if (Selected >= 0)
        {
            return Navigate(virtualKey, modifiers, popup);
        }

        if (Settings.NativeNavigation && virtualKey == Up && modifiers == KeyModifiers.None)
        {
            _navigatedSnapshot = popup;
            Selected = 0;
            return InterceptDecision.Swallow(InputEvent.Select(0));
        }

        if (Matches(Settings.AcceptFirst, virtualKey, modifiers))
        {
            return InterceptDecision.Swallow(InputEvent.Accept(0, popup.Words[0]));
        }

        if (modifiers == Settings.AcceptNthModifier && virtualKey is >= Digit1 and <= Digit9)
        {
            var index = virtualKey - Digit1;
            return index < popup.Words.Count
                ? InterceptDecision.Swallow(InputEvent.Accept(index, popup.Words[index]))
                : InterceptDecision.Pass;
        }

        if (Matches(Settings.Dismiss, virtualKey, modifiers))
        {
            return InterceptDecision.Swallow(InputEvent.Of(InputEventKind.DismissSuggestions));
        }

        return InterceptDecision.Pass;
    }

    /// <summary>
    /// Key released. True when a modifiers-only toggle (e.g. Ctrl+Alt) was pressed and is now being
    /// released without another key in between: toggle Itmam. The key itself is never swallowed.
    /// </summary>
    public bool OnKeyUp(int virtualKey)
    {
        if (_chordArmed && (HotkeyRules.ModifierOf(virtualKey) & Settings.ToggleEnabled.Modifiers) != 0)
        {
            _chordArmed = false;
            Selected = -1;
            return true;
        }

        return false;
    }

    /// <summary>A mouse click while the modifiers are held (Ctrl+Alt+click) is not the toggle.</summary>
    public void CancelChord() => _chordArmed = false;

    // Right-to-left bar: the best suggestion is on the right, so Left moves to the next one.
    private InterceptDecision Navigate(int virtualKey, KeyModifiers modifiers, PopupSnapshot popup)
    {
        // Ctrl+Delete on the selected suggestion: forget it as a learned word.
        if (modifiers == KeyModifiers.Ctrl && virtualKey == Delete)
        {
            var forgotten = Selected;
            Selected = -1;
            return InterceptDecision.Swallow(InputEvent.Forget(forgotten, popup.Words[forgotten]));
        }

        if (modifiers != KeyModifiers.None)
        {
            return Leave(InterceptAction.Pass);
        }

        switch (virtualKey)
        {
            case Left:
                Selected = Math.Min(popup.Words.Count - 1, Selected + 1);
                return InterceptDecision.Swallow(InputEvent.Select(Selected));
            case Right:
                Selected = Math.Max(0, Selected - 1);
                return InterceptDecision.Swallow(InputEvent.Select(Selected));
            case Enter:
            case 0x09 when Settings.AcceptFirst.VirtualKey == 0x09:
                var index = Selected;
                Selected = -1;
                return InterceptDecision.Swallow(InputEvent.Accept(index, popup.Words[index]));
            case Escape:
            case Down:
                return Leave(InterceptAction.Swallow);
            default:
                return Leave(InterceptAction.Pass); // typing continues normally
        }
    }

    private InterceptDecision Leave(InterceptAction action)
    {
        Selected = -1;
        return new InterceptDecision(action, InputEvent.Select(-1));
    }

    private static bool Matches(Hotkey hotkey, int virtualKey, KeyModifiers modifiers) =>
        hotkey.VirtualKey == virtualKey && hotkey.Modifiers == modifiers;
}
