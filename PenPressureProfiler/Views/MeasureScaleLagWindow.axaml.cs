using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using PenPressureProfiler.Controls;
using ScottPlot;

namespace PenPressureProfiler.Views;

/// <summary>
/// Non-modal tool that measures the pen → scale response lag. The user clicks
/// Start and taps the pen on the scale a number of times; each tap produces a
/// peak in the pen stream (fast) and a lagging peak in the scale stream. The
/// per-tap time deltas between the paired peaks give Min / Max / Avg / Median
/// delay in milliseconds.
/// <para>
/// Live samples are pushed in from <c>MainWindow</c> via <see cref="FeedPen"/> /
/// <see cref="FeedScale"/> while the window is open.
/// </para>
/// </summary>
public partial class MeasureScaleLagWindow : Window
{
    private const int    TargetTaps      = 10;
    private const double LivePenOnThresh = 0.10;   // normalized: rising edge of a tap
    private const double LivePenOffThresh= 0.05;   // must fall below this to re-arm
    private const double ChartRefreshMs  = 80;

    // "Play" bands: a tap's onset is the rise above the band, its release the fall
    // back below it. Detecting on a band (not zero) ignores baseline noise.
    private const double ScaleLowGf = 5.0;    // scale onset/release band (gf)
    private const double PenLowNorm = 0.02;   // pen onset/release band (normalized)

    // Recorded samples, seconds since Start.
    private readonly List<(double T, double V)> _pen   = [];
    private readonly List<(double T, double V)> _scale = [];

    private bool     _recording;
    private DateTime _t0;
    private DateTime _lastRefresh = DateTime.MinValue;

    // Live tap counter (pen rising edges).
    private bool _penAbove;
    private int  _liveTaps;

    // Detected per-tap excursions (seconds since Start), drawn after analysis.
    private IReadOnlyList<Excursion> _penEx   = [];
    private IReadOnlyList<Excursion> _scaleEx = [];

    /// <summary>One tap's edges in a stream: rise above the band, the peak, and
    /// the fall back below the band — all seconds since Start.</summary>
    private readonly record struct Excursion(double Rise, double Peak, double Fall);

