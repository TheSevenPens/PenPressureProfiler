namespace PenPressureProfiler;

/// <summary>
/// Watches the live pen and scale streams and fires <see cref="StableCaptured"/>
/// when both signals have been steady for at least <see cref="MinStableMs"/> ms.
/// Threading: all public methods must be called from the UI thread.
/// </summary>
public sealed class SweepController
{
    // ── Stability settings ────────────────────────────────────────────────────

    public double PenTolerance   { get; set; } = 0.005;
    public double ScaleTolerance { get; set; } = 0.25;
    public double MinStableMs    { get; set; } = 500;
    public double MinGapMs       { get; set; } = 500;
    public int    MaxCaptures    { get; set; } = 2000;

    // ── State ─────────────────────────────────────────────────────────────────

    private readonly List<SweepCapture>   _captures    = [];
    public IReadOnlyList<SweepCapture> Captures => _captures;

    private const double EstimatedScaleIntervalMs = 115.0;

    private readonly Queue<PenSample>   _penWindow   = new();
    private readonly Queue<ScaleSample> _scaleWindow = new();

    private DateTime? _stableStart;
    private DateTime  _lastCaptureTime = DateTime.MinValue;
    private double    _lastScaleGf;

    // ── Events ────────────────────────────────────────────────────────────────

    public event Action<double, double>? RawPairAvailable;
    public event Action<SweepCapture>?   StableCaptured;

    // ── Feed ──────────────────────────────────────────────────────────────────

    public void OnPenData(PenReadingData d)
    {
        if (d.PacketCount == 0) return;

        int penWindowDepth = Math.Max(5, (int)(MinStableMs / 21.0));
        _penWindow.Enqueue(new PenSample(DateTime.UtcNow, d.RawPressure, d.NormalizedPressure));
        while (_penWindow.Count > penWindowDepth)
            _penWindow.Dequeue();

        if (_penWindow.Count < 3) return;

        double penMin = _penWindow.Min(s => s.NormalizedPressure);
        double penMax = _penWindow.Max(s => s.NormalizedPressure);

        bool penStable     = (penMax - penMin) <= PenTolerance;
        bool penSaturated  = penMax >= 1.0;
        bool penHasZeroRaw = _penWindow.Any(s => s.RawPressure == 0);

        bool scaleStable = false;
        if (_scaleWindow.Count >= 2)
        {
            double scMin = _scaleWindow.Min(s => s.ForceGf);
            double scMax = _scaleWindow.Max(s => s.ForceGf);
            scaleStable = (scMax - scMin) <= ScaleTolerance;
        }

        if (penStable && scaleStable && _lastScaleGf > 0 && !penSaturated && !penHasZeroRaw)
        {
            var now = DateTime.UtcNow;
            _stableStart ??= now;

            if ((now - _stableStart.Value).TotalMilliseconds >= MinStableMs &&
                (now - _lastCaptureTime).TotalMilliseconds   >= MinGapMs)
            {
                double physGf   = _scaleWindow.Average(s => s.ForceGf);
                double logNorm  = _penWindow.Average(s => s.NormalizedPressure);

                // Check for an existing capture within the current tolerances.
                // If one exists, increment its count rather than adding a duplicate.
                var existing = _captures.FirstOrDefault(c =>
                    Math.Abs(c.PhysicalGf - physGf) <= ScaleTolerance &&
                    Math.Abs(c.LogicalNorm - logNorm) <= PenTolerance);

                if (existing is not null)
                {
                    existing.Count++;
                    StableCaptured?.Invoke(existing);
                }
                else if (_captures.Count < MaxCaptures)
                {
                    var capture = new SweepCapture(
                        PhysicalGf:   physGf,
                        LogicalNorm:  logNorm,
                        PenSamples:   _penWindow.ToList(),
                        ScaleSamples: _scaleWindow.ToList());

                    _captures.Add(capture);
                    StableCaptured?.Invoke(capture);
                }
                _lastCaptureTime = now;
                _stableStart     = null;
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

        int scaleWindowDepth = Math.Max(2, (int)(MinStableMs / EstimatedScaleIntervalMs) + 1);
        _scaleWindow.Enqueue(new ScaleSample(DateTime.UtcNow, gf));
        while (_scaleWindow.Count > scaleWindowDepth)
            _scaleWindow.Dequeue();

        double penNorm = _penWindow.Count > 0 ? _penWindow.Average(s => s.NormalizedPressure) : 0;
        RawPairAvailable?.Invoke(gf, penNorm);
    }

    // ── Control ───────────────────────────────────────────────────────────────

    public void Clear()
    {
        _captures.Clear();
        _penWindow.Clear();
        _scaleWindow.Clear();
        _stableStart     = null;
        _lastCaptureTime = DateTime.MinValue;
        _lastScaleGf     = 0;
    }

    public void LoadCaptures(IEnumerable<SweepCapture> captures)
    {
        Clear();
        _captures.AddRange(captures.Take(MaxCaptures));
    }
}
