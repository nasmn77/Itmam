using System.Drawing;
using System.Globalization;

namespace Itmam.Core.Presentation;

/// <summary>WCAG 2.x contrast helpers, so text on any bar color stays readable.</summary>
public static class ColorContrast
{
    /// <summary>WCAG AA for normal text.</summary>
    public const double MinimumReadable = 4.5;

    /// <summary>Relative luminance (0 black … 1 white).</summary>
    public static double Luminance(Color color)
    {
        static double Channel(byte value)
        {
            var c = value / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        return (0.2126 * Channel(color.R)) + (0.7152 * Channel(color.G)) + (0.0722 * Channel(color.B));
    }

    /// <summary>Contrast ratio between two colors (1 … 21).</summary>
    public static double Ratio(Color a, Color b)
    {
        var (light, dark) = (Luminance(a), Luminance(b));
        if (light < dark)
        {
            (light, dark) = (dark, light);
        }

        return (light + 0.05) / (dark + 0.05);
    }

    /// <summary>
    /// Near-black or white, whichever reads better on <paramref name="background"/>. On mid tones where
    /// neither reaches <see cref="MinimumReadable"/>, pure black is used instead of near-black
    /// (black or white always reaches at least ~4.58:1).
    /// </summary>
    public static Color AutoText(Color background)
    {
        var dark = Color.FromArgb(0x1A, 0x1A, 0x1A);
        var (white, nearBlack) = (Ratio(background, Color.White), Ratio(background, dark));
        if (Math.Max(white, nearBlack) < MinimumReadable)
        {
            dark = Color.Black;
            nearBlack = Ratio(background, dark);
        }

        return white >= nearBlack ? Color.White : dark;
    }

    /// <summary>"#RRGGBB" → color; null for anything else.</summary>
    public static Color? Parse(string? hex)
    {
        if (hex is not { Length: 7 } || hex[0] != '#'
            || !int.TryParse(hex.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
        {
            return null;
        }

        return Color.FromArgb((rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);
    }

    public static string ToHex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";
}

/// <summary>Background presets offered on the Appearance page (each tested for readable auto text).</summary>
public static class BarColorPresets
{
    public static readonly IReadOnlyList<(string Key, string Hex)> All =
    [
        ("White", "#FFFFFF"),
        ("LightGray", "#F3F3F3"),
        ("WarmBeige", "#F3EAD7"),
        ("SoftBlue", "#DCE8F7"),
        ("Charcoal", "#2C2C2C"),
        ("Black", "#111111"),
        ("Navy", "#1E2A44"),
        ("DarkGreen", "#1F3A2E"),
    ];

    /// <summary>Selection colors (Windows accent color choices).</summary>
    public static readonly IReadOnlyList<string> Highlights =
        ["#0078D4", "#0099BC", "#00B294", "#10893E", "#CA5010", "#E81123", "#C239B3", "#8764B8"];

    /// <summary>Text colors besides Automatic.</summary>
    public static readonly IReadOnlyList<string> Texts = ["#FFFFFF", "#1A1A1A"];
}
