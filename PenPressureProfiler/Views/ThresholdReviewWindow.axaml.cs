using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using PenPressureProfiler.Controls;
using ScottPlot;
using System.Text.Json;

namespace PenPressureProfiler.Views;

/// <summary>
/// One row of the review table — a single pen or scale sample. Numeric columns
/// not applicable to the row's source are left blank.
/// </summary>
public sealed record ReviewRow(
    string Time,
    string TimeMs,
    string DeltaMs,
    string Source,
    string Raw,
    string Norm,
    string Smooth,
    string Tip,
    string Gf,
    IBrush RowBackground,
    IBrush SourceColor);

/// <summary>
/// Read-only modal for reviewing the raw pen/scale samples leading up to a single
/// threshold detection. Shows a time-ordered table (left) and a dual-axis time
/// series (right) with a marker at the detection instant. The Focus buttons zoom
/// the chart to the lead-up window so capture issues can be diagnosed.
/// </summary>
public partial class ThresholdReviewWindow : Window
{
    private static readonly IBrush PenBrush       = new SolidColorBrush(Avalonia.Media.Color.FromRgb(0x25, 0x63, 0xEB));
    private static readonly IBrush ScaleBrush     = new SolidColorBrush(Avalonia.Media.Color.FromRgb(0x16, 0xA3, 0x4A));
    private static readonly IBrush NearHighlight  = new SolidColorBrush(Avalonia.Media.Color.FromArgb(0x30, 0xF5, 0x9E, 0x0B));
    private static readonly IBrush NoHighlight    = Brushes.Transparent;

    private static readonly JsonSerializerOptions JsonOptions =
        new() { WriteIndented = true };
    private static readonly FilePickerFileType JsonFilter =
        new("JSON files") { Patterns = ["*.json"] };

    private readonly ThresholdRecording _recording;
    private readonly string             _title;
    private double _tMin;   // earliest sample, seconds relative to detection (≤ 0)
    private double _tMax;   // latest sample, seconds relative to detection (≈ 0)

