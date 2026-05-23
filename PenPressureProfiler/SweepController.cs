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

    // ── State ─────────────────────────────────────────────────────────────

    private readonly List<SweepCapture> _captures = [];
    public IReadOnlyList<SweepCapture> Captures => _captures;

    private readonly Queue<double> _penWindow   = new();
    private readonly Queue<double> _scaleWindow = new();

    private DateTime? _stableStart;           // when the current stable run began
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
        // Skip zero-fill ticks (no actual WinTab packets).
        if (d.PacketCount == 0) return;

        // Maintain a pen sliding window — keep enough history to cover
        // MinStableMs at ~48 Hz, minimum 5 samples.
        int windowDepth = Math.Max(5, (int)(MinStableMs / 21.0));
        _penWindow.Enqueue(d.NormalizedPressure);
        while (_penWindow.Count > windowDepth)
            _penWindow.Dequeue();

        if (_penWindow.Count < 3) return;

        // ── Stability checks ─────────────────────────────────────────────

        double penMin = _penWindow.Min();
        double penMax = _penWindow.Max();
        bool penStable = (penMax - penMin) <= PenTolerance;

        bool scaleStable = false;
        if (_scaleWindow.Count >= 2)
        {
            double scaleMin = _scaleWindow.Min();
            double scaleMax = _scaleWindow.Max();
            scaleStable = (scaleMax - scaleMin) <= ScaleTolerance;
        }

        // Never capture at the pen's hard ceiling — readings at 100% are clipped.
        bool penSaturated = _penWindow.Max() >= 1.0;

        // Never capture when the pen is not pressing.
        bool penZero = _penWindow.Max() <= 0;

        if (penStable && scaleStable && _lastScaleGf > 0 && !penSaturated && !penZero)
        {
            var now = DateTime.UtcNow;
            _stableStart ??= now;

            double stableDurationMs = (now - _stableStart.Value).TotalMilliseconds;

            if (stableDurationMs >= MinStableMs &&
                (now - _lastCaptureTime).TotalMilliseconds >= MinGapMs)
            {
                var capture = new SweepCapture(
                    PhysicalGf:   _scaleWindow.Average(),
                    LogicalNorm:  _penWindow.Average(),
                    PenSamples:   _penWindow.ToList(),
                    ScaleSamples: _scaleWindow.ToList());

                _captures.Add(capture);
                _lastCaptureTime = now;
                _stableStart     = now; // reset so next capture requires a fresh stable window
                StableCaptured?.Invoke(capture);
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

        _scaleWindow.Enqueue(gf);
        while (_scaleWindow.Count > 5)
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
        _captures.AddRange(captures);
    }
}
