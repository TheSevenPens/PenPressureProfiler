namespace PenPressureProfiler.Detection;

/// <summary>Which transition the accumulator measures.</summary>
public enum AccumTarget
{
    /// <summary>Initial activation force — force where the pen first reads non-zero.</summary>
    Iaf,
    /// <summary>Saturation force — force where the pen reaches 100% (raw == driver max).</summary>
    Saturation,
}

/// <summary>
/// Accumulates pen-activation statistics against physical force. For each scale
/// sample the physical force is bucketed and an <i>off</i> or <i>on</i> counter
/// is incremented; the force where "on" overtakes "off" is the estimate (F0).
/// <para>
/// The same machinery serves two <see cref="AccumTarget"/>s that differ only in
/// the on/off classifier and the force scale:
/// <list type="bullet">
///   <item><b>IAF</b>: on = pen &gt; 0% (low force, fine buckets).</item>
///   <item><b>Saturation</b>: on = pen at 100% i.e. raw ≥ <see cref="MaxPressure"/>
///   (high force, coarse buckets).</item>
/// </list>
/// Each target keeps its own range, bucket-width set, selected width and counts,
/// preserved when toggling. Only the <see cref="Target"/> currently selected
/// accumulates new samples.
/// </para>
/// <para>Within a target every supported bucket width is accumulated
/// <b>simultaneously</b> (one layout each), so switching the displayed width just
/// swaps which layout is shown — no data is lost. Changing the force range
/// rebuilds and clears that target's layouts.</para>
/// <para>Fed one count per scale sample, paired with the current (lag-aligned)
/// pen state — the caller handles the scale-lag time alignment.</para>
/// </summary>
public sealed class AccumulatorController
{
    // ── Per-target defaults ───────────────────────────────────────────────────
    public const double IafDefaultMinGf  = 0.0;
    public const double IafDefaultMaxGf  = 10.0;
    public const double IafDefaultWidth  = 0.5;
    public const double SatDefaultMinGf  = 0.0;
    public const double SatDefaultMaxGf  = 500.0;
    public const double SatDefaultWidth  = 25.0;

    /// <summary>Bucket widths (gf) accumulated in parallel for the IAF target.</summary>
    public static readonly double[] IafBucketWidths = { 1.0, 0.5, 0.25, 0.1 };
    /// <summary>Bucket widths (gf) for the Saturation target (coarse — high force).</summary>
    public static readonly double[] SatBucketWidths = { 50.0, 25.0, 10.0, 5.0 };

    private readonly TargetState[] _states;
    private uint _lastPenRaw;

    /// <summary>The transition currently being measured (and accumulated).</summary>
    public AccumTarget Target { get; private set; } = AccumTarget.Iaf;

    /// <summary>Driver max raw pressure, used to classify "saturated" (raw ≥ max)
    /// for the <see cref="AccumTarget.Saturation"/> target. Set by the caller from
    /// the active pen session; 0 disables saturation detection.</summary>
    public int MaxPressure { get; set; }

    public AccumulatorController()
    {
        _states = new TargetState[2];
        _states[(int)AccumTarget.Iaf]        = new TargetState(IafBucketWidths, IafDefaultMinGf, IafDefaultMaxGf, IafDefaultWidth);
        _states[(int)AccumTarget.Saturation] = new TargetState(SatBucketWidths, SatDefaultMinGf, SatDefaultMaxGf, SatDefaultWidth);
    }

    private TargetState Active => _states[(int)Target];
    private Layout      Sel    => Active.Sel;

    /// <summary>Switches which target is measured/displayed. The other target's
    /// data and config are preserved.</summary>
    public void SetTarget(AccumTarget target) => Target = target;

    /// <summary>Bucket widths available for the active target (for the UI picker).</summary>
    public IReadOnlyList<double> CurrentBucketWidths => Active.Widths;

    public double MinGf       => Sel.Min;
    public double MaxGf       => Sel.Max;
    public double BucketWidth => Sel.Width;
    public int    BucketCount => Sel.Zero.Length;

    /// <summary>Per-bucket count of scale samples taken while the pen was "off".</summary>
    public IReadOnlyList<long> ZeroCounts    => Sel.Zero;
    /// <summary>Per-bucket count of scale samples taken while the pen was "on".</summary>
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

    /// <summary>Rebuilds and clears every width layout of the <b>active</b> target
    /// for the given range, then selects the one matching <paramref name="bucketWidth"/>.
    /// Use for range changes (which can't preserve data).</summary>
    public void Configure(double minGf, double maxGf, double bucketWidth)
    {
        Active.Configure(minGf, maxGf, bucketWidth);
        _lastPenRaw = 0;
    }

    /// <summary>Switches the active target's displayed bucket width WITHOUT clearing
    /// — that width's layout is already being accumulated, so data is preserved.</summary>
    public void SetWidth(double bucketWidth) => Active.SetWidth(bucketWidth);

    /// <summary>Tracks the latest pen state; the on/off classification at the next
    /// scale sample uses this.</summary>
    public void OnPenData(PenReadingData d) => _lastPenRaw = d.RawPressure;

    /// <summary>Classifies the current pen reading as "on" for the active target.</summary>
    private bool IsOn(uint raw) => Target == AccumTarget.Saturation
        ? MaxPressure > 0 && raw >= (uint)MaxPressure
        : raw > 0;

    /// <summary>Which group the most recent sample landed in (for live cell
    /// highlighting), evaluated against the active target's selected layout.</summary>
    public enum ChangedKind { None, Below, Bucket, Above }

