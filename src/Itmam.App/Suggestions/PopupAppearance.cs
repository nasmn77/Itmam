using Itmam.Core.Presentation;
using Itmam.Core.Settings;
using Microsoft.Win32;

namespace Itmam.App.Suggestions;

internal enum ThemeMode
{
    /// <summary>Follows Windows light/dark mode.</summary>
    System,
    Light,
    Dark,
}

/// <summary>User-adjustable look of the suggestion bar (Settings → Appearance).</summary>
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

    /// <summary>Custom background; null = the theme's.</summary>
    public Color? Background { get; init; }

    /// <summary>Custom navigation selection color; null = the Windows accent color.</summary>
    public Color? Highlight { get; init; }

    /// <summary>Custom text color; null = the theme's, or the most readable one on a custom background.</summary>
    public Color? Text { get; init; }

    /// <summary>Window opacity, 0.8–1.</summary>
    public double Opacity { get; init; } = 1;

    /// <summary>Text size in pixels at 100% scaling.</summary>
    public int FontPixels { get; init; } = 14;

    public static PopupAppearance From(AppSettings settings) => new()
    {
        Theme = settings.Theme switch
        {
            ThemeChoice.Light => ThemeMode.Light,
            ThemeChoice.Dark => ThemeMode.Dark,
            _ => ThemeMode.System,
        },
        HighlightFirst = settings.HighlightFirstSuggestion,
        ShowHotkeyHints = settings.ShowHotkeyHints,
        Background = ColorContrast.Parse(settings.BarBackground),
        Highlight = ColorContrast.Parse(settings.BarHighlight),
        Text = ColorContrast.Parse(settings.BarText),
        Opacity = settings.BarOpacity / 100.0,
        FontPixels = settings.BarFontSize,
    };
}

/// <summary>Colors of the bar for one theme.</summary>
internal sealed record PopupColors(Color Background, Color Text, Color Border, Color FirstHighlight, Color Hint)
{
    /// <summary>Navigation selection (arrow keys), drawn translucent over the background.</summary>
    public Color Selection { get; init; } = Color.FromArgb(0x60, 0x00, 0x78, 0xD4);

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

    /// <summary>
    /// The theme's colors with the user's custom background, text and selection colors applied.
    /// Without a custom text color, text on a custom background is black or white, whichever is
    /// readable (WCAG AA); border, first-item highlight and hints are derived from the two.
    /// </summary>
    public static PopupColors For(PopupAppearance appearance)
    {
        var theme = For(appearance.Theme);
        var colors = theme;
        if (appearance.Background is { } background)
        {
            var text = appearance.Text ?? ColorContrast.AutoText(background);
            var lightText = ColorContrast.Luminance(text) > ColorContrast.Luminance(background);
            colors = new PopupColors(
                Background: background,
                Text: text,
                Border: Mix(background, lightText ? Color.Black : Color.FromArgb(0x80, 0x80, 0x80), 0.45),
                FirstHighlight: Color.FromArgb(lightText ? 0x14 : 0x0C, lightText ? Color.White : Color.Black),
                Hint: Mix(background, text, 0.55));
        }
        else if (appearance.Text is { } text)
        {
            colors = theme with { Text = text, Hint = Mix(theme.Background, text, 0.55) };
        }

        return colors with { Selection = Color.FromArgb(0x60, appearance.Highlight ?? AccentColor()) };
    }

    /// <summary>Windows accent color (Settings → Personalization → Colors), for the navigation selection.</summary>
    public static Color AccentColor()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM");
        return key?.GetValue("AccentColor") is int abgr
            ? Color.FromArgb(abgr & 0xFF, (abgr >> 8) & 0xFF, (abgr >> 16) & 0xFF)
            : Color.FromArgb(0x00, 0x78, 0xD4); // Windows default blue
    }

    /// <summary>Windows "app mode" (Settings → Personalization → Colors).</summary>
    public static bool SystemUsesLightTheme()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme") is not int value || value != 0;
    }

    private static Color Mix(Color from, Color to, double amount) => Color.FromArgb(
        (int)Math.Round(from.R + ((to.R - from.R) * amount)),
        (int)Math.Round(from.G + ((to.G - from.G) * amount)),
        (int)Math.Round(from.B + ((to.B - from.B) * amount)));
}