    public ThresholdReviewWindow(ThresholdRecording recording, string title)
    {
        InitializeComponent();

        _recording = recording;
        _title     = title;
        txt_title.Text = title;

        BuildRows();
        BuildFooter();

        Loaded += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            InitPlot();
            // The detection is the last (most recent) row — open scrolled to it.
            if (listBox_rows.ItemCount > 0)
                listBox_rows.ScrollIntoView(listBox_rows.ItemCount - 1);
        }, DispatcherPriority.Background);
    }

    // ── Table ─────────────────────────────────────────────────────────────────

    private void BuildRows()
    {
        // Intermediate per-sample entries; Time and the inter-row delta are
        // assigned after merging + sorting both streams by timestamp.
        var entries = new List<Sample>();

        foreach (var p in _recording.PenSamples)
            entries.Add(new Sample(
                T:      p.Timestamp,
                Source: "pen",
                Raw:    p.RawPressure.ToString(),
                Norm:   (p.NormalizedPressure * 100).ToString("F1"),
                Smooth: (p.SmoothedPressure * 100).ToString("F1"),
                Tip:    p.TipDown ? "↓" : "·",
                Gf:     "",
                Color:  PenBrush));

        foreach (var s in _recording.ScaleSamples)
            entries.Add(new Sample(
                T:      s.Timestamp,
                Source: "scale",
                Raw:    "",
                Norm:   "",
                Smooth: "",
                Tip:    "",
                Gf:     s.ForceGf.ToString("F2"),
                Color:  ScaleBrush));

        var sorted = entries.OrderBy(s => s.T).ToList();

        var rows = new List<ReviewRow>(sorted.Count);
        DateTime? prev = null;
        foreach (var s in sorted)
        {
            double relMs = (s.T - _recording.DetectedAt).TotalMilliseconds;
            string delta = prev is { } p
                ? (s.T - p).TotalMilliseconds.ToString("F0")
                : "—";
            prev = s.T;

            rows.Add(new ReviewRow(
                Time:          s.T.ToLocalTime().ToString("HH:mm:ss.fff"),
                TimeMs:        FormatMs(relMs),
                DeltaMs:       delta,
                Source:        s.Source,
                Raw:           s.Raw,
                Norm:          s.Norm,
                Smooth:        s.Smooth,
                Tip:           s.Tip,
                Gf:            s.Gf,
                RowBackground: relMs >= -1000 ? NearHighlight : NoHighlight,
                SourceColor:   s.Color));
        }

        listBox_rows.ItemsSource = rows;
    }

    /// <summary>A merged-stream sample before Time / inter-row delta are assigned.</summary>
    private readonly record struct Sample(
        DateTime T, string Source, string Raw, string Norm,
        string Smooth, string Tip, string Gf, IBrush Color);

    private static string FormatMs(double ms) =>
        ((int)Math.Round(ms)).ToString();

    private void BuildFooter()
    {
        int penCount   = _recording.PenSamples.Count;
        int scaleCount = _recording.ScaleSamples.Count;

        ComputeSpan(out _tMin, out _tMax);
        double span = _tMax - _tMin;

        txt_footer.Text =
            $"{penCount} pen + {scaleCount} scale samples over {span:F1} s  ·  " +
            $"detection at {_recording.DetectedAt.ToLocalTime():HH:mm:ss.fff}  ·  " +
            "amber = last 1 s before detection";
    }

    /// <summary>Earliest / latest sample time, in seconds relative to detection.</summary>
    private void ComputeSpan(out double tMin, out double tMax)
    {
        double min = 0, max = 0;
        bool any = false;

        void Note(DateTime t)
        {
            double s = (t - _recording.DetectedAt).TotalSeconds;
            if (!any) { min = max = s; any = true; }
            else { min = Math.Min(min, s); max = Math.Max(max, s); }
        }

        foreach (var p in _recording.PenSamples)   Note(p.Timestamp);
        foreach (var s in _recording.ScaleSamples) Note(s.Timestamp);

        if (!any) { min = -1; max = 0; }
        tMin = min;
        tMax = max;
    }

    // ── Chart ───────────────────────────────────────────────────────────────

    private void InitPlot()
    {
        var plt = reviewPlot.Plot;
        plt.Clear();

        plt.XLabel("time (s rel. detection)");
        plt.YLabel("Pen (norm %)");

        // Pen normalized (%) on the left axis.
        var penX = _recording.PenSamples
            .Select(p => (p.Timestamp - _recording.DetectedAt).TotalSeconds).ToArray();
        var penY = _recording.PenSamples
            .Select(p => p.NormalizedPressure * 100).ToArray();

        if (penX.Length > 0)
        {
            var pen = plt.Add.Scatter(penX, penY);
            pen.Color      = ScottPlot.Color.FromHex("#2563EB");
            pen.LineWidth  = 1;
            pen.MarkerSize = 4;
        }

        // Scale force (gf) on the right axis.
        var scaleX = _recording.ScaleSamples
            .Select(s => (s.Timestamp - _recording.DetectedAt).TotalSeconds).ToArray();
        var scaleY = _recording.ScaleSamples
            .Select(s => s.ForceGf).ToArray();

        if (scaleX.Length > 0)
        {
            var scale = plt.Add.Scatter(scaleX, scaleY);
            scale.Color        = ScottPlot.Color.FromHex("#16A34A");
            scale.LineWidth    = 1.5f;
            scale.MarkerSize   = 4;
            scale.Axes.YAxis   = plt.Axes.Right;
        }

        // Detection marker at t = 0.
        var det = plt.Add.VerticalLine(0);
        det.Color       = ScottPlot.Color.FromHex("#DC2626");
        det.LineWidth   = 2;
        det.LinePattern = LinePattern.Dashed;
        det.Text        = "detection";

        // Axis ranges (Y fixed to data; X driven by the Focus buttons).
        double leftMax  = penY.Length   > 0 ? Math.Max(penY.Max()   * 1.15, 5)  : 100;
        double rightMax = scaleY.Length > 0 ? Math.Max(scaleY.Max() * 1.15, 10) : 100;
        plt.Axes.Left.Min  = 0; plt.Axes.Left.Max  = leftMax;
        plt.Axes.Right.Min = 0; plt.Axes.Right.Max = rightMax;
        plt.Axes.Right.Label.Text = "Scale (gf)";

        ChartTheme.Apply(reviewPlot);   // palette + zoom/pan enabled

        ApplyFocus(3);   // default to the 3 s lead-up — the debugging interest
    }

    /// <summary>Zooms the X axis to the last <paramref name="seconds"/> before
    /// detection (clamped to the recorded extent), keeping the detection marker
    /// in view.</summary>
    private void ApplyFocus(double seconds)
    {
        double left  = Math.Max(-seconds, _tMin - 0.05);
        double right = Math.Max(_tMax, 0) + 0.15;
        reviewPlot.Plot.Axes.SetLimitsX(left, right);
        reviewPlot.Refresh();
    }

    private void Focus1s_Click(object? sender, RoutedEventArgs e)   => ApplyFocus(1);
    private void Focus3s_Click(object? sender, RoutedEventArgs e)   => ApplyFocus(3);

    private void FocusFull_Click(object? sender, RoutedEventArgs e)
    {
        reviewPlot.Plot.Axes.SetLimitsX(_tMin - 0.05, Math.Max(_tMax, 0) + 0.15);
        reviewPlot.Refresh();
    }

    private async void Save_Click(object? sender, RoutedEventArgs e)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title             = "Save capture recording",
            SuggestedFileName = $"ThresholdCapture_{_recording.DetectedAt.ToLocalTime():yyyy-MM-dd_HHmmss}.json",
            FileTypeChoices   = [JsonFilter],
            DefaultExtension  = "json",
        });
        if (file is null) return;

        try
        {
            var dto = ThresholdRecordingFile.From(_recording, _title);
            await using var stream = await file.OpenWriteAsync();
            await JsonSerializer.SerializeAsync(stream, dto, JsonOptions);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[PPP2] Capture save failed: {ex.Message}");
        }
    }

    private void Close_Click(object? sender, RoutedEventArgs e) => Close();
}
