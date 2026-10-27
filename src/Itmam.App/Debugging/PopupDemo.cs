#if DEBUG
using Itmam.App.Suggestions;

namespace Itmam.App.Debugging;

/// <summary>Debug builds only: shows the bar at a fixed caret position for 5 seconds, for screenshots.</summary>
internal static class PopupDemo
{
    public static void Run(int caretX, int caretY, string[] options)
    {
        var theme = options.Contains("light") ? ThemeMode.Light : options.Contains("dark") ? ThemeMode.Dark : ThemeMode.System;
        using var popup = new SuggestionPopup
        {
            Appearance = new PopupAppearance { Theme = theme, HighlightFirst = !options.Contains("nohighlight"), ShowHotkeyHints = options.Contains("hints") },
        };
        _ = popup.Handle;
        popup.ShowSuggestions(["مرحبا", "مرحلة", "مرحى"], new Rectangle(caretX, caretY, 2, 20), rightToLeft: true, belowPointer: false);

        using var timer = new System.Windows.Forms.Timer { Interval = 5000 };
        timer.Tick += (_, _) => Application.ExitThread();
        timer.Start();
        Application.Run();
    }
}
#endif
