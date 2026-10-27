using Itmam.Core.Input;

namespace Itmam.Core.Tests.Input;

public class KeyClassifierTests
{
    private static KeyClassification Classify(int vk, KeyModifiers modifiers = KeyModifiers.None) =>
        KeyClassifier.Classify(vk, modifiers);

    [Theory]
    [InlineData(0x10)]
    [InlineData(0xA1)]
    [InlineData(0x11)]
    [InlineData(0x12)]
    [InlineData(0x5B)]
    [InlineData(0x14)]
    [InlineData(0x90)]
    public void Modifier_and_lock_keys_are_ignored(int vk) => Classify(vk).Action.ShouldBe(KeyAction.Ignore);

    [Theory]
    [InlineData(0x41)]   // A / ش on Arabic 101
    [InlineData(0xDE)]   // OEM quote / ط
    [InlineData(0x20)]   // Space
    [InlineData(0x0D)]   // Enter
    [InlineData(0x09)]   // Tab
    [InlineData(0x31)]   // 1
    public void Typing_keys_are_translated(int vk) => Classify(vk).Action.ShouldBe(KeyAction.Translate);

    [Fact]
    public void Shift_still_translates() => Classify(0x41, KeyModifiers.Shift).Action.ShouldBe(KeyAction.Translate);

    [Fact]
    public void AltGr_still_translates() =>
        Classify(0x41, KeyModifiers.Ctrl | KeyModifiers.Alt).Action.ShouldBe(KeyAction.Translate);

    [Fact]
    public void Backspace_variants()
    {
        Classify(0x08).ShouldBe(new KeyClassification(KeyAction.Event, InputEventKind.Backspace));
        Classify(0x08, KeyModifiers.Ctrl).ShouldBe(new KeyClassification(KeyAction.Event, InputEventKind.CtrlBackspace));
        Classify(0x08, KeyModifiers.Alt).ShouldBe(new KeyClassification(KeyAction.Event, InputEventKind.Shortcut));
    }

    [Theory]
    [InlineData(0x56, KeyModifiers.Ctrl)]
    [InlineData(0x2D, KeyModifiers.Shift)]
    public void Paste_shortcuts(int vk, KeyModifiers modifiers) =>
        Classify(vk, modifiers).Kind.ShouldBe(InputEventKind.Paste);

    [Theory]
    [InlineData(0x43, KeyModifiers.Ctrl)]   // Ctrl+C
    [InlineData(0x5A, KeyModifiers.Ctrl)]   // Ctrl+Z
    [InlineData(0x09, KeyModifiers.Alt)]    // Alt+Tab
    [InlineData(0x44, KeyModifiers.Win)]    // Win+D
    [InlineData(0x31, KeyModifiers.Ctrl)]   // Ctrl+1
    public void Ctrl_alt_and_win_shortcuts_reset(int vk, KeyModifiers modifiers) =>
        Classify(vk, modifiers).ShouldBe(new KeyClassification(KeyAction.Event, InputEventKind.Shortcut));

    [Theory]
    [InlineData(0x25)]
    [InlineData(0x26)]
    [InlineData(0x27)]
    [InlineData(0x28)]
    [InlineData(0x21)]
    [InlineData(0x22)]
    [InlineData(0x23)]
    [InlineData(0x24)]
    [InlineData(0x2E)]
    [InlineData(0x2D)]
    [InlineData(0xE5)]
    public void Navigation_delete_and_ime_keys_reset(int vk) => Classify(vk).Kind.ShouldBe(InputEventKind.Navigation);

    [Theory]
    [InlineData(0x25, KeyModifiers.Shift)]                    // Shift+Left
    [InlineData(0x27, KeyModifiers.Shift)]                    // Shift+Right
    [InlineData(0x24, KeyModifiers.Shift)]                    // Shift+Home
    [InlineData(0x23, KeyModifiers.Shift)]                    // Shift+End
    [InlineData(0x26, KeyModifiers.Shift)]                    // Shift+Up
    [InlineData(0x25, KeyModifiers.Shift | KeyModifiers.Ctrl)] // Ctrl+Shift+Left (word)
    [InlineData(0x41, KeyModifiers.Ctrl)]                     // Ctrl+A
    public void Selecting_text_is_a_selection_event(int vk, KeyModifiers modifiers) =>
        Classify(vk, modifiers).ShouldBe(new KeyClassification(KeyAction.Event, InputEventKind.Selection));

    [Fact]
    public void Escape_is_its_own_event() => Classify(0x1B).Kind.ShouldBe(InputEventKind.Escape);
}
