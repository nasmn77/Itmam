using System.Globalization;
using Itmam.Core.Settings;

namespace Itmam.App;

/// <summary>Switches the UI language (resource lookups) for all threads.</summary>
internal static class UiCulture
{
    public static void Apply(UiLanguage language)
    {
        var culture = CultureInfo.GetCultureInfo(language == UiLanguage.Arabic ? "ar" : "en");
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
    }
}
