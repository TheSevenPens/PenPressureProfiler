namespace PenPressureProfiler;

/// <summary>
/// Records saturation-point estimates from push sweeps. The user presses the
/// pen until logical pressure reaches 100% (raw == driver max); each
/// sub-saturated → saturated transition produces one estimate via linear
/// extrapolation across the last two sub-saturated samples in
/// (gf, normalized) space. Stops after <see cref="MaxEstimates"/>; the final
/// MAX is the median.
///
/// Threading: all public methods must be called from the UI thread.
/// </summary>
public sealed class MaxController
{
    public const int    MaxEstimates   = 10;
    public const double SaturationNorm = 1.0;

    private readonly List<MaxEstimate> _estimates = [];
    public IReadOnlyList<MaxEstimate> Estimates => _estimates;

    private double _lastScaleGf;

    // Last two sub-saturated nonzero pen samples in the current approach,
    // paired with concurrent scale gf. _prev is the older, _curr the newer.
    private (double Norm, double Gf)? _prev;
    private (double Norm, double Gf)? _curr;

    // Lowest scale gf seen since the last lift — the "baseline" the user
    // pushed up from.
    private double _baselineGf = double.PositiveInfinity;
    private bool   _hasSeenSubMax;

    // Each saturation hit consumes the cycle; the user must fully lift
    // (raw == 0) before the next saturation hit can be recorded. Prevents
    // dips back into the sub-saturation region from triggering duplicate
    // estimates without a real lift in between.
    private bool _readyForNextCycle = true;

    public event Action<MaxEstimate>? EstimateAdded;

    public bool IsFull => _estimates.Count >= MaxEstimates;

    /// <summary>
    /// True when the next saturation hit will record an estimate. Flips false
    /// briefly after each hit and re-arms on the next full lift (raw → 0).
    /// </summary>
    public bool Armed => _readyForNextCycle;

    public double? Median
    {
        get
        {
            if (_estimates.Count == 0) return null;
            var sorted = _estimates.Select(e => e.MaxGf).OrderBy(x => x).ToList();
            int n = sorted.Count;
            return n % 2 == 1
                ? sorted[n / 2]
                : (sorted[n / 2 - 1] + sorted[n / 2]) / 2.0;
        }
    }

    // ── Feed ──────────────────────────────────────────────────────────────────

    public void OnScaleData(double gf) => _lastScaleGf = gf;

    public void OnPenData(PenReadingData d)
    {
        if (d.PacketCount == 0) return;
        if (IsFull)             return;

        if (d.RawPressure == 0)
        {
            // Lift detected — arm for the next approach and reset transient state.
            _readyForNextCycle = true;
            ResetCycle();
            return;
        }

        if (d.NormalizedPressure >= SaturationNorm)
        {
            // Sub-saturated → saturated transition.
            if (_readyForNextCycle && _hasSeenSubMax && _curr is { } last)
            {
                double maxGf = ExtrapolateMax(_prev, last);
                var estimate = new MaxEstimate(
                    At:             DateTime.UtcNow,
                    MaxGf:          maxGf,
                    BaselineGf:     _baselineGf,
                    LastSubMaxNorm: last.Norm,
                    LastSubMaxGf:   last.Gf,
                    FirstAtMaxGf:   _lastScaleGf);
                _estimates.Add(estimate);
                EstimateAdded?.Invoke(estimate);
                _readyForNextCycle = false;
            }
            ResetCycle();
            return;
        }

        // Sub-saturated, nonzero. Track for extrapolation.
        _prev = _curr;
        _curr = (d.NormalizedPressure, _lastScaleGf);
        if (_lastScaleGf < _baselineGf) _baselineGf = _lastScaleGf;
        _hasSeenSubMax = true;
    }

    /// <summary>
    /// Line through the last two sub-saturated samples in (gf, norm) space,
    /// solving for gf where norm = 1.0. Falls back to the last sample's gf
    /// when the trend is flat, decreasing, or has identical gf values.
    /// </summary>
    private static double ExtrapolateMax(
        (double Norm, double Gf)? prev,
        (double Norm, double Gf)  last)
    {
        if (prev is not { } p)      return last.Gf;
        if (p.Norm >= last.Norm)    return last.Gf;
        if (p.Gf == last.Gf)        return last.Gf;

        double slope = (last.Norm - p.Norm) / (last.Gf - p.Gf);
        if (!double.IsFinite(slope) || slope <= 0) return last.Gf;

        return last.Gf + (SaturationNorm - last.Norm) / slope;
    }

    private void ResetCycle()
    {
        _prev          = null;
        _curr          = null;
        _baselineGf    = double.PositiveInfinity;
        _hasSeenSubMax = false;
    }

    // ── Control ───────────────────────────────────────────────────────────────

    public void Clear()
    {
        _estimates.Clear();
        ResetCycle();
        _readyForNextCycle = true;
        _lastScaleGf       = 0;
    }

    /// <summary>Drops the most recent estimate, if any. Returns true on success.</summary>
    public bool RemoveLast()
    {
        if (_estimates.Count == 0) return false;
        _estimates.RemoveAt(_estimates.Count - 1);
        return true;
    }

    /// <summary>Drops a specific estimate by index. Returns true on success.</summary>
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
        var estimate = new MaxEstimate(DateTime.UtcNow, gf, 0, 0, 0, 0);
        _estimates.Add(estimate);
        EstimateAdded?.Invoke(estimate);
    }
}

/// <summary>One MAX estimate from a single push sweep.</summary>
/// <param name="At">When the saturation transition was observed.</param>
/// <param name="MaxGf">Extrapolated saturation gf.</param>
/// <param name="BaselineGf">Lowest scale gf seen during this approach.</param>
/// <param name="LastSubMaxNorm">Normalized pen pressure at the last sample with norm &lt; 1.</param>
/// <param name="LastSubMaxGf">Scale gf paired with the last sub-saturated pen sample.</param>
/// <param name="FirstAtMaxGf">Scale gf paired with the first pen sample where norm ≥ 1.</param>
public readonly record struct MaxEstimate(
    DateTime At,
    double   MaxGf,
    double   BaselineGf,
    double   LastSubMaxNorm,
    double   LastSubMaxGf,
    double   FirstAtMaxGf
);
