using System.Drawing;

namespace Itmam.Core.Presentation;

/// <summary>
/// Proportions of the suggestion bar at 96 DPI, measured from the native Windows 11 text
/// suggestions bar: 35 px high, 14 px Segoe UI, ~15 px padding on each side of a word and no
/// dividers (words are separated by spacing only).
/// </summary>
public sealed record SuggestionBarMetrics
{
    public int Height { get; init; } = 35;

    public float FontPixels { get; init; } = 14f;

    /// <summary>Horizontal space on each side of a word.</summary>
    public int ItemPadding { get; init; } = 15;

    /// <summary>Space between the bar edge and the first/last item.</summary>
    public int BarPadding { get; init; } = 4;

    /// <summary>Inset of the (optional) first-item highlight inside the bar.</summary>
    public int HighlightInset { get; init; } = 4;

    public int HighlightRadius { get; init; } = 4;

    /// <summary>Gap between the caret and the bar.</summary>
    public int CaretGap { get; init; } = 6;

    /// <summary>
    /// Metrics for a text size other than the native 14 px: the bar height keeps the native
    /// 35 : 14 proportion; padding stays the same.
    /// </summary>
    public static SuggestionBarMetrics ForFontSize(int pixels)
    {
        var standard = new SuggestionBarMetrics();
        return standard with
        {
            FontPixels = pixels,
            Height = Round(standard.Height * pixels / standard.FontPixels),
        };
    }

    public SuggestionBarMetrics Scale(float factor) => new()
    {
        Height = Round(Height * factor),
        FontPixels = FontPixels * factor,
        ItemPadding = Round(ItemPadding * factor),
        BarPadding = Round(BarPadding * factor),
        HighlightInset = Round(HighlightInset * factor),
        HighlightRadius = Round(HighlightRadius * factor),
        CaretGap = Round(CaretGap * factor),
    };

    private static int Round(float value) => (int)MathF.Round(value, MidpointRounding.AwayFromZero);
}

/// <summary>Item rectangles inside the bar (bar coordinates), in suggestion order (best first).</summary>
public sealed record SuggestionBarLayout(Size Size, IReadOnlyList<Rectangle> Items)
{
    /// <param name="textWidths">Measured width of each suggestion, best first.</param>
    /// <param name="metrics">Metrics already scaled to the target DPI.</param>
    /// <param name="rightToLeft">Arabic: the best suggestion is the rightmost item.</param>
    public static SuggestionBarLayout Compute(IReadOnlyList<int> textWidths, SuggestionBarMetrics metrics, bool rightToLeft)
    {
        var itemWidths = textWidths.Select(w => w + (2 * metrics.ItemPadding)).ToArray();
        var width = itemWidths.Sum() + (2 * metrics.BarPadding);
        var items = new Rectangle[itemWidths.Length];

        var x = rightToLeft ? width - metrics.BarPadding : metrics.BarPadding;
        for (var i = 0; i < itemWidths.Length; i++)
        {
            var left = rightToLeft ? x - itemWidths[i] : x;
            items[i] = new Rectangle(left, 0, itemWidths[i], metrics.Height);
            x = rightToLeft ? left : left + itemWidths[i];
        }

        return new SuggestionBarLayout(new Size(width, metrics.Height), items);
    }
}