    public ChangedKind LastChanged
    {
        get
        {
            var a = Active;
            if (!a.HasLast)         return ChangedKind.None;
            if (a.LastGf < Sel.Min) return ChangedKind.Below;
            if (a.LastGf >= Sel.Max) return ChangedKind.Above;
            return ChangedKind.Bucket;
        }
    }
    public int  LastBucket          => LastChanged == ChangedKind.Bucket ? Sel.BucketIndex(Active.LastGf) : -1;
    public bool LastZeroIncremented => Active.LastWasOff;

    /// <summary>Records one scale sample into the active target's width layouts,
    /// using the current pen on/off state.</summary>
    public void OnScaleData(double gf) => Active.Add(gf, isOff: !IsOn(_lastPenRaw));

    /// <summary>Clears the active target's counts (the other target is preserved).</summary>
    public void Clear()
    {
        Active.ClearCounts();
        _lastPenRaw = 0;
    }

    // ── Save / load (per target) ──────────────────────────────────────────────

    public sealed record LayoutCounts(
        double Width, long[] Zero, long[] NonZero,
        long BelowZero, long BelowNonZero, long AboveZero, long AboveNonZero);

    /// <summary>The given target's current range + selected width.</summary>
    public (double Min, double Max, double Width) GetConfig(AccumTarget t)
    {
        var s = _states[(int)t].Sel;
        return (s.Min, s.Max, s.Width);
    }

    /// <summary>Snapshots every width layout (deep copies) of the given target.</summary>
    public IReadOnlyList<LayoutCounts> ExportLayouts(AccumTarget t) =>
        _states[(int)t].Layouts.Select(l => new LayoutCounts(
            l.Width, (long[])l.Zero.Clone(), (long[])l.NonZero.Clone(),
            l.BelowZero, l.BelowNonZero, l.AboveZero, l.AboveNonZero)).ToList();

    /// <summary>Rebuilds the given target's layouts for the range and fills them from
    /// saved data, selecting <paramref name="selectedWidth"/>.</summary>
    public void ImportLayouts(
        AccumTarget t, double minGf, double maxGf, double selectedWidth, IReadOnlyList<LayoutCounts> layouts)
    {
        var state = _states[(int)t];
        state.Configure(minGf, maxGf, selectedWidth);
        foreach (var lc in layouts)
        {
            var target = Array.Find(state.Layouts, l => Math.Abs(l.Width - lc.Width) < 1e-9);
            if (target is null) continue;
            int n = Math.Min(target.Zero.Length, Math.Min(lc.Zero?.Length ?? 0, lc.NonZero?.Length ?? 0));
            for (int i = 0; i < n; i++) { target.Zero[i] = lc.Zero![i]; target.NonZero[i] = lc.NonZero![i]; }
            target.BelowZero = lc.BelowZero;   target.BelowNonZero = lc.BelowNonZero;
            target.AboveZero = lc.AboveZero;   target.AboveNonZero = lc.AboveNonZero;
        }
    }

    // ── Estimates (over the active target's selected layout) ───────────────────

    /// <summary>
    /// Count-weighted logistic fit of P(on) = 1 / (1 + e^(−k·(F − F0))) over the
    /// selected layout's buckets. <paramref name="f0"/> is the 50% point (the force
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

    /// <summary>Lowest-force bucket (with data) where on ≥ off, in the active
    /// target's selected layout. Null if no crossover yet.</summary>
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

    /// <summary>One target's parallel width layouts over a shared force range.</summary>
    private sealed class TargetState
    {
        public readonly double[] Widths;
        public Layout[] Layouts = [];
        public int      Selected;

        public double LastGf;
        public bool   LastWasOff;
        public bool   HasLast;

        public TargetState(double[] widths, double min, double max, double width)
        {
            Widths = widths;
            Configure(min, max, width);
        }

        public Layout Sel => Layouts[Selected];

        public void Configure(double minGf, double maxGf, double bucketWidth)
        {
            if (minGf < 0)      minGf = 0;
            if (maxGf <= minGf) maxGf = minGf + (bucketWidth > 0 ? bucketWidth : Widths[0]);

            Layouts = new Layout[Widths.Length];
            for (int i = 0; i < Widths.Length; i++)
                Layouts[i] = new Layout(minGf, maxGf, Widths[i]);

            Selected = SelectIndex(bucketWidth);
            ResetLast();
        }

        public void SetWidth(double width) => Selected = SelectIndex(width);

        private int SelectIndex(double width)
        {
            int best = 0;
            double bestDist = double.MaxValue;
            for (int i = 0; i < Widths.Length; i++)
            {
                double d = Math.Abs(Widths[i] - width);
                if (d < 1e-9) return i;
                if (d < bestDist) { bestDist = d; best = i; }
            }
            return best;
        }

        public void Add(double gf, bool isOff)
        {
            foreach (var layout in Layouts) layout.Add(gf, isOff);
            LastGf = gf;
            LastWasOff = isOff;
            HasLast = true;
        }

        public void ClearCounts()
        {
            foreach (var layout in Layouts) layout.ClearCounts();
            ResetLast();
        }

        private void ResetLast()
        {
            LastGf = 0;
            LastWasOff = false;
            HasLast = false;
        }
    }

    /// <summary>One bucket width's counts over a fixed range. "Zero"/"NonZero" are
    /// the off/on counters for whichever target owns this layout.</summary>
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

        public void Add(double gf, bool isOff)
        {
            if (gf < Min)       { if (isOff) BelowZero++; else BelowNonZero++; }
            else if (gf >= Max) { if (isOff) AboveZero++; else AboveNonZero++; }
            else
            {
                int b = BucketIndex(gf);
                if (isOff) Zero[b]++; else NonZero[b]++;
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
