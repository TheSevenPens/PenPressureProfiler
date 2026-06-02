using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using PenPressureProfiler.Controls;
using ScottPlot;
using System.Diagnostics;
using System.IO;
using System.IO.Ports;
using System.Linq;
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
    private const string AxisDefault  = "Default";
    private const string AxisFull     = "Full";
    private const string AxisIAF      = "IAF";
    private const string AxisIAFLarge = "IAF Large";
    private const string AxisMax      = "Max";

    private PressureRecordCollection _recordCollection = new();
    private PressureTestFile         _metadata         = new();
    private double                   _logicalPressure;
    private string _chartAxisRange      = AxisDefault;
    // (sort direction lives on the SortToggleButton controls — `btn_*_sort.Ascending`)

    // ── Sweep ─────────────────────────────────────────────────────────────────

    private readonly SweepController _sweepController = new();
    private bool     _sweepEnabled;

    private readonly List<double> _sweepRawX         = [];
    private readonly List<double> _sweepRawY         = [];
    private const int    SweepRawMaxPoints           = 600;
    private const double SweepChartMinRefreshMs      = 100;
    private DateTime     _lastSweepChartRefresh       = DateTime.MinValue;

    // ── Threshold (IAF + MAX, picked via ComboBox) ───────────────────────────

    private enum ThresholdMode { IafFromAbove, IafFromBelow, MaxFromBelow }

    private const string ThresholdModeIafAbove = "IAF from above";
    private const string ThresholdModeIafBelow = "IAF from below";
    private const string ThresholdModeMax      = "MAX from below";

    private readonly IafController      _iafController      = new();
    private readonly IafBelowController _iafBelowController = new();
    private readonly MaxController      _maxController      = new();
    private ThresholdMode _thresholdMode = ThresholdMode.IafFromAbove;
    private bool          _thresholdEnabled;
    private const double  ThresholdChartMinRefreshMs = 100;
    private DateTime      _lastThresholdChartRefresh = DateTime.MinValue;

    // Shared visual: live-pressure indicator on the threshold chart.
    private static readonly ScottPlot.Color LivePressureColor = ScottPlot.Color.FromHex("#F97316");
    private const float LivePressureLineWidth = 3.0f;

    // ── Monitor (live scrolling EKG view) ───────────────────────────────────

    private const double MonitorWindowSeconds = 10.0;
    private const double MonitorRefreshMs     = 50;   // ~20 fps
    private const double MonitorScaleYFloor   = 50;   // gf — min y-axis ceiling for the scale chart

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

    // ── Scale state ───────────────────────────────────────────────────────────

    private double   _physicalPressure;
    private int      _scaleReadingCount;
    private DateTime _scaleRateWindowStart = DateTime.UtcNow;

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

        _sweepController.RawPairAvailable += OnSweepRawPair;
        _sweepController.StableCaptured   += OnSweepStableCapture;

        _iafController.EstimateAdded      += OnIafEstimateAdded;
        _iafController.SweepRejected      += OnIafSweepRejected;

        _iafBelowController.EstimateAdded += OnIafBelowEstimateAdded;
        _iafBelowController.SweepRejected += OnIafBelowSweepRejected;

        _maxController.EstimateAdded      += OnMaxEstimateAdded;

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

        foreach (var mode in new[] { AxisDefault, AxisFull, AxisIAF, AxisIAFLarge, AxisMax })
            comboBox_chart_axis.Items.Add(mode);
        comboBox_chart_axis.SelectedIndex = 0;

        // Threshold sub-mode picker.
        comboBox_threshold_mode.Items.Add(ThresholdModeIafAbove);
        comboBox_threshold_mode.Items.Add(ThresholdModeIafBelow);
        comboBox_threshold_mode.Items.Add(ThresholdModeMax);
        comboBox_threshold_mode.SelectedIndex = 0;

        // VIEW picker — top-level view dropdown in the ribbon.
        // Order maps to "manual" / "sweep" / "threshold" / "monitor" tabs.
        comboBox_view_mode.Items.Add("Manual");
        comboBox_view_mode.Items.Add("Auto");
        comboBox_view_mode.Items.Add("Threshold");
        comboBox_view_mode.Items.Add("Monitor");
        comboBox_view_mode.SelectedIndex = 0;

        _metadata.Date = DateTime.Today.ToString("yyyy-MM-dd");
        _metadata.User = Environment.UserName.ToUpper().Trim();
        _metadata.Os   = "WINDOWS";

        UpdateScaleDot();
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            InitializePlot();
            InitializeSweepPlot();
            InitializeThresholdPlot();
            InitializeMonitorPlots();
            UpdateChart();
            UpdateThresholdData();
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
        panel_right_recording.IsVisible = tab == "manual";
        panel_right_sweep.IsVisible     = tab == "sweep";
        panel_right_threshold.IsVisible = tab == "threshold";
        panel_right_monitor.IsVisible   = tab == "monitor";

        plotView.IsVisible       = tab == "manual";
        sweepPlotView.IsVisible  = tab == "sweep";
        threshPlotView.IsVisible = tab == "threshold";
        monitorView.IsVisible    = tab == "monitor";
    }

    private void comboBox_view_mode_Changed(object? sender, SelectionChangedEventArgs e)
    {
        // Guard: ComboBox.SelectedIndex set during OnOpened fires this handler
        // before the right-panel ScrollViewers exist as bound fields.
        if (panel_right_recording is null) return;

        switch (comboBox_view_mode.SelectedItem?.ToString())
        {
            case "Auto":
                SetActiveTab("sweep");
                RefreshSweepPlot();
                UpdateSweepData();
                break;
            case "Threshold":
                SetActiveTab("threshold");
                RefreshThresholdPlot();
                UpdateThresholdData();
                break;
            case "Monitor":
                SetActiveTab("monitor");
                // Reset epoch + buffers so traces start fresh on entry —
                // EKG-style monitoring is about "now", not history.
                ResetMonitor();
                RefreshMonitorPlots();
                break;
            default:        // "Manual" or any unrecognised value
                SetActiveTab("manual");
                if (plotView?.Plot is not null) UpdateChart();
                break;
        }
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
        btn_scale_record.Content = "Stop";
        await _scaleManager.StartAsync(port);
        btn_scale_record.Content = "Start";
        UpdateScaleDot();
    }

    private void OnScaleReading(ScaleRecord record)
    {
        _sessionLogger.LogScaleReading(record);
        if (_sweepEnabled) _sweepController.OnScaleData(record.ReadingAsDouble);
        if (_thresholdEnabled)
        {
            switch (_thresholdMode)
            {
                case ThresholdMode.IafFromAbove: _iafController     .OnScaleData(record.ReadingAsDouble); break;
                case ThresholdMode.IafFromBelow: _iafBelowController.OnScaleData(record.ReadingAsDouble); break;
                case ThresholdMode.MaxFromBelow: _maxController     .OnScaleData(record.ReadingAsDouble); break;
            }
        }

        // Refresh the threshold chart at ~10 fps when visible so the live
        // pressure line tracks the scale stream. The armed indicator depends
        // on the same scale stream, so refresh it on the same tick.
        if (threshPlotView is { IsVisible: true })
        {
            var now = DateTime.UtcNow;
            if ((now - _lastThresholdChartRefresh).TotalMilliseconds >= ThresholdChartMinRefreshMs)
            {
                _lastThresholdChartRefresh = now;
                RefreshThresholdPlot();
                UpdateThresholdArmedIndicator();
            }
        }

        // Monitor: append the scale sample and refresh if visible.
        AppendMonitorScale(record.ReadingAsDouble);
        RefreshMonitorIfDue();

        _physicalPressure = record.ReadingAsDouble;
        reading_phys_pressure.Value = $"{_physicalPressure:F1} gf";

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
        _sessionLogger.LogPenReading(d);
        if (_sweepEnabled) _sweepController.OnPenData(d);
        if (_thresholdEnabled)
        {
            switch (_thresholdMode)
            {
                case ThresholdMode.IafFromAbove: _iafController     .OnPenData(d); break;
                case ThresholdMode.IafFromBelow: _iafBelowController.OnPenData(d); break;
                case ThresholdMode.MaxFromBelow: _maxController     .OnPenData(d); break;
            }
        }
        _logicalPressure = d.SmoothedPressure;
        UpdateRibbon(d);
        UpdateCards(d);

        // Monitor: append the pen sample and refresh if visible.
        AppendMonitorPen(d.NormalizedPressure);
        RefreshMonitorIfDue();
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
            reading_pen_rate.Value = $"{_penPacketCount / elapsed:F0} pkt/s";
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
        monitorView.IsVisible    ? monitorPenPlot  :
        threshPlotView.IsVisible ? threshPlotView  :
        sweepPlotView.IsVisible  ? sweepPlotView   :
                                   plotView;

    private void OnChartAreaPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(PenInputSurface).Properties.IsRightButtonPressed) return;

        // Right-click → reset to the currently selected axis range mode.
        if (monitorView.IsVisible)
            RefreshMonitorPlots();   // re-applies the rolling window axes
        else if (threshPlotView.IsVisible)
            RefreshThresholdPlot();  // re-applies the fixed threshold axis range
        else if (sweepPlotView.IsVisible)
            RefreshSweepPlot();      // re-applies ApplySweepAxisRange()
        else
            UpdateChart();           // re-applies ApplyAxisRange()

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

    // ── Pressure chart ────────────────────────────────────────────────────────

    private void InitializePlot()
    {
        var plt = plotView.Plot;
        plt.XLabel("Physical pressure (gf)");
        plt.YLabel("Logical pressure (%)");
        plt.Axes.SetLimits(0, PlotAxisLimit, 0, PlotPressureLimit);
        ChartTheme.Apply(plotView);
        plotView.Refresh();
    }

    private void UpdateChart()
    {
        var plt   = plotView.Plot;
        plt.Clear();

        var dataX = _recordCollection.Items.Select(r => r.PhysicalPressure).ToArray();
        var dataY = _recordCollection.Items.Select(r => r.LogicalPressure * 100).ToArray();

        if (dataX.Length > 0)
        {
            var scatter = plt.Add.Scatter(dataX, dataY);
            scatter.Color      = ScottPlot.Color.FromHex("#2563EB");
            scatter.LineWidth  = 1.5f;
            scatter.MarkerSize = 6;
        }

        ApplyAxisRange(dataX, dataY);
        plotView.Refresh();

        // Build one ManualRecordCard per record, tagging each with its index
        // in the original (insertion-order) collection so the per-card delete
        // button can target the right entry regardless of current sort.
        var indexed = _recordCollection.Items
            .Select((r, i) => (Record: r, SourceIndex: i))
            .ToList();
        var ordered = btn_manual_sort.Ascending
            ? indexed.OrderBy(t => t.Record.PhysicalPressure)
            : (IEnumerable<(PressureRecord Record, int SourceIndex)>)indexed.OrderByDescending(t => t.Record.PhysicalPressure);

        var cards = ordered
            .Select((t, displayIdx) => new ManualRecordCard(
                SourceIndex: t.SourceIndex,
                Number:      $"#{displayIdx + 1}",
                Fields: new[]
                {
                    new EstimateField("PHYS:", $"{t.Record.PhysicalPressure:F1} gf"),
                    new EstimateField("LOG%:", $"{t.Record.LogicalPressure * 100.0:F2}"),
                }))
            .ToList();

        listBox_records.ItemsSource = null;
        listBox_records.ItemsSource = cards;

        int n = _recordCollection.Count;
        txt_record_count.Text = $"{n} record{(n == 1 ? "" : "s")}";
    }

    private void ApplyAxisRange(double[] dataX, double[] dataY)
    {
        var plt = plotView.Plot;
        switch (_chartAxisRange)
        {
            case AxisFull:
                double xFull = dataX.Length > 0
                    ? Math.Max(dataX.Max() * 1.1, PlotAxisLimit) : PlotAxisLimit;
                plt.Axes.SetLimits(0, xFull, 0, PlotPressureLimit);
                break;

            case AxisIAF:
                // Zoom to the activation threshold: X axis just past the minimum
                // recorded force, Y axis 0–5% so the first-contact response is visible.
                double iafXMax = dataX.Length > 0 ? dataX.Min() + 2 : 2;
                plt.Axes.SetLimits(0, iafXMax, 0, 5);
                break;

            case AxisIAFLarge:
                double iafLargeXMax = dataX.Length > 0 ? dataX.Min() + 6 : 6;
                plt.Axes.SetLimits(0, iafLargeXMax, 0, 5);
                break;

            case AxisMax:
                // Zoom to where the pen saturates (logical ≥ 95%).
                if (dataY.Length > 0)
                {
                    var saturatedX = dataX
                        .Where((x, i) => i < dataY.Length && dataY[i] >= 95.0)
                        .ToList();
                    if (saturatedX.Count > 0)
                        plt.Axes.SetLimits(
                            Math.Max(0, saturatedX.Min() - 0.5),
                            saturatedX.Max() + 0.5,
                            95, 100);
                    else
                        plt.Axes.SetLimits(0, PlotAxisLimit, 95, 100);
                }
                else
                {
                    plt.Axes.SetLimits(0, PlotAxisLimit, 95, 100);
                }
                break;

            default: // AxisDefault
                plt.Axes.SetLimits(0, PlotAxisLimit, 0, PlotPressureLimit);
                break;
        }
    }

    // ── Recording ─────────────────────────────────────────────────────────────

    private void btn_record_Click(object? sender, RoutedEventArgs e)
    { _recordCollection.Add(_physicalPressure, _logicalPressure); UpdateChart(); }

    private void btn_clear_all_Click(object? sender, RoutedEventArgs e)
    { _recordCollection.Clear(); UpdateChart(); }

    private void comboBox_chart_axis_Changed(object? sender, SelectionChangedEventArgs e)
    {
        _chartAxisRange = comboBox_chart_axis.SelectedItem?.ToString() ?? AxisDefault;

        // Apply to whichever chart is currently visible.
        if (sweepPlotView is { IsVisible: true, Plot: not null })
            RefreshSweepPlot();
        else if (plotView?.Plot is not null)
            UpdateChart();
    }

    // ── Metadata dialog ───────────────────────────────────────────────────────

    private async void btn_edit_metadata_Click(object? sender, RoutedEventArgs e)
    {
        var dialog = new MetadataEditWindow(_metadata);
        var result = await dialog.ShowDialog<PressureTestFile?>(this);
        if (result is null) return;     // cancelled

        _metadata = result;
    }

    // ── Sweep chart ───────────────────────────────────────────────────────────

    private void InitializeSweepPlot()
    {
        var plt = sweepPlotView.Plot;
        plt.XLabel("Physical pressure (gf)");
        plt.YLabel("Logical pressure (%)");
        plt.Axes.SetLimits(0, PlotAxisLimit, 0, PlotPressureLimit);
        ChartTheme.Apply(sweepPlotView);
        sweepPlotView.Refresh();
    }

    private void RefreshSweepPlot()
    {
        var plt = sweepPlotView.Plot;
        plt.Clear();

        // Raw pairs (medium grey, small dots, ~10 fps throttled)
        if (_sweepRawX.Count > 0)
        {
            var raw = plt.Add.Scatter(_sweepRawX.ToArray(), _sweepRawY.ToArray());
            raw.Color      = ScottPlot.Color.FromHex("#888888");
            raw.LineWidth  = 0;
            raw.MarkerSize = 5;
        }

        // Stable captures (sorted by physical pressure).
        var sorted = _sweepController.Captures.OrderBy(c => c.PhysicalGf).ToList();
        if (sorted.Count > 0)
        {
            var stX = sorted.Select(c => c.PhysicalGf).ToArray();
            var stY = sorted.Select(c => c.LogicalNorm * 100).ToArray();

            var stable = plt.Add.Scatter(stX, stY);
            stable.Color      = ScottPlot.Color.FromHex("#2563EB");
            stable.LineWidth  = 1.5f;
            stable.MarkerSize = 7;
        }

        ApplySweepAxisRange();
        sweepPlotView.Refresh();
    }

    private void ApplySweepAxisRange()
    {
        var plt = sweepPlotView.Plot;

        // Combine raw + stable X values for Full range; IAF uses stable only
        // (raw scatter includes noise that would skew the minimum too low).
        var allX = _sweepRawX
            .Concat(_sweepController.Captures.Select(c => c.PhysicalGf))
            .ToList();
        var stableX = _sweepController.Captures
            .Select(c => c.PhysicalGf)
            .Where(x => x > 0)
            .ToList();

        // Note: AxisMax (saturation zoom) is pressure-chart-only; on the sweep
        // chart it falls through to the default case (full calibrated range).
        switch (_chartAxisRange)
        {
            case AxisFull:
                double xFull = allX.Count > 0
                    ? Math.Max(allX.Max() * 1.1, PlotAxisLimit) : PlotAxisLimit;
                plt.Axes.SetLimits(0, xFull, 0, PlotPressureLimit);
                break;

            case AxisIAF:
                double iafXMax = stableX.Count > 0 ? stableX.Min() + 2 : 2;
                plt.Axes.SetLimits(0, iafXMax, 0, 5);
                break;

            case AxisIAFLarge:
                double iafLargeXMax = stableX.Count > 0 ? stableX.Min() + 6 : 6;
                plt.Axes.SetLimits(0, iafLargeXMax, 0, 5);
                break;

            default: // AxisDefault
                plt.Axes.SetLimits(0, PlotAxisLimit, 0, PlotPressureLimit);
                break;
        }
    }

    private void OnSweepRawPair(double physGf, double logNorm)
    {
        if (_sweepRawX.Count >= SweepRawMaxPoints)
        { _sweepRawX.RemoveAt(0); _sweepRawY.RemoveAt(0); }
        _sweepRawX.Add(physGf);
        _sweepRawY.Add(logNorm * 100);

        // Throttle raw-data chart refresh to ~10 fps; always refresh on stable captures.
        var now = DateTime.UtcNow;
        if ((now - _lastSweepChartRefresh).TotalMilliseconds >= SweepChartMinRefreshMs
            && sweepPlotView.IsVisible)
        {
            _lastSweepChartRefresh = now;
            RefreshSweepPlot();
        }
    }

    private void OnSweepStableCapture(SweepCapture capture)
    {
        RefreshSweepPlot();
        UpdateSweepData();
    }

    // ── Sweep controls ────────────────────────────────────────────────────────

    private void btn_sweep_enable_Click(object? sender, RoutedEventArgs e)
    {
        _sweepEnabled = !_sweepEnabled;
        btn_sweep_enable.Content = _sweepEnabled ? "Stop Auto-Capture" : "Start Auto-Capture";

        // Convenience: starting Auto-Capture also starts the scale (if a COM
        // port is selected and the scale isn't already reading). Stopping
        // Auto-Capture leaves the scale running — user can stop it separately.
        if (_sweepEnabled) _ = StartScaleIfIdleAsync();
    }

    private void btn_sweep_record_Click(object? sender, RoutedEventArgs e)
    {
        // Force a capture at the current live values. The controller's
        // StableCaptured event fires from inside RecordManual, which triggers
        // OnSweepStableCapture → RefreshSweepPlot + UpdateSweepData.
        _sweepController.RecordManual(_physicalPressure, _logicalPressure);
    }

    private void btn_sweep_clear_Click(object? sender, RoutedEventArgs e)
    {
        _sweepController.Clear();
        _sweepRawX.Clear();
        _sweepRawY.Clear();
        RefreshSweepPlot();
        UpdateSweepData();
    }

    private async void btn_sweep_save_Click(object? sender, RoutedEventArgs e)
    {
        if (_sweepController.Captures.Count == 0) return;
        var tl = TopLevel.GetTopLevel(this);
        if (tl is null) return;

        var file = await tl.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title             = "Save sweep data",
            SuggestedFileName = BuildSweepSuggestedFileName(),
            FileTypeChoices   = [JsonFilter],
            DefaultExtension  = "json"
        });
        if (file is null) return;

        try
        {
            var snapshot = SweepSnapshotFile.From(_sweepController.Captures);
            await using var stream = await file.OpenWriteAsync();
            await JsonSerializer.SerializeAsync(stream, snapshot, JsonWriteOptions);
        }
        catch (Exception ex) { Debug.WriteLine($"[PPP2] Sweep save failed: {ex.Message}"); }
    }

    private async void btn_sweep_load_Click(object? sender, RoutedEventArgs e)
    {
        var tl = TopLevel.GetTopLevel(this);
        if (tl is null) return;

        var files = await tl.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Load sweep data", AllowMultiple = false, FileTypeFilter = [JsonFilter]
        });
        if (files.Count == 0) return;
        if (files[0] is not IStorageFile file) return;

        try
        {
            await using var stream = await file.OpenReadAsync();
            var snapshot = await JsonSerializer.DeserializeAsync<SweepSnapshotFile>(stream);
            if (snapshot is null) return;

            var captures = snapshot.ToSweepCaptures()
                .OrderBy(c => c.PhysicalGf).ToList();
            _sweepController.LoadCaptures(captures);
            _sweepRawX.Clear();
            _sweepRawY.Clear();
            RefreshSweepPlot();
            UpdateSweepData();
        }
        catch (Exception ex) { Debug.WriteLine($"[PPP2] Sweep load failed: {ex.Message}"); }
    }

    private void OnSweepSliderChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        // Guard: controls may not be fully initialised during XAML loading.
        if (label_penTolerance is null) return;

        _sweepController.PenTolerance   = slider_penTolerance.Value;
        _sweepController.ScaleTolerance = slider_scaleTolerance.Value;
        _sweepController.MinStableMs    = slider_stableDuration.Value;
        _sweepController.MinGapMs       = slider_minGap.Value;

        label_penTolerance.Text   = $"{slider_penTolerance.Value * 100:F1}%";
        label_scaleTolerance.Text = $"{slider_scaleTolerance.Value:F1} gf";
        label_stableDuration.Text = $"{(int)slider_stableDuration.Value} ms";
        label_minGap.Text         = $"{(int)slider_minGap.Value} ms";
    }

    private void UpdateSweepData()
    {
        // Per-card source index points into the controller's underlying list
        // (insertion order) so the ✕ button can RemoveAt regardless of sort.
        var indexed = _sweepController.Captures
            .Select((c, i) => (Capture: c, SourceIndex: i))
            .ToList();
        var ordered = btn_sweep_sort.Ascending
            ? indexed.OrderBy(t => t.Capture.PhysicalGf)
            : (IEnumerable<(SweepCapture Capture, int SourceIndex)>)indexed.OrderByDescending(t => t.Capture.PhysicalGf);

        var cards = ordered
            .Select((t, displayIdx) => new SweepCaptureCard(
                SourceIndex: t.SourceIndex,
                Number:      $"#{displayIdx + 1}",
                Fields: new[]
                {
                    new EstimateField("PHYS:", $"{t.Capture.PhysicalGf:F2} gf"),
                    new EstimateField("LOG%:", $"{t.Capture.LogicalNorm * 100:F2}"),
                    new EstimateField("",      $"×{t.Capture.Count}"),
                }))
            .ToList();

        listBox_sweep_captures.ItemsSource = null;
        listBox_sweep_captures.ItemsSource = cards;
        reading_sweep_unique.Value = _sweepController.Captures.Count.ToString();
        reading_sweep_total.Value  = _sweepController.Captures.Sum(c => c.Count).ToString();
    }

    private void btn_sweep_sort_Click(object? sender, RoutedEventArgs e) => UpdateSweepData();
    private void btn_manual_sort_Click(object? sender, RoutedEventArgs e) => UpdateChart();

    private void btn_auto_params_toggle_Click(object? sender, RoutedEventArgs e)
    {
        panel_auto_params.IsVisible = !panel_auto_params.IsVisible;
        chevron_auto_params.Text    = panel_auto_params.IsVisible ? "▾" : "▸";
    }

    private async void btn_sweep_edit_Click(object? sender, RoutedEventArgs e)
    {
        var dialog = new SweepEditWindow(_sweepController.Captures);
        var result = await dialog.ShowDialog<List<SweepCapture>?>(this);
        if (result is null) return;   // cancelled

        _sweepController.LoadCaptures(result);
        _sweepRawX.Clear();
        _sweepRawY.Clear();
        RefreshSweepPlot();
        UpdateSweepData();
    }

    // ── Threshold chart + controls ───────────────────────────────────────────
    //
    // One chart, one panel, two sub-modes: "IAF from above" (release sweep)
    // and "MAX from below" (push sweep). Both controllers persist their
    // estimates independently — switching mode stops any active capture and
    // swaps the view to the other controller's data.

    private const int    ThresholdMaxEstimates = 10;    // matches both IafController.MaxEstimates and MaxController.MaxEstimates
    private const double ThresholdChartYMin    = 0;
    private const double ThresholdIafYMax      = 20;    // initial gf range for the activation region
    private const double ThresholdMaxYMax      = 200;   // initial gf range for the saturation region

    /// <summary>One row of mode-agnostic data: index + extrapolated physical gf.</summary>
    private readonly record struct ThresholdEntry(int Number, double PhysicalGf);

    // ── Mode-dispatched helpers ───────────────────────────────────────────────

    private bool IsIafMode =>
        _thresholdMode == ThresholdMode.IafFromAbove ||
        _thresholdMode == ThresholdMode.IafFromBelow;

    private string ThresholdYLabel() => IsIafMode ? "IAF (gf)" : "MAX (gf)";

    private double DefaultThresholdYMax() => IsIafMode ? ThresholdIafYMax : ThresholdMaxYMax;

    /// <summary>
    /// Boundary logical-pressure value the current mode is measuring,
    /// formatted as a bare number (the "%" lives in the card's LOG% label).
    /// </summary>
    private string ThresholdLogicalText() => IsIafMode ? "0" : "100";

    /// <summary>
    /// Driver raw pressure at the boundary the mode is measuring: always 0 for
    /// IAF; the driver's <see cref="PenSessionManager.MaxPressure"/> for MAX.
    /// Falls back to "—" when no pen session is running.
    /// </summary>
    private string ThresholdRawText()
    {
        if (IsIafMode) return "0";
        int max = _penManager.MaxPressure;
        return max > 0 ? max.ToString() : "—";
    }

    private IReadOnlyList<ThresholdEntry> CurrentThresholdEntries() => _thresholdMode switch
    {
        ThresholdMode.IafFromAbove =>
            _iafController.Estimates.Select((e, i) => new ThresholdEntry(i + 1, e.IafGf)).ToList(),
        ThresholdMode.IafFromBelow =>
            _iafBelowController.Estimates.Select((e, i) => new ThresholdEntry(i + 1, e.IafGf)).ToList(),
        ThresholdMode.MaxFromBelow =>
            _maxController.Estimates.Select((e, i) => new ThresholdEntry(i + 1, e.MaxGf)).ToList(),
        _ => [],
    };

    private double? CurrentThresholdMedian() => _thresholdMode switch
    {
        ThresholdMode.IafFromAbove => _iafController.Median,
        ThresholdMode.IafFromBelow => _iafBelowController.Median,
        ThresholdMode.MaxFromBelow => _maxController.Median,
        _ => null,
    };

    private bool CurrentThresholdIsFull() => _thresholdMode switch
    {
        ThresholdMode.IafFromAbove => _iafController.IsFull,
        ThresholdMode.IafFromBelow => _iafBelowController.IsFull,
        ThresholdMode.MaxFromBelow => _maxController.IsFull,
        _ => false,
    };

    private int CurrentThresholdCount() => _thresholdMode switch
    {
        ThresholdMode.IafFromAbove => _iafController.Estimates.Count,
        ThresholdMode.IafFromBelow => _iafBelowController.Estimates.Count,
        ThresholdMode.MaxFromBelow => _maxController.Estimates.Count,
        _ => 0,
    };

    private void CurrentThresholdControllerClear()
    {
        switch (_thresholdMode)
        {
            case ThresholdMode.IafFromAbove: _iafController     .Clear(); break;
            case ThresholdMode.IafFromBelow: _iafBelowController.Clear(); break;
            case ThresholdMode.MaxFromBelow: _maxController     .Clear(); break;
        }
    }

    private bool CurrentThresholdControllerRemoveAt(int index) => _thresholdMode switch
    {
        ThresholdMode.IafFromAbove => _iafController     .RemoveAt(index),
        ThresholdMode.IafFromBelow => _iafBelowController.RemoveAt(index),
        ThresholdMode.MaxFromBelow => _maxController     .RemoveAt(index),
        _ => false,
    };

    private string ThresholdShortName() => IsIafMode ? "IAF" : "MAX";

    private string ThresholdStartLabel() => $"Start Auto-{ThresholdShortName()}";
    private string ThresholdStopLabel()  => $"Stop Auto-{ThresholdShortName()}";

    private string ThresholdResultsHeader() => $"{ThresholdShortName()} estimates";

    private string ThresholdHelpText() => _thresholdMode switch
    {
        ThresholdMode.IafFromAbove =>
            $"Press the pen until at least {IafController.MinPeakGf:F0} gf, then release fully to zero. "
            + "Repeat 10 times. Each release produces one IAF estimate by linearly extrapolating the falling raw signal "
            + "to raw = 0; the final IAF is the median.",
        ThresholdMode.IafFromBelow =>
            $"Lift the pen so the scale reads less than {IafBelowController.MaxRestingGf:F1} gf, then press down gently "
            + "until raw pressure becomes nonzero. Repeat 10 times. Each activation produces one IAF estimate by linearly "
            + "extrapolating the rising raw signal back to raw = 0; the final IAF is the median.",
        ThresholdMode.MaxFromBelow =>
            "Press the pen until logical pressure reaches 100% (saturation), then lift the pen fully off. "
            + "Repeat 10 times. Each saturation hit produces one MAX estimate by linear extrapolation; the final MAX is the median.",
        _ => "",
    };

    private string ThresholdArmedHint() => _thresholdMode switch
    {
        ThresholdMode.IafFromAbove =>
            $"Armed. Press to ≥{IafController.MinPeakGf:F0} gf, then release. 10 sweeps.",
        ThresholdMode.IafFromBelow =>
            $"Armed. Lift below {IafBelowController.MaxRestingGf:F1} gf, then press to activate. 10 sweeps.",
        ThresholdMode.MaxFromBelow =>
            "Armed. Press until logical pressure reads 100%, then lift. 10 sweeps.",
        _ => "",
    };

    private string ThresholdProgressText() => _thresholdMode switch
    {
        ThresholdMode.IafFromAbove =>
            $"Captured {_iafController.Estimates.Count} / {IafController.MaxEstimates}. "
            + "Release the pen to finish the next sweep.",
        ThresholdMode.IafFromBelow =>
            $"Captured {_iafBelowController.Estimates.Count} / {IafBelowController.MaxEstimates}. "
            + $"Lift below {IafBelowController.MaxRestingGf:F1} gf and press again.",
        ThresholdMode.MaxFromBelow =>
            $"Captured {_maxController.Estimates.Count} / {MaxController.MaxEstimates}. "
            + "Lift and press again to record another.",
        _ => "",
    };

    private string ThresholdDoneText() => _thresholdMode switch
    {
        ThresholdMode.IafFromAbove =>
            $"Done. Median IAF = {_iafController.Median:F2} gf across {_iafController.Estimates.Count} sweeps.",
        ThresholdMode.IafFromBelow =>
            $"Done. Median IAF = {_iafBelowController.Median:F2} gf across {_iafBelowController.Estimates.Count} sweeps.",
        ThresholdMode.MaxFromBelow =>
            $"Done. Median MAX = {_maxController.Median:F2} gf across {_maxController.Estimates.Count} sweeps.",
        _ => "",
    };

    // ── Plot / panel ─────────────────────────────────────────────────────────

    private void InitializeThresholdPlot()
    {
        var plt = threshPlotView.Plot;
        plt.XLabel("Estimate #");
        plt.YLabel(ThresholdYLabel());
        plt.Axes.SetLimits(0, ThresholdMaxEstimates + 1, ThresholdChartYMin, DefaultThresholdYMax());
        ChartTheme.Apply(threshPlotView);
        threshPlotView.Refresh();
    }

    private void RefreshThresholdPlot()
    {
        var plt = threshPlotView.Plot;
        plt.Clear();
        plt.YLabel(ThresholdYLabel());

        var entries = CurrentThresholdEntries();

        // Main IAF/MAX dot per estimate.
        if (entries.Count > 0)
        {
            var xs = entries.Select(en => (double)en.Number).ToArray();
            var ys = entries.Select(en => en.PhysicalGf).ToArray();

            var scatter = plt.Add.Scatter(xs, ys);
            scatter.Color      = ScottPlot.Color.FromHex("#2563EB");
            scatter.LineWidth  = 0;
            scatter.MarkerSize = 8;
        }

        if (CurrentThresholdMedian() is { } med)
        {
            var line = plt.Add.HorizontalLine(med);
            line.Color       = ScottPlot.Color.FromHex("#DC2626");
            line.LineWidth   = 2;
            line.LinePattern = ScottPlot.LinePattern.Dashed;
            line.Text        = $"median = {med:F2} gf";
        }

        // Live pressure indicator: thick solid orange horizontal line tracks
        // the current scale reading so the user can gauge sweep speed.
        var live = plt.Add.HorizontalLine(_physicalPressure);
        live.Color     = LivePressureColor;
        live.LineWidth = LivePressureLineWidth;
        live.Text      = $"live = {_physicalPressure:F1} gf";

        // Y axis stretches to fit dots and the live indicator.
        double yMax = DefaultThresholdYMax();
        if (entries.Count > 0)
            yMax = Math.Max(yMax, entries.Max(en => en.PhysicalGf) * 1.2);
        yMax = Math.Max(yMax, _physicalPressure * 1.2);
        plt.Axes.SetLimits(0, ThresholdMaxEstimates + 1, ThresholdChartYMin, yMax);
        threshPlotView.Refresh();
    }

    private void UpdateThresholdData()
    {
        reading_threshold_count.Value  = $"{CurrentThresholdCount()} / {ThresholdMaxEstimates}";
        reading_threshold_median.Value = CurrentThresholdMedian() is { } med
            ? $"{med:F2} gf"
            : "—";

        string rawText     = ThresholdRawText();
        string logicalText = ThresholdLogicalText();
        var cards = CurrentThresholdEntries()
            .Select((en, i) => new ThresholdEstimateCard(
                Index:  i,
                Number: $"#{en.Number}",
                Fields: new[]
                {
                    new EstimateField("PHYS:", $"{en.PhysicalGf:F2} gf"),
                    new EstimateField("RAW:",  rawText),
                    new EstimateField("LOG%:", logicalText),
                }))
            .ToList();

        listBox_threshold_estimates.ItemsSource = null;
        listBox_threshold_estimates.ItemsSource = cards;

        section_threshold.Header = ThresholdResultsHeader();
        txt_threshold_help.Text           = ThresholdHelpText();
        UpdateThresholdArmedIndicator();
    }

    private bool CurrentThresholdArmed() => _thresholdMode switch
    {
        ThresholdMode.IafFromAbove => _iafController.Armed,
        ThresholdMode.IafFromBelow => _iafBelowController.Armed,
        ThresholdMode.MaxFromBelow => _maxController.Armed,
        _ => false,
    };

    /// <summary>(text shown when armed, text shown when not armed)</summary>
    private (string Armed, string NotArmed) ThresholdArmedLabels() => _thresholdMode switch
    {
        ThresholdMode.IafFromAbove => (
            "Armed — release to record",
            $"Not armed (press to ≥ {IafController.MinPeakGf:F0} gf)"),
        ThresholdMode.IafFromBelow => (
            "Armed",
            $"Not armed (lift to ≤ {IafBelowController.MaxRestingGf:F1} gf)"),
        ThresholdMode.MaxFromBelow => (
            "Armed — press to 100% to record",
            "Cooling down — lift the pen"),
        _ => ("", ""),
    };

    /// <summary>
    /// Refreshes the armed-status dot. The dot is green when the active
    /// controller is ready to record its next estimate, gray otherwise; the
    /// label text describes what the user needs to do next.
    /// </summary>
    private void UpdateThresholdArmedIndicator()
    {
        if (row_threshold_armed is null) return;   // pre-XAML-load guard

        row_threshold_armed.IsVisible = true;
        bool armed = CurrentThresholdArmed();
        row_threshold_armed.State = armed ? DotState.Active : DotState.Inactive;
        var (yes, no) = ThresholdArmedLabels();
        txt_threshold_armed.Text = armed ? yes : no;
    }

    // ── Controller events ────────────────────────────────────────────────────
    // Both IAF and MAX controllers point at the same shared UI update path.
    // Only the active controller is being fed, so only one ever fires.

    private void OnIafEstimateAdded(IafEstimate _)      => OnAnyThresholdEstimateAdded();
    private void OnIafBelowEstimateAdded(IafEstimate _) => OnAnyThresholdEstimateAdded();
    private void OnMaxEstimateAdded(MaxEstimate _)      => OnAnyThresholdEstimateAdded();

    private void OnAnyThresholdEstimateAdded()
    {
        RefreshThresholdPlot();
        UpdateThresholdData();

        if (CurrentThresholdIsFull())
        {
            _thresholdEnabled            = false;
            btn_threshold_enable.Content = ThresholdStartLabel();
            section_threshold.Status    = ThresholdDoneText();
        }
        else
        {
            section_threshold.Status = ThresholdProgressText();
        }
    }

    private void OnIafSweepRejected()
    {
        // Fires only while IafController is active (from-above mode).
        section_threshold.Status =
            $"Release didn't reach {IafController.MinPeakGf:F0} gf — sweep ignored. Press harder before lifting.";
    }

    private void OnIafBelowSweepRejected()
    {
        // Fires only while IafBelowController is active (from-below mode).
        section_threshold.Status =
            $"Press started without first lifting below {IafBelowController.MaxRestingGf:F1} gf — sweep ignored. "
            + "Lift the pen fully, then press again.";
    }

    // ── Click handlers ───────────────────────────────────────────────────────

    private void btn_threshold_enable_Click(object? sender, RoutedEventArgs e)
    {
        // If the active controller is already full, the next Start resets it.
        if (CurrentThresholdIsFull())
        {
            CurrentThresholdControllerClear();
            RefreshThresholdPlot();
            UpdateThresholdData();
        }

        _thresholdEnabled            = !_thresholdEnabled;
        btn_threshold_enable.Content = _thresholdEnabled ? ThresholdStopLabel() : ThresholdStartLabel();
        section_threshold.Status    = _thresholdEnabled ? ThresholdArmedHint() : "Idle.";

        // Convenience: starting Threshold detection also starts the scale (if
        // a COM port is selected and the scale isn't already reading).
        if (_thresholdEnabled) _ = StartScaleIfIdleAsync();
    }

    private void btn_threshold_clear_Click(object? sender, RoutedEventArgs e)
    {
        CurrentThresholdControllerClear();
        RefreshThresholdPlot();
        UpdateThresholdData();
        section_threshold.Status = "Cleared.";
    }

    private void btn_manual_card_delete_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not EstimateCard { DataContext: ManualRecordCard card }) return;
        if (!_recordCollection.RemoveAt(card.SourceIndex)) return;
        UpdateChart();
    }

    private void btn_sweep_card_delete_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not EstimateCard { DataContext: SweepCaptureCard card }) return;
        if (!_sweepController.RemoveAt(card.SourceIndex)) return;
        RefreshSweepPlot();
        UpdateSweepData();
    }

    private void btn_card_delete_Click(object? sender, RoutedEventArgs e)
    {
        // The EstimateCard's DataContext is the bound view-model; it carries
        // the 0-based index into the active controller's estimate list.
        if (sender is not EstimateCard { DataContext: ThresholdEstimateCard card }) return;
        if (!CurrentThresholdControllerRemoveAt(card.Index)) return;

        // Rebuilding the card list always renumbers from 1; the chart's x-axis
        // (estimate index) also re-flows because RefreshThresholdPlot reads the
        // controller fresh.
        RefreshThresholdPlot();
        UpdateThresholdData();
        section_threshold.Status =
            $"Deleted estimate — {CurrentThresholdCount()} / {ThresholdMaxEstimates}.";
    }

    private void comboBox_threshold_mode_Changed(object? sender, SelectionChangedEventArgs e)
    {
        // Switching mode stops any active capture; estimates in each sub-mode
        // persist independently.
        _thresholdMode = comboBox_threshold_mode.SelectedItem?.ToString() switch
        {
            ThresholdModeIafBelow => ThresholdMode.IafFromBelow,
            ThresholdModeMax      => ThresholdMode.MaxFromBelow,
            _                     => ThresholdMode.IafFromAbove,
        };

        _thresholdEnabled = false;

        // Guard: ComboBox.SelectedIndex set during OnOpened fires this
        // handler before the dependent UI controls (and the plot) are wired.
        if (btn_threshold_enable is null) return;

        btn_threshold_enable.Content = ThresholdStartLabel();
        section_threshold.Status    = "Idle.";

        RefreshThresholdPlot();
        UpdateThresholdData();
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
        if (plotView?.Plot is null) return;

        ChartTheme.Apply(plotView);
        ChartTheme.Apply(sweepPlotView);
        ChartTheme.Apply(threshPlotView);
        ChartTheme.Apply(monitorPenPlot,   userInputEnabled: false);
        ChartTheme.Apply(monitorScalePlot, userInputEnabled: false);

        plotView.Refresh();
        sweepPlotView.Refresh();
        threshPlotView.Refresh();
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

        // ── Top chart: always shows the pen trace. In overlay mode it also
        //    hosts the scale trace on its secondary (right) y-axis.
        var pp = monitorPenPlot.Plot;
        pp.Clear();
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

    private void check_monitor_overlay_Changed(object? sender, RoutedEventArgs e)
    {
        if (monitorPenPlot is null) return;   // pre-XAML-load guard
        _monitorOverlay = check_monitor_overlay.IsChecked == true;
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

    private void btn_monitor_clear_Click(object? sender, RoutedEventArgs e)
    {
        ResetMonitor();
        RefreshMonitorPlots();
    }

    private string BuildSweepSuggestedFileName()
    {
        var id   = BlankTo(_metadata.InventoryId, "sweep");
        var date = BlankTo(_metadata.Date, DateTime.Today.ToString("yyyy-MM-dd"));
        return $"sweep_{id}_{date}.json";
    }

    // ── Save / Load / Drag-drop ───────────────────────────────────────────────

    private static readonly JsonSerializerOptions JsonWriteOptions =
        new() { WriteIndented = true };
    private static readonly FilePickerFileType JsonFilter =
        new("JSON files") { Patterns = ["*.json"] };

    private async void btn_save_Click(object? sender, RoutedEventArgs e)
    {
        if (_recordCollection.Count == 0) return;
        var tl = TopLevel.GetTopLevel(this);
        if (tl is null) return;

        var file = await tl.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title             = "Save pressure data",
            SuggestedFileName = BuildSuggestedFileName(),
            FileTypeChoices   = [JsonFilter],
            DefaultExtension  = "json"
        });
        if (file is null) return;

        try
        {
            await using var stream = await file.OpenWriteAsync();
            await JsonSerializer.SerializeAsync(stream, BuildTestFile(), JsonWriteOptions);
            section_manual.Status = $"Saved: {file.Name}";
        }
        catch (Exception ex) { section_manual.Status = $"Save failed: {ex.Message}"; }
    }

    private async void btn_load_Click(object? sender, RoutedEventArgs e)
    {
        var tl = TopLevel.GetTopLevel(this);
        if (tl is null) return;
        var files = await tl.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Load pressure data", AllowMultiple = false, FileTypeFilter = [JsonFilter]
        });
        if (files.Count == 0) return;
        await LoadFromStorageFileAsync(files[0]);
    }

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

    private async Task LoadFromStorageFileAsync(IStorageItem item)
    {
        if (item is not IStorageFile file) return;
        try
        {
            await using var stream = await file.OpenReadAsync();
            var data = await JsonSerializer.DeserializeAsync<PressureTestFile>(stream);
            if (data is null) return;

            _recordCollection = data.ToRecordCollection();
            _metadata         = data;

            UpdateChart();
            section_manual.Status = $"Loaded {_recordCollection.Count} records from {file.Name}";
        }
        catch (Exception ex) { section_manual.Status = $"Load failed: {ex.Message}"; }
    }

    private PressureTestFile BuildTestFile() => new()
    {
        Brand       = _metadata.Brand,
        Pen         = _metadata.Pen,
        PenFamily   = _metadata.PenFamily,
        InventoryId = _metadata.InventoryId,
        Date        = _metadata.Date,
        User        = _metadata.User,
        Tablet      = _metadata.Tablet,
        Driver      = _metadata.Driver,
        Os          = _metadata.Os,
        Tags        = _metadata.Tags,
        Notes       = _metadata.Notes,
        Records     = _recordCollection.ToRecordArrays()
    };

    private string BuildSuggestedFileName()
    {
        var id   = BlankTo(_metadata.InventoryId, "data");
        var date = BlankTo(_metadata.Date, DateTime.Today.ToString("yyyy-MM-dd"));
        return $"{id}_{date}.json";
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string BlankTo(string? s, string fallback) =>
        string.IsNullOrWhiteSpace(s) ? fallback : s.Trim();

    private Task ShowMessageAsync(string message, string title)
    {
        Debug.WriteLine($"[PPP2 {title}] {message}");
        return Task.CompletedTask;
    }
}
