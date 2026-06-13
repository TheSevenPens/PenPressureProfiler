namespace PenPressureProfiler.Detection;

/// <summary>
/// Records IAF (Initial Activation Force) estimates from release sweeps. The
/// user presses the pen above <see cref="MinPeakGf"/> gf and then releases
/// slowly to zero.
///
/// On release the estimate brackets the activation using the scale's own
/// samples: the last scale reading taken while the pen still registered
/// (<c>LastNonZeroGf</c>) and the first scale reading once it read 0%
/// (<c>FirstZeroGf</c>). The reported IAF is the midpoint of that bracket.
/// Sampling at scale-update boundaries keeps the two points a real scale
/// interval apart, so they differ on a slow release. A release while still
/// under load (last on-force ≥ <see cref="MinPeakGf"/>) is rejected as a jump.
/// After <see cref="MaxEstimates"/> estimates, capture stops; the final IAF is
/// the median.
///
/// Threading: all public methods must be called from the UI thread.
/// </summary>
public sealed class IafController
{
    public const int    MaxEstimates = 20;
    public const double MinPeakGf    = 30.0;

    private readonly List<IafEstimate> _estimates = [];
    public IReadOnlyList<IafEstimate> Estimates => _estimates;

    // Latest scale reading and latest pen raw level.
    private double _lastScaleGf;
    private uint   _lastPenRaw;

    // Scale force + raw at the most recent scale sample taken while the pen was
    // still registering — the non-zero ("on") side of the release bracket.
    private (uint Raw, double Gf)? _activeForce;

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

    /// <summary>Manually arms the sweep, bypassing the peak-force requirement.</summary>
    public void Arm() => _armed = true;

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

    public void OnScaleData(double gf)
    {
        _lastScaleGf = gf;

        if (_lastPenRaw > 0)
        {
            // Pen registering — track the peak (for arming) and the latest
            // on-force as the bracket's non-zero side.
            if (gf > _peakGf) _peakGf = gf;
            if (_peakGf >= MinPeakGf) _armed = true;
            _activeForce = (_lastPenRaw, gf);
            return;
        }

        // Pen reads 0%. If a press just ended, the first 0%-reading scale sample
        // closes the bracket: non-zero side = last on-force, 0% side = this force.
        if (_activeForce is { } active)
        {
            // Record only an armed, clean release (the pen glided off rather than
            // jumping to zero while still under heavy load).
            if (_armed && !IsFull && active.Gf < MinPeakGf)
                RecordBracket(gf, active.Gf, active.Raw);
            else
                SweepRejected?.Invoke();

            ResetSweepState();
        }
    }

    public void OnPenData(PenReadingData d)
    {
        if (d.PacketCount == 0) return;
        _lastPenRaw = d.RawPressure;   // the release is captured from the scale stream
    }

    /// <summary>
    /// Records a release sweep from its bracketing scale samples:
    /// <paramref name="nonZeroGf"/> (last on-force, at raw <paramref name="nonZeroRaw"/>)
    /// and <paramref name="zeroGf"/> (first 0%-reading force). IAF is the
    /// midpoint; a non-positive midpoint is rejected.
    /// </summary>
    private void RecordBracket(double zeroGf, double nonZeroGf, uint nonZeroRaw)
    {
        double iafGf = (zeroGf + nonZeroGf) / 2.0;
        if (iafGf <= 0) { SweepRejected?.Invoke(); return; }

        var estimate = new IafEstimate(
            At:             DateTime.UtcNow,
            IafGf:          iafGf,
            PeakGf:         _peakGf,
            LastNonZeroRaw: nonZeroRaw,
            LastNonZeroGf:  nonZeroGf,    // last force the pen still registered at
            FirstZeroGf:    zeroGf);      // first force that read 0%
        _estimates.Add(estimate);
        EstimateAdded?.Invoke(estimate);
    }

    private void ResetSweepState()
    {
        _activeForce = null;
        _peakGf      = 0;
        _armed       = false;
    }

    // ── Control ───────────────────────────────────────────────────────────────

    public void Clear()
    {
        _estimates.Clear();
        ResetSweepState();
        _lastScaleGf = 0;
        _lastPenRaw  = 0;
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
        if (IsFull)   return;
        if (gf <= 0)  return;   // never record a zero/negative activation force
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
/// <param name="LastNonZeroGf">Scale gf at the non-zero pen sample bordering activation
/// (the last non-zero before release for "from above"; the first non-zero of the press
/// for "from below"). The force at the first non-zero reading.</param>
/// <param name="FirstZeroGf">Scale gf at the 0%-reading pen sample bordering activation
/// (the first zero after release for "from above"; the last zero before the press for
/// "from below"). The force at the 0% reading.</param>
public readonly record struct IafEstimate(
    DateTime At,
    double   IafGf,
    double   PeakGf,
    uint     LastNonZeroRaw,
    double   LastNonZeroGf,
    double   FirstZeroGf
);
