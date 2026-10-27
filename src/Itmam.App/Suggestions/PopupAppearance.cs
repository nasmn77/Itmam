using Microsoft.Win32;

namespace Itmam.App.Suggestions;

internal enum ThemeMode
{
    /// <summary>Follows Windows light/dark mode.</summary>
    System,
    Light,
    Dark,
}

/// <summary>User-adjustable look of the suggestion bar (settings page arrives in Phase 6).</summary>
internal sealed record PopupAppearance
{
    public ThemeMode Theme { get; init; } = ThemeMode.System;

    /// <summary>
    /// Faint background behind the first suggestion (the one Tab accepts). The native bar has none;
    /// turning this off matches Windows exactly.
    /// </summary>
    public bool HighlightFirst { get; init; } = true;

    /// <summary>Small 1 2 3 hints. Off by default (the native bar shows none).</summary>
    public bool ShowHotkeyHints { get; init; }
}

/// <summary>Colors of the bar for one theme.</summary>
internal sealed record PopupColors(Color Background, Color Text, Color Border, Color FirstHighlight, Color Hint)
{
    /// <summary>Measured from the native Windows 11 suggestion bar in dark mode.</summary>
    public static readonly PopupColors Dark = new(
        Background: Color.FromArgb(0x2C, 0x2C, 0x2C),
        Text: Color.White,
        Border: Color.FromArgb(0x14, 0x14, 0x14),
        FirstHighlight: Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF),
        Hint: Color.FromArgb(0x9A, 0x9A, 0x9A));

    /// <summary>Light counterpart with the same proportions (Windows 11 light flyout colors).</summary>
    public static readonly PopupColors Light = new(
        Background: Color.FromArgb(0xF9, 0xF9, 0xF9),
        Text: Color.FromArgb(0x1A, 0x1A, 0x1A),
        Border: Color.FromArgb(0xDF, 0xDF, 0xDF),
        FirstHighlight: Color.FromArgb(0x0C, 0x00, 0x00, 0x00),
        Hint: Color.FromArgb(0x70, 0x70, 0x70));

    public static PopupColors For(ThemeMode mode) => mode switch
    {
        ThemeMode.Light => Light,
        ThemeMode.Dark => Dark,
        _ => SystemUsesLightTheme() ? Light : Dark,
    };

    /// <summary>Windows "app mode" (Settings → Personalization → Colors).</summary>
    public static bool SystemUsesLightTheme()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme") is not int value || value != 0;
    }
}
