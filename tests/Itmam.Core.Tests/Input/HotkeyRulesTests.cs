using Itmam.Core.Input;

namespace Itmam.Core.Tests.Input;

public class HotkeyRulesTests
{
    private static readonly HotkeySettings Defaults = new();

    [Fact]
    public void Defaults_are_valid()
    {
        foreach (var action in Enum.GetValues<HotkeyAction>())
        {
            HotkeyRules.Check(Defaults, action, HotkeyRules.Get(Defaults, action)).ShouldBe(HotkeyProblem.None, action.ToString());
        }

        HotkeyRules.CheckAcceptNthModifier(Defaults, Defaults.AcceptNthModifier).ShouldBe(HotkeyProblem.None);
    }

    [Theory]
    [InlineData('A', KeyModifiers.None)]
    [InlineData('A', KeyModifiers.Shift)]
    [InlineData('5', KeyModifiers.None)]
    [InlineData(0x20, KeyModifiers.None)]   // Space
    [InlineData(0xBE, KeyModifiers.None)]   // . (OEM period)
    [InlineData(0x61, KeyModifiers.None)]   // numpad 1
    [InlineData(0xE7, KeyModifiers.None)]   // VK_PACKET
    [InlineData(0xE5, KeyModifiers.None)]   // VK_PROCESSKEY (IME)
    public void Keys_that_type_text_are_refused(int key, KeyModifiers mods) =>
        HotkeyRules.Check(Defaults, HotkeyAction.AcceptFirst, new(key, mods)).ShouldBe(HotkeyProblem.TypesText);

    [Theory]
    [InlineData(0x08)]   // Backspace
    [InlineData(0x25)]   // Left
    [InlineData(0x28)]   // Down
    [InlineData(0x24)]   // Home
    [InlineData(0x2E)]   // Delete
    public void Editing_keys_are_refused(int key) =>
        HotkeyRules.Check(Defaults, HotkeyAction.Dismiss, new(key, KeyModifiers.None)).ShouldBe(HotkeyProblem.EditingKey);

    [Theory]
    [InlineData('C', KeyModifiers.Ctrl)]
    [InlineData('V', KeyModifiers.Ctrl)]
    [InlineData('Z', KeyModifiers.Ctrl)]
    [InlineData(0x09, KeyModifiers.Alt)]   // Alt+Tab
    [InlineData(0x73, KeyModifiers.Alt)]   // Alt+F4
    [InlineData(0x1B, KeyModifiers.Ctrl)]  // Ctrl+Esc
    [InlineData('E', KeyModifiers.Win)]
    [InlineData(0x09, KeyModifiers.Shift)] // Shift+Tab
    public void Common_shortcuts_are_refused(int key, KeyModifiers mods) =>
        HotkeyRules.Check(Defaults, HotkeyAction.AcceptFirst, new(key, mods)).ShouldBe(HotkeyProblem.CommonShortcut);

    [Fact]
    public void The_global_toggle_needs_ctrl_or_alt()
    {
        HotkeyRules.Check(Defaults, HotkeyAction.ToggleEnabled, new(0x7B, KeyModifiers.None)).ShouldBe(HotkeyProblem.NeedsModifier);
        HotkeyRules.Check(Defaults, HotkeyAction.ToggleEnabled, new(0x7B, KeyModifiers.Shift)).ShouldBe(HotkeyProblem.NeedsModifier);
        HotkeyRules.Check(Defaults, HotkeyAction.ToggleEnabled, new('I', KeyModifiers.Ctrl | KeyModifiers.Alt)).ShouldBe(HotkeyProblem.None);
    }

    [Fact]
    public void Modifiers_alone_are_only_for_the_toggle_and_only_ctrl_alt()
    {
        var ctrlAlt = new Hotkey(0, KeyModifiers.Ctrl | KeyModifiers.Alt);

        Defaults.ToggleEnabled.ShouldBe(ctrlAlt);
        HotkeyRules.Check(Defaults, HotkeyAction.ToggleEnabled, ctrlAlt).ShouldBe(HotkeyProblem.None);
        HotkeyRules.Check(Defaults, HotkeyAction.ToggleEnabled, new(0, KeyModifiers.Ctrl | KeyModifiers.Alt | KeyModifiers.Shift)).ShouldBe(HotkeyProblem.None);
        HotkeyRules.Check(Defaults, HotkeyAction.ToggleEnabled, new(0, KeyModifiers.Ctrl | KeyModifiers.Shift)).ShouldBe(HotkeyProblem.CommonShortcut);
        HotkeyRules.Check(Defaults, HotkeyAction.ToggleEnabled, new(0, KeyModifiers.Alt | KeyModifiers.Shift)).ShouldBe(HotkeyProblem.CommonShortcut);
        HotkeyRules.Check(Defaults, HotkeyAction.ToggleEnabled, new(0, KeyModifiers.Ctrl)).ShouldBe(HotkeyProblem.CommonShortcut);
        HotkeyRules.Check(Defaults, HotkeyAction.AcceptFirst, ctrlAlt).ShouldBe(HotkeyProblem.ModifiersOnly);
    }

    [Fact]
    public void A_shortcut_used_by_another_action_is_refused()
    {
        HotkeyRules.Check(Defaults, HotkeyAction.AcceptFirst, Defaults.Dismiss).ShouldBe(HotkeyProblem.Duplicate);

        // Keeping its own shortcut is fine.
        HotkeyRules.Check(Defaults, HotkeyAction.Dismiss, Defaults.Dismiss).ShouldBe(HotkeyProblem.None);
    }

    [Fact]
    public void Modifier_with_a_digit_is_reserved_for_accepting_suggestion_n()
    {
        HotkeyRules.Check(Defaults, HotkeyAction.Dismiss, new('3', KeyModifiers.Ctrl)).ShouldBe(HotkeyProblem.AcceptsNthSuggestion);
        HotkeyRules.Check(Defaults, HotkeyAction.Dismiss, new('3', KeyModifiers.Ctrl | KeyModifiers.Shift)).ShouldBe(HotkeyProblem.None);
    }

    [Fact]
    public void Accept_n_modifier_must_not_type_or_clash()
    {
        HotkeyRules.CheckAcceptNthModifier(Defaults, KeyModifiers.Shift).ShouldBe(HotkeyProblem.TypesText);
        HotkeyRules.CheckAcceptNthModifier(Defaults, KeyModifiers.Alt).ShouldBe(HotkeyProblem.None);

        var settings = Defaults with { Dismiss = new('2', KeyModifiers.Alt) };
        HotkeyRules.CheckAcceptNthModifier(settings, KeyModifiers.Alt).ShouldBe(HotkeyProblem.Duplicate);
    }

    [Fact]
    public void With_replaces_only_the_chosen_action()
    {
        var hotkey = new Hotkey(0x71, KeyModifiers.None); // F2
        var changed = HotkeyRules.With(Defaults, HotkeyAction.Dismiss, hotkey);

        changed.Dismiss.ShouldBe(hotkey);
        changed.AcceptFirst.ShouldBe(Defaults.AcceptFirst);
        changed.ToggleEnabled.ShouldBe(Defaults.ToggleEnabled);
        HotkeyRules.Get(changed, HotkeyAction.Dismiss).ShouldBe(hotkey);
    }

    [Theory]
    [InlineData(0x10, true)]
    [InlineData(0xA3, true)]
    [InlineData(0x5B, true)]
    [InlineData(0x09, false)]
    public void Modifier_keys_are_recognized(int key, bool modifier) =>
        HotkeyRules.IsModifierKey(key).ShouldBe(modifier);
}
