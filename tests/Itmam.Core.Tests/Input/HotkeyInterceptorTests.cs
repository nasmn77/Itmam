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
    [InlineData(Up, KeyModifiers.None)]     // native navigation is off by default
    [InlineData(Enter, KeyModifiers.None)]
    public void Other_keys_pass_through_even_while_the_bar_is_shown(int vk, KeyModifiers modifiers) =>
        Key(vk, modifiers).ShouldBe(InterceptDecision.Pass);

    [Fact]
    public void Global_toggle_works_whether_or_not_the_bar_is_shown()
    {
        var toggle = InterceptDecision.Swallow(InputEvent.Of(InputEventKind.ToggleEnabled));

        Key(KeyP, KeyModifiers.Ctrl | KeyModifiers.Alt, PopupSnapshot.Hidden).ShouldBe(toggle);
        Key(KeyP, KeyModifiers.Ctrl | KeyModifiers.Alt).ShouldBe(toggle);
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
    public void A_new_bar_ends_navigation()
    {
        var interceptor = new HotkeyInterceptor(new HotkeySettings { NativeNavigation = true });
        interceptor.OnKeyDown(Up, KeyModifiers.None, Bar);

        var newBar = Bar with { Words = ["مدرسة"] };
        interceptor.OnKeyDown(Enter, KeyModifiers.None, newBar).ShouldBe(InterceptDecision.Pass);
    }
}
