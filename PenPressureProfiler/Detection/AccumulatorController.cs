namespace PenPressureProfiler.Detection;

/// <summary>
/// Accumulates pen-activation statistics against physical force for the IAF
/// estimate. For each scale sample the physical force is bucketed and an off
/// (pen 0%) or on (pen non-zero) counter is incremented; the force where "on"
/// overtakes "off" is the IAF.
/// <para>
/// Every supported bucket width is accumulated <b>simultaneously</b> (one layout
/// each), so switching the displayed width just swaps which layout is shown — no
/// data is lost. Changing the force range rebuilds and clears all layouts.
/// </para>
/// <para>Fed one count per scale sample, paired with the current (lag-aligned)
/// pen state — the caller handles the scale-lag time alignment.</para>
/// </summary>
public sealed class AccumulatorController
{
    public const double DefaultMinGf       = 0.0;
    public const double DefaultMaxGf       = 10.0;
    public const double DefaultBucketWidth = 0.5;

    /// <summary>Bucket widths (gf) accumulated in parallel. Must match the UI picker.</summary>
    public static readonly double[] BucketWidths = { 1.0, 0.5, 0.25, 0.1 };

    private Layout[] _layouts = [];
    private int      _selected;
    private uint     _lastPenRaw;
    private double   _lastGf;
    private bool     _lastWasZero;
    private bool     _hasLast;

    public AccumulatorController() => Configure(DefaultMinGf, DefaultMaxGf, DefaultBucketWidth);

    private Layout Sel => _layouts[_selected];

    public double MinGf       => Sel.Min;
    public double MaxGf       => Sel.Max;
    public double BucketWidth => Sel.Width;
    public int    BucketCount => Sel.Zero.Length;

    /// <summary>Per-bucket count of scale samples taken while the pen read 0%.</summary>
    public IReadOnlyList<long> ZeroCounts    => Sel.Zero;
    /// <summary>Per-bucket count of scale samples taken while the pen read non-zero.</summary>
    public IReadOnlyList<long> NonZeroCounts => Sel.NonZero;

    /// <summary>Counts for samples below the range (force &lt; <see cref="MinGf"/>).</summary>
    public long BelowZero    => Sel.BelowZero;
    public long BelowNonZero => Sel.BelowNonZero;
    /// <summary>Counts for samples at/above the range (force &gt;= <see cref="MaxGf"/>).</summary>
    public long AboveZero    => Sel.AboveZero;
    public long AboveNonZero => Sel.AboveNonZero;

    /// <summary>Lower edge (gf) of bucket <paramref name="i"/>.</summary>
    public double BucketLowerGf(int i)  => Sel.Min + i * Sel.Width;
    /// <summary>Centre (gf) of bucket <paramref name="i"/>.</summary>
    public double BucketCenterGf(int i) => Sel.Min + (i + 0.5) * Sel.Width;

    public long TotalSamples
    {
        get
        {
            var s = Sel;
            long t = s.BelowZero + s.BelowNonZero + s.AboveZero + s.AboveNonZero;
            for (int i = 0; i < s.Zero.Length; i++) t += s.Zero[i] + s.NonZero[i];
            return t;
        }
    }

    /// <summary>Rebuilds and clears every width layout for the given range, then
    /// selects the one matching <paramref name="bucketWidth"/>. Use for the initial
    /// setup and for range changes (which can't preserve data).</summary>
    public void Configure(double minGf, double maxGf, double bucketWidth)
    {
        if (minGf < 0)      minGf = 0;
        if (maxGf <= minGf) maxGf = minGf + (bucketWidth > 0 ? bucketWidth : DefaultBucketWidth);

        _layouts = new Layout[BucketWidths.Length];
        for (int i = 0; i < BucketWidths.Length; i++)
            _layouts[i] = new Layout(minGf, maxGf, BucketWidths[i]);

        _selected = SelectIndex(bucketWidth);
        ResetLast();
    }

    /// <summary>Switches the displayed bucket width WITHOUT clearing — that width's
    /// layout is already being accumulated, so the data is preserved.</summary>
    public void SetWidth(double bucketWidth) => _selected = SelectIndex(bucketWidth);

    private static int SelectIndex(double width)
    {
        int best = 0;
        double bestDist = double.MaxValue;
        for (int i = 0; i < BucketWidths.Length; i++)
        {
            double d = Math.Abs(BucketWidths[i] - width);
            if (d < 1e-9) return i;
            if (d < bestDist) { bestDist = d; best = i; }
        }
        return best;
    }

    /// <summary>Tracks the latest pen state; the on/off classification at the next
    /// scale sample uses this.</summary>
    public void OnPenData(PenReadingData d) => _lastPenRaw = d.RawPressure;

    /// <summary>Which group the most recent sample landed in (for live cell
    /// highlighting), evaluated against the selected layout.</summary>
    public enum ChangedKind { None, Below, Bucket, Above }

    public ChangedKind LastChanged
    {
        get
        {
            if (!_hasLast)          return ChangedKind.None;
            if (_lastGf < Sel.Min)  return ChangedKind.Below;
            if (_lastGf >= Sel.Max) return ChangedKind.Above;
            return ChangedKind.Bucket;
        }
    }
    public int  LastBucket          => LastChanged == ChangedKind.Bucket ? Sel.BucketIndex(_lastGf) : -1;
    public bool LastZeroIncremented => _lastWasZero;

