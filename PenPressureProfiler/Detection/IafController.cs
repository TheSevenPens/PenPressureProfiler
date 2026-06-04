namespace PenPressureProfiler.Detection;

/// <summary>
/// Records IAF (Initial Activation Force) estimates from release sweeps.
/// The user presses the pen above <see cref="MinPeakGf"/> gf and then releases
/// to zero; each nonzero→zero transition in raw pen pressure produces one
/// estimate via linear extrapolation from the last two nonzero samples.
/// After <see cref="MaxEstimates"/> estimates, capture stops; the final IAF
/// is the median.
///
/// Threading: all public methods must be called from the UI thread.
/// </summary>
public sealed class IafController
{
    public const int    MaxEstimates = 10;
    public const double MinPeakGf    = 30.0;

    private readonly List<IafEstimate> _estimates = [];
    public IReadOnlyList<IafEstimate> Estimates => _estimates;

    // Latest scale reading — paired with each pen tick as the concurrent gf.
    private double _lastScaleGf;

    // Last two non-zero pen samples in the current press, paired with the
    // concurrent scale gf. _prev is the older, _curr is the newer.
    private (uint Raw, double Gf)? _prev;
    private (uint Raw, double Gf)? _curr;

    // Highest scale gf seen during the current press. The sweep is "armed"
    // (eligible to produce an IAF estimate on release) once this reaches
    // MinPeakGf.
    private double _peakGf;
    private bool   _armed;

    public event Action<IafEstimate>? EstimateAdded;
    public event Action?              SweepRejected;

    public bool IsFull => _estimates.Count >= MaxEstimates;

    /// <summary>
    /// True once the peak gf of the current press has reached
    /// <see cref="MinPeakGf"/>; only an armed sweep produces an estimate on
    /// release. Resets after firing / clearing.
    /// </summary>
    public bool Armed => _armed;

    /// <summary>Median of all collected IAF estimates, or null if none yet.</summary>
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

    public void OnScaleData(double gf) => _lastScaleGf = gf;

    public void OnPenData(PenReadingData d)
    {
        if (d.PacketCount == 0) return;
        if (IsFull)             return;

        uint raw = d.RawPressure;

        if (raw > 0)
        {
            _prev = _curr;
            _curr = (raw, _lastScaleGf);

            if (_lastScaleGf > _peakGf) _peakGf = _lastScaleGf;
            if (_peakGf >= MinPeakGf)   _armed  = true;
        }
        else if (_curr is { } last)
        {
            // Raw transitioned nonzero → zero. Record only if the sweep was
            // armed (peak gf reached the threshold) AND the release was a
            // clean glide: the last nonzero physical force must have dropped
            // below the arm threshold. If the pen was still under heavy load
            // (last.Gf >= MinPeakGf) when raw hit zero, it jumped to zero —
            // a spurious movement, not a controlled release — so reject it.
            if (_armed && last.Gf < MinPeakGf)
            {
                double iafGf = ExtrapolateIaf(_prev, last);
                var estimate = new IafEstimate(
                    At:             DateTime.UtcNow,
                    IafGf:          iafGf,
                    PeakGf:         _peakGf,
                    LastNonZeroRaw: last.Raw,
                    LastNonZeroGf:  last.Gf,
                    FirstZeroGf:    _lastScaleGf);
                _estimates.Add(estimate);
                EstimateAdded?.Invoke(estimate);
            }
            else
            {
                SweepRejected?.Invoke();
            }

            ResetSweepState();
        }
        // else: raw is 0 and no prior nonzero sample — just sit idle.
    }

    /// <summary>
    /// Linear extrapolation across the last two nonzero samples in (gf, raw)
    /// space, solving for gf where raw = 0. Falls back to last.Gf when there
    /// is no usable two-point trend (only one sample, flat or rising raw,
    /// or identical gf values that would divide by zero).
    /// </summary>
    private static double ExtrapolateIaf(
        (uint Raw, double Gf)? prev,
        (uint Raw, double Gf)  last)
    {
        if (prev is not { } p)       return last.Gf;
        if (p.Raw <= last.Raw)       return last.Gf;
        if (p.Gf == last.Gf)         return last.Gf;

        double slope = ((double)last.Raw - p.Raw) / (last.Gf - p.Gf);
        if (!double.IsFinite(slope) || slope == 0) return last.Gf;

        return last.Gf - last.Raw / slope;
    }

    private void ResetSweepState()
    {
        _prev   = null;
        _curr   = null;
        _peakGf = 0;
        _armed  = false;
    }

    // ── Control ───────────────────────────────────────────────────────────────

    public void Clear()
    {
        _estimates.Clear();
        ResetSweepState();
        _lastScaleGf = 0;
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
    /// Bracket fields are left at 0 (no sweep produced it). Fires
    /// <see cref="EstimateAdded"/>. No-op once <see cref="MaxEstimates"/> is hit.
    /// </summary>
    public void RecordManual(double gf)
    {
        if (IsFull) return;
        var estimate = new IafEstimate(DateTime.UtcNow, gf, 0, 0, 0, 0);
        _estimates.Add(estimate);
        EstimateAdded?.Invoke(estimate);
    }
}

/// <summary>One IAF estimate from a single release sweep.</summary>
/// <param name="At">When the zero-crossing was observed.</param>
/// <param name="IafGf">Extrapolated IAF in grams-force.</param>
/// <param name="PeakGf">Highest scale gf seen during this sweep.</param>
/// <param name="LastNonZeroRaw">Raw pen pressure at the last sample with raw &gt; 0.</param>
/// <param name="LastNonZeroGf">Scale gf paired with the last-nonzero pen sample.</param>
/// <param name="FirstZeroGf">Scale gf paired with the first pen sample where raw == 0.</param>
public readonly record struct IafEstimate(
    DateTime At,
    double   IafGf,
    double   PeakGf,
    uint     LastNonZeroRaw,
    double   LastNonZeroGf,
    double   FirstZeroGf
);
