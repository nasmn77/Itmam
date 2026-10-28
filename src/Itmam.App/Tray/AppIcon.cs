using Itmam.Win32;

namespace Itmam.App.Tray;

/// <summary>Icons drawn at run time from <see cref="AppIconArt"/>, crisp at the current DPI.</summary>
internal static class AppIcon
{
    /// <param name="size">Pixel size (e.g. the tray's small icon size for the current DPI).</param>
    /// <param name="enabled">Off is drawn in gray.</param>
    public static Icon Create(int size, bool enabled)
    {
        using var bitmap = AppIconArt.Render(size, enabled);
        var handle = bitmap.GetHicon();
        try
        {
            using var borrowed = Icon.FromHandle(handle);
            return (Icon)borrowed.Clone(); // owns its own native copy
        }
        finally
        {
            WindowChrome.DestroyIcon(handle);
        }
    }
}
