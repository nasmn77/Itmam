using System.Globalization;
using Itmam.App.Resources;
using Itmam.Core.Settings;

namespace Itmam.App.SettingsUi.Pages;

/// <summary>About: version, counts, links, sources and the privacy summary.</summary>
internal sealed class AboutPage(SettingsWindow window) : SettingsPageBase(window)
{
    private const string RepositoryUrl = "https://github.com/nasmn77/Itmam";
    // The website's policy, the same one registered in Partner Center; English for the English interface.
    private const string WebsiteUrl = "https://nasmn77.github.io/Itmam/";
    private const string WebsiteUrlEnglish = "https://nasmn77.github.io/Itmam/en/";
    private const string PrivacyUrl = "https://nasmn77.github.io/Itmam/privacy.html";
    private const string PrivacyUrlEnglish = "https://nasmn77.github.io/Itmam/en/privacy.html";
    private const string DeveloperEmail = "nasmn77@gmail.com";

    public override void Build()
    {
        AddTitle(Strings.AppName);
        AddText(string.Format(CultureInfo.CurrentCulture, Strings.AboutVersion, AppVersion()), secondary: true);
        AddText(Strings.AboutTagline, secondary: false);
        if (App.Settings.ShowSuggestionBar)
        {
            AddText(string.Format(CultureInfo.CurrentCulture, Strings.AboutDictionary, App.LoadedDictionaries.Sum(d => d.Count)), secondary: false);
        }

        AddText(string.Format(CultureInfo.CurrentCulture, Strings.AboutLearned, App.Learning.Languages.Sum(l => App.Learning.Snapshot(l).Count)), secondary: false);
        AddLink(Strings.AboutWebsite, App.Settings.Language == UiLanguage.Arabic ? WebsiteUrl : WebsiteUrlEnglish);
        AddRateCard();

        AddSection(Strings.SectionDeveloper);
        AddText(Strings.DeveloperName, secondary: false);
        AddLink(DeveloperEmail, $"mailto:{DeveloperEmail}");

        AddSection(Strings.SectionOpenSource);
        AddText(Strings.AboutOpenSource, secondary: false);
        AddLink(Strings.AboutSourceCode, RepositoryUrl);

        if (App.Settings.ShowSuggestionBar)
        {
            AddSection(Strings.AboutSources);
            foreach (var source in App.LoadedDictionaries.SelectMany(d => d.Sources).Distinct())
            {
                AddLink($"{source.Name} — {source.License}", source.Url);
            }
        }

        AddSection(Strings.AboutPrivacyLink);
        AddText(Strings.AboutPrivacy, secondary: false);
        AddLink(Strings.AboutPrivacyLink, App.Settings.Language == UiLanguage.Arabic ? PrivacyUrl : PrivacyUrlEnglish);
    }
}
