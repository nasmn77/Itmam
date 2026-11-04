namespace Itmam.Core.Settings;

/// <summary>A window's client size at 100 % scaling (96 DPI), and whether it was maximized.</summary>
/// <param name="Width">Client width in pixels at 96 DPI (the normal size, also when maximized).</param>
/// <param name="Height">Client height in pixels at 96 DPI.</param>
/// <param name="Maximized">The window was maximized.</param>
public sealed record WindowSize(int Width, int Height, bool Maximized = false)
{
    /// <summary>The smallest client width kept (the window's own minimum).</summary>
    public const int MinWidth = 420;

    /// <summary>The smallest client height kept.</summary>
    public const int MinHeight = 360;

    /// <summary>A larger value is a damaged file, not a window.</summary>
    public const int MaxSide = 20_000;
}
