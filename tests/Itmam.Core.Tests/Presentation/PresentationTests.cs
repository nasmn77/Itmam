using System.Drawing;
using Itmam.Core.Presentation;

namespace Itmam.Core.Tests.Presentation;

public class SuggestionBarLayoutTests
{
    private static readonly SuggestionBarMetrics Metrics = new();

    [Fact]
    public void Right_to_left_puts_the_best_suggestion_on_the_right()
    {
        var layout = SuggestionBarLayout.Compute([40, 30, 50], Metrics, rightToLeft: true);

        layout.Items[0].Right.ShouldBe(layout.Size.Width - Metrics.BarPadding);
        layout.Items[0].Left.ShouldBeGreaterThan(layout.Items[1].Left);
        layout.Items[1].Left.ShouldBeGreaterThan(layout.Items[2].Left);
        layout.Items[2].Left.ShouldBe(Metrics.BarPadding);
    }

    [Fact]
    public void Left_to_right_puts_the_best_suggestion_on_the_left()
    {
        var layout = SuggestionBarLayout.Compute([40, 30], Metrics, rightToLeft: false);

        layout.Items[0].Left.ShouldBe(Metrics.BarPadding);
        layout.Items[1].Left.ShouldBe(layout.Items[0].Right);
    }

    [Fact]
    public void Size_matches_the_native_proportions()
    {
        // Native bar: "hey here hello" (ink 21/26/29 px) measured 167 px wide, 35 px high at 100%.
        var layout = SuggestionBarLayout.Compute([21, 26, 29], Metrics, rightToLeft: false);

        layout.Size.Height.ShouldBe(35);
        layout.Size.Width.ShouldBeInRange(160, 175);
        layout.Items.ShouldAllBe(item => item.Height == 35);
    }

    [Fact]
    public void Items_are_adjacent_without_dividers()
    {
        var layout = SuggestionBarLayout.Compute([40, 30, 50], Metrics, rightToLeft: true);

        layout.Items[1].Right.ShouldBe(layout.Items[0].Left);
        layout.Items[2].Right.ShouldBe(layout.Items[1].Left);
    }

    [Fact]
    public void Metrics_scale_with_dpi()
    {
        var scaled = Metrics.Scale(1.5f);

        scaled.Height.ShouldBe(53);
        scaled.FontPixels.ShouldBe(21f);
        scaled.ItemPadding.ShouldBe(23);
    }
}

public class PopupPlacementTests
{
    private static readonly Rectangle Primary = new(0, 0, 1920, 1040);          // work area above the taskbar
    private static readonly Rectangle LeftMonitor = new(-2560, -200, 2560, 1400); // second monitor, negative coordinates
    private static readonly Size Popup = new(200, 35);
    private const int Gap = 6;

    [Fact]
    public void Arabic_bar_sits_above_the_caret_and_extends_left_from_it()
    {
        var caret = new Rectangle(800, 500, 2, 20);

        var position = PopupPlacement.Place(caret, Popup, Primary, rightToLeft: true, Gap);

        position.ShouldBe(new Point(800 - 200, 500 - Gap - 35));
    }

    [Fact]
    public void Left_to_right_bar_starts_at_the_caret()
    {
        var caret = new Rectangle(800, 500, 2, 20);

        PopupPlacement.Place(caret, Popup, Primary, rightToLeft: false, Gap).X.ShouldBe(802);
    }

    [Fact]
    public void Near_the_top_edge_it_goes_below_the_caret()
    {
        var caret = new Rectangle(800, 10, 2, 20);

        var position = PopupPlacement.Place(caret, Popup, Primary, rightToLeft: true, Gap);

        position.Y.ShouldBe(caret.Bottom + Gap);
    }

    [Fact]
    public void Near_the_left_edge_an_arabic_bar_is_clamped_inside()
    {
        var caret = new Rectangle(50, 500, 2, 20);

        PopupPlacement.Place(caret, Popup, Primary, rightToLeft: true, Gap).X.ShouldBe(0);
    }

    [Fact]
    public void Near_the_right_edge_it_is_clamped_inside()
    {
        var caret = new Rectangle(1900, 500, 2, 20);

        PopupPlacement.Place(caret, Popup, Primary, rightToLeft: false, Gap).X.ShouldBe(1920 - 200);
    }

    [Fact]
    public void Near_the_bottom_it_stays_above_the_taskbar()
    {
        var caret = new Rectangle(800, 1030, 2, 20);

        var position = PopupPlacement.Place(caret, Popup, Primary, rightToLeft: true, Gap);

        position.Y.ShouldBe(1030 - Gap - 35);
        (position.Y + Popup.Height).ShouldBeLessThanOrEqualTo(Primary.Bottom);
    }

    [Fact]
    public void Works_on_a_monitor_with_negative_coordinates()
    {
        var caret = new Rectangle(-2500, -190, 2, 20); // near the top-left of the left monitor (top is -200)

        var position = PopupPlacement.Place(caret, Popup, LeftMonitor, rightToLeft: true, Gap);

        position.X.ShouldBe(-2560);
        position.Y.ShouldBe(caret.Bottom + Gap);
    }

    [Fact]
    public void Mouse_fallback_goes_below_the_pointer_unless_at_the_bottom()
    {
        var pointer = new Rectangle(800, 500, 1, 24);
        PopupPlacement.Place(pointer, Popup, Primary, rightToLeft: true, Gap, preferBelow: true).Y.ShouldBe(524 + Gap);

        var low = new Rectangle(800, 1010, 1, 24);
        PopupPlacement.Place(low, Popup, Primary, rightToLeft: true, Gap, preferBelow: true).Y.ShouldBe(1010 - Gap - 35);
    }

    [Fact]
    public void Never_leaves_a_work_area_smaller_than_the_popup()
    {
        var tiny = new Rectangle(0, 0, 100, 20);

        PopupPlacement.Place(new Rectangle(50, 5, 2, 10), Popup, tiny, rightToLeft: true, Gap).ShouldBe(new Point(0, 0));
    }
}
