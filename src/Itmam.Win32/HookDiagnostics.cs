namespace Itmam.Win32;

/// <summary>
/// Counters written by the hook thread and read (approximately) by the UI. Contains no typed
/// characters, only counts and timings.
/// </summary>
public sealed class HookDiagnostics
{
    private long _maxCallbackTicks;
    private long _lastCallbackTicks;

    public long KeyEvents { get; internal set; }

    public long MouseClicks { get; internal set; }

    public long FocusEvents { get; internal set; }

    public long Reinstalls { get; internal set; }

    public long CallbackErrors { get; internal set; }

    public TimeSpan LastCallback => TimeSpan.FromTicks(Volatile.Read(ref _lastCallbackTicks));

    public TimeSpan MaxCallback => TimeSpan.FromTicks(Volatile.Read(ref _maxCallbackTicks));

    public void ResetMax() => Volatile.Write(ref _maxCallbackTicks, 0);

    internal void RecordCallback(TimeSpan duration)
    {
        Volatile.Write(ref _lastCallbackTicks, duration.Ticks);
        if (duration.Ticks > Volatile.Read(ref _maxCallbackTicks))
        {
            Volatile.Write(ref _maxCallbackTicks, duration.Ticks);
        }
    }
}
