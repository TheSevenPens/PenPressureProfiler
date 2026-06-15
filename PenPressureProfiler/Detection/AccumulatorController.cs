namespace PenPressureProfiler.Detection;

/// <summary>
/// Accumulates pen-activation statistics against physical force for the IAF
/// threshold. The considered force range [<see cref="MinGf"/>, <see cref="MaxGf"/>)
/// is split into fixed <see cref="BucketWidth"/> gf buckets. Each bucket counts
/// samples where the pen read 0% (off) versus non-zero (on); the force where
/// "on" overtakes "off" is the IAF. Samples outside the range are ignored.
/// <para>
/// Fed <b>one count per scale sample</b>, paired with the current (lag-aligned)
/// pen state — the caller handles the scale-lag time alignment.
/// </para>
/// </summary>
public sealed class AccumulatorController
{
    public const double DefaultMinGf       = 0.0;
    public const double DefaultMaxGf       = 10.0;
    public const double DefaultBucketWidth = 0.5;

    private double  _minGf       = DefaultMinGf;
    private double  _maxGf       = DefaultMaxGf;
    private double  _bucketWidth = DefaultBucketWidth;
    private long[]  _zero        = [];
    private long[]  _nonZero     = [];
    private uint    _lastPenRaw;

    public AccumulatorController() => Configure(DefaultMinGf, DefaultMaxGf, DefaultBucketWidth);

    public double MinGf       => _minGf;
    public double MaxGf       => _maxGf;
    public double BucketWidth => _bucketWidth;
    public int    BucketCount => _zero.Length;

    /// <summary>Per-bucket count of scale samples taken while the pen read 0%.</summary>
    public IReadOnlyList<long> ZeroCounts    => _zero;
    /// <summary>Per-bucket count of scale samples taken while the pen read non-zero.</summary>
    public IReadOnlyList<long> NonZeroCounts => _nonZero;

    /// <summary>Lower edge (gf) of bucket <paramref name="i"/>.</summary>
    public double BucketLowerGf(int i)  => _minGf + i * _bucketWidth;
    /// <summary>Centre (gf) of bucket <paramref name="i"/>.</summary>
    public double BucketCenterGf(int i) => _minGf + (i + 0.5) * _bucketWidth;

    public long TotalSamples
    {
        get
        {
            long t = 0;
            for (int i = 0; i < _zero.Length; i++) t += _zero[i] + _nonZero[i];
            return t;
        }
    }

    /// <summary>(Re)configures the force range and bucket width, re-allocating and
    /// clearing all buckets. A non-positive width or empty/inverted range is
    /// coerced to something valid.</summary>
    public void Configure(double minGf, double maxGf, double bucketWidth)
    {
        if (bucketWidth <= 0)   bucketWidth = DefaultBucketWidth;
        if (minGf < 0)          minGf = 0;
        if (maxGf <= minGf)     maxGf = minGf + bucketWidth;

        _minGf = minGf;
        _maxGf = maxGf;
        _bucketWidth = bucketWidth;

        int count = Math.Max(1, (int)Math.Round((maxGf - minGf) / bucketWidth));
        _zero       = new long[count];
        _nonZero    = new long[count];
        _lastPenRaw = 0;
        LastBucket  = -1;
    }

    /// <summary>Tracks the latest pen state; the on/off classification at the next
    /// scale sample uses this.</summary>
    public void OnPenData(PenReadingData d) => _lastPenRaw = d.RawPressure;

    /// <summary>Bucket index of the most recent increment, or -1 if none. With
    /// <see cref="LastZeroIncremented"/> this identifies the cell that just changed
    /// (for live highlighting).</summary>
    public int  LastBucket          { get; private set; } = -1;
    public bool LastZeroIncremented { get; private set; }

    /// <summary>Buckets the force and increments the off/on accumulator for the
    /// current pen state. One call per scale sample; out-of-range forces ignored.</summary>
    public void OnScaleData(double gf)
    {
        int b = BucketIndex(gf);
        if (b < 0) return;

        bool isZero = _lastPenRaw == 0;
        if (isZero) _zero[b]++;
        else        _nonZero[b]++;

        LastBucket          = b;
        LastZeroIncremented = isZero;
    }

    private int BucketIndex(double gf)
    {
        if (gf < _minGf || gf >= _maxGf) return -1;   // outside the considered range
        int b = (int)((gf - _minGf) / _bucketWidth);
        if (b < 0) return -1;
        return b >= _zero.Length ? _zero.Length - 1 : b;
    }

    public void Clear()
    {
        Array.Clear(_zero);
        Array.Clear(_nonZero);
        _lastPenRaw = 0;
        LastBucket  = -1;
    }

    /// <summary>
    /// Count-weighted logistic fit of P(on) = 1 / (1 + e^(−k·(F − F0))) over the
    /// buckets, via weighted linear regression on the logit. <paramref name="f0"/>
    /// is the 50% point (the IAF estimate) and <paramref name="k"/> the steepness.
    /// Returns false when there isn't enough data or the trend isn't increasing.
    /// </summary>
    public bool TryLogisticFit(out double f0, out double k)
    {
        f0 = 0; k = 0;

        double sw = 0, swx = 0, swy = 0, swxx = 0, swxy = 0;
        int used = 0;
        for (int i = 0; i < _zero.Length; i++)
        {
            long tot = _zero[i] + _nonZero[i];
            if (tot == 0) continue;

            // Add-0.5 smoothing keeps the logit finite at all-on / all-off buckets.
            double p     = (_nonZero[i] + 0.5) / (tot + 1.0);
            double logit = Math.Log(p / (1.0 - p));
            double x     = BucketCenterGf(i);
            double w     = tot;

            sw += w; swx += w * x; swy += w * logit;
            swxx += w * x * x; swxy += w * x * logit;
            used++;
        }
        if (used < 2) return false;

        double denom = sw * swxx - swx * swx;
        if (Math.Abs(denom) < 1e-9) return false;

        double b = (sw * swxy - swx * swy) / denom;   // slope = k
        double a = (swy - b * swx) / sw;              // intercept
        if (b <= 0 || !double.IsFinite(b) || !double.IsFinite(a)) return false;

        k  = b;
        f0 = -a / b;
        return true;
    }

    /// <summary>
    /// Estimated IAF: the lower edge of the lowest-force bucket (with data) where
    /// the on-count meets or exceeds the off-count. Null if no crossover yet.
    /// </summary>
    public double? CrossoverGf
    {
        get
        {
            for (int i = 0; i < _zero.Length; i++)
            {
                long on = _nonZero[i], off = _zero[i];
                if (on + off > 0 && on >= off) return BucketLowerGf(i);
            }
            return null;
        }
    }
}
