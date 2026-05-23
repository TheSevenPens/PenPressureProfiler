namespace PenPressureProfiler;

/// <summary>
/// Watches the live pen and scale streams and detects stable moments where
/// both signals have been steady long enough to be trusted as a clean
/// (physical, logical) pressure pair.
///
/// Threading: all public methods must be called from the UI thread.
/// </summary>
public sealed class SweepController
{
    // ── Stability settings (driven by UI sliders) ─────────────────────────

    /// <summary>Allowed range in normalised pen pressure (0–1) across the
    /// stability window before the pen is considered stable.</summary>
    public double PenTolerance { get; set; } = 0.03;

    /// <summary>Allowed range in scale readings (gf) across the scale
    /// history window before the scale is considered stable.</summary>
    public double ScaleTolerance { get; set; } = 5.0;

    /// <summary>How long both signals must remain stable before a capture
    /// is triggered, in milliseconds.</summary>
    public double MinStableMs { get; set; } = 400;

    /// <summary>Minimum milliseconds between two successive stable captures
    /// to avoid repeated captures in a sustained stable period.</summary>
    public double MinGapMs { get; set; } = 500;

    /// <summary>Maximum number of stable captures before the list stops growing.
    /// Prevents unbounded memory use during very long sessions.</summary>
    public int MaxCaptures { get; set; } = 2000;

    // ── State ─────────────────────────────────────────────────────────────

    private readonly List<SweepCapture> _captures = [];
    public IReadOnlyList<SweepCapture> Captures => _captures;

    // Approximate scale sample interval (ms). Used to size the scale window
    // consistently with the pen window so both cover ~MinStableMs.
    private const double EstimatedScaleIntervalMs = 115.0;

    private readonly Queue<double> _penWindow   = new();
    private readonly Queue<double> _scaleWindow = new();

    private DateTime? _stableStart;
    private DateTime  _lastCaptureTime = DateTime.MinValue;
    private double    _lastScaleGf;

    // ── Events ────────────────────────────────────────────────────────────

    /// <summary>Fired on each new scale reading with the paired
    /// (scaleGf, penNorm) snapshot for scatter-plot streaming.</summary>
    public event Action<double, double>? RawPairAvailable;

    /// <summary>Fired when a new stable capture is recorded.</summary>
    public event Action<SweepCapture>? StableCaptured;

    // ── Feed ──────────────────────────────────────────────────────────────

    public void OnPenData(PenReadingData d)
    {
        if (d.PacketCount == 0) return;

        // Size the pen window to cover approximately MinStableMs at ~48 Hz.
        int penWindowDepth = Math.Max(5, (int)(MinStableMs / 21.0));
        _penWindow.Enqueue(d.NormalizedPressure);
        while (_penWindow.Count > penWindowDepth)
            _penWindow.Dequeue();

        if (_penWindow.Count < 3) return;

        double penMin = _penWindow.Min();
        double penMax = _penWindow.Max(); // reused for zero/saturation checks

        bool penStable    = (penMax - penMin) <= PenTolerance;
        bool penSaturated = penMax >= 1.0;   // #27: use pre-computed penMax
        bool penZero      = penMax <= 0;      // #27: use pre-computed penMax

        bool scaleStable = false;
        if (_scaleWindow.Count >= 2)
            scaleStable = (_scaleWindow.Max() - _scaleWindow.Min()) <= ScaleTolerance;

        if (penStable && scaleStable && _lastScaleGf > 0 && !penSaturated && !penZero)
        {
            var now = DateTime.UtcNow;
            _stableStart ??= now;

            double stableDurationMs = (now - _stableStart.Value).TotalMilliseconds;

            if (stableDurationMs >= MinStableMs &&
                (now - _lastCaptureTime).TotalMilliseconds >= MinGapMs)
            {
                if (_captures.Count < MaxCaptures)
                {
                    var capture = new SweepCapture(
                        PhysicalGf:   _scaleWindow.Average(),
                        LogicalNorm:  _penWindow.Average(),
                        PenSamples:   _penWindow.ToList(),
                        ScaleSamples: _scaleWindow.ToList());

                    _captures.Add(capture);
                    StableCaptured?.Invoke(capture);
                }

                _lastCaptureTime = now;
                _stableStart     = null; // #25: reset to null so next capture
                                         // needs a fresh stability run, not now+MinStableMs
            }
        }
        else
        {
            _stableStart = null;
        }
    }

    public void OnScaleData(double gf)
    {
        _lastScaleGf = gf;

        // #26: size scale window to cover ~MinStableMs at the estimated scale rate.
        int scaleWindowDepth = Math.Max(2, (int)(MinStableMs / EstimatedScaleIntervalMs) + 1);
        _scaleWindow.Enqueue(gf);
        while (_scaleWindow.Count > scaleWindowDepth)
            _scaleWindow.Dequeue();

        double penNorm = _penWindow.Count > 0 ? _penWindow.Average() : 0;
        RawPairAvailable?.Invoke(gf, penNorm);
    }

    // ── Control ───────────────────────────────────────────────────────────

    public void Clear()
    {
        _captures.Clear();
        _penWindow.Clear();
        _scaleWindow.Clear();
        _stableStart     = null;
        _lastCaptureTime = DateTime.MinValue;
        _lastScaleGf     = 0;
    }

    /// <summary>Replaces the current capture list with a loaded set.</summary>
    public void LoadCaptures(IEnumerable<SweepCapture> captures)
    {
        Clear();
        _captures.AddRange(captures.Take(MaxCaptures));
    }
}
