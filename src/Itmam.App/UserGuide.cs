using System.ComponentModel;
using System.Diagnostics;
using Itmam.Core.Settings;

namespace Itmam.App;

/// <summary>
/// The user guide (docs/guide, built by tools/Build-Guide.ps1): one PDF per interface language, shipped
/// in the package and opened in the user's PDF viewer. Nothing is downloaded.
/// </summary>
internal static class UserGuide
{
    public static string PathFor(UiLanguage language) =>
        Path.Combine(AppContext.BaseDirectory, "guide", language == UiLanguage.Arabic ? "itmam-guide-ar.pdf" : "itmam-guide-en.pdf");

    /// <summary>Opens the guide; false when the file is missing or no app can open PDF files.</summary>
    public static bool Open(UiLanguage language)
    {
        var path = PathFor(language);
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            return true;
        }
        catch (Win32Exception)
        {
            return false;
        }
    }
}
