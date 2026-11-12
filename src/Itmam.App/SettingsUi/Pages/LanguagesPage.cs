using System.Diagnostics;
using System.Globalization;
using Itmam.App.Resources;
using Itmam.Core.Languages;
using Itmam.Core.Settings;
using Itmam.Win32;

namespace Itmam.App.SettingsUi.Pages;

/// <summary>Languages: interface language, keyboard languages, the Arabic and English dictionaries.</summary>
internal sealed class LanguagesPage(SettingsWindow window) : SettingsPageBase(window)
{
    // User decisions 2.0: Itmam follows the keyboard layout. Arabic and English have dictionaries, other
    // alphabetic languages learn from typing (off until turned on), input-method languages stay silent.
    public override void Build()
    {
        var settings = App.Settings;
        AddTitle(Strings.NavLanguages);
        AddText(Strings.LanguagesIntro, secondary: true);

        // The interface language lives here too (user decision 2.0), moved from General.
        AddSection(Strings.SectionLanguage);
        var uiLanguage = new SegmentedChoice { Options = [Strings.LanguageArabic, Strings.LanguageEnglish] };
        uiLanguage.SelectedIndex = settings.Language == UiLanguage.Arabic ? 0 : 1;
        uiLanguage.SelectedIndexChanged += (_, _) => Update(s => s with { Language = uiLanguage.SelectedIndex == 0 ? UiLanguage.Arabic : UiLanguage.English });
        AddCard(Strings.UiLanguage, null, uiLanguage);

        AddSection(Strings.SectionKeyboardLanguages);
        var installed = KeyboardLayouts.Installed()
            .Select(layout => (Tag: LanguageRegistry.ForLayout(layout)?.Id ?? TagOf(layout), Language: LanguageRegistry.ForLayout(layout)))
            .DistinctBy(l => l.Tag)
            .ToList();
        var notInstalled = settings.EnabledLanguages
            .Where(tag => installed.All(l => l.Tag != tag))
            .Select(tag => (Tag: tag, Language: (LanguageProfile?)LanguageRegistry.ForTag(tag)));
        foreach (var (tag, language) in installed.Concat(notInstalled).OrderBy(l => l.Tag switch { "ar" => 0, "en" => 1, _ => 2 }).ThenBy(l => l.Tag, StringComparer.Ordinal))
        {
            var description = LanguageRegistry.Support(language) switch
            {
                LanguageSupport.Full => Strings.LanguageFull,
                LanguageSupport.LearningOnly => Strings.LanguageLearningOnly,
                _ => Strings.LanguageUnsupported,
            };
            if (installed.All(l => l.Tag != tag))
            {
                description += " " + Strings.LanguageNoKeyboard;
            }

            var toggle = new ToggleSwitch { Checked = language is not null && settings.EnabledLanguages.Contains(tag), Enabled = language is not null };
            toggle.CheckedChanged += (_, _) => Update(s => s with
            {
                EnabledLanguages = toggle.Checked ? [.. s.EnabledLanguages, tag] : [.. s.EnabledLanguages.Where(l => l != tag)],
            });
            AddCard(LanguageName(tag), description, toggle);
        }

        // Two shipped Arabic dictionaries (user decision 1.1): the basic one, or the larger Wikipedia one.
        AddSection(Strings.SectionDictionary);
        var dictionary = new SegmentedChoice { Options = [Strings.DictionaryBasic, Strings.DictionaryWikipedia] };
        dictionary.SelectedIndex = settings.UseWikipediaDictionary ? 1 : 0;
        dictionary.SelectedIndexChanged += (_, _) => Update(s => s with { UseWikipediaDictionary = dictionary.SelectedIndex == 1 });
        AddCard(Strings.DictionaryChoice, Strings.DictionaryChoiceHint, dictionary);

        // The same choice for English (user decision 2.0).
        AddSection(Strings.SectionEnglishDictionary);
        var english = new SegmentedChoice { Options = [Strings.DictionaryBasic, Strings.DictionaryWikipedia] };
        english.SelectedIndex = settings.UseEnglishWikipediaDictionary ? 1 : 0;
        english.SelectedIndexChanged += (_, _) => Update(s => s with { UseEnglishWikipediaDictionary = english.SelectedIndex == 1 });
        AddCard(Strings.DictionaryChoice, Strings.EnglishDictionaryChoiceHint, english);

        // Windows' own suggestions for the physical keyboard would show a second bar.
        AddSection(Strings.SectionTwoBars);
        var open = new ThemedButton { Text = Strings.OpenButton };
        open.Click += (_, _) =>
        {
            try
            {
                Process.Start(new ProcessStartInfo("ms-settings:typing") { UseShellExecute = true });
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                ShowInfo(Strings.FileError);
            }
        };
        AddCard(Strings.TwoBars, Strings.TwoBarsHint, open);
    }

    // The language of a layout Itmam does not support (Chinese …), as a tag for its name.
    private static string TagOf(nint layout)
    {
        try
        {
            return CultureInfo.GetCultureInfo((int)(layout & 0xFFFF)).TwoLetterISOLanguageName;
        }
        catch (CultureNotFoundException)
        {
            return $"x{layout & 0x3FF:x2}";
        }
    }
}
