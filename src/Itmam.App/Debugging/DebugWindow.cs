#if DEBUG
using System.Diagnostics;
using Itmam.App.Suggestions;
using Itmam.Core.Input;
using Itmam.Core.Text;
using Itmam.Win32;

namespace Itmam.App.Debugging;

/// <summary>
/// Diagnostics window, Debug builds only (tray → Diagnostics): the live word buffer, the layout
/// state and hook health. The event list shows event kinds and latencies only, never characters.
/// </summary>
internal sealed class DebugWindow : Form
{
    private const int MaxLogLines = 200;

    private readonly InputHookService _hooks;
    private readonly InputPipeline _pipeline;
    private readonly SuggestionController _suggestions;
    private readonly Label _word = new() { Dock = DockStyle.Top, Height = 70, Font = new Font("Segoe UI", 28f), TextAlign = ContentAlignment.MiddleRight, RightToLeft = RightToLeft.Yes };
    private readonly Label _state = new() { Dock = DockStyle.Top, Height = 66, Font = new Font("Segoe UI", 10f) };
    private readonly Label _health = new() { Dock = DockStyle.Top, Height = 66, Font = new Font("Consolas", 9.5f) };
    private readonly ListBox _log = new() { Dock = DockStyle.Fill, Font = new Font("Consolas", 9.5f), IntegralHeight = false };
    private readonly System.Windows.Forms.Timer _healthTimer = new() { Interval = 500 };
    private readonly Func<int> _learnedCount;
    private int _commits;
    private int _learned;
    private string _lastShown = "—";

    public DebugWindow(InputHookService hooks, InputPipeline pipeline, SuggestionController suggestions, string? recoveredBackup, Func<int> learnedCount)
    {
        _hooks = hooks;
        _pipeline = pipeline;
        _suggestions = suggestions;
        _learnedCount = learnedCount;
        pipeline.Updated += OnPipelineUpdated;
        suggestions.Shown += OnShown;
        Text = "Itmam — diagnostics";
        if (recoveredBackup is not null)
        {
            _log.Items.Add($"user.db was damaged: kept as {Path.GetFileName(recoveredBackup)}, started fresh");
        }
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        Size = new Size(760, 520);
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
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _pipeline.Updated -= OnPipelineUpdated;
        _suggestions.Shown -= OnShown;
        _healthTimer.Dispose();
        base.OnFormClosed(e);
    }

    private void OnShown(SuggestionShown shown) => _lastShown =
        $"{shown.Count} suggestions, caret via {shown.Source}, {shown.Latency.TotalMilliseconds:F1} ms after the key";

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

        if (update.WordToLearn is not null)
        {
            _learned++;
        }

        _state.Text =
            $"Layout: {(update.IsArabicLayout ? "Arabic" : "other (silent)")}  0x{update.Layout:X8}" +
            (update.Silenced != SilenceReason.None ? $"   SILENT: {update.Silenced}" : string.Empty) + "\n" +
            $"Letters: {update.Letters}   Last: {update.Result.Outcome}   Commits: {_commits}   Learned: {_learned} (user words: {_learnedCount()})" +
            (update.IsDetached ? "   [paused: caret inside an unseen word]" : string.Empty) +
            $"\nBar: {_lastShown}";

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
            $"{(_hooks.IsEnabled ? "ENABLED" : "DISABLED (toggle shortcut)")}   keys {d.KeyEvents}  clicks {d.MouseClicks}  focus {d.FocusEvents} (+{d.RepeatedFocusEvents} repeated, ignored)  reinstalls {d.Reinstalls}  errors {d.CallbackErrors}\n" +
            $"hook callback: p99 {d.P99.TotalMicroseconds:F0} µs {Verdict(d.P99 < HookDiagnostics.P99Target)} (< {HookDiagnostics.P99Target.TotalMicroseconds:F0}), " +
            $"max {d.MaxCallback.TotalMicroseconds:F0} µs {Verdict(d.MaxCallback < HookDiagnostics.MaxTarget)} (< {HookDiagnostics.MaxTarget.TotalMicroseconds:F0}), " +
            $"last {d.LastCallback.TotalMicroseconds:F0} µs, max after {HookDiagnostics.SteadyAfterKeys} keys {d.MaxCallbackSteady.TotalMicroseconds:F0} µs\n" +
            $"slow (> {HookDiagnostics.SlowThreshold.TotalMicroseconds:F0} µs): {d.SlowCallbacks}, of which during GC: {d.SlowCallbacksDuringGc}   " +
            $"process GCs gen0/1/2: {GC.CollectionCount(0)}/{GC.CollectionCount(1)}/{GC.CollectionCount(2)}";
    }

    private static string Verdict(bool ok) => ok ? "OK" : "OVER";

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
#endif
