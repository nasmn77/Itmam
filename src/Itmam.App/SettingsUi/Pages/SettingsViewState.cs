using Itmam.Core.Languages;

namespace Itmam.App.SettingsUi.Pages;

/// <summary>What the settings window remembers while its pages are built again and again.</summary>
internal sealed class SettingsViewState
{
    /// <summary>The Excluded apps search: kept when the page is rebuilt after add/remove, cleared on leaving it.</summary>
    public string ExcludedSearch { get; set; } = string.Empty;

    /// <summary>The My words search, cleared on leaving the page.</summary>
    public string WordsFilter { get; set; } = string.Empty;

    /// <summary>The My words sort order (most used, recent, alphabetical).</summary>
    public int WordsSort { get; set; }

    /// <summary>The language of My words, and of the learned phrases on My phrases.</summary>
    public LanguageProfile WordsLanguage { get; set; } = LanguageProfile.Arabic;
}
