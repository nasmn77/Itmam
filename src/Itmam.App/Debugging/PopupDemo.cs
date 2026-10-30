#if DEBUG
using Itmam.App.Suggestions;

namespace Itmam.App.Debugging;

/// <summary>Debug builds only: shows the bar at a fixed caret position for 5 seconds, for screenshots.</summary>
internal static class PopupDemo
{
    /// <summary>
    /// Store screenshots: the real suggestions for <paramref name="typed"/> (dictionary + learned words),
    /// shown at the real caret of the foreground window (automated typing is ignored by the hook, so a
    /// script cannot make the bar appear by typing). Stays for <paramref name="seconds"/>.
    /// </summary>
    public static void RunAtCaret(string typed, int seconds)
    {
        using var app = new ItmamApp(new SynchronizationContext());
        IReadOnlyList<string> words;
        lock (app.Learning.SyncRoot)
        {
            words = [.. app.Engine.Suggest(typed).Select(s => s.Word)];
        }

        var watch = System.Diagnostics.Stopwatch.StartNew();
        var caret = Task.Run(() => new Win32.CaretLocator(app.UiAutomation).LocateAsync(rightToLeft: true, CancellationToken.None)).GetAwaiter().GetResult();

        // Diagnostics for caret problems: which method found the caret, where, and how long it took.
        File.AppendAllText(Path.Combine(Path.GetTempPath(), "itmam-caret.txt"),
            FormattableString.Invariant($"{DateTime.Now:HH:mm:ss} {caret.Source} {caret.Bounds} {watch.ElapsedMilliseconds} ms{Environment.NewLine}"));
        _ = app.Popup.Handle;
        app.Popup.ShowSuggestions(words, caret.Bounds, rightToLeft: true, belowPointer: caret.Source == Win32.CaretSource.Mouse);

        using var timer = new System.Windows.Forms.Timer { Interval = seconds * 1000 };
        timer.Tick += (_, _) => Application.ExitThread();
        timer.Start();
        Application.Run();
    }

    // Caret context check (web editors): how many characters UI Automation returns before the caret of
    // the focused element. Logs the count and status only, never the text.
    public static void ReadContext()
    {
        using var worker = new Win32.UiaWorker();
        var source = new Win32.UiaCaretContextSource(worker, _ => false);
        var context = Task.Run(() => source.ReadBeforeCaretAsync(49, CancellationToken.None)).GetAwaiter().GetResult();
        File.AppendAllText(Path.Combine(Path.GetTempPath(), "itmam-context.txt"),
            FormattableString.Invariant($"{DateTime.Now:HH:mm:ss} {context.Status} {context.Text.Length} chars{Environment.NewLine}"));
    }

    public static void Run(int caretX, int caretY, string[] options)
    {
        var theme = options.Contains("light") ? ThemeMode.Light : options.Contains("dark") ? ThemeMode.Dark : ThemeMode.System;
        using var popup = new SuggestionPopup(new Core.Presentation.PopupState())
        {
            Appearance = new PopupAppearance
            {
                Theme = theme,
                HighlightFirst = !options.Contains("nohighlight"),
                ShowHotkeyHints = options.Contains("hints"),
                Background = options.Contains("navy") ? Color.FromArgb(0x1E, 0x2A, 0x44) : null,
                Opacity = options.Contains("translucent") ? 0.85 : 1,
                FontPixels = options.Contains("large") ? 18 : 14,
            },
        };
        _ = popup.Handle;
        popup.ShowSuggestions(["مرحبا", "مرحلة", "مرحى"], new Rectangle(caretX, caretY, 2, 20), rightToLeft: true, belowPointer: false);

        // "hide": hide after 1 s (verifies hiding), then exit at 5 s.
        using var hideTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        hideTimer.Tick += (_, _) => { hideTimer.Stop(); popup.HideSuggestions(); };
        if (options.Contains("hide"))
        {
            hideTimer.Start();
        }

        using var timer = new System.Windows.Forms.Timer { Interval = 5000 };
        timer.Tick += (_, _) => Application.ExitThread();
        timer.Start();
        Application.Run();
    }
}
#endif
