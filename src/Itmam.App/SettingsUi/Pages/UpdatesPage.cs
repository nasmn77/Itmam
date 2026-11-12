using System.Globalization;
using Itmam.App.Resources;
using Itmam.Win32;

namespace Itmam.App.SettingsUi.Pages;

/// <summary>Updates: the Store update check and the Store page.</summary>
internal sealed class UpdatesPage(SettingsWindow window) : SettingsPageBase(window)
{
    // User decision: updates come from Microsoft Store. Itmam only asks the Store whether a newer version
    // exists (StoreUpdates); it never downloads anything itself, and the button opens the Store page.
    public override void Build()
    {
        AddTitle(Strings.NavUpdates);
        AddText(string.Format(CultureInfo.CurrentCulture, Strings.AboutVersion, AppVersion()), secondary: true);

        var onStart = new ToggleSwitch { Checked = App.Settings.CheckForUpdates };
        onStart.CheckedChanged += (_, _) => Update(s => s with { CheckForUpdates = onStart.Checked });
        AddCard(Strings.CheckOnStart, Strings.CheckOnStartHint, onStart);

        var check = new ThemedButton { Text = Strings.CheckNowButton };
        var checkCard = AddCard(Strings.CheckNow, null, check);
        check.Click += async (_, _) =>
        {
            check.Enabled = false;
            check.Invalidate();
            checkCard.SetDescription(Strings.UpdateChecking);
            var result = await StoreUpdates.CheckAsync();
            if (checkCard.IsDisposed)
            {
                return; // the page was left meanwhile
            }

            checkCard.SetDescription(result switch
            {
                UpdateCheck.UpdateAvailable => Strings.UpdateAvailable,
                UpdateCheck.UpToDate => Strings.UpdateUpToDate,
                _ => Strings.UpdateUnavailable,
            });
            check.Enabled = true;
            check.Invalidate();
        };

        var open = new ThemedButton { Text = Strings.OpenButton };
        open.Click += (_, _) => Tray.TrayApplication.OpenStorePage();
        AddCard(Strings.OpenStore, Strings.OpenStoreHint, open);
        AddRateCard();
    }
}
