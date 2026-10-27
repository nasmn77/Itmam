namespace Itmam.Win32;

/// <summary>
/// Counters written by the hook thread and read (approximately) by the UI. Contains no typed
/// characters, only counts and timings.
/// </summary>
public sealed class HookDiagnostics
{
    /// <summary>Key presses after which <see cref="MaxCallbackSteady"/> starts recording.</summary>
    public const int SteadyAfterKeys = 10;

    private long _maxCallbackTicks;
    private long _maxSteadyTicks;
    private long _lastCallbackTicks;

    public long KeyEvents { get; internal set; }

    public long MouseClicks { get; internal set; }

    public long FocusEvents { get; internal set; }

    /// <summary>Focus events for the element that already had focus (ignored).</summary>
    public long RepeatedFocusEvents { get; internal set; }

    public long Reinstalls { get; internal set; }

    public long CallbackErrors { get; internal set; }

    public TimeSpan LastCallback => TimeSpan.FromTicks(Volatile.Read(ref _lastCallbackTicks));

    /// <summary>Slowest keyboard callback since start, including the first key presses.</summary>
    public TimeSpan MaxCallback => TimeSpan.FromTicks(Volatile.Read(ref _maxCallbackTicks));

    /// <summary>Slowest keyboard callback after the first <see cref="SteadyAfterKeys"/> key presses.</summary>
    public TimeSpan MaxCallbackSteady => TimeSpan.FromTicks(Volatile.Read(ref _maxSteadyTicks));

    public void ResetMax()
    {
        Volatile.Write(ref _maxCallbackTicks, 0);
        Volatile.Write(ref _maxSteadyTicks, 0);
    }

    internal void RecordCallback(TimeSpan duration)
    {
        Volatile.Write(ref _lastCallbackTicks, duration.Ticks);
        if (duration.Ticks > Volatile.Read(ref _maxCallbackTicks))
        {
            Volatile.Write(ref _maxCallbackTicks, duration.Ticks);
        }

        if (KeyEvents > SteadyAfterKeys && duration.Ticks > Volatile.Read(ref _maxSteadyTicks))
        {
            Volatile.Write(ref _maxSteadyTicks, duration.Ticks);
        }
    }
}
