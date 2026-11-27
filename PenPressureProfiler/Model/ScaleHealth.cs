namespace PenPressureProfiler.Model;

/// <summary>The scale connection's health, as shown by the navigation's status dot.</summary>
public enum ScaleHealthState
{
    /// <summary>Not working: no COM port, not reading, or the last attempt failed.</summary>
    Off,
    /// <summary>Reading has started but no data is arriving (scale off, wrong port, cable).</summary>
    NoData,
    /// <summary>Connected, listening and receiving readings.</summary>
    Live,
}

public static class ScaleHealth
{
    /// <summary>
    /// How long without a reading before a running scale counts as silent. The
    /// scale reports about every 0.1 s, so 1 s is ten missed readings: long
    /// enough to ride out a hiccup, short enough to notice quickly.
    /// </summary>
    public static readonly TimeSpan DataTimeout = TimeSpan.FromSeconds(1);

    public static ScaleHealthState Evaluate(bool isReading, bool hasError,
                                            DateTime? lastReadingUtc, DateTime nowUtc)
    {
        if (!isReading || hasError) return ScaleHealthState.Off;
        if (lastReadingUtc is { } last && nowUtc - last <= DataTimeout) return ScaleHealthState.Live;
        return ScaleHealthState.NoData;
    }
}
