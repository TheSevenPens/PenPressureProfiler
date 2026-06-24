namespace PenPressureProfiler.Model;

/// <summary>
/// User-adjustable application options, surfaced in the <b>Tools ▸ Options</b>
/// dialog. UI-free; <c>MainWindow</c> owns the live values and applies a copy
/// returned from the dialog.
/// </summary>
public sealed class AppOptions
{
    /// <summary>Accumulator mode: time-align the pen feed to the slower/lagging
    /// scale by the measured response lag
    /// (<see cref="Sessions.ScaleSessionManager.ResponseLagMs"/>) while
    /// accumulating. On by default.</summary>
    public bool ScaleLagComp { get; set; } = true;

    /// <summary>Accumulator mode: only record scale samples while the pen is in
    /// proximity (skip the tablet's resting weight when the pen is lifted away).
    /// <b>Off</b> by default — the original behaviour records regardless of
    /// proximity.</summary>
    public bool AccumulatorRequirePenProximity { get; set; }
}
