namespace PenPressureProfiler.Detection;

/// <summary>
/// How an <see cref="IafBelowController"/> turns a push sweep into an IAF
/// estimate + bracket. All measure the same activation but resolve the bracket
/// (and so the DeltaPhys span) differently — useful for comparing on real data.
/// </summary>
public enum IafBelowMethod
{
    /// <summary>Immediate bracket: last 0% force → first non-zero force, midpoint.
    /// Span collapses to 0 when the force is steady across activation.</summary>
    Current,

    /// <summary>A — press-through: hold the lower bound at the last 0% force but
    /// extend the upper bound until the pen presses through to
    /// <see cref="PressThroughLevels"/>, guaranteeing a span; midpoint.</summary>
    PressThrough,

    /// <summary>B — regression: least-squares fit of (gf, raw) over the rising
    /// press, extrapolated to raw 0 for the IAF; bracket = [IAF, first non-zero
    /// force].</summary>
    Regression,

    /// <summary>C — time window: scale force at activation − W vs activation + W
    /// (<see cref="TimeWindowMs"/>); midpoint.</summary>
    TimeWindow,

    /// <summary>D — min-delta: the immediate bracket, with the lower bound walked
    /// back through recent history until the span reaches
    /// <see cref="MinDeltaGf"/>; midpoint.</summary>
    MinDelta,
}

/// <summary>
/// Records IAF estimates from <b>push</b> sweeps. The user lifts the pen so the
/// scale reaches ≤ <see cref="MaxRestingGf"/> (the "rest" floor) and then presses
/// down slowly until the pen registers. The activation is bracketed by two scale
/// readings — a 0% side and a non-zero side — and the IAF is reported from that
/// bracket. <see cref="Method"/> selects how the bracket is resolved.
///
/// Counterpart to <see cref="IafController"/>, which approaches IAF from above
/// (release sweep). <c>PeakGf</c> is left at 0 — it doesn't apply here.
///
/// Threading: all public methods must be called from the UI thread.
/// </summary>
public sealed class IafBelowController
{
    public const int    MaxEstimates    = 20;
    public const double MaxRestingGf    = 2.0;   // scale must dip to ≤ this (gf) to (re-)arm a sweep
    public const uint   ActivationRaw   = 1;     // smallest meaningful non-zero driver level;
                                                 // used by the UI to label the activation boundary

    // Method tunables.
    public const uint   PressThroughLevels = 5;     // A/B: raw level to press through to
    public const double TimeWindowMs       = 200.0; // C: half-window around activation
    public const double MinDeltaGf         = 0.5;   // D: minimum bracket span (gf)
    private const double HistorySeconds    = 2.0;   // rolling scale history kept for C/D

    /// <summary>Active estimation method. Defaults to <see cref="IafBelowMethod.Current"/>.</summary>
    public IafBelowMethod Method { get; set; } = IafBelowMethod.Current;

    private readonly List<IafEstimate> _estimates = [];
    public IReadOnlyList<IafEstimate> Estimates => _estimates;

    private double  _lastScaleGf;
    private uint    _lastPenRaw;
    private double? _zeroForce;        // last scale force seen while the pen read 0%
    private bool    _armed;
    private bool    _lastWasNonZero;   // edge detection for the "pressed without lifting" rejection

    // Rolling scale history (time-ordered) for the time-window (C) and min-delta (D) methods.
    private readonly List<(DateTime T, double Gf)> _scaleHistory = [];

    // Collection for the current active press (between activation and completion).
    private bool     _active;
    private DateTime _activationTime;
    private double   _activationZeroForce;
    private readonly List<(uint Raw, double Gf)> _activeSamples = [];

    public event Action<IafEstimate>? EstimateAdded;
    public event Action?              SweepRejected;

    public bool IsFull => _estimates.Count >= MaxEstimates;

    /// <summary>True once the scale has dipped to ≤ <see cref="MaxRestingGf"/> since
    /// the last estimate / clear; surfaced to the UI as the armed indicator.</summary>
    public bool Armed => _armed;

    /// <summary>Manually arms the sweep, bypassing the lift-to-rest-floor requirement.</summary>
    public void Arm() => _armed = true;

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
        var now = DateTime.UtcNow;
        _lastScaleGf = gf;
        _scaleHistory.Add((now, gf));
        TrimHistory(now);

        if (_lastPenRaw == 0)
        {
            // Pen lifted. Dipping to the rest floor (re-)arms the sweep; arming
            // only while lifted prevents a light press (< MaxRestingGf) from
            // re-arming mid-stroke and firing repeated captures.
            if (gf <= MaxRestingGf) _armed = true;
            // Freshest 0%-side bracket force.
            _zeroForce = gf;
            return;
        }

        if (!_armed || IsFull) return;

        if (!_active)
        {
            // First scale sample after the pen registered → open the press.
            if (_zeroForce is not { } z) return;   // no 0% reference yet
            _active              = true;
            _activationTime      = now;
            _activationZeroForce = z;
            _activeSamples.Clear();
        }

        _activeSamples.Add((_lastPenRaw, gf));

