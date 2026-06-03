namespace PenPressureProfiler.Detection;

/// <summary>
/// Records IAF estimates from <b>push</b> sweeps. The user lifts the pen so
/// the scale reaches ≤ <see cref="MaxRestingGf"/> (the "rest" floor) and then
/// presses down gently until the pen first reports a non-zero raw value. The
/// estimate is the gf at which the rising (gf, raw) trend would cross
/// <c>raw = <see cref="ActivationRaw"/></c> (=1, the smallest meaningful
/// driver value), found by linear extrapolation across the first two non-zero
/// pen samples after arming.
///
/// Counterpart to <see cref="IafController"/>, which approaches IAF from above
/// (release sweep). Estimates are stored as <see cref="IafEstimate"/> — the
/// bracket-related fields (<c>FirstZeroGf</c>, <c>PeakGf</c>) are left at 0
/// since they don't apply in this direction.
///
/// Threading: all public methods must be called from the UI thread.
/// </summary>
public sealed class IafBelowController
{
    public const int    MaxEstimates    = 10;
    public const double MaxRestingGf    = 0.1;   // scale must reach ≤ this to arm a sweep
    public const uint   ActivationRaw   = 1;     // extrapolation target: gf where raw would equal 1
                                                 // (the smallest meaningful non-zero driver value)

    private readonly List<IafEstimate> _estimates = [];
    public IReadOnlyList<IafEstimate> Estimates => _estimates;

    private double _lastScaleGf;

    // First two non-zero pen samples in the current activation, paired with
    // concurrent scale gf. Once both are filled, the estimate fires.
    private (uint Raw, double Gf)? _first;
    private (uint Raw, double Gf)? _second;

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
    }

    public void OnPenData(PenReadingData d)
    {
        if (d.PacketCount == 0) return;
        if (IsFull)             return;

        uint raw = d.RawPressure;

        if (raw == 0)
        {
            // Pen below activation. Reset any partial collection so a future
            // press starts fresh.
            _first          = null;
            _second         = null;
            _lastWasNonZero = false;
            return;
        }

        bool isActivationEdge = !_lastWasNonZero;
        _lastWasNonZero = true;

        if (!_armed)
        {
            // Press without ever lifting to the rest floor — surface as a
            // one-shot rejection on the activation edge only.
            if (isActivationEdge) SweepRejected?.Invoke();
            return;
        }

        if (_first is null) { _first = (raw, _lastScaleGf); return; }
        if (_second is not null) return;   // shouldn't happen — IsFull guards

        _second = (raw, _lastScaleGf);

        double iafGf = ExtrapolateBackward(_first.Value, _second.Value);
        var estimate = new IafEstimate(
            At:             DateTime.UtcNow,
            IafGf:          iafGf,
            PeakGf:         0,            // not meaningful in this direction
            LastNonZeroRaw: _first.Value.Raw,
            LastNonZeroGf:  _first.Value.Gf,
            FirstZeroGf:    0);
        _estimates.Add(estimate);
        EstimateAdded?.Invoke(estimate);

        // Cycle consumed — user must dip back below MaxRestingGf to re-arm.
        _armed   = false;
        _first   = null;
        _second  = null;
    }

    /// <summary>
    /// Line through the first two non-zero samples in (gf, raw) space,
    /// solving for gf where raw = <see cref="ActivationRaw"/> (the smallest
    /// meaningful driver value, 1). Falls back to <c>first.Gf</c> when the
    /// trend is flat (identical raw) or has identical gf values.
    /// </summary>
    private static double ExtrapolateBackward(
        (uint Raw, double Gf) first,
        (uint Raw, double Gf) second)
    {
        if (first.Raw == second.Raw) return first.Gf;
        if (first.Gf  == second.Gf)  return first.Gf;

        double slope = ((double)second.Raw - first.Raw) / (second.Gf - first.Gf);
        if (!double.IsFinite(slope) || slope == 0) return first.Gf;

        // Solve raw = ActivationRaw  =>  ActivationRaw = first.Raw + slope*(gf - first.Gf)
        //                            =>  gf = first.Gf + (ActivationRaw - first.Raw) / slope
        return first.Gf + (ActivationRaw - (double)first.Raw) / slope;
    }

    // ── Control ───────────────────────────────────────────────────────────────

    public void Clear()
    {
        _estimates.Clear();
        _first          = null;
        _second         = null;
        _armed          = false;
        _lastWasNonZero = false;
        _lastScaleGf    = 0;
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
        if (IsFull) return;
        var estimate = new IafEstimate(DateTime.UtcNow, gf, 0, 0, 0, 0);
        _estimates.Add(estimate);
        EstimateAdded?.Invoke(estimate);
    }
}
