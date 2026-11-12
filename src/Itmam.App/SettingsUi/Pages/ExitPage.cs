using System.Globalization;
using Itmam.App.Resources;

namespace Itmam.App.SettingsUi.Pages;

/// <summary>Exit: turn Itmam off or close it.</summary>
internal sealed class ExitPage(SettingsWindow window) : SettingsPageBase(window)
{
    // Its own page: exiting is different from "Enabled" off, and is explained here.
    public override void Build()
    {
        AddTitle(Strings.SectionExit);

        // Turning off keeps Itmam next to the clock (gray icon) and is remembered, like the toggle shortcut.
        var toggle = new ThemedButton { Text = App.IsEnabled ? Strings.TurnOffButton : Strings.TurnOnButton };
        toggle.Click += (_, _) =>
        {
            App.SetEnabled(!App.IsEnabled);
            BeginInvoke(RebuildPage); // after this click is handled
        };
        var shortcut = "\u202A" + HotkeyNames.Format(App.Settings.Hotkeys.ToggleEnabled) + "\u202C";
        AddCard(App.IsEnabled ? Strings.TurnOffTitle : Strings.TurnOnTitle,
            string.Format(CultureInfo.CurrentCulture, Strings.TurnOffHint, shortcut), toggle);
        var exit = new ThemedButton { Text = Strings.ExitButton };
        exit.Click += (_, _) => ExitApp();
        AddCard(Strings.ExitTitle, Strings.ExitHint, exit);
    }
}
