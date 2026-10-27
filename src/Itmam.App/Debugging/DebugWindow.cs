using System.Diagnostics;
using Itmam.Core.Input;
using Itmam.Core.Text;
using Itmam.Win32;

namespace Itmam.App.Debugging;

/// <summary>
/// Temporary Phase 2 window: shows the live word buffer, the layout state and hook health.
/// The event list shows event kinds and latencies only, never typed characters.
/// Replaced by the tray application in Phase 6.
/// </summary>
internal sealed class DebugWindow : Form
{
    private const int MaxLogLines = 200;

    private readonly InputHookService _hooks;
    private readonly InputTracker _tracker = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly Label _word = new() { Dock = DockStyle.Top, Height = 70, Font = new Font("Segoe UI", 28f), TextAlign = ContentAlignment.MiddleRight, RightToLeft = RightToLeft.Yes };
    private readonly Label _state = new() { Dock = DockStyle.Top, Height = 48, Font = new Font("Segoe UI", 10f) };
    private readonly Label _health = new() { Dock = DockStyle.Top, Height = 48, Font = new Font("Consolas", 9.5f) };
    private readonly ListBox _log = new() { Dock = DockStyle.Fill, Font = new Font("Consolas", 9.5f), IntegralHeight = false };
    private readonly System.Windows.Forms.Timer _healthTimer = new() { Interval = 500 };
    private int _commits;

    public DebugWindow(InputHookService hooks)
    {
        _hooks = hooks;
        Text = "Itmam — debug (Phase 2)";
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        Size = new Size(560, 520);
        Location = new Point(Screen.PrimaryScreen!.WorkingArea.Right - Width - 24, Screen.PrimaryScreen.WorkingArea.Top + 24);

        Controls.Add(_log);
        Controls.Add(_health);
        Controls.Add(_state);
        Controls.Add(_word);

        _healthTimer.Tick += (_, _) => ShowHealth();
        RenderState(new BufferResult(BufferOutcome.None));
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        _healthTimer.Start();
        _ = Task.Run(() => ReadEventsAsync(_stop.Token));
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _stop.Cancel();
        _healthTimer.Dispose();
        base.OnFormClosed(e);
    }

    // Worker thread: owns the tracker; only snapshots cross to the UI thread.
    private async Task ReadEventsAsync(CancellationToken cancellation)
    {
        try
        {
            await foreach (var hookEvent in _hooks.Events.ReadAllAsync(cancellation))
            {
                var result = _tracker.Process(hookEvent.Input, hookEvent.KeyboardLayout);
                var latency = Stopwatch.GetElapsedTime(hookEvent.Timestamp);
                var line = $"{DateTime.Now:HH:mm:ss.fff}  {Describe(hookEvent.Input),-22} → {result.Outcome,-9} {latency.TotalMilliseconds,6:F2} ms";
                var snapshot = (Text: _tracker.Buffer.Text, Letters: _tracker.Buffer.LetterCount, Arabic: _tracker.IsArabicLayout, Layout: _tracker.Layout, Result: result);
                BeginInvoke(() => Render(snapshot, line));
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void Render((string Text, int Letters, bool Arabic, nint Layout, BufferResult Result) snapshot, string line)
    {
        if (IsDisposed)
        {
            return;
        }

        _word.Text = snapshot.Text.Length == 0 ? "—" : snapshot.Text;
        if (snapshot.Result.Outcome == BufferOutcome.Committed)
        {
            _commits++;
        }

        _state.Text =
            $"Layout: {(snapshot.Arabic ? "Arabic" : "other (silent)")}  0x{snapshot.Layout:X8}\n" +
            $"Letters: {snapshot.Letters}   Last: {snapshot.Result.Outcome}   Commits: {_commits}";

        _log.Items.Insert(0, line);
        if (_log.Items.Count > MaxLogLines)
        {
            _log.Items.RemoveAt(_log.Items.Count - 1);
        }
    }

    private void RenderState(BufferResult result) =>
        Render((string.Empty, 0, false, 0, result), $"{DateTime.Now:HH:mm:ss.fff}  started");

    private void ShowHealth()
    {
        var d = _hooks.Diagnostics;
        _health.Text =
            $"keys {d.KeyEvents}  clicks {d.MouseClicks}  focus {d.FocusEvents} (+{d.RepeatedFocusEvents} repeated, ignored)  reinstalls {d.Reinstalls}  errors {d.CallbackErrors}\n" +
            $"hook callback: last {d.LastCallback.TotalMicroseconds:F0} µs, max {d.MaxCallback.TotalMicroseconds:F0} µs, " +
            $"max after first {HookDiagnostics.SteadyAfterKeys} keys {d.MaxCallbackSteady.TotalMicroseconds:F0} µs";
    }

    // Event kind only: letters are reported as a category, never as the character itself.
    private static string Describe(InputEvent input) => input.Kind switch
    {
        InputEventKind.Character when ArabicText.IsArabicLetter(input.Character) => "Character (letter)",
        InputEventKind.Character when ArabicText.IsIgnorable(input.Character) => "Character (mark)",
        InputEventKind.Character when ArabicText.IsWordBoundary(input.Character) => "Character (boundary)",
        InputEventKind.Character => "Character (other)",
        _ => input.Kind.ToString(),
    };
}