        if (IsComplete(now)) CommitSweep();
    }

    public void OnPenData(PenReadingData d)
    {
        if (d.PacketCount == 0) return;

        uint raw        = d.RawPressure;
        bool wasNonZero = _lastWasNonZero;
        _lastPenRaw     = raw;
        _lastWasNonZero = raw > 0;

        if (raw == 0)
        {
            // Released. Methods that wait past activation finalize with whatever
            // they collected; Current/MinDelta already fired on the first sample.
            if (_active) CommitSweep();
            return;
        }

        // Pressed without ever lifting to the rest floor — reject on the edge only.
        if (!wasNonZero && !_armed)
            SweepRejected?.Invoke();
    }

    /// <summary>Whether enough has been collected for the active method to commit.</summary>
    private bool IsComplete(DateTime now) => Method switch
    {
        IafBelowMethod.PressThrough => _lastPenRaw >= PressThroughLevels,
        IafBelowMethod.Regression   => _lastPenRaw >= PressThroughLevels,
        IafBelowMethod.TimeWindow   => (now - _activationTime).TotalMilliseconds >= TimeWindowMs,
        _                           => true,   // Current, MinDelta: commit immediately
    };

    private void CommitSweep()
    {
        if (!_active) return;

        switch (Method)
        {
            case IafBelowMethod.PressThrough: EmitPressThrough(); break;
            case IafBelowMethod.Regression:   EmitRegression();   break;
            case IafBelowMethod.TimeWindow:   EmitTimeWindow();   break;
            case IafBelowMethod.MinDelta:     EmitMinDelta();     break;
            default:                          EmitCurrent();      break;
        }

        // Cycle consumed — the user must lift below the floor again to re-arm.
        _active = false;
        _armed  = false;
        _activeSamples.Clear();
    }

    // ── Method estimators ───────────────────────────────────────────────────

    private void EmitCurrent()
    {
        var (raw, gf) = _activeSamples[0];
        RecordBracket(_activationZeroForce, gf, raw);
    }

    private void EmitPressThrough()
    {
        // Lower = last 0% force; upper = where we pressed through to (latest sample).
        var (raw, gf) = _activeSamples[^1];
        RecordBracket(_activationZeroForce, gf, raw);
    }

    private void EmitRegression()
    {
        // Fit gf = m·raw + b over the detected levels; IAF = b (gf at raw 0).
        int n = _activeSamples.Count;
        if (n >= 2 && TryFit(out double m, out double b) && m > 0)
        {
            var (raw, gf) = _activeSamples[0];   // first detected level
            RecordBracket(zeroGf: b, nonZeroGf: gf, nonZeroRaw: raw, iaf: b);
        }
        else
        {
            EmitCurrent();   // not enough distinct data to fit — fall back
        }
    }

    private void EmitTimeWindow()
    {
        double lower = ForceAtOrBefore(_activationTime.AddMilliseconds(-TimeWindowMs));
        var (raw, gf) = _activeSamples[^1];      // ≈ activation + window
        RecordBracket(lower, gf, raw);
    }

    private void EmitMinDelta()
    {
        var (raw, gf) = _activeSamples[0];
        double lower = _activationZeroForce;

        // Widen the lower bound backward through history until the span is wide
        // enough (or history runs out).
        if (gf - lower < MinDeltaGf)
            for (int i = _scaleHistory.Count - 1; i >= 0; i--)
            {
                if (_scaleHistory[i].T > _activationTime) continue;
                lower = Math.Min(lower, _scaleHistory[i].Gf);
                if (gf - lower >= MinDeltaGf) break;
            }

        RecordBracket(lower, gf, raw);
    }

    // ── Shared record + helpers ───────────────────────────────────────────────

    /// <summary>Records a bracket, defaulting the IAF to the midpoint. Rejects a
    /// zero/negative lower bound, a downstroke (upper &lt; lower), or a
    /// non-positive IAF.</summary>
    private void RecordBracket(double zeroGf, double nonZeroGf, uint nonZeroRaw, double? iaf = null)
    {
        double iafGf = iaf ?? (zeroGf + nonZeroGf) / 2.0;
        if (zeroGf <= 0 || nonZeroGf < zeroGf || iafGf <= 0)
        {
            SweepRejected?.Invoke();
            return;
        }

        var estimate = new IafEstimate(
            At:             DateTime.UtcNow,
            IafGf:          iafGf,
            PeakGf:         0,            // not meaningful in this direction
            LastNonZeroRaw: nonZeroRaw,
            LastNonZeroGf:  nonZeroGf,    // upper (non-zero) bracket
            FirstZeroGf:    zeroGf);      // lower (0%) bracket
        _estimates.Add(estimate);
        EstimateAdded?.Invoke(estimate);
    }

    /// <summary>Least-squares fit of gf = m·raw + b over the active samples.</summary>
    private bool TryFit(out double m, out double b)
    {
        m = 0; b = 0;
        int n = _activeSamples.Count;
        double sx = 0, sy = 0, sxx = 0, sxy = 0;
        foreach (var (raw, gf) in _activeSamples)
        {
            double x = raw;
            sx += x; sy += gf; sxx += x * x; sxy += x * gf;
        }
        double denom = n * sxx - sx * sx;
        if (denom == 0) return false;
        m = (n * sxy - sx * sy) / denom;
        b = (sy - m * sx) / n;
        return double.IsFinite(m) && double.IsFinite(b);
    }

    /// <summary>Most recent scale force at or before <paramref name="t"/>.</summary>
    private double ForceAtOrBefore(DateTime t)
    {
        double result = _activationZeroForce;
        foreach (var (T, Gf) in _scaleHistory)
        {
            if (T <= t) result = Gf;
            else break;
        }
        return result;
    }

    private void TrimHistory(DateTime now)
    {
        var cutoff = now.AddSeconds(-HistorySeconds);
        int drop = 0;
        while (drop < _scaleHistory.Count && _scaleHistory[drop].T < cutoff) drop++;
        if (drop > 0) _scaleHistory.RemoveRange(0, drop);
    }

    // ── Control ───────────────────────────────────────────────────────────────

    public void Clear()
    {
        _estimates.Clear();
        _scaleHistory.Clear();
        _activeSamples.Clear();
        _active         = false;
        _armed          = false;
        _lastWasNonZero = false;
        _lastScaleGf    = 0;
        _lastPenRaw     = 0;
        _zeroForce      = null;
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
