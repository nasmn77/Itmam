using System.Globalization;
using Itmam.App.Resources;
using Itmam.App.Suggestions;
using Itmam.Core.Presentation;
using Itmam.Core.Settings;

namespace Itmam.App.SettingsUi.Pages;

/// <summary>Appearance: theme, bar colors, opacity, text size, with a live preview.</summary>
internal sealed class AppearancePage(SettingsWindow window) : SettingsPageBase(window)
{
    private BarPreview? _preview;
    private SettingCard? _textCard;

    // Every change shows at once in the preview (and in the real bar).
    public override void Build()
    {
        var settings = App.Settings;
        AddTitle(Strings.NavAppearance);

        AddSection(Strings.SectionPreview);
        _preview = new BarPreview { Appearance = PopupAppearance.From(settings) };
        AddPageControl(_preview);

        AddSection(Strings.SectionTheme);
        var theme = new SegmentedChoice { Options = [Strings.ThemeSystem, Strings.ThemeLight, Strings.ThemeDark] };
        theme.SelectedIndex = (int)settings.Theme;
        theme.SelectedIndexChanged += (_, _) => UpdateLook(s => s with { Theme = (ThemeChoice)theme.SelectedIndex });
        AddCard(Strings.ThemeLabel, null, theme);

        AddSection(Strings.SectionColors);
        var background = Swatches(BarColorPresets.All.Select(p => p.Hex), settings.BarBackground);
        background.ValueChanged += (_, _) => UpdateLook(s => s with { BarBackground = Hex(background.Value) });
        AddCard(Strings.BarBackground, Strings.BarBackgroundHint, background, stacked: true);

        var text = Swatches(BarColorPresets.Texts, settings.BarText);
        text.ValueChanged += (_, _) => UpdateLook(s => s with { BarText = Hex(text.Value) });
        _textCard = AddCard(Strings.BarText, Strings.BarTextHint, text, stacked: true);

        var highlight = Swatches(BarColorPresets.Highlights, settings.BarHighlight);
        highlight.ValueChanged += (_, _) => UpdateLook(s => s with { BarHighlight = Hex(highlight.Value) });
        AddCard(Strings.BarHighlight, Strings.BarHighlightHint, highlight, stacked: true);

        AddSection(Strings.SectionBarLook);
        int[] opacities = [100, 95, 90, 85, 80];
        var opacity = new SegmentedChoice { Options = [.. opacities.Select(o => $"{o}%")] };
        opacity.SelectedIndex = Math.Max(0, Array.IndexOf(opacities, settings.BarOpacity - (settings.BarOpacity % 5)));
        opacity.SelectedIndexChanged += (_, _) => UpdateLook(s => s with { BarOpacity = opacities[opacity.SelectedIndex] });
        AddCard(Strings.BarOpacity, null, opacity);

        int[] sizes = [12, 13, 14, 16, 18, 20];
        var size = new SegmentedChoice { Options = [.. sizes.Select(n => n.ToString(CultureInfo.CurrentCulture))] };
        size.SelectedIndex = Array.IndexOf(sizes, settings.BarFontSize) is var index and >= 0 ? index : Array.IndexOf(sizes, 14);
        size.SelectedIndexChanged += (_, _) => UpdateLook(s => s with { BarFontSize = sizes[size.SelectedIndex] });
        AddCard(Strings.BarFontSize, null, size);

        var first = new ToggleSwitch { Checked = settings.HighlightFirstSuggestion };
        first.CheckedChanged += (_, _) => UpdateLook(s => s with { HighlightFirstSuggestion = first.Checked });
        AddCard(Strings.HighlightFirst, Strings.HighlightFirstHint, first);

        var hints = new ToggleSwitch { Checked = settings.ShowHotkeyHints };
        hints.CheckedChanged += (_, _) => UpdateLook(s => s with { ShowHotkeyHints = hints.Checked });
        AddCard(Strings.HotkeyHints, Strings.HotkeyHintsHint, hints);

        var reset = new ThemedButton { Text = Strings.ResetButton };
        reset.Click += (_, _) =>
        {
            var defaults = new AppSettings();
            UpdateLook(s => s with
            {
                Theme = defaults.Theme,
                BarBackground = null,
                BarHighlight = null,
                BarText = null,
                BarOpacity = defaults.BarOpacity,
                BarFontSize = defaults.BarFontSize,
                HighlightFirstSuggestion = defaults.HighlightFirstSuggestion,
                ShowHotkeyHints = defaults.ShowHotkeyHints,
            });
            BeginInvoke(() => SelectPage(SettingsPage.Appearance)); // rebuild after this click is handled
        };
        AddCard(Strings.ResetLook, Strings.ResetLookHint, reset);
        UpdateContrastWarning();
    }

    private ColorSwatches Swatches(IEnumerable<string> presets, string? selected)
    {
        var swatches = new ColorSwatches([.. presets.Select(h => ColorContrast.Parse(h)!.Value)], Strings.ColorAuto, Strings.ColorCustom)
        {
            Value = ColorContrast.Parse(selected),
        };
        swatches.CustomRequested += (_, _) =>
        {
            using var dialog = new ColorDialog { FullOpen = true, AnyColor = true, Color = swatches.Value ?? Color.White };
            if (dialog.ShowDialog(Window) == DialogResult.OK)
            {
                swatches.Value = Color.FromArgb(dialog.Color.R, dialog.Color.G, dialog.Color.B);
            }
        };
        return swatches;
    }

    private static string? Hex(Color? color) => color is { } c ? ColorContrast.ToHex(c) : null;

    // Saves, then refreshes the preview, the window's own theme and the contrast warning.
    private void UpdateLook(Func<AppSettings, AppSettings> change)
    {
        if (Loading)
        {
            return;
        }

        var theme = App.Settings.Theme;
        Update(change);
        if (_preview is not null)
        {
            _preview.Appearance = PopupAppearance.From(App.Settings);
        }

        if (App.Settings.Theme != theme)
        {
            RefreshTheme();
        }

        UpdateContrastWarning();
    }

    // A custom text color can be hard to read on the bar: say so under "Text color".
    private void UpdateContrastWarning()
    {
        var colors = PopupColors.For(PopupAppearance.From(App.Settings));
        var ratio = ColorContrast.Ratio(colors.Background, colors.Text);
        _textCard?.SetDescription(ratio < ColorContrast.MinimumReadable
            ? string.Format(CultureInfo.CurrentCulture, Strings.ContrastWarning, ratio)
            : Strings.BarTextHint);
    }
}
