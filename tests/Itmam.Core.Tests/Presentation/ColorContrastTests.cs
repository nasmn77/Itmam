using System.Drawing;
using Itmam.Core.Presentation;
using Itmam.Core.Settings;

namespace Itmam.Core.Tests.Presentation;

public class ColorContrastTests
{
    [Fact]
    public void Black_on_white_is_21_to_1() =>
        ColorContrast.Ratio(Color.Black, Color.White).ShouldBe(21, tolerance: 0.01);

    [Fact]
    public void Same_color_is_1_to_1() =>
        ColorContrast.Ratio(Color.Teal, Color.Teal).ShouldBe(1, tolerance: 0.0001);

    [Fact]
    public void Every_preset_gets_readable_automatic_text()
    {
        foreach (var (key, hex) in BarColorPresets.All)
        {
            var background = ColorContrast.Parse(hex)!.Value;
            var ratio = ColorContrast.Ratio(background, ColorContrast.AutoText(background));

            ratio.ShouldBeGreaterThanOrEqualTo(ColorContrast.MinimumReadable, $"{key} {hex}");
        }
    }

    [Theory]
    [InlineData("#FFFFFF", "#1A1A1A")]
    [InlineData("#2C2C2C", "#FFFFFF")]
    [InlineData("#1E2A44", "#FFFFFF")]
    [InlineData("#F3EAD7", "#1A1A1A")]
    public void Automatic_text_is_dark_on_light_and_white_on_dark(string background, string expected) =>
        ColorContrast.ToHex(ColorContrast.AutoText(ColorContrast.Parse(background)!.Value)).ShouldBe(expected);

    [Fact]
    public void Arbitrary_custom_colors_always_get_readable_text()
    {
        var random = new Random(7);
        for (var i = 0; i < 2000; i++)
        {
            var background = Color.FromArgb(random.Next(256), random.Next(256), random.Next(256));

            ColorContrast.Ratio(background, ColorContrast.AutoText(background))
                .ShouldBeGreaterThanOrEqualTo(ColorContrast.MinimumReadable, ColorContrast.ToHex(background));
        }
    }

    [Theory]
    [InlineData("#1E2A44", true)]
    [InlineData("#abcdef", true)]
    [InlineData("1E2A44", false)]
    [InlineData("#12345", false)]
    [InlineData("#GGGGGG", false)]
    [InlineData(null, false)]
    public void Hex_colors_are_validated(string? hex, bool valid) =>
        (ColorContrast.Parse(hex) is not null).ShouldBe(valid);

    [Fact]
    public void Appearance_settings_are_clamped_and_invalid_colors_dropped()
    {
        var settings = new AppSettings { BarOpacity = 10, BarFontSize = 99, BarBackground = "red", BarText = "#000000" }.Normalized();

        settings.BarOpacity.ShouldBe(80);
        settings.BarFontSize.ShouldBe(20);
        settings.BarBackground.ShouldBeNull();
        settings.BarText.ShouldBe("#000000");
    }
}
