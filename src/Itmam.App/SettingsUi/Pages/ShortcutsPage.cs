using Itmam.App.Resources;
using Itmam.Core.Input;

namespace Itmam.App.SettingsUi.Pages;

/// <summary>Keyboard shortcuts: the bar's keys and the shortcuts that work everywhere.</summary>
internal sealed class ShortcutsPage(SettingsWindow window) : SettingsPageBase(window)
{
    // Shortcuts are checked (HotkeyRules) before they are saved; a refused one shows why under its card.
    public override void Build()
    {
        var hotkeys = App.Settings.Hotkeys;
        AddTitle(Strings.NavShortcuts);
        AddText(Strings.ShortcutsIntro, secondary: true);

        AddSection(Strings.SectionBarKeys);
        AddHotkeyCard(HotkeyAction.AcceptFirst, Strings.ShortcutAcceptFirst, null);

        KeyModifiers[] nthModifiers = [KeyModifiers.Ctrl, KeyModifiers.Alt];
        var nth = new SegmentedChoice { Options = [.. nthModifiers.Select(HotkeyNames.Format)] };
        nth.SelectedIndex = Math.Max(0, Array.IndexOf(nthModifiers, hotkeys.AcceptNthModifier));
        var nthCard = AddCard(Strings.ShortcutAcceptNth, Strings.ShortcutAcceptNthHint, nth);
        nth.SelectedIndexChanged += (_, _) =>
        {
            if (Loading)
            {
                return;
            }

            var modifier = nthModifiers[nth.SelectedIndex];
            var problem = HotkeyRules.CheckAcceptNthModifier(App.Settings.Hotkeys, modifier);
            if (problem == HotkeyProblem.None)
            {
                Update(s => s with { Hotkeys = s.Hotkeys with { AcceptNthModifier = modifier } });
                nthCard.SetDescription(Strings.ShortcutAcceptNthHint);
                return;
            }

            nthCard.SetDescription(ProblemText(problem, new Hotkey('1', modifier)));
            Loading = true;
            nth.SelectedIndex = Array.IndexOf(nthModifiers, App.Settings.Hotkeys.AcceptNthModifier);
            Loading = false;
        };

        AddHotkeyCard(HotkeyAction.Dismiss, Strings.ShortcutDismiss, Strings.ShortcutDismissHint);

        var navigation = new ToggleSwitch { Checked = hotkeys.NativeNavigation };
        navigation.CheckedChanged += (_, _) => Update(s => s with { Hotkeys = s.Hotkeys with { NativeNavigation = navigation.Checked } });
        AddCard(Strings.NativeNavigation, Strings.NativeNavigationHint, navigation);

        AddSection(Strings.SectionEverywhere);
        AddHotkeyCard(HotkeyAction.ToggleEnabled, Strings.ShortcutToggle, Strings.ShortcutToggleHint);
        AddHotkeyCard(HotkeyAction.OpenLongPhrases, Strings.ShortcutLongPhrases, Strings.ShortcutLongPhrasesHint);

        var reset = new ThemedButton { Text = Strings.ResetButton };
        reset.Click += (_, _) =>
        {
            Update(s => s with { Hotkeys = new HotkeySettings() });
            BeginInvoke(() => SelectPage(SettingsPage.Shortcuts)); // rebuild after this click is handled
        };
        AddCard(Strings.ResetShortcuts, Strings.ResetShortcutsHint, reset);
    }
}