    public MeasureScaleLagWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => Dispatcher.UIThread.Post(InitPlots, DispatcherPriority.Background);
    }

    // ── Live feed (called from MainWindow on the UI thread) ──────────────────

    public void FeedPen(double normalized)
    {
        if (!_recording) return;
        double t = (DateTime.UtcNow - _t0).TotalSeconds;
        _pen.Add((t, normalized));

        // Count taps on the rising edge; auto-stop at the target.
        if (normalized >= LivePenOnThresh && !_penAbove)
        {
            _penAbove = true;
            _liveTaps++;
            txt_status.Text = $"Recording — {_liveTaps} / {TargetTaps} taps";
            if (_liveTaps >= TargetTaps)
            {
                StopAndAnalyze();
                return;
            }
        }
        else if (normalized <= LivePenOffThresh && _penAbove)
        {
            _penAbove = false;
        }

        RefreshChartsThrottled();
    }

    public void FeedScale(double gf)
    {
        if (!_recording) return;
        double t = (DateTime.UtcNow - _t0).TotalSeconds;
        _scale.Add((t, gf));
        RefreshChartsThrottled();
    }

    // ── Controls ─────────────────────────────────────────────────────────────

    private void Start_Click(object? sender, RoutedEventArgs e)
    {
        _pen.Clear();
        _scale.Clear();
        _penEx = [];
        _scaleEx = [];
        _penAbove = false;
        _liveTaps = 0;
        _recording = true;
        _t0 = DateTime.UtcNow;

        txt_status.Text = $"Recording — 0 / {TargetTaps} taps";
        ResetResults();
        txt_detail.Text = "";

        btn_start.IsEnabled = false;
        btn_stop.IsEnabled  = true;

        RefreshCharts();
    }

    private void Stop_Click(object? sender, RoutedEventArgs e) => StopAndAnalyze();

    private void StopAndAnalyze()
    {
        if (!_recording) return;
        _recording = false;
        btn_start.IsEnabled = true;
        btn_stop.IsEnabled  = false;

        Analyze();
        RefreshCharts();   // redraw with peak markers
    }

    // ── Analysis ─────────────────────────────────────────────────────────────

    private void Analyze()
    {
        _penEx   = DetectExcursions(_pen,   PenLowNorm);
        _scaleEx = DetectExcursions(_scale, ScaleLowGf);

        // Pair pen and scale taps by peak proximity; collect the three deltas.
        var rise = new List<double>();
        var peak = new List<double>();
        var fall = new List<double>();

        var used = new bool[_scaleEx.Count];
        foreach (var pe in _penEx)
        {
            int    bestIdx  = -1;
            double bestDist = double.MaxValue;
            for (int i = 0; i < _scaleEx.Count; i++)
            {
                if (used[i]) continue;
                double d = _scaleEx[i].Peak - pe.Peak;
                if (d < -PairEarlyToleranceS || d > PairWindowS) continue;
                double dist = Math.Abs(d);
                if (dist < bestDist) { bestDist = dist; bestIdx = i; }
            }
            if (bestIdx < 0) continue;

            used[bestIdx] = true;
            var se = _scaleEx[bestIdx];
            rise.Add((se.Rise - pe.Rise) * 1000.0);
            peak.Add((se.Peak - pe.Peak) * 1000.0);
            fall.Add((se.Fall - pe.Fall) * 1000.0);
        }

        ResetResults();

        if (peak.Count == 0)
        {
            txt_status.Text = "No taps paired — check that both pen and scale are streaming, and tap firmly.";
            txt_detail.Text = $"pen taps: {_penEx.Count}, scale taps: {_scaleEx.Count}";
            return;
        }

        ShowStats(rise, txt_rise_min, txt_rise_max, txt_rise_avg, txt_rise_med);
        ShowStats(peak, txt_peak_min, txt_peak_max, txt_peak_avg, txt_peak_med);
        ShowStats(fall, txt_fall_min, txt_fall_max, txt_fall_avg, txt_fall_med);

        txt_status.Text = $"Done — {peak.Count} taps paired.";
        txt_detail.Text =
            $"pen taps: {_penEx.Count}, scale taps: {_scaleEx.Count}, paired: {peak.Count}  ·  " +
            $"peak deltas (ms): {string.Join(", ", peak.OrderBy(d => d).Select(d => d.ToString("F0")))}";
    }

    private const double PairEarlyToleranceS = 0.3;   // allow a slightly-early scale peak (noise)
    private const double PairWindowS         = 3.0;   // scale peak must arrive within this of the pen peak

    /// <summary>One excursion per tap: each contiguous run with value ≥
    /// <paramref name="band"/>, capturing the rise (entry), peak (max) and fall
    /// (exit) times.</summary>
    private static List<Excursion> DetectExcursions(List<(double T, double V)> samples, double band)
    {
        var result = new List<Excursion>();
        bool inExc = false;
        double riseT = 0, peakT = 0, peakV = double.MinValue, lastT = 0;

        foreach (var (t, v) in samples)
        {
            if (v >= band)
            {
                if (!inExc) { inExc = true; riseT = t; peakV = double.MinValue; }
                if (v > peakV) { peakV = v; peakT = t; }
                lastT = t;
            }
            else if (inExc)
            {
                // First sample back below the band is the release edge.
                result.Add(new Excursion(riseT, peakT, t));
                inExc = false;
            }
        }
        if (inExc) result.Add(new Excursion(riseT, peakT, lastT));
        return result;
    }

    private static void ShowStats(List<double> deltas, TextBlock min, TextBlock max, TextBlock avg, TextBlock med)
    {
        var sorted = deltas.OrderBy(d => d).ToList();
        min.Text = $"{sorted[0]:F0}";
        max.Text = $"{sorted[^1]:F0}";
        avg.Text = $"{sorted.Average():F0}";
        med.Text = $"{Median(sorted):F0}";
    }

    private void ResetResults()
    {
        foreach (var tb in new[]
        {
            txt_rise_min, txt_rise_max, txt_rise_avg, txt_rise_med,
            txt_peak_min, txt_peak_max, txt_peak_avg, txt_peak_med,
            txt_fall_min, txt_fall_max, txt_fall_avg, txt_fall_med,
        })
            tb.Text = "—";
    }

    private static double Median(List<double> sorted)
    {
        int n = sorted.Count;
        return n % 2 == 1
            ? sorted[n / 2]
            : (sorted[n / 2 - 1] + sorted[n / 2]) / 2.0;
    }

    // ── Charts ───────────────────────────────────────────────────────────────

    private void InitPlots()
    {
        var pp = penPlot.Plot;
        pp.XLabel("time (s)");
        pp.YLabel("Pen (norm)");
        pp.Axes.SetLimits(0, 10, 0, 1);
        ChartTheme.Apply(penPlot, userInputEnabled: false);
        penPlot.Refresh();

        var sp = scalePlot.Plot;
        sp.XLabel("time (s)");
        sp.YLabel("Scale (gf)");
        sp.Axes.SetLimits(0, 10, 0, 10);
        ChartTheme.Apply(scalePlot, userInputEnabled: false);
        scalePlot.Refresh();
    }

    private void RefreshChartsThrottled()
    {
        var now = DateTime.UtcNow;
        if ((now - _lastRefresh).TotalMilliseconds < ChartRefreshMs) return;
        _lastRefresh = now;
        RefreshCharts();
    }

    private void RefreshCharts()
    {
        DrawStream(penPlot,   _pen,   _penEx,   "#2563EB", PenLowNorm, forceMinYMax: 1.0);
        DrawStream(scalePlot, _scale, _scaleEx, "#16A34A", ScaleLowGf, forceMinYMax: 10.0);
    }

    private void DrawStream(
        ScottPlot.Avalonia.AvaPlot view,
        List<(double T, double V)> samples,
        IReadOnlyList<Excursion> excursions,
        string hexColor,
        double band,
        double forceMinYMax)
    {
        var plt = view.Plot;
        plt.Clear();

        double tMax = samples.Count > 0 ? Math.Max(samples[^1].T, 1) : 10;
        double yMax = samples.Count > 0 ? Math.Max(samples.Max(s => s.V) * 1.1, forceMinYMax) : forceMinYMax;

        if (samples.Count > 0)
        {
            var line = plt.Add.Scatter(
                samples.Select(s => s.T).ToArray(),
                samples.Select(s => s.V).ToArray());
            line.Color      = ScottPlot.Color.FromHex(hexColor);
            line.LineWidth  = 1.5f;
            line.MarkerSize = 0;
        }

        // Faint band line — the onset/release threshold.
        var bandLine = plt.Add.HorizontalLine(band);
        bandLine.Color       = ScottPlot.Color.FromHex("#9CA3AF");
        bandLine.LineWidth   = 1;
        bandLine.LinePattern = LinePattern.Dotted;

        // Edge markers (after analysis): rise (green), peak (red dashed), fall (orange).
        foreach (var ex in excursions)
        {
            AddMarker(plt, ex.Rise, "#16A34A", LinePattern.Solid);
            AddMarker(plt, ex.Peak, "#DC2626", LinePattern.Dashed);
            AddMarker(plt, ex.Fall, "#F97316", LinePattern.Solid);
        }

        plt.Axes.SetLimits(0, tMax, 0, yMax);
        view.Refresh();
    }

    private static void AddMarker(ScottPlot.Plot plt, double t, string hex, LinePattern pattern)
    {
        var v = plt.Add.VerticalLine(t);
        v.Color       = ScottPlot.Color.FromHex(hex);
        v.LineWidth   = 1;
        v.LinePattern = pattern;
    }
}
