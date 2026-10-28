using System.Drawing;
using Itmam.Core.Input;
using Itmam.Core.Presentation;

namespace Itmam.Core.Tests.Input;

public class HotkeyInterceptorTests
{
    private const int Tab = 0x09, Enter = 0x0D, Esc = 0x1B, Left = 0x25, Up = 0x26, Right = 0x27, Down = 0x28;
    private const int Digit1 = 0x31, Digit2 = 0x32, Digit3 = 0x33, Digit4 = 0x34, KeyA = 0x41, KeyP = 0x50;

    private static readonly PopupSnapshot Bar = new(["مدرسة", "مدرسين", "مدرس"], new Rectangle(100, 100, 200, 35));
    private readonly HotkeyInterceptor _interceptor = new(new HotkeySettings());

    private InterceptDecision Key(int vk, KeyModifiers modifiers = KeyModifiers.None, PopupSnapshot? popup = null) =>
        _interceptor.OnKeyDown(vk, modifiers, popup ?? Bar);

    [Theory]
    [InlineData(Tab, KeyModifiers.None)]
    [InlineData(Esc, KeyModifiers.None)]
    [InlineData(Digit1, KeyModifiers.Ctrl)]
    [InlineData(Up, KeyModifiers.None)]
    public void Nothing_is_intercepted_while_the_bar_is_hidden(int vk, KeyModifiers modifiers) =>
        Key(vk, modifiers, PopupSnapshot.Hidden).ShouldBe(InterceptDecision.Pass);

    [Fact]
    public void Tab_accepts_the_first_suggestion() =>
        Key(Tab).ShouldBe(InterceptDecision.Swallow(InputEvent.Accept(0, "مدرسة")));

    [Theory]
    [InlineData(Digit1, 0, "مدرسة")]
    [InlineData(Digit2, 1, "مدرسين")]
    [InlineData(Digit3, 2, "مدرس")]
    public void Ctrl_digit_accepts_suggestion_n(int vk, int index, string word) =>
        Key(vk, KeyModifiers.Ctrl).ShouldBe(InterceptDecision.Swallow(InputEvent.Accept(index, word)));

    [Fact]
    public void Ctrl_digit_beyond_the_shown_suggestions_passes() =>
        Key(Digit4, KeyModifiers.Ctrl).ShouldBe(InterceptDecision.Pass);

    [Fact]
    public void Esc_dismisses_the_bar() =>
        Key(Esc).ShouldBe(InterceptDecision.Swallow(InputEvent.Of(InputEventKind.DismissSuggestions)));

    [Theory]
    [InlineData(KeyA, KeyModifiers.None)]
    [InlineData(Tab, KeyModifiers.Shift)]   // Shift+Tab is not the accept key
    [InlineData(Digit1, KeyModifiers.None)] // a plain digit is typed
    [InlineData(Enter, KeyModifiers.None)]  // Enter only accepts once navigating
    [InlineData(Up, KeyModifiers.Shift)]    // Shift+Up selects text as usual
    public void Other_keys_pass_through_even_while_the_bar_is_shown(int vk, KeyModifiers modifiers) =>
        Key(vk, modifiers).ShouldBe(InterceptDecision.Pass);

    [Fact]
    public void Native_navigation_is_on_by_default_and_can_be_turned_off()
    {
        Key(Up).ShouldBe(InterceptDecision.Swallow(InputEvent.Select(0)));

        new HotkeyInterceptor(new HotkeySettings { NativeNavigation = false })
            .OnKeyDown(Up, KeyModifiers.None, Bar).ShouldBe(InterceptDecision.Pass);
    }

    private const int LCtrl = 0xA2, RCtrl = 0xA3, LAlt = 0xA4, RAlt = 0xA5, LShift = 0xA0, Delete = 0x2E;

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Ctrl_alt_pressed_and_released_alone_toggles(bool barShown)
    {
        var popup = barShown ? Bar : PopupSnapshot.Hidden;

        // Modifier keys always pass: nothing is swallowed.
        Key(LCtrl, KeyModifiers.None, popup).ShouldBe(InterceptDecision.Pass);
        Key(LAlt, KeyModifiers.Ctrl, popup).ShouldBe(InterceptDecision.Pass);

        _interceptor.OnKeyUp(LAlt).ShouldBeTrue();
        _interceptor.OnKeyUp(LCtrl).ShouldBeFalse(); // once per press
    }

    [Fact]
    public void Ctrl_alt_works_in_either_order_and_with_right_hand_keys()
    {
        Key(RAlt);
        Key(RCtrl, KeyModifiers.Alt);
        _interceptor.OnKeyUp(RCtrl).ShouldBeTrue();
    }

    [Theory]
    [InlineData(Delete)]   // Ctrl+Alt+Del
    [InlineData(Left)]     // Ctrl+Alt+arrow (screen rotation on some PCs)
    [InlineData(KeyP)]     // AltGr+letter on European layouts
    public void Ctrl_alt_with_another_key_does_not_toggle(int other)
    {
        Key(LCtrl);
        Key(LAlt, KeyModifiers.Ctrl);
        Key(other, KeyModifiers.Ctrl | KeyModifiers.Alt);

        _interceptor.OnKeyUp(other).ShouldBeFalse();
        _interceptor.OnKeyUp(LAlt).ShouldBeFalse();
    }

