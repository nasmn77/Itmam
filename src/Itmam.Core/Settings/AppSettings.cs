using Itmam.Core.Input;

namespace Itmam.Core.Settings;

public enum UiLanguage
{
    Arabic,
    English,
}

public enum ThemeChoice
{
    /// <summary>Follows Windows light/dark mode (default).</summary>
    System,
    Light,
    Dark,
}

/// <summary>
/// Everything the user can change, saved as <c>%LOCALAPPDATA%\Itmam\settings.json</c>.
/// Contains preferences only — never typed text or learned words (those are in user.db).
/// </summary>
public sealed record AppSettings
{
    /// <summary>Itmam suggests words (tray "Enable/Disable", Ctrl+Alt by default).</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>Start Itmam when the user signs in.</summary>
    public bool StartWithWindows { get; init; } = true;

    /// <summary>
    /// Ask the Microsoft Store (through Windows) whether a newer version exists, shortly after start-up.
    /// User decision: the app itself still has no network capability and sends nothing else.
    /// </summary>
    public bool CheckForUpdates { get; init; } = true;

    /// <summary>
    /// Use the larger Wikipedia dictionary (data/itmam-ar-wikipedia.dict) instead of the basic one. Both are
    /// shipped (user decision 1.1); the basic one stays the default because it needs less memory.
    /// </summary>
    public bool UseWikipediaDictionary { get; init; }

    /// <summary>Show a notification when Itmam is turned off, paused or resumed.</summary>
    public bool NotifyOnToggle { get; init; } = true;

    /// <summary>Complete fixed expressions (data/phrases.txt and <see cref="MyPhrases"/>).</summary>
    public bool SuggestPhrases { get; init; } = true;

    /// <summary>Learn word sequences typed often (user decision 1.1: on by default; see LearningService.Phrases).</summary>
    public bool LearnPhrases { get; init; } = true;

    /// <summary>
    /// Phrases the user added in Settings ("My phrases"), suggested before the shipped ones. Only what
    /// the user typed here on purpose: nothing is collected from their typing.
    /// </summary>
    public IReadOnlyList<string> MyPhrases { get; init; } = [];

    /// <summary>Built-in phrases the user deleted from the bar (user decision 1.1); restorable in Settings.</summary>
    public IReadOnlyList<string> HiddenPhrases { get; init; } = [];

    /// <summary>
    /// Text shortcuts the user added in Settings (user decision 1.1): typing a key shows its text first
    /// in the bar, and Tab inserts it.
    /// </summary>
    public IReadOnlyList<Suggestions.TextShortcut> TextShortcuts { get; init; } = [];

    /// <summary>Long phrases (support replies), chosen by name from a list (user decision 1.1).</summary>
    public IReadOnlyList<Suggestions.LongPhrase> LongPhrases { get; init; } = [];

    public UiLanguage Language { get; init; } = UiLanguage.Arabic;

    /// <summary>Suggestions shown (1–5).</summary>
    public int MaxSuggestions { get; init; } = 3;

    /// <summary>Letters typed before suggestions appear (1–4).</summary>
    public int MinLetters { get; init; } = 2;

    public bool AddSpaceAfterAccept { get; init; } = true;

    public bool LearningPaused { get; init; }

    /// <summary>Uses before a new word is suggested (1–5).</summary>
    public int MinUsesToSuggest { get; init; } = 2;

    public HotkeySettings Hotkeys { get; init; } = new();

    public ThemeChoice Theme { get; init; } = ThemeChoice.System;

    public bool HighlightFirstSuggestion { get; init; } = true;

    public bool ShowHotkeyHints { get; init; }

    /// <summary>Bar background "#RRGGBB"; null = the theme's color (System/Light/Dark, like the native bar).</summary>
    public string? BarBackground { get; init; }

    /// <summary>Highlight (first suggestion, navigation selection) "#RRGGBB"; null = Windows accent / theme default.</summary>
    public string? BarHighlight { get; init; }

    /// <summary>Text "#RRGGBB"; null = automatic, readable on the background (WCAG AA).</summary>
    public string? BarText { get; init; }

    /// <summary>Bar opacity in percent (80–100).</summary>
    public int BarOpacity { get; init; } = 100;

    /// <summary>Suggestion text size in pixels at 100% scaling (12–20; the native bar uses 14).</summary>
    public int BarFontSize { get; init; } = 14;

    public IReadOnlyList<string> ExcludedApps { get; init; } = ExcludedAppsDefaults();

    /// <summary>Values out of range (hand-edited file, older version) are brought back into range.</summary>
    public AppSettings Normalized() => this with
    {
        MaxSuggestions = Math.Clamp(MaxSuggestions, 1, 5),
        MinLetters = Math.Clamp(MinLetters, 1, 4),
        MinUsesToSuggest = Math.Clamp(MinUsesToSuggest, 1, 5),
        BarOpacity = Math.Clamp(BarOpacity, 80, 100),
        BarFontSize = Math.Clamp(BarFontSize, 12, 20),
        BarBackground = Presentation.ColorContrast.Parse(BarBackground) is null ? null : BarBackground,
        BarHighlight = Presentation.ColorContrast.Parse(BarHighlight) is null ? null : BarHighlight,
        BarText = Presentation.ColorContrast.Parse(BarText) is null ? null : BarText,
        Hotkeys = Hotkeys ?? new HotkeySettings(),
        ExcludedApps = ExcludedApps ?? ExcludedAppsDefaults(),
        MyPhrases = [.. (MyPhrases ?? []).Select(Suggestions.PhraseIndex.Clean).OfType<string>().Distinct()],
        HiddenPhrases = [.. (HiddenPhrases ?? []).Select(Suggestions.PhraseIndex.Clean).OfType<string>().Distinct()],
        TextShortcuts = [.. (TextShortcuts ?? [])
            .Where(s => s is not null)
            .Select(s => Suggestions.TextShortcuts.CleanKey(s.Key ?? "") is { } key && Suggestions.TextShortcuts.CleanText(s.Text ?? "") is { } text
                ? new Suggestions.TextShortcut(key, text)
                : null)
            .OfType<Suggestions.TextShortcut>()
            .DistinctBy(s => s.Key)],
        LongPhrases = [.. (LongPhrases ?? [])
            .Where(p => p is not null)
            .Select(p => Suggestions.LongPhrases.CleanName(p.Name ?? "") is { } name && Suggestions.LongPhrases.CleanText(p.Text ?? "") is { } text
                ? new Suggestions.LongPhrase(name, text)
                : null)
            .OfType<Suggestions.LongPhrase>()
            .DistinctBy(p => p.Name)],
    };

    private static IReadOnlyList<string> ExcludedAppsDefaults() => [.. Input.ExcludedApps.Defaults];
}
