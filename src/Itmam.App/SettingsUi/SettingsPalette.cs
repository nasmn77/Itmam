using Itmam.App.Suggestions;
using Itmam.Core.Settings;

namespace Itmam.App.SettingsUi;

/// <summary>Colors of the settings window, modeled on the Windows 11 Settings app.</summary>
internal sealed record SettingsPalette(
    bool IsDark,
    Color Window,
    Color Card,
    Color CardBorder,
    Color Text,
    Color SecondaryText,
    Color NavHover,
    Color NavSelected,
    Color ControlFill,
    Color ControlBorder,
    Color Accent,
    Color OnAccent)
{
    public static SettingsPalette For(ThemeChoice theme)
    {
        var dark = theme switch
        {
            ThemeChoice.Dark => true,
            ThemeChoice.Light => false,
            _ => !PopupColors.SystemUsesLightTheme(),
        };
        var accent = PopupColors.AccentColor();

        return dark
            ? new SettingsPalette(
                IsDark: true,
                Window: Color.FromArgb(0x20, 0x20, 0x20),
                Card: Color.FromArgb(0x2B, 0x2B, 0x2B),
                CardBorder: Color.FromArgb(0x1D, 0x1D, 0x1D),
                Text: Color.White,
                SecondaryText: Color.FromArgb(0xC5, 0xC5, 0xC5),
                NavHover: Color.FromArgb(0x2D, 0x2D, 0x2D),
                NavSelected: Color.FromArgb(0x32, 0x32, 0x32),
                ControlFill: Color.FromArgb(0x37, 0x37, 0x37),
                ControlBorder: Color.FromArgb(0x5A, 0x5A, 0x5A),
                Accent: Lighten(accent),
                OnAccent: Color.Black)
            : new SettingsPalette(
                IsDark: false,
                Window: Color.FromArgb(0xF3, 0xF3, 0xF3),
                Card: Color.FromArgb(0xFB, 0xFB, 0xFB),
                CardBorder: Color.FromArgb(0xE5, 0xE5, 0xE5),
                Text: Color.FromArgb(0x1A, 0x1A, 0x1A),
                SecondaryText: Color.FromArgb(0x5D, 0x5D, 0x5D),
                NavHover: Color.FromArgb(0xEA, 0xEA, 0xEA),
                NavSelected: Color.FromArgb(0xE6, 0xE6, 0xE6),
                ControlFill: Color.White,
                ControlBorder: Color.FromArgb(0x8A, 0x8A, 0x8A),
                Accent: accent,
                OnAccent: Color.White);
    }

    // Dark mode uses a lighter accent so it stays readable on dark backgrounds (as Windows does).
    private static Color Lighten(Color color) => Color.FromArgb(
        color.R + ((255 - color.R) * 2 / 5),
        color.G + ((255 - color.G) * 2 / 5),
        color.B + ((255 - color.B) * 2 / 5));
}
