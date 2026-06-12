namespace PenPressureProfiler.Detection;

/// <summary>
/// Records IAF estimates from <b>push</b> sweeps. The user lifts the pen so the
/// scale reaches ≤ <see cref="MaxRestingGf"/> (the "rest" floor) and then presses
/// down slowly until the pen first reports a non-zero raw value.
///
/// The estimate brackets the activation using the scale's own samples: the last
/// scale reading taken while the pen still read 0% (<c>FirstZeroGf</c>) and the
/// first scale reading taken once the pen registered (<c>LastNonZeroGf</c>). The
/// reported IAF is the midpoint of that bracket. Sampling at scale-update
/// boundaries (not per pen tick) keeps the two points a real scale interval
/// apart, so they differ on a slow press; a fast jab yields a wider bracket, and
/// a press faster than one scale update can't be resolved.
///
/// Counterpart to <see cref="IafController"/>, which approaches IAF from above
/// (release sweep). <c>PeakGf</c> is left at 0 — it doesn't apply here.
///
/// Threading: all public methods must be called from the UI thread.
/// </summary>
public sealed class IafBelowController
{
    public const int    MaxEstimates    = 20;
    public const double MaxRestingGf    = 0.1;   // scale must reach ≤ this to arm a sweep
    public const uint   ActivationRaw   = 1;     // smallest meaningful non-zero driver level;
                                                 // used by the UI to label the activation boundary

    private readonly List<IafEstimate> _estimates = [];
    public IReadOnlyList<IafEstimate> Estimates => _estimates;

    private double _lastScaleGf;
    private uint   _lastPenRaw;

    // Scale force at the most recent scale sample taken while the pen still read
    // raw 0 — the "0%" side of the bracket. Null until a 0%-reading sample is seen.
    private double? _zeroForce;

    // Becomes true as soon as the scale dips below MaxRestingGf. Stays true
    // through the next activation; consumed (set false) when the estimate
    // fires — the user must lift below the floor again to re-arm.
    private bool _armed;

    // Edge detection for "press started without first lifting": only fire
    // SweepRejected on the raw 0→>0 transition, not every nonzero tick after.
    private bool _lastWasNonZero;

    public event Action<IafEstimate>? EstimateAdded;
    public event Action?              SweepRejected;

    public bool IsFull => _estimates.Count >= MaxEstimates;

    /// <summary>
    /// True once the scale has dipped to ≤ <see cref="MaxRestingGf"/> since
    /// the last estimate / clear; surfaced to the UI as the armed indicator.
    /// </summary>
    public bool Armed => _armed;

    public double? Median
    {
        get
        {
            if (_estimates.Count == 0) return null;
            var sorted = _estimates.Select(e => e.IafGf).OrderBy(x => x).ToList();
            int n = sorted.Count;
            return n % 2 == 1
                ? sorted[n / 2]
                : (sorted[n / 2 - 1] + sorted[n / 2]) / 2.0;
        }
    }

    // ── Feed ──────────────────────────────────────────────────────────────────

    public void OnScaleData(double gf)
    {
        _lastScaleGf = gf;
        if (gf <= MaxRestingGf) _armed = true;

        if (_lastPenRaw == 0)
        {
            // Pen still reads 0% — this is the freshest 0%-side bracket force.
            _zeroForce = gf;
            return;
        }

        // Pen is registering. The first scale sample after activation closes the
        // bracket: lower edge = last 0% force, upper edge = this force.
        if (_armed && !IsFull && _zeroForce is { } zeroGf)
        {
            RecordBracket(zeroGf, gf, _lastPenRaw);
            _armed = false;   // consumed — lift below the floor to re-arm
        }
    }

    public void OnPenData(PenReadingData d)
    {
        if (d.PacketCount == 0) return;

        uint raw        = d.RawPressure;
        bool wasNonZero = _lastWasNonZero;
        _lastPenRaw     = raw;
        _lastWasNonZero = raw > 0;

        // Pressed without ever lifting to the rest floor — surface as a one-shot
        // rejection on the activation edge only. (A real sweep is captured from
        // the scale stream in OnScaleData.)
        if (raw > 0 && !wasNonZero && !_armed)
            SweepRejected?.Invoke();
    }

    /// <summary>
    /// Records a sweep from its bracketing scale samples: <paramref name="zeroGf"/>
    /// (last 0%-reading force) and <paramref name="nonZeroGf"/> (first force once
    /// the pen registered, at raw <paramref name="nonZeroRaw"/>). IAF is the
    /// midpoint; a non-positive midpoint is rejected.
    /// </summary>
    private void RecordBracket(double zeroGf, double nonZeroGf, uint nonZeroRaw)
    {
        double iafGf = (zeroGf + nonZeroGf) / 2.0;
        if (iafGf <= 0) { SweepRejected?.Invoke(); return; }

        var estimate = new IafEstimate(
            At:             DateTime.UtcNow,
            IafGf:          iafGf,
            PeakGf:         0,            // not meaningful in this direction
            LastNonZeroRaw: nonZeroRaw,
            LastNonZeroGf:  nonZeroGf,    // first force the pen registered at
            FirstZeroGf:    zeroGf);      // last force that still read 0%
        _estimates.Add(estimate);
        EstimateAdded?.Invoke(estimate);
    }

    // ── Control ───────────────────────────────────────────────────────────────

    public void Clear()
    {
        _estimates.Clear();
        _zeroForce      = null;
        _armed          = false;
        _lastWasNonZero = false;
        _lastScaleGf    = 0;
        _lastPenRaw     = 0;
    }

    public bool RemoveLast()
    {
        if (_estimates.Count == 0) return false;
        _estimates.RemoveAt(_estimates.Count - 1);
        return true;
    }

    public bool RemoveAt(int index)
    {
        if (index < 0 || index >= _estimates.Count) return false;
        _estimates.RemoveAt(index);
        return true;
    }

    /// <summary>
    /// Force-appends an estimate at the supplied gf, bypassing sweep detection.
    /// Fires <see cref="EstimateAdded"/>. No-op once <see cref="MaxEstimates"/> is hit.
    /// </summary>
    public void RecordManual(double gf)
    {
        if (IsFull)   return;
        if (gf <= 0)  return;   // never record a zero/negative activation force
        var estimate = new IafEstimate(DateTime.UtcNow, gf, 0, 0, 0, 0);
        _estimates.Add(estimate);
        EstimateAdded?.Invoke(estimate);
    }
}
