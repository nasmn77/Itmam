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
    /// <summary>Itmam suggests words (tray "Enable/Disable", Ctrl+Alt+P).</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>Start Itmam when the user signs in.</summary>
    public bool StartWithWindows { get; init; } = true;

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

    public IReadOnlyList<string> ExcludedApps { get; init; } = ExcludedAppsDefaults();

    /// <summary>Values out of range (hand-edited file, older version) are brought back into range.</summary>
    public AppSettings Normalized() => this with
    {
        MaxSuggestions = Math.Clamp(MaxSuggestions, 1, 5),
        MinLetters = Math.Clamp(MinLetters, 1, 4),
        MinUsesToSuggest = Math.Clamp(MinUsesToSuggest, 1, 5),
        Hotkeys = Hotkeys ?? new HotkeySettings(),
        ExcludedApps = ExcludedApps ?? ExcludedAppsDefaults(),
    };

    private static IReadOnlyList<string> ExcludedAppsDefaults() => [.. Input.ExcludedApps.Defaults];
}
