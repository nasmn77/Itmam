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
    private readonly InputPipeline _pipeline;
    private readonly CancellationTokenSource _stop = new();
    private readonly Label _word = new() { Dock = DockStyle.Top, Height = 70, Font = new Font("Segoe UI", 28f), TextAlign = ContentAlignment.MiddleRight, RightToLeft = RightToLeft.Yes };
    private readonly Label _state = new() { Dock = DockStyle.Top, Height = 48, Font = new Font("Segoe UI", 10f) };
    private readonly Label _health = new() { Dock = DockStyle.Top, Height = 48, Font = new Font("Consolas", 9.5f) };
    private readonly ListBox _log = new() { Dock = DockStyle.Fill, Font = new Font("Consolas", 9.5f), IntegralHeight = false };
    private readonly System.Windows.Forms.Timer _healthTimer = new() { Interval = 500 };
    private int _commits;

    public DebugWindow(InputHookService hooks, ICaretContextSource caretContext)
    {
        _hooks = hooks;
        _pipeline = new InputPipeline(hooks.Events, caretContext);
        _pipeline.Updated += OnPipelineUpdated;
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
        _ = Task.Run(() => _pipeline.RunAsync(_stop.Token));
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _stop.Cancel();
        _healthTimer.Dispose();
        base.OnFormClosed(e);
    }

    // Pipeline thread: only the snapshot crosses to the UI thread.
    private void OnPipelineUpdated(PipelineUpdate update)
    {
        var what = update.Input is { } record
            ? $"{Describe(record.Input),-22} → {update.Result.Outcome,-9} {Stopwatch.GetElapsedTime(record.Timestamp).TotalMilliseconds,6:F2} ms"
            : $"{"Context probe",-22} → {update.Probe} ({update.Letters} letters)";
        var line = $"{DateTime.Now:HH:mm:ss.fff}  {what}";
        if (!IsDisposed)
        {
            BeginInvoke(() => Render(update, line));
        }
    }

    private void Render(PipelineUpdate update, string line)
    {
        if (IsDisposed)
        {
            return;
        }

        _word.Text = update.Word.Length == 0 ? "—" : update.Word;
        if (update.Result.Outcome == BufferOutcome.Committed)
        {
            _commits++;
        }

        _state.Text =
            $"Layout: {(update.IsArabicLayout ? "Arabic" : "other (silent)")}  0x{update.Layout:X8}\n" +
            $"Letters: {update.Letters}   Last: {update.Result.Outcome}   Commits: {_commits}" +
            (update.IsDetached ? "   [paused: caret inside an unseen word]" : string.Empty);

        _log.Items.Insert(0, line);
        if (_log.Items.Count > MaxLogLines)
        {
            _log.Items.RemoveAt(_log.Items.Count - 1);
        }
    }

    private void RenderState(BufferResult result) =>
        Render(new PipelineUpdate(null, result, null, string.Empty, 0, false, 0, false), $"{DateTime.Now:HH:mm:ss.fff}  started");

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