    [Fact]
    public void Ctrl_alt_shift_or_a_click_does_not_toggle()
    {
        Key(LCtrl);
        Key(LAlt, KeyModifiers.Ctrl);
        Key(LShift, KeyModifiers.Ctrl | KeyModifiers.Alt);
        _interceptor.OnKeyUp(LAlt).ShouldBeFalse();

        Key(LCtrl);
        Key(LAlt, KeyModifiers.Ctrl);
        _interceptor.CancelChord();
        _interceptor.OnKeyUp(LAlt).ShouldBeFalse();
    }

    [Fact]
    public void Ctrl_alone_does_not_toggle()
    {
        Key(LCtrl);
        _interceptor.OnKeyUp(LCtrl).ShouldBeFalse();
    }

    [Fact]
    public void A_toggle_with_a_key_still_works_and_is_swallowed()
    {
        var interceptor = new HotkeyInterceptor(new HotkeySettings { ToggleEnabled = new(KeyP, KeyModifiers.Ctrl | KeyModifiers.Alt) });
        var toggle = InterceptDecision.Swallow(InputEvent.Of(InputEventKind.ToggleEnabled));

        interceptor.OnKeyDown(KeyP, KeyModifiers.Ctrl | KeyModifiers.Alt, PopupSnapshot.Hidden).ShouldBe(toggle);
        interceptor.OnKeyDown(KeyP, KeyModifiers.Ctrl | KeyModifiers.Alt, Bar).ShouldBe(toggle);
        interceptor.OnKeyUp(LAlt).ShouldBeFalse(); // not a modifiers-only toggle
    }

    [Fact]
    public void Pressing_ctrl_while_navigating_keeps_the_selection_for_ctrl_delete()
    {
        Key(Up);
        Key(LCtrl).ShouldBe(InterceptDecision.Pass);
        _interceptor.Selected.ShouldBe(0);
        Key(Delete, KeyModifiers.Ctrl).ShouldBe(InterceptDecision.Swallow(InputEvent.Forget(0, "مدرسة")));
    }

    [Fact]
    public void Hotkeys_are_configurable()
    {
        var interceptor = new HotkeyInterceptor(new HotkeySettings { AcceptFirst = new Hotkey(Right, KeyModifiers.Ctrl) });

        interceptor.OnKeyDown(Tab, KeyModifiers.None, Bar).ShouldBe(InterceptDecision.Pass);
        interceptor.OnKeyDown(Right, KeyModifiers.Ctrl, Bar).Command.ShouldBe(InputEvent.Accept(0, "مدرسة"));
    }

    [Fact]
    public void Native_navigation_selects_with_arrows_and_inserts_with_enter()
    {
        var interceptor = new HotkeyInterceptor(new HotkeySettings { NativeNavigation = true });

        interceptor.OnKeyDown(Up, KeyModifiers.None, Bar).ShouldBe(InterceptDecision.Swallow(InputEvent.Select(0)));
        interceptor.OnKeyDown(Left, KeyModifiers.None, Bar).ShouldBe(InterceptDecision.Swallow(InputEvent.Select(1))); // RTL: next is to the left
        interceptor.OnKeyDown(Left, KeyModifiers.None, Bar).ShouldBe(InterceptDecision.Swallow(InputEvent.Select(2)));
        interceptor.OnKeyDown(Left, KeyModifiers.None, Bar).ShouldBe(InterceptDecision.Swallow(InputEvent.Select(2))); // stays on the last
        interceptor.OnKeyDown(Right, KeyModifiers.None, Bar).ShouldBe(InterceptDecision.Swallow(InputEvent.Select(1)));

        interceptor.OnKeyDown(Enter, KeyModifiers.None, Bar).ShouldBe(InterceptDecision.Swallow(InputEvent.Accept(1, "مدرسين")));
        interceptor.Selected.ShouldBe(-1);
    }

    [Theory]
    [InlineData(Esc, InterceptAction.Swallow)]
    [InlineData(Down, InterceptAction.Swallow)]
    [InlineData(KeyA, InterceptAction.Pass)]   // typing leaves the bar and reaches the app
    public void Native_navigation_can_be_left(int vk, InterceptAction action)
    {
        var interceptor = new HotkeyInterceptor(new HotkeySettings { NativeNavigation = true });
        interceptor.OnKeyDown(Up, KeyModifiers.None, Bar);

        interceptor.OnKeyDown(vk, KeyModifiers.None, Bar).ShouldBe(new InterceptDecision(action, InputEvent.Select(-1)));
        interceptor.Selected.ShouldBe(-1);
    }

    [Fact]
    public void Ctrl_delete_while_navigating_forgets_the_selected_suggestion()
    {
        Key(Up);
        Key(Left);

        Key(0x2E, KeyModifiers.Ctrl).ShouldBe(InterceptDecision.Swallow(InputEvent.Forget(1, "مدرسين")));
        _interceptor.Selected.ShouldBe(-1);
    }

    [Fact]
    public void Ctrl_delete_without_navigating_passes_to_the_app() =>
        Key(0x2E, KeyModifiers.Ctrl).ShouldBe(InterceptDecision.Pass);

    [Fact]
    public void A_new_bar_ends_navigation()
    {
        var interceptor = new HotkeyInterceptor(new HotkeySettings { NativeNavigation = true });
        interceptor.OnKeyDown(Up, KeyModifiers.None, Bar);

        var newBar = Bar with { Words = ["مدرسة"] };
        interceptor.OnKeyDown(Enter, KeyModifiers.None, newBar).ShouldBe(InterceptDecision.Pass);
    }
}