    /// <summary>Records one scale sample into every width layout, using the current
    /// pen on/off state.</summary>
    public void OnScaleData(double gf)
    {
        bool isZero = _lastPenRaw == 0;
        foreach (var layout in _layouts) layout.Add(gf, isZero);
        _lastGf = gf;
        _lastWasZero = isZero;
        _hasLast = true;
    }

    public void Clear()
    {
        foreach (var layout in _layouts) layout.ClearCounts();
        ResetLast();
    }

    private void ResetLast()
    {
        _lastPenRaw = 0;
        _lastGf = 0;
        _lastWasZero = false;
        _hasLast = false;
    }

    // ── Save / load (all layouts) ─────────────────────────────────────────────

    public sealed record LayoutCounts(
        double Width, long[] Zero, long[] NonZero,
        long BelowZero, long BelowNonZero, long AboveZero, long AboveNonZero);

    /// <summary>Snapshots every width layout (deep copies) for saving.</summary>
    public IReadOnlyList<LayoutCounts> ExportLayouts() =>
        _layouts.Select(l => new LayoutCounts(
            l.Width, (long[])l.Zero.Clone(), (long[])l.NonZero.Clone(),
            l.BelowZero, l.BelowNonZero, l.AboveZero, l.AboveNonZero)).ToList();

    /// <summary>Rebuilds layouts for the range and fills them from saved data,
    /// selecting <paramref name="selectedWidth"/>.</summary>
    public void ImportLayouts(
        double minGf, double maxGf, double selectedWidth, IReadOnlyList<LayoutCounts> layouts)
    {
        Configure(minGf, maxGf, selectedWidth);
        foreach (var lc in layouts)
        {
            var target = Array.Find(_layouts, l => Math.Abs(l.Width - lc.Width) < 1e-9);
            if (target is null) continue;
            int n = Math.Min(target.Zero.Length, Math.Min(lc.Zero?.Length ?? 0, lc.NonZero?.Length ?? 0));
            for (int i = 0; i < n; i++) { target.Zero[i] = lc.Zero![i]; target.NonZero[i] = lc.NonZero![i]; }
            target.BelowZero = lc.BelowZero;   target.BelowNonZero = lc.BelowNonZero;
            target.AboveZero = lc.AboveZero;   target.AboveNonZero = lc.AboveNonZero;
        }
        _selected = SelectIndex(selectedWidth);
    }

    // ── Estimates (over the selected layout) ──────────────────────────────────

    /// <summary>
    /// Count-weighted logistic fit of P(on) = 1 / (1 + e^(−k·(F − F0))) over the
    /// selected layout's buckets. <paramref name="f0"/> is the 50% point (the IAF
    /// estimate) and <paramref name="k"/> the steepness. False when there isn't
    /// enough data or the trend isn't increasing.
    /// </summary>
    public bool TryLogisticFit(out double f0, out double k)
    {
        f0 = 0; k = 0;
        var s = Sel;

        double sw = 0, swx = 0, swy = 0, swxx = 0, swxy = 0;
        int used = 0;
        for (int i = 0; i < s.Zero.Length; i++)
        {
            long tot = s.Zero[i] + s.NonZero[i];
            if (tot == 0) continue;

            double p     = (s.NonZero[i] + 0.5) / (tot + 1.0);   // add-0.5 smoothing
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

        double b = (sw * swxy - swx * swy) / denom;
        double a = (swy - b * swx) / sw;
        if (b <= 0 || !double.IsFinite(b) || !double.IsFinite(a)) return false;

        k  = b;
        f0 = -a / b;
        return true;
    }

    /// <summary>Lowest-force bucket (with data) where on ≥ off, in the selected
    /// layout. Null if no crossover yet.</summary>
    public double? CrossoverGf
    {
        get
        {
            var s = Sel;
            for (int i = 0; i < s.Zero.Length; i++)
            {
                long on = s.NonZero[i], off = s.Zero[i];
                if (on + off > 0 && on >= off) return BucketLowerGf(i);
            }
            return null;
        }
    }

    /// <summary>One bucket width's counts over a fixed range.</summary>
    private sealed class Layout
    {
        public readonly double Min, Max, Width;
        public readonly long[] Zero, NonZero;
        public long BelowZero, BelowNonZero, AboveZero, AboveNonZero;

        public Layout(double min, double max, double width)
        {
            Min = min; Max = max; Width = width;
            int count = Math.Max(1, (int)Math.Round((max - min) / width));
            Zero    = new long[count];
            NonZero = new long[count];
        }

        public int BucketIndex(double gf)
        {
            int b = (int)((gf - Min) / Width);
            return b < 0 ? 0 : (b >= Zero.Length ? Zero.Length - 1 : b);
        }

        public void Add(double gf, bool isZero)
        {
            if (gf < Min)       { if (isZero) BelowZero++; else BelowNonZero++; }
            else if (gf >= Max) { if (isZero) AboveZero++; else AboveNonZero++; }
            else
            {
                int b = BucketIndex(gf);
                if (isZero) Zero[b]++; else NonZero[b]++;
            }
        }

        public void ClearCounts()
        {
            Array.Clear(Zero);
            Array.Clear(NonZero);
            BelowZero = BelowNonZero = AboveZero = AboveNonZero = 0;
        }
    }
}
