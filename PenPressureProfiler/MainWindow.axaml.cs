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

    // ── Threshold Accumulator ────────────────────────────────────────────────

    // Threshold Accumulator bucket-size presets. 0.5 gf is the default.
    private const string AccumBucket1   = "1 gf";
    private const string AccumBucket05  = "0.5 gf";
    private const string AccumBucket025 = "0.25 gf";
    private const string AccumBucket01  = "0.1 gf";


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
                InputApi.AvaloniaPointer => "Avalonia Pointer",
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
        comboBox_view_mode.Items.Add("Threshold Accumulator");
        comboBox_view_mode.SelectedIndex = 0;

        // Threshold Accumulator bucket-size picker. 0.5 gf is the default (index 1).
        comboBox_accum_bucket.Items.Add(AccumBucket1);
        comboBox_accum_bucket.Items.Add(AccumBucket05);
        comboBox_accum_bucket.Items.Add(AccumBucket025);
        comboBox_accum_bucket.Items.Add(AccumBucket01);
        comboBox_accum_bucket.SelectedIndex = 1;

        // Capture centre chart-type picker (scatter plot vs live time series).
        comboBox_capture_chart.Items.Add("Scatter Plot");
        comboBox_capture_chart.Items.Add("Time series");
        comboBox_capture_chart.SelectedIndex = 0;

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
        bool capture     = tab == "capture";
        bool accumulator = tab == "accumulator";

        panel_right_stability.IsVisible   = capture;
        panel_right_accumulator.IsVisible = accumulator;

        // The auto-capture control groups live in the ribbon, one per mode.
        if (group_curve_capture is not null) group_curve_capture.IsVisible = capture;
        if (group_accumulator is not null)   group_accumulator.IsVisible   = accumulator;

        accumPlotView.IsVisible = accumulator;

        // Capture hosts two interchangeable centre charts (scatter / time-series);
        // the right panel (captures list, Record, save/load) is shared by both.
        stabilityPlotView.IsVisible = capture && !_captureTimeSeries;
        monitorView.IsVisible       = capture &&  _captureTimeSeries;

        // The VIEW ribbon group (chart-type picker + the active chart's option)
        // is shown only for Capture.
        if (group_view_follow is not null)
        {
            group_view_follow.IsVisible = capture;
            UpdateCaptureViewControls();
        }
    }

    private void comboBox_view_mode_Changed(object? sender, SelectionChangedEventArgs e)
    {
        // Guard: ComboBox.SelectedIndex set during OnOpened fires this handler
        // before the right-panel ScrollViewers exist as bound fields.
        if (panel_right_stability is null) return;

        // Leaving Threshold Accumulator stops its accumulation.
        bool toAccumulator = comboBox_view_mode.SelectedItem?.ToString() == "Threshold Accumulator";
        if (!toAccumulator) _accumulatorEnabled = false;
        _penLagQueue.Clear();

        switch (comboBox_view_mode.SelectedItem?.ToString())
        {
            case "Threshold Accumulator":
                SetActiveTab("accumulator");
                btn_accumulator_enable.Content = _accumulatorEnabled ? "Stop" : "Start";
                RefreshAccumulatorPlot();
                UpdateAccumulatorData();
                break;
            default:        // "Curve" or any unrecognised value
                SetActiveTab("capture");
                RefreshCaptureChart();
                break;
        }
    }

    // ── Capture chart type (scatter plot / time series) ─────────────────────────

    private void comboBox_capture_chart_Changed(object? sender, SelectionChangedEventArgs e)
    {
        // Guard: fires once during init before the plots exist.
        if (stabilityPlotView is null) return;

        _captureTimeSeries = comboBox_capture_chart.SelectedIndex == 1;

        // Only the centre chart changes; the Capture right panel is shared, so
        // act only while Capture is the active tab.
        if (panel_right_stability.IsVisible)
        {
            stabilityPlotView.IsVisible = !_captureTimeSeries;
            monitorView.IsVisible       =  _captureTimeSeries;
            UpdateCaptureViewControls();
            RefreshCaptureChart();
        }
    }

    /// <summary>
    /// Shows the option relevant to the active Capture chart type in the VIEW
    /// ribbon group: "Follow live" for the scatter plot, "Overlay traces" for
    /// the time series. The chart-type picker itself is always shown.
    /// </summary>
    private void UpdateCaptureViewControls()
    {
        if (chk_live_follow is null) return;
        chk_live_follow.IsVisible    = !_captureTimeSeries;
        chk_capture_overlay.IsVisible =  _captureTimeSeries;
    }

    /// <summary>
    /// Repaints the active Capture centre chart and rebuilds the captures list
    /// (shared by both chart types). The time series starts fresh on each entry.
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
            RefreshAccumulatorIfDue();
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

        ProximityDot.Fill   = d.TipDown ? DotActive : inProx ? Brushes.Orange : DotInactive;
        ProximityLabel.Text = d.TipDown ? "Tip down" : inProx ? "Proximity" : "Out";
        TipDot.Fill     = d.TipDown     ? DotActive : DotInactive;
        Barrel1Dot.Fill = d.Barrel1Down ? DotActive : DotInactive;
        Barrel2Dot.Fill = d.Barrel2Down ? DotActive : DotInactive;

        RibbonAzLabel.Text     = $"Az: {d.Azimuth:F1}";
        RibbonAltLabel.Text    = $"Alt: {d.Altitude:F1}";
        RibbonTxLabel.Text     = $"TX: {d.TiltX:F1}";
        RibbonTyLabel.Text     = $"TY: {d.TiltY:F1}";
    }

    private void UpdateCards(PenReadingData d)
    {
        reading_pressure_raw.Value    = d.RawPressure.ToString();
        reading_pressure_norm.Value   = $"{d.NormalizedPressure * 100.0:F2} %";
        reading_pressure_smooth.Value = $"{d.SmoothedPressure   * 100.0:F2} %";
        pressureBar.Value             = d.NormalizedPressure * 100.0;

        _penPacketCount += d.PacketCount;
        var elapsed = (DateTime.UtcNow - _penRateWindowStart).TotalSeconds;
        if (elapsed >= 1.0)
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

    private void FeedPenToActiveController(PenReadingData d) =>
        _accumulatorController.OnPenData(d);

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

    // ── Threshold Accumulator ────────────────────────────────────────────────

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

    /// <summary>Applies the range / bucket-size picker values to the controller,
    /// re-allocating (and clearing) the buckets. No-op until the controls exist.</summary>
    private void ApplyAccumulatorConfig()
    {
        if (comboBox_accum_bucket is null || numeric_accum_min is null || numeric_accum_max is null)
            return;

        double min = (double)(numeric_accum_min.Value ?? 0m);
        double max = (double)(numeric_accum_max.Value ?? 10m);
        if (max <= min) return;   // ignore an invalid range mid-edit

        double width = comboBox_accum_bucket.SelectedItem?.ToString() switch
        {
            AccumBucket1   => 1.0,
            AccumBucket025 => 0.25,
            AccumBucket01  => 0.1,
            _              => 0.5,
        };

        _accumulatorController.Configure(min, max, width);

        if (!_accumReady) return;   // plots not initialised yet (early OnOpened)
        InitializeAccumulatorPlot();   // reset axis range to the new bounds
        RefreshAccumulatorPlot();
        UpdateAccumulatorData();
    }

    private void accum_range_Changed(object? sender, NumericUpDownValueChangedEventArgs e)
        => ApplyAccumulatorConfig();

    private void comboBox_accum_bucket_Changed(object? sender, SelectionChangedEventArgs e)
        => ApplyAccumulatorConfig();

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

        plt.Axes.SetLimits(_accumulatorController.MinGf, _accumulatorController.MaxGf, 0, 100);
        accumPlotView.Refresh();
    }

    /// <summary>Per-bucket activation fraction as markers sized by sample
    /// count (confidence), plus a count-weighted logistic fit and its 50% point
    /// as the IAF estimate.</summary>
    private void DrawAccumulatorFractionFit(ScottPlot.Plot plt)
    {
        int  n       = _accumulatorController.BucketCount;
        var  zero    = _accumulatorController.ZeroCounts;
        var  nonZero = _accumulatorController.NonZeroCounts;

        long maxCount = 1;
        for (int i = 0; i < n; i++) maxCount = Math.Max(maxCount, zero[i] + nonZero[i]);

        // B: confidence-sized markers — area ∝ sample count (sqrt scaling).
        var marker = ScottPlot.Color.FromHex("#7C3AED");
        for (int i = 0; i < n; i++)
        {
            long tot = zero[i] + nonZero[i];
            if (tot == 0) continue;

            double c     = _accumulatorController.BucketCenterGf(i);
            double onPct = (double)nonZero[i] / tot * 100.0;
            float  size  = (float)(5 + 16 * Math.Sqrt((double)tot / maxCount));
            plt.Add.Marker(c, onPct, ScottPlot.MarkerShape.FilledCircle, size, marker);
        }

        // C: count-weighted logistic fit + its 50% point (the IAF estimate).
        if (_accumulatorController.TryLogisticFit(out double f0, out double k))
        {
            double min = _accumulatorController.MinGf;
            double max = _accumulatorController.MaxGf;
            const int steps = 160;
            var fx = new double[steps];
            var fy = new double[steps];
            for (int s = 0; s < steps; s++)
            {
                double x = min + (max - min) * s / (steps - 1);
                fx[s] = x;
                fy[s] = 100.0 / (1.0 + Math.Exp(-k * (x - f0)));
            }
            var fit = plt.Add.Scatter(fx, fy);
            fit.Color      = marker;
            fit.LineWidth  = 2;
            fit.MarkerSize = 0;

            if (f0 >= min && f0 <= max)
                AddAccumulatorIafLine(plt, f0, "IAF (fit)");
        }
        else if (_accumulatorController.CrossoverGf is { } x)
        {
            AddAccumulatorIafLine(plt, x, "IAF");
        }
    }

    private static void AddAccumulatorIafLine(ScottPlot.Plot plt, double gf, string label)
    {
        var v = plt.Add.VerticalLine(gf);
        v.Color       = ScottPlot.Color.FromHex("#DC2626");
        v.LineWidth   = 2;
        v.LinePattern = ScottPlot.LinePattern.Dashed;
        v.Text        = $"{label} ≈ {gf:F2} gf";
    }

    private void UpdateAccumulatorData()
    {
        if (reading_accum_samples is null) return;   // pre-XAML-load guard
        reading_accum_samples.Value = _accumulatorController.TotalSamples.ToString("N0");

        // Prefer the logistic-fit 50% point; fall back to the simple crossover.
        reading_accum_iaf.Value =
            _accumulatorController.TryLogisticFit(out double f0, out _) ? $"{f0:F2} gf (fit)"
            : _accumulatorController.CrossoverGf is { } x               ? $"{x:F2} gf"
            : "—";

        UpdateAccumulatorTable();
    }

    private static readonly IBrush AccumRowEven     = Brushes.White;
    private static readonly IBrush AccumRowOdd      = new SolidColorBrush(Avalonia.Media.Color.FromRgb(0xF0, 0xF0, 0xF0));
    private static readonly IBrush AccumCellChanged = new SolidColorBrush(Avalonia.Media.Color.FromRgb(0xFF, 0xE0, 0xB2));

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

        var  zero     = _accumulatorController.ZeroCounts;
        var  nonZero  = _accumulatorController.NonZeroCounts;
        var  kind     = _accumulatorController.LastChanged;
        int  lastB    = _accumulatorController.LastBucket;
        bool lastZero = _accumulatorController.LastZeroIncremented;

        // Index 0 = below-range, 1..n = buckets, n+1 = above-range.
        SetAccumRow(_accumRows![0],
            _accumulatorController.BelowZero, _accumulatorController.BelowNonZero,
            kind == AccumulatorController.ChangedKind.Below, lastZero);

        for (int i = 0; i < n; i++)
            SetAccumRow(_accumRows![i + 1], zero[i], nonZero[i],
                kind == AccumulatorController.ChangedKind.Bucket && i == lastB, lastZero);

        SetAccumRow(_accumRows![n + 1],
            _accumulatorController.AboveZero, _accumulatorController.AboveNonZero,
            kind == AccumulatorController.ChangedKind.Above, lastZero);
    }

    private static void SetAccumRow(AccumulatorRow row, long zero, long nonZero, bool isChangedRow, bool changedZero)
    {
        long total = zero + nonZero;
        row.ZeroCnt    = zero.ToString("N0");
        row.NonZeroCnt = nonZero.ToString("N0");
        row.OnPct      = total > 0 ? (nonZero * 100.0 / total).ToString("F1") : "—";
        row.ZeroBg     = (isChangedRow &&  changedZero) ? AccumCellChanged : row.RowBg;
        row.NonZeroBg  = (isChangedRow && !changedZero) ? AccumCellChanged : row.RowBg;
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
            sp.Axes.SetLimits(tMin, tNow, 0, yMaxScale);
            monitorScalePlot.Refresh();
        }
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
