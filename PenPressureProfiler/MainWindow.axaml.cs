using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using ScottPlot;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Text.Json;
using WinPenKit;
using DotState = PenPressureProfiler.Controls.StatusDotRow.DotState;

namespace PenPressureProfiler;

public partial class MainWindow : Window
{
    // ── Sessions ──────────────────────────────────────────────────────────────

    private readonly PenSessionManager   _penManager;
    private readonly ScaleSessionManager _scaleManager;
    private readonly SessionLogger       _sessionLogger;
    private IReadOnlyList<InputApi>      _apis = [];

    private static readonly string LogDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "PenPressureProfiler", "Logs");

    // ── Chart / recording ─────────────────────────────────────────────────────

    private const int    PlotAxisLimit     = 1000;
    private const int    PlotPressureLimit = 100;

    private SessionMetadata          _metadata = new();
    private double                   _logicalPressure;
    // (sort direction lives on the SortToggleButton controls — `btn_*_sort.Ascending`)

    // ── Stability ─────────────────────────────────────────────────────────────────

    private readonly StabilityController _stabilityController = new();
    private bool     _stabilityEnabled;

    private readonly List<double> _stabilityRawX         = [];
    private readonly List<double> _stabilityRawY         = [];
    private const int    StabilityRawMaxPoints           = 600;
    private const double StabilityChartMinRefreshMs      = 100;
    private DateTime     _lastStabilityChartRefresh       = DateTime.MinValue;

    // Throttle for the live vertical-line refresh on the Capture chart.
    private DateTime     _lastDataChartLiveRefresh    = DateTime.MinValue;

    // Live-follow: rolling trail of recent (physical gf, logical %) live points
    // used to auto zoom/pan the Capture chart to the last second.
    private const double LiveFollowWindowMs = 1000.0;
    private readonly List<(DateTime At, double PhysGf, double LogPct)> _liveTrail = [];
    private bool _liveFollow;

    // The followed view is eased toward its target each refresh frame rather
    // than snapped, so the live trace glides instead of jumping. Each axis edge
    // expands quickly (keeps the trace from clipping) but contracts slowly (so a
    // peak ageing out of the trail window doesn't snap the view inward). A
    // minimum span keeps near-steady readings from zooming in wildly.
    private const double LiveFollowExpandAlpha   = 0.40;   // per ~100 ms frame
    private const double LiveFollowContractAlpha = 0.08;
    private const double LiveFollowMinSpanX      = 20.0;   // gf
    private const double LiveFollowMinSpanY      = 15.0;   // %
    private bool   _followLimitsValid;
    private double _followXMin, _followXMax, _followYMin, _followYMax;

    // ── Accumulator ────────────────────────────────────────────────

    // Accumulator target presets (the MEASURE picker). Order matches AccumTarget.
    private const string AccumTargetIaf         = "IAF (activation)";
    private const string AccumTargetMaxPressure = "Max pressure (100%)";


    // Stability tolerance presets. LOW is the baseline (the original defaults);
    // MEDIUM and HIGH use explicit values for both pen tolerance (as a fraction:
    // 0.0125 = 1.25%) and scale tolerance (in gf).
    private const string TolerancePresetLow    = "LOW";
    private const string TolerancePresetMedium = "MEDIUM";
    private const string TolerancePresetHigh   = "HIGH";

    private const double PenToleranceLow    = 0.005;    // 0.5%
    private const double PenToleranceMedium = 0.0125;   // 1.25%
    private const double PenToleranceHigh   = 0.025;    // 2.5%

    private const double ScaleToleranceLow    = 0.25;
    private const double ScaleToleranceMedium = 5.0;   // gf
    private const double ScaleToleranceHigh   = 10.0;  // gf

    // Guards against re-entrancy between the preset combo and the tolerance
    // sliders (each programmatic change fires the other's change handler).
    private bool _applyingTolerancePreset;

    private readonly AccumulatorController _accumulatorController = new();
    private bool _accumulatorEnabled;

    // Open scale-lag measurement tool (non-modal). While set, live pen/scale
    // samples are forwarded to it. Cleared when the window closes.
    private MeasureScaleLagWindow? _lagWindow;

    // Scale-lag compensation (checkbox-controlled, on by default). When on, pen
    // events into the accumulator are time-aligned by the measured scale response
    // lag: a pen event is released to the accumulator only once it is older than
    // τ, so the scale sample that arrives alongside it carries the force that was
    // truly applied at that pen state. Corrects the lag-induced bias.
    private bool _scaleLagComp = true;
    private static readonly TimeSpan ScaleLagDelay =
        TimeSpan.FromMilliseconds(ScaleSessionManager.ResponseLagMs);
    private readonly List<(DateTime T, PenReadingData D)> _penLagQueue = [];

    // Shared visual: live-pressure indicator on the charts.
    private static readonly ScottPlot.Color LivePressureColor = ScottPlot.Color.FromHex("#F97316");
    private const float LivePressureLineWidth = 3.0f;

    // ── Capture "Time series" chart (live scrolling EKG view) ───────────────
    // Formerly a standalone "Monitor" mode; now one of Capture's two centre
    // chart types (the other is the scatter plot). Selected via
    // comboBox_capture_chart / _captureTimeSeries.

    private const double MonitorWindowSeconds = 10.0;
    private const double MonitorRefreshMs     = 50;   // ~20 fps
    private const double MonitorScaleYFloor   = 5;    // gf — min y-axis ceiling for the scale chart

    // Tolerance-band fills: each trace's colour at ~20% alpha (8-digit hex).
    private const string PenBandFill   = "#2563EB33";   // pen trace blue
    private const string ScaleBandFill = "#F9731633";   // scale trace orange

    // Parallel time/value buffers per chart. Times are seconds since
    // _monitorEpoch; trimmed every append to keep only points inside the
    // visible window.
    private readonly List<double> _monitorPenT   = [];
    private readonly List<double> _monitorPenY   = [];
    private readonly List<double> _monitorScaleT = [];
    private readonly List<double> _monitorScaleY = [];
    // Capture markers: where a stability capture landed on each trace (so the
    // time-series view shows the capture points). Trimmed to the visible window.
    private readonly List<(double T, double Y)> _monitorPenMarks   = [];
    private readonly List<(double T, double Y)> _monitorScaleMarks = [];
    private DateTime _monitorEpoch         = DateTime.UtcNow;
    private DateTime _lastMonitorRefresh   = DateTime.MinValue;
    private bool     _monitorOverlay;      // true → one chart with dual y-axes
    private bool     _captureTimeSeries;   // Capture chart type: false = scatter, true = time series

    // ── Scale state ───────────────────────────────────────────────────────────

    private double   _physicalPressure;
    private int      _scaleReadingCount;
    private DateTime _scaleRateWindowStart = DateTime.UtcNow;

    // Highest decimal resolution the connected scale has reported this session,
    // floored at 1 so old single-decimal scales read exactly as before. Drives
    // the live gf displays so a finer scale's digits (e.g. "0.04") aren't
    // rounded away, while staying stable if the scale occasionally drops a "0".
    private int      _scaleDecimals = 1;

    // ── Pen rate tracking ─────────────────────────────────────────────────────

    private int      _penPacketCount;
    private DateTime _penRateWindowStart = DateTime.UtcNow;
    private DateTime _lastActiveTime     = DateTime.MinValue;
    // True while the pen is on/near the tablet (recent packets or tip held down).
    // Live PEN / PEN PRESSURE readouts blank to a placeholder when it goes false,
    // so a lifted pen doesn't leave stale values on screen.
    private bool     _penPresent;

    // ── Dot colours ──────────────────────────────────────────────────────────

    private static readonly ISolidColorBrush DotActive   = new SolidColorBrush(Avalonia.Media.Color.FromRgb(34,  197, 94));
    private static readonly ISolidColorBrush DotInactive = new SolidColorBrush(Avalonia.Media.Color.FromRgb(156, 163, 175));
    private static readonly ISolidColorBrush DotWarning  = new SolidColorBrush(Avalonia.Media.Color.FromRgb(234, 179, 8));
    private static readonly ISolidColorBrush DotError    = new SolidColorBrush(Avalonia.Media.Color.FromRgb(239, 68, 68));

    // ── Construction ─────────────────────────────────────────────────────────

    public MainWindow()
    {
        // Mica on Win11, Acrylic on Win10, plain on anything older.
        TransparencyLevelHint = new[]
        {
            WindowTransparencyLevel.Mica,
            WindowTransparencyLevel.AcrylicBlur,
            WindowTransparencyLevel.None
        };

        InitializeComponent();

        _penManager = new PenSessionManager(
            PenInputSurface,
            OnPenDataReceived,
            ShowMessageAsync);

        _scaleManager  = new ScaleSessionManager(OnScaleReading, ShowMessageAsync);
        _sessionLogger = new SessionLogger(LogDirectory);

        _stabilityController.RawPairAvailable += OnStabilityRawPair;
        _stabilityController.StableCaptured   += OnStabilityStableCapture;

        Opened  += OnOpened;
        Loaded  += OnLoaded;
        Closing += OnClosing;

        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent,     OnDrop);

        // PenInputSurface overlays the charts — forward wheel as zoom and
        // right-click to reset view. (Pointer-moved handling for space-pan
        // was removed when the keyboard hotkeys were dropped.)
        PenInputSurface.PointerWheelChanged += OnChartAreaWheel;
        PenInputSurface.PointerPressed      += OnChartAreaPointerPressed;

        // Re-theme the ScottPlot charts when the app/OS theme flips. The XAML
        // controls follow theme automatically via DynamicResource brushes;
        // ScottPlot colours are baked in code so they need a manual refresh.
        ActualThemeVariantChanged += (_, _) => ReapplyChartThemes();
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private void OnOpened(object? sender, EventArgs e)
    {
        var apiList = WinPenKit.PenSessionFactory.GetAvailableApis()
            .Where(a => a != InputApi.WmPointer).ToList();
        apiList.Add(InputApi.AvaloniaPointer);
        _apis = apiList;

        foreach (var api in _apis)
            ApiCombo.Items.Add(api switch
            {
                InputApi.WintabSystem    => "WinTab",
                InputApi.WintabDigitizer => "WinTab (high-res)",
                InputApi.AvaloniaPointer => "WM_POINTER (Avalonia)",
                _                        => api.ToString()
            });

        ApiCombo.SelectionChanged += ApiCombo_SelectionChanged;
        if (ApiCombo.Items.Count > 0) ApiCombo.SelectedIndex = 0;

        foreach (var port in SerialPort.GetPortNames())
            comboBox_comport.Items.Add(port);
        if (comboBox_comport.Items.Count > 0)
            comboBox_comport.SelectedIndex = comboBox_comport.Items.Count - 1;

        // Stability tolerance preset picker. Items only — the initial selection
        // is synced to the sliders' default (LOW) values below.
        comboBox_tolerancePreset.Items.Add(TolerancePresetLow);
        comboBox_tolerancePreset.Items.Add(TolerancePresetMedium);
        comboBox_tolerancePreset.Items.Add(TolerancePresetHigh);
        SyncTolerancePresetSelection();
        UpdateCurveSummary();

        // VIEW picker — top-level view dropdown in the ribbon.
        comboBox_view_mode.Items.Add("Curve");
        comboBox_view_mode.Items.Add("Time series");
        comboBox_view_mode.Items.Add("Accumulator");
        comboBox_view_mode.SelectedIndex = 0;

        // Accumulator MEASURE picker (IAF / Max pressure). IAF is the default.
        _suppressAccumConfig = true;
        comboBox_accum_target.Items.Add(AccumTargetIaf);
        comboBox_accum_target.Items.Add(AccumTargetMaxPressure);
        comboBox_accum_target.SelectedIndex = (int)_accumulatorController.Target;
        // Bucket-size picker is populated from the active target's width set.
        PopulateAccumBucketCombo(_accumulatorController.CurrentBucketWidths, _accumulatorController.BucketWidth);
        UpdateAccumLabels();
        _suppressAccumConfig = false;

        _metadata.Date = DateTime.Today.ToString("yyyy-MM-dd");
        _metadata.User = Environment.UserName.ToUpper().Trim();
        _metadata.Os   = "WINDOWS";

        UpdateScaleDot();
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            InitializeStabilityPlot();
            InitializeAccumulatorPlot();
            InitializeMonitorPlots();
            RefreshStabilityPlot();
            UpdateStabilityData();
        }, DispatcherPriority.Background);
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        _penManager.Dispose();
        _scaleManager.Dispose();
        _sessionLogger.Dispose();
    }

    // ── Session ───────────────────────────────────────────────────────────────

    private void StartSession()
    {
        _penManager.Stop();
        row_tablet.State = DotState.Inactive;
        if (_apis.Count == 0 || ApiCombo.SelectedIndex < 0) return;
        _penManager.Start(_apis[ApiCombo.SelectedIndex]);
        row_tablet.State = _penManager.IsRunning ? DotState.Active : DotState.Inactive;
    }

    private void ApiCombo_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        => StartSession();

    // ── View switching ────────────────────────────────────────────────────────
    // The ribbon VIEW ComboBox picks which right-panel + centre chart are
    // visible. SetActiveTab is the visibility toggle; comboBox_view_mode_Changed
    // is the entry point that also kicks the appropriate refresh.

    private void SetActiveTab(string tab)
    {
        bool capture     = tab == "capture";      // Curve (scatter)
        bool timeseries  = tab == "timeseries";
        bool accumulator = tab == "accumulator";

        _captureTimeSeries = timeseries;

        // Curve and Time series share the stability captures pane + auto-capture
        // controls; they differ only in the centre chart.
        bool curveLike = capture || timeseries;
        panel_right_stability.IsVisible   = curveLike;
        panel_right_accumulator.IsVisible = accumulator;

        if (group_curve_capture is not null) group_curve_capture.IsVisible = curveLike;
        if (group_accumulator is not null)   group_accumulator.IsVisible   = accumulator;

        stabilityPlotView.IsVisible = capture;
        monitorView.IsVisible       = timeseries;
        accumPlotView.IsVisible     = accumulator;

        // Per-mode option row: Follow-live (Curve) / Overlay-traces (Time series).
        if (group_view_follow is not null)
        {
            group_view_follow.IsVisible = curveLike;
            UpdateCaptureViewControls();
        }
    }

    private void comboBox_view_mode_Changed(object? sender, SelectionChangedEventArgs e)
    {
        // Guard: ComboBox.SelectedIndex set during OnOpened fires this handler
        // before the right-panel ScrollViewers exist as bound fields.
        if (panel_right_stability is null) return;

        // Leaving Accumulator stops its accumulation.
        bool toAccumulator = comboBox_view_mode.SelectedItem?.ToString() == "Accumulator";
        if (!toAccumulator) _accumulatorEnabled = false;
        _penLagQueue.Clear();

        switch (comboBox_view_mode.SelectedItem?.ToString())
        {
            case "Accumulator":
                SetActiveTab("accumulator");
                btn_accumulator_enable.Content = _accumulatorEnabled ? "Stop" : "Start";
                RefreshAccumulatorPlot();
                UpdateAccumulatorData();
                break;
            case "Time series":
                SetActiveTab("timeseries");
                RefreshCaptureChart();
                break;
            default:        // "Curve" or any unrecognised value
                SetActiveTab("capture");
                RefreshCaptureChart();
                break;
        }
    }

    /// <summary>
    /// Shows the option relevant to the active mode in the VIEW ribbon group:
    /// "Follow live" for Curve (scatter), "Overlay traces" for Time series.
    /// </summary>
    private void UpdateCaptureViewControls()
    {
        if (chk_live_follow is null) return;
        chk_live_follow.IsVisible     = !_captureTimeSeries;
        chk_capture_overlay.IsVisible =  _captureTimeSeries;
    }

    /// <summary>
    /// Repaints the active Curve/Time-series centre chart and rebuilds the
    /// captures list (shared by both). The time series starts fresh on each entry.
    /// </summary>
    private void RefreshCaptureChart()
    {
        if (stabilityPlotView?.Plot is null) return;   // pre-init

        UpdateStabilityData();   // captures list — visible for both chart types
        if (_captureTimeSeries)
        {
            // Reset epoch + buffers so the rolling traces start at "now".
            ResetMonitor();
            RefreshMonitorPlots();
        }
        else
        {
            RefreshStabilityPlot();
        }
    }

    // ── Live-follow ─────────────────────────────────────────────────────────────

    private void chk_live_follow_Changed(object? sender, RoutedEventArgs e)
    {
        _liveFollow = chk_live_follow.IsChecked == true;

        // Drop any eased state so the next follow frame snaps straight to the
        // current trail instead of gliding over from a stale view.
        _followLimitsValid = false;

        // Turning it off restores the calibrated range; turning it on snaps
        // straight to the current trail.
        if (stabilityPlotView is { IsVisible: true }) RefreshStabilityPlot(resetAxes: !_liveFollow);
    }

    /// <summary>Appends the current live (physical, logical) point to the trail
    /// and drops anything older than the follow window.</summary>
    private void PushLiveTrail()
    {
        var now = DateTime.UtcNow;
        _liveTrail.Add((now, _physicalPressure, _logicalPressure * 100.0));
        int i = 0;
        while (i < _liveTrail.Count &&
               (now - _liveTrail[i].At).TotalMilliseconds > LiveFollowWindowMs) i++;
        if (i > 0) _liveTrail.RemoveRange(0, i);
    }

    /// <summary>
    /// Computes the axis limits the followed view should glide toward: the
    /// padded bounds of the last second of live points, eased from the previous
    /// frame's limits so the view tracks smoothly rather than snapping. Returns
    /// false when the trail is empty (nothing to follow).
    /// </summary>
    private bool TryGetLiveFollowLimits(out double xMin, out double xMax, out double yMin, out double yMax)
    {
        xMin = xMax = yMin = yMax = 0;
        if (_liveTrail.Count == 0) return false;

        double pxMin = _liveTrail.Min(p => p.PhysGf), pxMax = _liveTrail.Max(p => p.PhysGf);
        double pyMin = _liveTrail.Min(p => p.LogPct), pyMax = _liveTrail.Max(p => p.LogPct);

        double xPad = Math.Max((pxMax - pxMin) * 0.15, 5.0);   // ≥ 5 gf of headroom
        double yPad = Math.Max((pyMax - pyMin) * 0.15, 2.0);   // ≥ 2 % of headroom

        // Target box, padded and clamped to the valid data range.
        double tXMin = Math.Max(0, pxMin - xPad), tXMax = pxMax + xPad;
        double tYMin = Math.Max(0, pyMin - yPad), tYMax = Math.Min(100, pyMax + yPad);

        // Hold a minimum span so a near-steady reading doesn't zoom in wildly.
        EnsureMinSpan(ref tXMin, ref tXMax, LiveFollowMinSpanX, 0, double.PositiveInfinity);
        EnsureMinSpan(ref tYMin, ref tYMax, LiveFollowMinSpanY, 0, 100);

        if (!_followLimitsValid)
        {
            // First frame after enabling: snap straight to the target.
            (_followXMin, _followXMax, _followYMin, _followYMax) = (tXMin, tXMax, tYMin, tYMax);
            _followLimitsValid = true;
        }
        else
        {
            // Ease each edge toward the target. "expand" = the edge moving
            // outward (lower min / higher max) uses the fast alpha; contracting
            // inward uses the slow one.
            _followXMin = EaseEdge(_followXMin, tXMin, expandWhenTargetLower: true);
            _followXMax = EaseEdge(_followXMax, tXMax, expandWhenTargetLower: false);
            _followYMin = EaseEdge(_followYMin, tYMin, expandWhenTargetLower: true);
            _followYMax = EaseEdge(_followYMax, tYMax, expandWhenTargetLower: false);
        }

        xMin = _followXMin; xMax = _followXMax; yMin = _followYMin; yMax = _followYMax;
        return true;
    }

    /// <summary>Eases one axis edge toward <paramref name="target"/>, expanding
    /// the view quickly and contracting it slowly.</summary>
    private static double EaseEdge(double current, double target, bool expandWhenTargetLower)
    {
        bool expanding = expandWhenTargetLower ? target < current : target > current;
        double alpha   = expanding ? LiveFollowExpandAlpha : LiveFollowContractAlpha;
        return current + (target - current) * alpha;
    }

    /// <summary>Widens [lo, hi] symmetrically to at least <paramref name="minSpan"/>,
    /// keeping it within [clampLo, clampHi].</summary>
    private static void EnsureMinSpan(ref double lo, ref double hi, double minSpan,
                                      double clampLo, double clampHi)
    {
        double span = hi - lo;
        if (span >= minSpan) return;

        double mid  = (lo + hi) / 2.0;
        lo = mid - minSpan / 2.0;
        hi = mid + minSpan / 2.0;

        if (lo < clampLo) { hi += clampLo - lo; lo = clampLo; }
        if (hi > clampHi) { lo = Math.Max(clampLo, lo - (hi - clampHi)); hi = clampHi; }
    }

    // ── Logging ───────────────────────────────────────────────────────────────

    private void btn_log_toggle_Click(object? sender, RoutedEventArgs e)
        => ToggleLogging();

    private void btn_open_log_folder_Click(object? sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(LogDirectory);
        Process.Start(new ProcessStartInfo(LogDirectory) { UseShellExecute = true });
    }

    private void ToggleLogging()
    {
        if (_sessionLogger.IsLogging)
        {
            _sessionLogger.StopLogging();
            btn_log_toggle.Content = "Start Logging";
            row_logging.State = DotState.Inactive;
        }
        else
        {
            _sessionLogger.StartLogging();
            btn_log_toggle.Content = "Stop Logging";
            row_logging.State = DotState.Active;
        }
    }

    // ── Scale ─────────────────────────────────────────────────────────────────

    private async void btn_scale_record_Click(object? sender, RoutedEventArgs e)
    {
        if (_scaleManager.IsReading) { _scaleManager.Stop(); return; }
        await StartScaleIfIdleAsync();
    }

    private void btn_scale_tare_Click(object? sender, RoutedEventArgs e)
        => _scaleManager.SendTare();

    /// <summary>
    /// Starts the scale read loop if it isn't already running and a COM port
    /// is selected. No-op otherwise — silently ignored when no port is
    /// available so the Auto / Threshold Start buttons can call this
    /// unconditionally.
    /// </summary>
    private async Task StartScaleIfIdleAsync()
    {
        if (_scaleManager.IsReading) return;
        var port = comboBox_comport.SelectedItem?.ToString();
        if (port is null) return;
        // Re-learn the device's resolution from scratch (a different scale may
        // be on this port now), keeping the single-decimal floor.
        _scaleDecimals = 1;
        btn_scale_record.Content = "Stop";
        await _scaleManager.StartAsync(port);
        btn_scale_record.Content = "Start";
        UpdateScaleDot();
    }

    private void OnScaleReading(ScaleRecord record)
    {
        _sessionLogger.LogScaleReading(record);
        if (_stabilityEnabled) _stabilityController.OnScaleData(record.ReadingAsDouble);

        if (_accumulatorEnabled)
        {
            // Lag-align the pen feed, then one count for this scale sample.
            if (_scaleLagComp)
                FlushPenLagQueue(DateTime.UtcNow - ScaleLagDelay);
            _accumulatorController.OnScaleData(record.ReadingAsDouble);
        }

        // Monitor: append the scale sample and refresh if visible.
        AppendMonitorScale(record.ReadingAsDouble);
        RefreshMonitorIfDue();

        _lagWindow?.FeedScale(record.ReadingAsDouble);

        _physicalPressure = record.ReadingAsDouble;
        if (record.DecimalPlaces > _scaleDecimals) _scaleDecimals = record.DecimalPlaces;
        reading_phys_pressure.Value = $"{FormatGf(_physicalPressure)}";

        // Capture chart: move the live vertical line with the scale.
        // Plot-only refresh (no list rebuild), throttled to ~10 fps.
        if (stabilityPlotView is { IsVisible: true })
        {
            var now = DateTime.UtcNow;
            if ((now - _lastDataChartLiveRefresh).TotalMilliseconds >= StabilityChartMinRefreshMs)
            {
                _lastDataChartLiveRefresh = now;
                RefreshStabilityPlot(resetAxes: false);
            }
        }

        // Accumulator chart: move the live force line with the scale (throttled),
        // whether or not accumulation is running.
        if (accumPlotView is { IsVisible: true }) RefreshAccumulatorIfDue();

        _scaleReadingCount++;
        var elapsed = (DateTime.UtcNow - _scaleRateWindowStart).TotalSeconds;
        if (elapsed >= 1.0)
        {
            reading_scale_rate.Value  = $"{_scaleReadingCount / elapsed:F0} /s";
            _scaleReadingCount        = 0;
            _scaleRateWindowStart     = DateTime.UtcNow;
        }

        // Data flowing → dot turns green (cheap: only repaints on transition).
        if (row_scale.State != DotState.Active) UpdateScaleDot();
    }

    /// <summary>
    /// Formats a grams-force value at the connected scale's reported resolution
    /// (see <see cref="_scaleDecimals"/>), e.g. "0.04 gf" for a fine scale or
    /// "50.0 gf" for a single-decimal one.
    /// </summary>
    private string FormatGf(double gf) =>
        $"{gf.ToString("F" + _scaleDecimals, CultureInfo.InvariantCulture)} gf";

    /// <summary>
    /// Scale status dot:
    /// red   = no COM port available, or last attempt failed
    /// yellow = COM port available but not reading
    /// green = actively reading
    /// </summary>
    private void UpdateScaleDot()
    {
        if (_scaleManager.HasError || comboBox_comport.Items.Count == 0)
            row_scale.State = DotState.Error;
        else if (_scaleManager.IsReading)
            row_scale.State = DotState.Active;
        else
            row_scale.State = DotState.Warning;
    }

    // ── Pen data callback ─────────────────────────────────────────────────────

    private void OnPenDataReceived(PenReadingData d)
    {
        // This callback writes directly to Avalonia controls, so it must run on
        // the UI thread. PenSessionManager delivers it via a DispatcherTimer;
        // assert it (Debug only) to catch any future off-thread caller.
        Debug.Assert(Dispatcher.UIThread.CheckAccess(),
            "OnPenDataReceived must be called on the UI thread.");

        _sessionLogger.LogPenReading(d);
        if (_stabilityEnabled) _stabilityController.OnPenData(d);

        // Accumulator lag compensation: hold pen events in a queue and release
        // them aligned to the (late) scale stream; else feed directly.
        if (_accumulatorEnabled)
        {
            if (_scaleLagComp)
                _penLagQueue.Add((DateTime.UtcNow, d));
            else
                FeedPenToActiveController(d);
        }
        _logicalPressure = d.SmoothedPressure;
        UpdateRibbon(d);
        UpdateCards(d);

        // Live-follow: record the live point each pen tick (~60 fps) and, when
        // enabled, drive the data-chart refresh from the pen stream (throttled)
        // so the auto zoom/pan tracks smoothly even between scale samples.
        if (_liveFollow)
        {
            PushLiveTrail();
            var now = DateTime.UtcNow;
            if ((now - _lastDataChartLiveRefresh).TotalMilliseconds >= StabilityChartMinRefreshMs)
            {
                _lastDataChartLiveRefresh = now;
                if (stabilityPlotView is { IsVisible: true }) RefreshStabilityPlot(resetAxes: false);
            }
        }

        // Monitor: append the pen sample and refresh if visible.
        AppendMonitorPen(d.NormalizedPressure);
        RefreshMonitorIfDue();

        _lagWindow?.FeedPen(d.NormalizedPressure);
    }

    private void UpdateRibbon(PenReadingData d)
    {
        if (d.PacketCount > 0) _lastActiveTime = DateTime.UtcNow;
        var inProx = (DateTime.UtcNow - _lastActiveTime).TotalMilliseconds < 300;
        // "Present" = recent packets or tip held down. The Avalonia backend sends
        // no packets during a still press, so fall back to TipDown there.
        _penPresent = inProx || d.TipDown;

        // Proximity field: in-range only. A tip-down press implies in-range, which
        // also covers the Avalonia still-press case (no packets while pressing).
        ProximityDot.Fill   = _penPresent ? Brushes.Orange : DotInactive;
        ProximityLabel.Text = _penPresent ? "Proximity" : "Out";
        // Tip field: tip contact only.
        TipDot.Fill     = d.TipDown     ? DotActive : DotInactive;
        Barrel1Dot.Fill = d.Barrel1Down ? DotActive : DotInactive;
        Barrel2Dot.Fill = d.Barrel2Down ? DotActive : DotInactive;

        RibbonAzLabel.Text     = _penPresent ? $"{d.Azimuth:F1}"  : "--";
        RibbonAltLabel.Text    = _penPresent ? $"{d.Altitude:F1}" : "--";
        RibbonTxLabel.Text     = _penPresent ? $"{d.TiltX:F1}"    : "--";
        RibbonTyLabel.Text     = _penPresent ? $"{d.TiltY:F1}"    : "--";
        reading_hover_z.Value  = !d.SupportsZ ? "-" : _penPresent ? d.Z.ToString() : "--";
    }

    private void UpdateCards(PenReadingData d)
    {
        reading_pressure_raw.Value    = _penPresent ? d.RawPressure.ToString()               : "--";
        reading_pressure_norm.Value   = _penPresent ? $"{d.NormalizedPressure * 100.0:F2} %" : "--";
        reading_pressure_smooth.Value = _penPresent ? $"{d.SmoothedPressure   * 100.0:F2} %" : "--";
        pressureBar.Value             = _penPresent ? d.NormalizedPressure * 100.0 : 0;

        _penPacketCount += d.PacketCount;
        var elapsed = (DateTime.UtcNow - _penRateWindowStart).TotalSeconds;
        if (!_penPresent)
        {
            // Lifted: blank the rate and reset the window so it restarts clean.
            reading_pen_rate.Value = "--";
            _penPacketCount        = 0;
            _penRateWindowStart    = DateTime.UtcNow;
        }
        else if (elapsed >= 1.0)
        {
            reading_pen_rate.Value = $"{_penPacketCount / elapsed:F0}";
            _penPacketCount        = 0;
            _penRateWindowStart    = DateTime.UtcNow;
        }

    }

    // ── Chart wheel zoom + right-click reset ─────────────────────────────────

    /// <summary>
    /// PenInputSurface overlays the charts. Wheel events are intercepted here
    /// and applied as zoom on the active chart; right-click resets the axis
    /// range. Pen-drag panning was removed when the keyboard hotkeys (which
    /// gated the Space+drag pan) were dropped.
    /// </summary>
    private ScottPlot.Avalonia.AvaPlot ActiveChart() =>
        monitorView.IsVisible   ? monitorPenPlot  :
        accumPlotView.IsVisible ? accumPlotView   :
                                  stabilityPlotView;

    /// <summary>
    /// The visual to snapshot for image export. Same selection as
    /// <see cref="ActiveChart"/>, except Time series returns the whole
    /// <c>monitorView</c> container so both stacked traces are captured.
    /// </summary>
    private Control ActiveChartVisual() =>
        monitorView.IsVisible   ? monitorView      :
        accumPlotView.IsVisible ? accumPlotView    :
                                  stabilityPlotView;

    private async void btn_chart_save_png_Click(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not { } tl) return;
        try { await ChartImage.SavePngAsync(ActiveChartVisual(), tl, BuildChartImageFileName()); }
        catch (Exception ex) { Debug.WriteLine($"[PPP2] Chart PNG save failed: {ex.Message}"); }
    }

    private void btn_chart_copy_image_Click(object? sender, RoutedEventArgs e)
    {
        try { ChartImage.CopyToClipboard(ActiveChartVisual()); }
        catch (Exception ex) { Debug.WriteLine($"[PPP2] Chart image copy failed: {ex.Message}"); }
    }

    private string BuildChartImageFileName()
    {
        string mode = monitorView.IsVisible   ? "TimeSeries"
                    : accumPlotView.IsVisible  ? "Accumulator"
                    :                            "Curve";
        return $"{mode}_{DateTime.Now:yyyy-MM-dd_HHmmss}.png";
    }

    private void OnChartAreaPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(PenInputSurface).Properties.IsRightButtonPressed) return;

        // Right-click → reset the active chart to its default axis range.
        if (monitorView.IsVisible)
            RefreshMonitorPlots();      // rolling-window axes
        else if (accumPlotView.IsVisible)
            RefreshAccumulatorPlot();   // fixed 0–100% range
        else
            RefreshStabilityPlot();     // default calibrated range

        e.Handled = true;
    }

    private void OnChartAreaWheel(object? sender, PointerWheelEventArgs e)
    {
        var chart = ActiveChart();
        var pos   = e.GetPosition(PenInputSurface);

        // Scroll up (delta.Y > 0) → zoom in (show less); down → zoom out.
        double factor = e.Delta.Y > 0 ? 1.15 : 1.0 / 1.15;

        ZoomChartAtPoint(chart, (float)pos.X, (float)pos.Y, factor);
        e.Handled = true;
    }

    private static void ZoomChartAtPoint(
        ScottPlot.Avalonia.AvaPlot chart, float px, float py, double factor)
    {
        var plt    = chart.Plot;
        var coords = plt.GetCoordinates(px, py);

        double xMin = plt.Axes.Bottom.Min, xMax = plt.Axes.Bottom.Max;
        double yMin = plt.Axes.Left.Min,   yMax = plt.Axes.Left.Max;

        // Expand / contract each side proportionally around the cursor position.
        plt.Axes.SetLimits(
            left:   coords.X - (coords.X - xMin) / factor,
            right:  coords.X + (xMax - coords.X) / factor,
            bottom: coords.Y - (coords.Y - yMin) / factor,
            top:    coords.Y + (yMax - coords.Y) / factor);

        chart.Refresh();
    }

    // ── About dialog ──────────────────────────────────────────────────────────

    private async void btn_about_Click(object? sender, RoutedEventArgs e)
        => await new AboutWindow().ShowDialog(this);

    // ── Tools ─────────────────────────────────────────────────────────────────

    private void btn_measure_scale_lag_Click(object? sender, RoutedEventArgs e)
    {
        if (_lagWindow is not null) { _lagWindow.Activate(); return; }
        _lagWindow = new MeasureScaleLagWindow();
        _lagWindow.Closed += (_, _) => _lagWindow = null;
        _lagWindow.Show(this);   // non-modal so pen/scale input keeps flowing
    }

    // ── Metadata dialog ───────────────────────────────────────────────────────

    private async void btn_edit_metadata_Click(object? sender, RoutedEventArgs e)
    {
        var dialog = new MetadataEditWindow(_metadata);
        var result = await dialog.ShowDialog<SessionMetadata?>(this);
        if (result is null) return;     // cancelled

        _metadata = result;
    }

    // ── Stability chart ───────────────────────────────────────────────────────────

    private void InitializeStabilityPlot()
    {
        var plt = stabilityPlotView.Plot;
        plt.XLabel("Physical pressure (gf)");
        plt.YLabel("Logical pressure (%)");
        plt.Axes.SetLimits(0, PlotAxisLimit, 0, PlotPressureLimit);
        ChartTheme.Apply(stabilityPlotView);
        stabilityPlotView.Refresh();
    }

    private void RefreshStabilityPlot(bool resetAxes = true)
    {
        var plt = stabilityPlotView.Plot;
        plt.Clear();

        // Crosshair + tolerance box first so the raw/stable points render in
        // front of them.
        AddLiveCrosshair(plt);
        AddStabilityToleranceBox(plt);

        // Raw pairs (medium grey, small dots, ~10 fps throttled)
        if (_stabilityRawX.Count > 0)
        {
            var raw = plt.Add.Scatter(_stabilityRawX.ToArray(), _stabilityRawY.ToArray());
            raw.Color      = ScottPlot.Color.FromHex("#888888");
            raw.LineWidth  = 0;
            raw.MarkerSize = 5;
        }

        // Stable captures (sorted by physical pressure).
        var sorted = _stabilityController.Captures.OrderBy(c => c.PhysicalGf).ToList();
        if (sorted.Count > 0)
        {
            var stX = sorted.Select(c => c.PhysicalGf).ToArray();
            var stY = sorted.Select(c => c.LogicalNorm * 100).ToArray();

            var stable = plt.Add.Scatter(stX, stY);
            stable.Color      = ScottPlot.Color.FromHex("#2563EB");
            stable.LineWidth  = 1.5f;
            stable.MarkerSize = 7;
        }

        // Live-follow tracks the last second of live points. Otherwise live
        // refreshes (scale stream) preserve the user's current zoom/pan and
        // only explicit rebuilds reset to the calibrated range.
        if (_liveFollow && TryGetLiveFollowLimits(out var xn, out var xx, out var yn, out var yx))
            plt.Axes.SetLimits(xn, xx, yn, yx);
        else if (resetAxes)
            plt.Axes.SetLimits(0, PlotAxisLimit, 0, PlotPressureLimit);
        stabilityPlotView.Refresh();
    }

    /// <summary>
    /// Adds a live crosshair to a (X = physical gf, Y = logical %) chart:
    /// a vertical line at the current scale force and a horizontal line at the
    /// current smoothed pen pressure. Their intersection is the point a manual
    /// Record would capture. Thick solid orange, matching the live indicators
    /// on the Threshold and Monitor charts.
    /// </summary>
    private void AddLiveCrosshair(ScottPlot.Plot plt)
    {
        var vScale = plt.Add.VerticalLine(_physicalPressure);
        vScale.Color     = LivePressureColor;
        vScale.LineWidth = LivePressureLineWidth;
        vScale.Text      = FormatGf(_physicalPressure);

        var hPen = plt.Add.HorizontalLine(_logicalPressure * 100.0);
        hPen.Color     = LivePressureColor;
        hPen.LineWidth = LivePressureLineWidth;
        hPen.Text      = $"{_logicalPressure * 100.0:F1} %";
    }

    /// <summary>
    /// Draws a red outline box around the live crosshair on the Stability chart
    /// showing the pen/scale tolerance window: ±ScaleTolerance gf horizontally
    /// and ±PenTolerance% vertically. A reading that stays inside the box is
    /// within tolerance of the current point (i.e. counts as the same capture).
    /// </summary>
    private void AddStabilityToleranceBox(ScottPlot.Plot plt)
    {
        double cx    = _physicalPressure;
        double cy    = _logicalPressure * 100.0;
        double xHalf = _stabilityController.ScaleTolerance;        // gf
        double yHalf = _stabilityController.PenTolerance * 100.0;  // percent

        var box = plt.Add.Rectangle(cx - xHalf, cx + xHalf, cy - yHalf, cy + yHalf);
        box.FillStyle.Color = ScottPlot.Colors.Transparent;
        box.LineStyle.Color = ScottPlot.Color.FromHex("#DC2626");
        box.LineStyle.Width = 1.5f;
    }

    /// <summary>
    /// Draws a horizontal shaded band of ±<paramref name="tol"/> around
    /// <paramref name="center"/> across the full visible time range, showing the
    /// stability-tolerance window for a time-series trace. A trace that stays
    /// inside its band is within tolerance (i.e. counts as stable). Optionally
    /// bound to a specific y-axis (the scale trace's right axis when overlaid).
    /// </summary>
    private static void AddTimeSeriesToleranceBand(
        ScottPlot.Plot plt, double xMin, double xMax,
        double center, double tol, string hexFill, ScottPlot.IYAxis? yAxis = null)
    {
        var band = plt.Add.Rectangle(xMin, xMax, center - tol, center + tol);
        band.FillStyle.Color = ScottPlot.Color.FromHex(hexFill);
        band.LineStyle.Width = 0;
        if (yAxis is not null) band.Axes.YAxis = yAxis;
    }

    private void OnStabilityRawPair(double physGf, double logNorm)
    {
        if (_stabilityRawX.Count >= StabilityRawMaxPoints)
        { _stabilityRawX.RemoveAt(0); _stabilityRawY.RemoveAt(0); }
        _stabilityRawX.Add(physGf);
        _stabilityRawY.Add(logNorm * 100);

        // Throttle raw-data chart refresh to ~10 fps; always refresh on stable captures.
        var now = DateTime.UtcNow;
        if ((now - _lastStabilityChartRefresh).TotalMilliseconds >= StabilityChartMinRefreshMs
            && stabilityPlotView.IsVisible)
        {
            _lastStabilityChartRefresh = now;
            RefreshStabilityPlot();
        }
    }

    private void OnStabilityStableCapture(StabilityCapture capture)
    {
        RefreshStabilityPlot();
        UpdateStabilityData();

        // Mark where this capture landed on the live time-series traces.
        if (monitorView.IsVisible) AddMonitorCaptureMark();
    }

    /// <summary>Records a capture marker at the latest point of each live trace
    /// (so the dot sits on the line), trims to the window, and redraws.</summary>
    private void AddMonitorCaptureMark()
    {
        if (_monitorPenT.Count > 0)
            _monitorPenMarks.Add((_monitorPenT[^1], _monitorPenY[^1]));
        if (_monitorScaleT.Count > 0)
            _monitorScaleMarks.Add((_monitorScaleT[^1], _monitorScaleY[^1]));

        TrimMonitorMarks((DateTime.UtcNow - _monitorEpoch).TotalSeconds);
        RefreshMonitorIfDue();
    }

    private void TrimMonitorMarks(double tNow)
    {
        double cutoff = tNow - MonitorWindowSeconds;
        _monitorPenMarks.RemoveAll(m => m.T < cutoff);
        _monitorScaleMarks.RemoveAll(m => m.T < cutoff);
    }

    // ── Stability controls ────────────────────────────────────────────────────────

    private void btn_stability_enable_Click(object? sender, RoutedEventArgs e)
    {
        _stabilityEnabled = !_stabilityEnabled;
        btn_stability_enable.Content = _stabilityEnabled ? "Stop" : "Start";

        // Convenience: starting capture also starts the scale (if a COM port is
        // selected and the scale isn't already reading). Stopping capture
        // leaves the scale running — user can stop it separately.
        if (_stabilityEnabled) _ = StartScaleIfIdleAsync();
    }

    private void btn_stability_record_Click(object? sender, RoutedEventArgs e)
    {
        // Convenience: clicking Record also starts the scale if it isn't already
        // reading, so later captures have live force without a separate Start.
        _ = StartScaleIfIdleAsync();

        // Force a capture at the current live values. The controller's
        // StableCaptured event fires from inside RecordManual, which triggers
        // OnStabilityStableCapture → RefreshStabilityPlot + UpdateStabilityData.
        _stabilityController.RecordManual(_physicalPressure, _logicalPressure);
    }

    private void btn_stability_clear_Click(object? sender, RoutedEventArgs e)
    {
        _stabilityController.Clear();
        _stabilityRawX.Clear();
        _stabilityRawY.Clear();
        RefreshStabilityPlot();
        UpdateStabilityData();
    }

    /// <summary>Clears just the temporary grey raw dots; recorded captures stay.</summary>
    private void btn_stability_clear_raw_Click(object? sender, RoutedEventArgs e)
    {
        _stabilityRawX.Clear();
        _stabilityRawY.Clear();
        RefreshStabilityPlot(resetAxes: false);
    }

    // Saved files must be attributable, so these metadata fields are mandatory
    // before any save (manual captures or stability snapshots) is allowed.
    private bool IsMetadataComplete() =>
        !string.IsNullOrWhiteSpace(_metadata.Brand)       &&
        !string.IsNullOrWhiteSpace(_metadata.Pen)         &&
        !string.IsNullOrWhiteSpace(_metadata.InventoryId) &&
        !string.IsNullOrWhiteSpace(_metadata.Tablet)      &&
        !string.IsNullOrWhiteSpace(_metadata.Driver)      &&
        !string.IsNullOrWhiteSpace(_metadata.Date)        &&
        !string.IsNullOrWhiteSpace(_metadata.Os);

    /// <summary>Ensures metadata is complete before a save; prompts (blocking
    /// dialog) if not. Returns false if the user cancelled — caller aborts.</summary>
    private async Task<bool> EnsureMetadataAsync()
    {
        if (IsMetadataComplete()) return true;
        var dialog = new MetadataEditWindow(_metadata, requireAll: true);
        var edited = await dialog.ShowDialog<SessionMetadata?>(this);
        if (edited is null) return false;
        _metadata = edited;
        return true;
    }

    private async void btn_stability_save_Click(object? sender, RoutedEventArgs e)
    {
        if (_stabilityController.Captures.Count == 0) return;
        var tl = TopLevel.GetTopLevel(this);
        if (tl is null) return;

        // Require complete metadata before saving; cancelling aborts the save.
        if (!await EnsureMetadataAsync()) return;

        var file = await tl.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title             = "Save stability data",
            SuggestedFileName = BuildStabilitySuggestedFileName(),
            FileTypeChoices   = [JsonFilter],
            DefaultExtension  = "json"
        });
        if (file is null) return;

        try
        {
            var snapshot = StabilitySnapshotFile.From(_stabilityController.Captures, _metadata);
            await using var stream = await file.OpenWriteAsync();
            await JsonSerializer.SerializeAsync(stream, snapshot, JsonWriteOptions);
        }
        catch (Exception ex) { Debug.WriteLine($"[PPP2] Stability save failed: {ex.Message}"); }
    }

    private async void btn_stability_load_Click(object? sender, RoutedEventArgs e)
    {
        var tl = TopLevel.GetTopLevel(this);
        if (tl is null) return;

        var files = await tl.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Load stability data", AllowMultiple = false, FileTypeFilter = [JsonFilter]
        });
        if (files.Count == 0) return;
        if (files[0] is not IStorageFile file) return;

        try
        {
            await using var stream = await file.OpenReadAsync();
            var snapshot = await JsonSerializer.DeserializeAsync<StabilitySnapshotFile>(stream);
            if (snapshot is null) return;

            if (snapshot.Metadata is { } m)
                _metadata = m;

            var captures = snapshot.ToStabilityCaptures()
                .OrderBy(c => c.PhysicalGf).ToList();
            _stabilityController.LoadCaptures(captures);
            _stabilityRawX.Clear();
            _stabilityRawY.Clear();
            RefreshStabilityPlot();
            UpdateStabilityData();
        }
        catch (Exception ex) { Debug.WriteLine($"[PPP2] Stability load failed: {ex.Message}"); }
    }

    private void OnStabilitySliderChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        // Guard: controls may not be fully initialised during XAML loading.
        if (label_penTolerance is null) return;

        _stabilityController.PenTolerance   = slider_penTolerance.Value;
        _stabilityController.ScaleTolerance = slider_scaleTolerance.Value;
        _stabilityController.MinStableMs    = slider_stableDuration.Value;
        _stabilityController.MinGapMs       = slider_minGap.Value;

        label_penTolerance.Text   = $"{slider_penTolerance.Value * 100:F1}%";
        label_scaleTolerance.Text = $"{slider_scaleTolerance.Value:F1} gf";
        label_stableDuration.Text = $"{(int)slider_stableDuration.Value} ms";
        label_minGap.Text         = $"{(int)slider_minGap.Value} ms";

        UpdateCurveSummary();

        // Reflect a manual tolerance change in the preset combo (clearing it
        // when the values no longer match any preset). Skipped while a preset
        // is being applied, since that path drives the sliders itself.
        if (!_applyingTolerancePreset) SyncTolerancePresetSelection();
    }

    /// <summary>Refreshes the inline settings summary shown in the CURVE
    /// AUTO-CAPTURE ribbon section (the sliders themselves live in its flyout).</summary>
    private void UpdateCurveSummary()
    {
        if (txt_curve_settings is null || slider_penTolerance is null) return;
        txt_curve_settings.Text =
            $"Pen {slider_penTolerance.Value * 100:F1}%  ·  " +
            $"Scale {slider_scaleTolerance.Value:F1} gf\n" +
            $"Dur {(int)slider_stableDuration.Value} ms  ·  " +
            $"Gap {(int)slider_minGap.Value} ms";
    }

    /// <summary>
    /// The (pen, scale-gf) tolerance pair for a preset name, or null for an
    /// unknown / unset name.
    /// </summary>
    private static (double Pen, double Scale)? TolerancePreset(string? name) => name switch
    {
        TolerancePresetLow    => (PenToleranceLow,    ScaleToleranceLow),
        TolerancePresetMedium => (PenToleranceMedium, ScaleToleranceMedium),
        TolerancePresetHigh   => (PenToleranceHigh,   ScaleToleranceHigh),
        _                     => null,
    };

    /// <summary>
    /// Applies a LOW / MEDIUM / HIGH tolerance preset to the pen and scale
    /// tolerance sliders.
    /// </summary>
    private void comboBox_tolerancePreset_Changed(object? sender, SelectionChangedEventArgs e)
    {
        // Ignore the echo from SyncTolerancePresetSelection, and guard against
        // firing before the sliders exist during XAML load.
        if (_applyingTolerancePreset || slider_penTolerance is null) return;

        if (TolerancePreset(comboBox_tolerancePreset.SelectedItem?.ToString()) is not { } preset)
            return;

        _applyingTolerancePreset = true;
        slider_penTolerance.Value   = preset.Pen;
        slider_scaleTolerance.Value = preset.Scale;
        _applyingTolerancePreset = false;

        // Push the new values to the controller and labels in one shot.
        OnStabilitySliderChanged(this, null!);
    }

    /// <summary>
    /// Selects the preset whose tolerances match the current slider values, or
    /// clears the selection when they match none (a custom combination).
    /// </summary>
    private void SyncTolerancePresetSelection()
    {
        if (comboBox_tolerancePreset is null) return;

        string? match =
            MatchesPreset(TolerancePresetLow)    ? TolerancePresetLow    :
            MatchesPreset(TolerancePresetMedium) ? TolerancePresetMedium :
            MatchesPreset(TolerancePresetHigh)   ? TolerancePresetHigh   :
            null;

        _applyingTolerancePreset = true;
        comboBox_tolerancePreset.SelectedItem = match;   // null clears selection
        _applyingTolerancePreset = false;

        bool MatchesPreset(string name) =>
            TolerancePreset(name) is { } p &&
            Approx(slider_penTolerance.Value,   p.Pen) &&
            Approx(slider_scaleTolerance.Value, p.Scale);

        static bool Approx(double a, double b) => Math.Abs(a - b) < 1e-6;
    }

    private void UpdateStabilityData()
    {
        // Per-card source index points into the controller's underlying list
        // (insertion order) so the ✕ button can RemoveAt regardless of sort.
        var indexed = _stabilityController.Captures
            .Select((c, i) => (Capture: c, SourceIndex: i))
            .ToList();
        var ordered = btn_stability_sort.Ascending
            ? indexed.OrderBy(t => t.Capture.PhysicalGf)
            : (IEnumerable<(StabilityCapture Capture, int SourceIndex)>)indexed.OrderByDescending(t => t.Capture.PhysicalGf);

        var cards = ordered
            .Select((t, displayIdx) => new StabilityCaptureCard(
                SourceIndex: t.SourceIndex,
                Number:      $"#{displayIdx + 1}",
                Segments:    ReadingLine(
                                 $"{t.Capture.PhysicalGf:F2}",
                                 $"{t.Capture.LogicalNorm * 100:F2}",
                                 count: t.Capture.Count)))
            .ToList();

        listBox_stability_captures.ItemsSource = null;
        listBox_stability_captures.ItemsSource = cards;
        reading_stability_unique.Value = _stabilityController.Captures.Count.ToString();
    }

    private void btn_stability_sort_Click(object? sender, RoutedEventArgs e) => UpdateStabilityData();


    private async void btn_stability_edit_Click(object? sender, RoutedEventArgs e)
    {
        var dialog = new StabilityEditWindow(_stabilityController.Captures);
        var result = await dialog.ShowDialog<List<StabilityCapture>?>(this);
        if (result is null) return;   // cancelled

        _stabilityController.LoadCaptures(result);
        _stabilityRawX.Clear();
        _stabilityRawY.Clear();
        RefreshStabilityPlot();
        UpdateStabilityData();
    }

    // ── Scale-lag compensation (pen feed alignment) ─────────────────────────

    private void FeedPenToActiveController(PenReadingData d)
    {
        _accumulatorController.MaxRawPressure = _penManager.MaxPressure;
        _accumulatorController.OnPenData(d);
    }

    /// <summary>Releases queued pen events timestamped at or before
    /// <paramref name="upTo"/> to the active controller, in order.</summary>
    private void FlushPenLagQueue(DateTime upTo)
    {
        int i = 0;
        while (i < _penLagQueue.Count && _penLagQueue[i].T <= upTo)
        {
            FeedPenToActiveController(_penLagQueue[i].D);
            i++;
        }
        if (i > 0) _penLagQueue.RemoveRange(0, i);
    }

    private void btn_stability_card_delete_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not EstimateCard { DataContext: StabilityCaptureCard card }) return;
        if (!_stabilityController.RemoveAt(card.SourceIndex)) return;
        RefreshStabilityPlot();
        UpdateStabilityData();
    }

    private void chk_scale_lag_Changed(object? sender, RoutedEventArgs e)
    {
        // Read from sender — this can fire during XAML init before the named
        // field is assigned.
        _scaleLagComp = (sender as CheckBox)?.IsChecked == true;
        _penLagQueue.Clear();   // switch cleanly between compensated / raw feed
    }

    // ── Accumulator ────────────────────────────────────────────────

    private const double AccumulatorChartMinRefreshMs = 150;
    private DateTime _lastAccumRefresh = DateTime.MinValue;
    private bool     _accumReady;

    private void InitializeAccumulatorPlot()
    {
        var plt = accumPlotView.Plot;
        plt.XLabel("Physical force (gf)");
        plt.YLabel("Pen on (%)");
        plt.Axes.SetLimits(_accumulatorController.MinGf, _accumulatorController.MaxGf, 0, 100);
        ChartTheme.Apply(accumPlotView);
        accumPlotView.Refresh();
        _accumReady = true;
    }

    /// <summary>Applies the range / bucket-size picker values to the controller. A
    /// bucket-size-only change preserves the data (all widths accumulate at once);
    /// a range change rebuilds and clears. No-op until the controls exist.</summary>
    // Set while loading a snapshot so syncing the range/bucket pickers doesn't
    // reconfigure (and clear) the counts we're about to load.
    private bool _suppressAccumConfig;

    private void ApplyAccumulatorConfig()
    {
        if (_suppressAccumConfig) return;
        if (comboBox_accum_bucket is null || numeric_accum_min is null || numeric_accum_max is null)
            return;

        double min = (double)(numeric_accum_min.Value ?? 0m);
        double max = (double)(numeric_accum_max.Value ?? 10m);
        if (max <= min) return;   // ignore an invalid range mid-edit

        double width = ParseBucketLabel(comboBox_accum_bucket.SelectedItem as string,
                                        fallback: _accumulatorController.BucketWidth);

        // Width-only change (same range) just switches which layout is shown —
        // data preserved. A range change rebuilds/clears all layouts.
        bool rangeSame = Math.Abs(min - _accumulatorController.MinGf) < 1e-9
                      && Math.Abs(max - _accumulatorController.MaxGf) < 1e-9;
        if (rangeSame) _accumulatorController.SetWidth(width);
        else           _accumulatorController.Configure(min, max, width);

        if (!_accumReady) return;   // plots not initialised yet (early OnOpened)
        InitializeAccumulatorPlot();   // reset axis range to the new bounds
        RefreshAccumulatorPlot();
        UpdateAccumulatorData();
    }

    private void accum_range_Changed(object? sender, NumericUpDownValueChangedEventArgs e)
        => ApplyAccumulatorConfig();

    /// <summary>Mouse-wheel over a range field nudges it by one increment (×5 with
    /// Shift), so refocusing the range doesn't need many spinner clicks.</summary>
    private void accum_range_Wheel(object? sender, Avalonia.Input.PointerWheelEventArgs e)
    {
        if (sender is not NumericUpDown nud) return;
        decimal mult = e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Shift) ? 5m : 1m;
        decimal step = nud.Increment * mult * (e.Delta.Y >= 0 ? 1m : -1m);
        nud.Value = Math.Clamp((nud.Value ?? 0m) + step, nud.Minimum, nud.Maximum);
        e.Handled = true;
    }

    private void comboBox_accum_bucket_Changed(object? sender, SelectionChangedEventArgs e)
        => ApplyAccumulatorConfig();

    private void comboBox_accum_target_Changed(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressAccumConfig) return;
        var target = comboBox_accum_target.SelectedIndex == (int)AccumTarget.MaxPressure
            ? AccumTarget.MaxPressure : AccumTarget.Iaf;
        _accumulatorController.SetTarget(target);
        SyncAccumUiToActiveTarget();
    }

    /// <summary>Re-points the range / bucket pickers, labels, plot and table at the
    /// active target after a target switch (without reconfiguring/clearing its data).</summary>
    private void SyncAccumUiToActiveTarget()
    {
        _suppressAccumConfig = true;
        numeric_accum_min.Value = (decimal)_accumulatorController.MinGf;
        numeric_accum_max.Value = (decimal)_accumulatorController.MaxGf;
        PopulateAccumBucketCombo(_accumulatorController.CurrentBucketWidths, _accumulatorController.BucketWidth);
        UpdateAccumLabels();
        _suppressAccumConfig = false;

        _accumRows = null;   // rebuild table rows for the target's span
        if (!_accumReady) return;
        InitializeAccumulatorPlot();
        RefreshAccumulatorPlot();
        UpdateAccumulatorData();
    }

    /// <summary>Target-aware wording for the description, estimate caption, and table headers.</summary>
    private void UpdateAccumLabels()
    {
        bool max = _accumulatorController.Target == AccumTarget.MaxPressure;
        if (txt_accum_desc        is not null) txt_accum_desc.Text        = max
            ? "Max pressure (pen <100% vs =100%) by force bucket"
            : "IAF (pen 0% vs non-zero) by force bucket";
        if (reading_accum_estimate is not null) reading_accum_estimate.Caption = max ? "Est. Max:" : "Est. IAF:";
        if (txt_accum_hdr_off     is not null) txt_accum_hdr_off.Text     = max ? "<max" : "0%";
        if (txt_accum_hdr_on      is not null) txt_accum_hdr_on.Text      = max ? "max"  : ">0%";
    }

    /// <summary>Fills the bucket-size combo from a target's width set, selecting
    /// <paramref name="selected"/>. Caller manages <see cref="_suppressAccumConfig"/>.</summary>
    private void PopulateAccumBucketCombo(IReadOnlyList<double> widths, double selected)
    {
        comboBox_accum_bucket.Items.Clear();
        foreach (var w in widths) comboBox_accum_bucket.Items.Add(BucketLabel(w));
        comboBox_accum_bucket.SelectedItem = BucketLabel(selected);
        if (comboBox_accum_bucket.SelectedItem is null && comboBox_accum_bucket.Items.Count > 0)
            comboBox_accum_bucket.SelectedIndex = 0;

        // Scale the range spinner/wheel step to the target's force range: Max
        // pressure runs to hundreds of gf, so 1-gf steps would be far too slow.
        decimal step = _accumulatorController.Target == AccumTarget.MaxPressure ? 50m : 1m;
        numeric_accum_min.Increment = step;
        numeric_accum_max.Increment = step;
    }

    private static string BucketLabel(double width)
        => width.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + " gf";

    private static double ParseBucketLabel(string? label, double fallback)
    {
        if (label is null) return fallback;
        var num = label.Replace("gf", "", StringComparison.OrdinalIgnoreCase).Trim();
        return double.TryParse(num, System.Globalization.NumberStyles.Float,
                               System.Globalization.CultureInfo.InvariantCulture, out var w) ? w : fallback;
    }

    private void RefreshAccumulatorIfDue()
    {
        var now = DateTime.UtcNow;
        if ((now - _lastAccumRefresh).TotalMilliseconds < AccumulatorChartMinRefreshMs) return;
        _lastAccumRefresh = now;
        RefreshAccumulatorPlot();
        UpdateAccumulatorData();
    }

    private void RefreshAccumulatorPlot()
    {
        var plt = accumPlotView.Plot;
        plt.Clear();

        DrawAccumulatorFractionFit(plt);

        // 50% reference line.
        var mid = plt.Add.HorizontalLine(50);
        mid.Color       = ScottPlot.Color.FromHex("#9CA3AF");
        mid.LineWidth   = 1;
        mid.LinePattern = ScottPlot.LinePattern.Dotted;

        // Live physical-force indicator — a vertical line at the current scale
        // reading, matching Curve mode's live pressure line.
        var vForce = plt.Add.VerticalLine(_physicalPressure);
        vForce.Color     = LivePressureColor;
        vForce.LineWidth = LivePressureLineWidth;
        vForce.Text      = FormatGf(_physicalPressure);

        plt.Axes.SetLimits(_accumulatorController.MinGf, _accumulatorController.MaxGf, 0, 100);
        accumPlotView.Refresh();
    }

    /// <summary>Per-bucket activation fraction as markers sized by sample
    /// count (confidence), plus a count-weighted logistic fit and its 50% point
    /// as the IAF estimate.</summary>
    private void DrawAccumulatorFractionFit(ScottPlot.Plot plt)
    {
        int  n        = _accumulatorController.BucketCount;
        var  under    = _accumulatorController.UnderCounts;
        var  atOrOver = _accumulatorController.AtOrOverCounts;

        long maxCount = 1;
        for (int i = 0; i < n; i++) maxCount = Math.Max(maxCount, under[i] + atOrOver[i]);

        // B: confidence-sized markers — area ∝ sample count (sqrt scaling).
        var marker = ScottPlot.Color.FromHex("#7C3AED");
        for (int i = 0; i < n; i++)
        {
            long tot = under[i] + atOrOver[i];
            if (tot == 0) continue;

            double c     = _accumulatorController.BucketCenterGf(i);
            double onPct = (double)atOrOver[i] / tot * 100.0;
            float  size  = (float)(5 + 16 * Math.Sqrt((double)tot / maxCount));
            plt.Add.Marker(c, onPct, ScottPlot.MarkerShape.FilledCircle, size, marker);
        }
        // (No IAF line on the chart — the Est. IAF readout shows the value.)
    }

    private void UpdateAccumulatorData()
    {
        if (reading_accum_samples is null) return;   // pre-XAML-load guard
        reading_accum_samples.Value = _accumulatorController.TotalSamples.ToString("N0");

        // Prefer the logistic-fit 50% point; fall back to the simple crossover.
        reading_accum_estimate.Value =
            _accumulatorController.TryLogisticFit(out double f0, out _) ? $"{f0:F2} gf (fit)"
            : _accumulatorController.CrossoverGf is { } x               ? $"{x:F2} gf"
            : "—";

        UpdateAccumulatorTable();
    }

    private static readonly IBrush AccumRowEven     = Brushes.White;
    private static readonly IBrush AccumRowOdd      = new SolidColorBrush(Avalonia.Media.Color.FromRgb(0xF0, 0xF0, 0xF0));
    private static readonly IBrush AccumCellChanged = new SolidColorBrush(Avalonia.Media.Color.FromRgb(0xFF, 0xE0, 0xB2));
    // Settled-row tints (once a row has enough samples): very light blue when the
    // pen is mostly off, very light purple when mostly on.
    private static readonly IBrush AccumRowLowOn    = new SolidColorBrush(Avalonia.Media.Color.FromRgb(0xDB, 0xEA, 0xFE));   // ≤20% on
    private static readonly IBrush AccumRowHighOn   = new SolidColorBrush(Avalonia.Media.Color.FromRgb(0xED, 0xE9, 0xFE));   // ≥80% on
    private const long   AccumRowSettledMin = 50;   // min total samples before tinting
    private const double AccumRowLowOnPct   = 20.0;
    private const double AccumRowHighOnPct  = 80.0;

    // Stable row set for the table; built once per span (bucket count), then the
    // counts / change-tint are updated in place so rows never shift.
    private List<AccumulatorRow>? _accumRows;

    /// <summary>Builds the fixed row set for the configured span — an out-of-range
    /// "&lt; min" row, one row per bucket, and an out-of-range "&gt;= max" row — all
    /// initialised to zero, so rows never shift and missing ranges show as 0/0.</summary>
    private void BuildAccumulatorRows(int n)
    {
        var rows = new List<AccumulatorRow>(n + 2);

        // Below-range row (top). Zebra is assigned by absolute row position.
        rows.Add(new AccumulatorRow($"< {_accumulatorController.MinGf:F2}", AccumRowEven));
        for (int i = 0; i < n; i++)
        {
            double lo = _accumulatorController.BucketLowerGf(i);
            double hi = lo + _accumulatorController.BucketWidth;
            int    r  = i + 1;   // +1 for the below row at index 0
            IBrush rowBg = (r % 2 == 0) ? AccumRowEven : AccumRowOdd;
            rows.Add(new AccumulatorRow($"{lo:F2} < {hi:F2}", rowBg));
        }
        // Above-range row (bottom).
        IBrush aboveBg = ((n + 1) % 2 == 0) ? AccumRowEven : AccumRowOdd;
        rows.Add(new AccumulatorRow($"≥ {_accumulatorController.MaxGf:F2}", aboveBg));

        _accumRows = rows;
        listBox_accum_table.ItemsSource = rows;
    }

    private void UpdateAccumulatorTable()
    {
        int n = _accumulatorController.BucketCount;
        if (_accumRows is null || _accumRows.Count != n + 2) BuildAccumulatorRows(n);

        var  under    = _accumulatorController.UnderCounts;
        var  atOrOver = _accumulatorController.AtOrOverCounts;
        var  kind     = _accumulatorController.LastChanged;
        int  lastB    = _accumulatorController.LastBucket;
        bool lastUnder = _accumulatorController.LastUnderIncremented;

        // Index 0 = below-range, 1..n = buckets, n+1 = above-range.
        SetAccumRow(_accumRows![0],
            _accumulatorController.BelowUnder, _accumulatorController.BelowAtOrOver,
            kind == AccumulatorController.ChangedKind.Below, lastUnder);

        for (int i = 0; i < n; i++)
            SetAccumRow(_accumRows![i + 1], under[i], atOrOver[i],
                kind == AccumulatorController.ChangedKind.Bucket && i == lastB, lastUnder);

        SetAccumRow(_accumRows![n + 1],
            _accumulatorController.AboveUnder, _accumulatorController.AboveAtOrOver,
            kind == AccumulatorController.ChangedKind.Above, lastUnder);
    }

    private static void SetAccumRow(AccumulatorRow row, long under, long atOrOver, bool isChangedRow, bool changedUnder)
    {
        long total = under + atOrOver;
        row.UnderCnt    = under.ToString("N0");
        row.AtOrOverCnt = atOrOver.ToString("N0");
        row.AtOrOverPct = total > 0 ? (atOrOver * 100.0 / total).ToString("F1") : "—";

        // Settled-row tint: once a row has enough samples, colour it by how
        // strongly the pen is under (light blue) or at-or-over (light purple) the
        // threshold; otherwise keep the zebra stripe.
        IBrush baseBg = row.RowBg;
        if (total >= AccumRowSettledMin)
        {
            double onPct = atOrOver * 100.0 / total;
            if      (onPct <= AccumRowLowOnPct)  baseBg = AccumRowLowOn;
            else if (onPct >= AccumRowHighOnPct) baseBg = AccumRowHighOn;
        }

        row.PhysBg     = baseBg;
        row.UnderBg    = (isChangedRow &&  changedUnder) ? AccumCellChanged : baseBg;
        row.AtOrOverBg = (isChangedRow && !changedUnder) ? AccumCellChanged : baseBg;
    }

    private void btn_accumulator_enable_Click(object? sender, RoutedEventArgs e)
    {
        _accumulatorEnabled = !_accumulatorEnabled;
        btn_accumulator_enable.Content = _accumulatorEnabled ? "Stop" : "Start";
        txt_accum_status.Text = _accumulatorEnabled
            ? "Accumulating — vary the force across the range."
            : "Stopped.";
        _penLagQueue.Clear();

        // Starting accumulation also starts the scale (if a port is selected).
        if (_accumulatorEnabled) _ = StartScaleIfIdleAsync();

        RefreshAccumulatorPlot();
        UpdateAccumulatorData();
    }

    private void btn_accumulator_clear_Click(object? sender, RoutedEventArgs e)
    {
        _accumulatorController.Clear();
        RefreshAccumulatorPlot();
        UpdateAccumulatorData();
    }

    private async void btn_accum_copy_Click(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard) return;
        await clipboard.SetTextAsync(BuildAccumulatorMarkdown());
    }

    /// <summary>Renders the accumulator buckets (incl. out-of-range rows) as a
    /// Markdown table with a header line of range / sample / IAF context.</summary>
    private string BuildAccumulatorMarkdown()
    {
        var c  = _accumulatorController;
        var sb = new StringBuilder();

        bool   max   = c.Target == AccumTarget.MaxPressure;
        string what  = max ? "Max pressure" : "IAF";
        string offH  = max ? "<max" : "0%";
        string onH   = max ? "max"  : ">0%";

        string est = c.TryLogisticFit(out double f0, out _) ? $"{f0:F2} gf (fit)"
                   : c.CrossoverGf is { } x               ? $"{x:F2} gf"
                   : "—";

        sb.AppendLine($"## Accumulator — {what} by physical force");
        sb.AppendLine();
        sb.AppendLine($"Range: {c.MinGf:F2}–{c.MaxGf:F2} gf · bucket {c.BucketWidth:F2} gf · " +
                      $"samples {c.TotalSamples:N0} · est. {what} {est}");
        sb.AppendLine();
        sb.AppendLine($"| PHYS (gf) | {offH} | {onH} | %ON |");
        sb.AppendLine("| --- | --: | --: | --: |");

        sb.AppendLine(Row($"< {c.MinGf:F2}", c.BelowUnder, c.BelowAtOrOver));
        for (int i = 0; i < c.BucketCount; i++)
        {
            double lo = c.BucketLowerGf(i);
            sb.AppendLine(Row($"{lo:F2} < {lo + c.BucketWidth:F2}", c.UnderCounts[i], c.AtOrOverCounts[i]));
        }
        sb.AppendLine(Row($"≥ {c.MaxGf:F2}", c.AboveUnder, c.AboveAtOrOver));
        return sb.ToString();

        static string Row(string phys, long under, long atOrOver)
        {
            long   tot = under + atOrOver;
            string pct = tot > 0 ? (atOrOver * 100.0 / tot).ToString("F1") : "—";
            return $"| {phys} | {under:N0} | {atOrOver:N0} | {pct} |";
        }
    }

    private AccumulatorTargetSnapshot BuildTargetSnapshot(AccumTarget t)
    {
        var (min, max, width) = _accumulatorController.GetConfig(t);
        return new AccumulatorTargetSnapshot
        {
            Target        = t.ToString(),
            MinGf         = min,
            MaxGf         = max,
            SelectedWidth = width,
            Layouts       = _accumulatorController.ExportLayouts(t).Select(l => new AccumulatorLayoutSnapshot
            {
                Width         = l.Width,
                Under         = l.Under.ToList(),
                AtOrOver      = l.AtOrOver.ToList(),
                BelowUnder    = l.BelowUnder,
                BelowAtOrOver = l.BelowAtOrOver,
                AboveUnder    = l.AboveUnder,
                AboveAtOrOver = l.AboveAtOrOver,
            }).ToList(),
        };
    }

    private async void btn_accum_save_Click(object? sender, RoutedEventArgs e)
    {
        var tl = TopLevel.GetTopLevel(this);
        if (tl is null) return;

        var file = await tl.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title             = "Save accumulator data",
            SuggestedFileName = $"Accumulator_{DateTime.Now:yyyy-MM-dd_HHmmss}.json",
            FileTypeChoices   = [JsonFilter],
            DefaultExtension  = "json",
        });
        if (file is null) return;

        try
        {
            var snapshot = new AccumulatorSnapshotFile
            {
                Metadata     = _metadata,
                Version      = 2,
                ActiveTarget = _accumulatorController.Target.ToString(),
                Targets      =
                [
                    BuildTargetSnapshot(AccumTarget.Iaf),
                    BuildTargetSnapshot(AccumTarget.MaxPressure),
                ],
            };
            await using var stream = await file.OpenWriteAsync();
            await JsonSerializer.SerializeAsync(stream, snapshot, JsonWriteOptions);
        }
        catch (Exception ex) { Debug.WriteLine($"[PPP2] Accumulator save failed: {ex.Message}"); }
    }

    private async void btn_accum_load_Click(object? sender, RoutedEventArgs e)
    {
        var tl = TopLevel.GetTopLevel(this);
        if (tl is null) return;

        var files = await tl.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Load accumulator data", AllowMultiple = false, FileTypeFilter = [JsonFilter]
        });
        if (files.Count == 0 || files[0] is not IStorageFile file) return;

        try
        {
            await using var stream = await file.OpenReadAsync();
            var snap = await JsonSerializer.DeserializeAsync<AccumulatorSnapshotFile>(stream);
            if (snap is null) return;

            // Stop any running accumulation so it doesn't overwrite the loaded data.
            _accumulatorEnabled = false;
            btn_accumulator_enable.Content = "Start";
            _penLagQueue.Clear();
            if (snap.Metadata is { } m) _metadata = m;

            if (snap.Targets is { Count: > 0 })
            {
                // v2: one entry per target.
                foreach (var ts in snap.Targets)
                {
                    if (ParseAccumTarget(ts.Target) is not { } t) continue;
                    _accumulatorController.ImportLayouts(t, ts.MinGf, ts.MaxGf, ts.SelectedWidth,
                                                        ToLayoutCounts(ts.Layouts));
                }
                if (ParseAccumTarget(snap.ActiveTarget) is { } active)
                    _accumulatorController.SetTarget(active);
            }
            else
            {
                // Legacy v1: single IAF target stored at the top level.
                _accumulatorController.SetTarget(AccumTarget.Iaf);
                _accumulatorController.ImportLayouts(AccumTarget.Iaf, snap.MinGf, snap.MaxGf,
                    snap.SelectedWidth, ToLayoutCounts(snap.Layouts ?? []));
            }

            // Sync the MEASURE / range / bucket pickers + labels WITHOUT reconfiguring.
            _suppressAccumConfig = true;
            comboBox_accum_target.SelectedIndex = (int)_accumulatorController.Target;
            numeric_accum_min.Value = (decimal)_accumulatorController.MinGf;
            numeric_accum_max.Value = (decimal)_accumulatorController.MaxGf;
            PopulateAccumBucketCombo(_accumulatorController.CurrentBucketWidths, _accumulatorController.BucketWidth);
            UpdateAccumLabels();
            _suppressAccumConfig = false;

            _accumRows = null;   // rebuild the table rows for the loaded span
            txt_accum_status.Text = "Loaded.";
            InitializeAccumulatorPlot();
            RefreshAccumulatorPlot();
            UpdateAccumulatorData();
        }
        catch (Exception ex) { Debug.WriteLine($"[PPP2] Accumulator load failed: {ex.Message}"); }
    }

    private static List<AccumulatorController.LayoutCounts> ToLayoutCounts(
        IEnumerable<AccumulatorLayoutSnapshot> layouts) =>
        layouts.Select(l => new AccumulatorController.LayoutCounts(
            l.Width, [.. l.Under], [.. l.AtOrOver],
            l.BelowUnder, l.BelowAtOrOver, l.AboveUnder, l.AboveAtOrOver)).ToList();

    /// <summary>Parses a saved target string, accepting the legacy "Saturation"
    /// name (renamed to MaxPressure). Null if unrecognised.</summary>
    private static AccumTarget? ParseAccumTarget(string? s)
    {
        if (string.Equals(s, "Saturation", StringComparison.OrdinalIgnoreCase))
            return AccumTarget.MaxPressure;
        return Enum.TryParse<AccumTarget>(s, out var t) ? t : null;
    }

    // ── Monitor chart + controls ─────────────────────────────────────────────

    private void InitializeMonitorPlots()
    {
        var pp = monitorPenPlot.Plot;
        pp.XLabel("time (s, relative)");
        pp.YLabel("Pen (normalized)");
        pp.Axes.SetLimits(-MonitorWindowSeconds, 0, 0, 1);
        ChartTheme.Apply(monitorPenPlot, userInputEnabled: false);   // EKG view doesn't pan/zoom
        monitorPenPlot.Refresh();

        var sp = monitorScalePlot.Plot;
        sp.XLabel("time (s, relative)");
        sp.YLabel("Scale (gf)");
        sp.Axes.SetLimits(-MonitorWindowSeconds, 0, 0, MonitorScaleYFloor);
        ChartTheme.Apply(monitorScalePlot, userInputEnabled: false);
        monitorScalePlot.Refresh();
    }

    /// <summary>
    /// Re-applies the light/dark chart palette to every plot and repaints.
    /// Colours only — axis limits are untouched, so the current zoom / view
    /// survives a theme flip. No-op until the plots have been initialised.
    /// </summary>
    private void ReapplyChartThemes()
    {
        if (stabilityPlotView?.Plot is null) return;

        ChartTheme.Apply(stabilityPlotView);
        ChartTheme.Apply(accumPlotView);
        ChartTheme.Apply(monitorPenPlot,   userInputEnabled: false);
        ChartTheme.Apply(monitorScalePlot, userInputEnabled: false);

        stabilityPlotView.Refresh();
        accumPlotView.Refresh();
        monitorPenPlot.Refresh();
        monitorScalePlot.Refresh();
    }

    private void AppendMonitorPen(double normalized)
    {
        if (!monitorView.IsVisible) return;
        double t = (DateTime.UtcNow - _monitorEpoch).TotalSeconds;
        _monitorPenT.Add(t);
        _monitorPenY.Add(normalized);
        TrimMonitor(_monitorPenT, _monitorPenY, t);
    }

    private void AppendMonitorScale(double gf)
    {
        if (!monitorView.IsVisible) return;
        double t = (DateTime.UtcNow - _monitorEpoch).TotalSeconds;
        _monitorScaleT.Add(t);
        _monitorScaleY.Add(gf);
        TrimMonitor(_monitorScaleT, _monitorScaleY, t);
    }

    private static void TrimMonitor(List<double> times, List<double> values, double tNow)
    {
        double cutoff = tNow - MonitorWindowSeconds;
        int drop = 0;
        while (drop < times.Count && times[drop] < cutoff) drop++;
        if (drop > 0)
        {
            times.RemoveRange(0, drop);
            values.RemoveRange(0, drop);
        }
    }

    private void RefreshMonitorIfDue()
    {
        if (!monitorView.IsVisible) return;
        var now = DateTime.UtcNow;
        if ((now - _lastMonitorRefresh).TotalMilliseconds < MonitorRefreshMs) return;
        _lastMonitorRefresh = now;
        RefreshMonitorPlots();
    }

    private void RefreshMonitorPlots()
    {
        double tNow = (DateTime.UtcNow - _monitorEpoch).TotalSeconds;
        double tMin = tNow - MonitorWindowSeconds;
        TrimMonitorMarks(tNow);   // scroll old capture markers off with the traces

        // Scale's y-axis ceiling: auto-grows upward, with a floor so the chart
        // isn't squashed when forces are small.
        double yMaxScale = MonitorScaleYFloor;
        if (_monitorScaleY.Count > 0)
            yMaxScale = Math.Max(yMaxScale, _monitorScaleY.Max() * 1.1);

        // Tolerance bands hug the current reading (±tolerance), mirroring the
        // scatter chart's tolerance box: a trace staying inside its band is
        // within tolerance (i.e. counts as stable).
        double penTol      = _stabilityController.PenTolerance;
        double scaleTol    = _stabilityController.ScaleTolerance;
        double penCenter   = _monitorPenY.Count   > 0 ? _monitorPenY[^1]   : _logicalPressure;
        double scaleCenter = _monitorScaleY.Count > 0 ? _monitorScaleY[^1] : _physicalPressure;

        // ── Top chart: always shows the pen trace. In overlay mode it also
        //    hosts the scale trace on its secondary (right) y-axis.
        var pp = monitorPenPlot.Plot;
        pp.Clear();

        // Bands first so the traces render on top of them.
        AddTimeSeriesToleranceBand(pp, tMin, tNow, penCenter, penTol, PenBandFill);
        if (_monitorOverlay)
            AddTimeSeriesToleranceBand(pp, tMin, tNow, scaleCenter, scaleTol, ScaleBandFill, pp.Axes.Right);

        if (_monitorPenT.Count > 0)
        {
            var line = pp.Add.Scatter(_monitorPenT.ToArray(), _monitorPenY.ToArray());
            line.Color      = ScottPlot.Color.FromHex("#2563EB");
            line.LineWidth  = 1.5f;
            line.MarkerSize = 0;
        }

        if (_monitorOverlay && _monitorScaleT.Count > 0)
        {
            var scaleLine = pp.Add.Scatter(_monitorScaleT.ToArray(), _monitorScaleY.ToArray());
            scaleLine.Color      = LivePressureColor;
            scaleLine.LineWidth  = 1.5f;
            scaleLine.MarkerSize = 0;
            scaleLine.Axes.YAxis = pp.Axes.Right;
        }

        // Capture markers on top of the pen trace (and the scale trace in overlay).
        DrawMonitorMarks(pp, _monitorPenMarks);
        if (_monitorOverlay)
            DrawMonitorMarks(pp, _monitorScaleMarks, pp.Axes.Right);

        // Primary (left + bottom) limits.
        pp.Axes.SetLimits(tMin, tNow, 0, 1);
        pp.YLabel("Pen (normalized)");

        if (_monitorOverlay)
        {
            // Secondary (right) axis only when overlaying.
            pp.Axes.Right.Min        = 0;
            pp.Axes.Right.Max        = yMaxScale;
            pp.Axes.Right.Label.Text = "Scale (gf)";
        }
        monitorPenPlot.Refresh();

        // ── Bottom chart: only used in split mode.
        if (!_monitorOverlay)
        {
            var sp = monitorScalePlot.Plot;
            sp.Clear();
            AddTimeSeriesToleranceBand(sp, tMin, tNow, scaleCenter, scaleTol, ScaleBandFill);
            if (_monitorScaleT.Count > 0)
            {
                var line = sp.Add.Scatter(_monitorScaleT.ToArray(), _monitorScaleY.ToArray());
                line.Color      = LivePressureColor;
                line.LineWidth  = 1.5f;
                line.MarkerSize = 0;
            }
            DrawMonitorMarks(sp, _monitorScaleMarks);
            sp.Axes.SetLimits(tMin, tNow, 0, yMaxScale);
            monitorScalePlot.Refresh();
        }
    }

    /// <summary>Draws capture markers (red dots) on a time-series trace, optionally
    /// bound to a specific y-axis (the scale trace's right axis when overlaid).</summary>
    private static void DrawMonitorMarks(
        ScottPlot.Plot plt, List<(double T, double Y)> marks, ScottPlot.IYAxis? yAxis = null)
    {
        if (marks.Count == 0) return;
        var s = plt.Add.Scatter(marks.Select(m => m.T).ToArray(), marks.Select(m => m.Y).ToArray());
        s.Color       = ScottPlot.Color.FromHex("#DC2626");
        s.LineWidth   = 0;
        s.MarkerSize  = 9;
        s.MarkerShape = ScottPlot.MarkerShape.FilledCircle;
        if (yAxis is not null) s.Axes.YAxis = yAxis;
    }

    /// <summary>
    /// Toggles the Monitor centre layout to follow <see cref="_monitorOverlay"/>:
    /// overlay-on hides the scale chart and stretches the pen chart across
    /// both rows; overlay-off restores the side-by-side split.
    /// </summary>
    private void UpdateMonitorLayout()
    {
        if (monitorScalePlot is null) return;
        if (_monitorOverlay)
        {
            Avalonia.Controls.Grid.SetRowSpan(monitorPenPlot, 2);
            monitorScalePlot.IsVisible = false;
        }
        else
        {
            Avalonia.Controls.Grid.SetRowSpan(monitorPenPlot, 1);
            monitorScalePlot.IsVisible = true;
        }
    }

    private void chk_capture_overlay_Changed(object? sender, RoutedEventArgs e)
    {
        if (monitorPenPlot is null) return;   // pre-XAML-load guard
        _monitorOverlay = chk_capture_overlay.IsChecked == true;
        UpdateMonitorLayout();
        RefreshMonitorPlots();
    }

    private void ResetMonitor()
    {
        _monitorPenT.Clear();
        _monitorPenY.Clear();
        _monitorScaleT.Clear();
        _monitorScaleY.Clear();
        _monitorPenMarks.Clear();
        _monitorScaleMarks.Clear();
        _monitorEpoch       = DateTime.UtcNow;
        _lastMonitorRefresh = DateTime.MinValue;
    }

    private string BuildStabilitySuggestedFileName()
    {
        var id   = BlankTo(_metadata.InventoryId, "stability");
        var date = BlankTo(_metadata.Date, DateTime.Today.ToString("yyyy-MM-dd"));
        return $"stability_{id}_{date}.json";
    }

    // ── Save / Load / Drag-drop ───────────────────────────────────────────────

    private static readonly JsonSerializerOptions JsonWriteOptions =
        new() { WriteIndented = true };
    private static readonly FilePickerFileType JsonFilter =
        new("JSON files") { Patterns = ["*.json"] };

    private void OnDragOver(object? sender, DragEventArgs e)
    {
#pragma warning disable CS0618
        e.DragEffects = e.Data.Contains(DataFormats.Files)
            ? DragDropEffects.Copy : DragDropEffects.None;
#pragma warning restore CS0618
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
#pragma warning disable CS0618
        if (!e.Data.Contains(DataFormats.Files)) return;
        var items = e.Data.GetFiles();
#pragma warning restore CS0618
        var json = items?
            .FirstOrDefault(f => f.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase));
        if (json is not null) await LoadFromStorageFileAsync(json);
    }

    /// <summary>Loads a dropped stability snapshot file into the capture list.</summary>
    private async Task LoadFromStorageFileAsync(IStorageItem item)
    {
        if (item is not IStorageFile file) return;
        try
        {
            await using var stream = await file.OpenReadAsync();
            var snapshot = await JsonSerializer.DeserializeAsync<StabilitySnapshotFile>(stream);
            if (snapshot is null) return;

            if (snapshot.Metadata is { } m)
                _metadata = m;

            var captures = snapshot.ToStabilityCaptures()
                .OrderBy(c => c.PhysicalGf).ToList();
            _stabilityController.LoadCaptures(captures);
            _stabilityRawX.Clear();
            _stabilityRawY.Clear();
            RefreshStabilityPlot();
            UpdateStabilityData();
        }
        catch (Exception ex) { Debug.WriteLine($"[PPP2] Load failed: {ex.Message}"); }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a capture-card reading line of the form "X gf → Y% (Z)" where the
    /// numbers (X, Y, Z) are bold. <paramref name="raw"/> appends the "(Z)" raw
    /// pressure part; <paramref name="count"/> appends a trailing "×N" multiplier.
    /// Both are omitted when null.
    /// </summary>
    private static IReadOnlyList<ReadingSegment> ReadingLine(
        string phys, string logicalPct, string? raw = null, int? count = null)
    {
        var segs = new List<ReadingSegment>
        {
            new(phys,        Bold: true),
            new(" gf → ",    Bold: false),
            new(logicalPct,  Bold: true),
            new("%",         Bold: false),
        };
        if (raw is not null)
        {
            segs.Add(new(" (",  Bold: false));
            segs.Add(new(raw,   Bold: true));
            segs.Add(new(")",   Bold: false));
        }
        if (count is { } n)
        {
            segs.Add(new("  ×",         Bold: false));
            segs.Add(new(n.ToString(),  Bold: true));
        }
        return segs;
    }

    private static string BlankTo(string? s, string fallback) =>
        string.IsNullOrWhiteSpace(s) ? fallback : s.Trim();

    private Task ShowMessageAsync(string message, string title)
    {
        Debug.WriteLine($"[PPP2 {title}] {message}");
        return Task.CompletedTask;
    }
}
