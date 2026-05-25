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
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Text.Json;
using WinPenKit;

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
    private string _selectedAxisRange      = AxisDefault;
    private string _selectedSweepAxisRange = AxisDefault;
    private bool   _sweepSortAscending    = true;

    // ── Sweep ─────────────────────────────────────────────────────────────────

    private readonly SweepController _sweepController = new();
    private bool     _sweepEnabled;

    private readonly List<double> _sweepRawX         = [];
    private readonly List<double> _sweepRawY         = [];
    private const int    SweepRawMaxPoints           = 600;
    private const double SweepChartMinRefreshMs      = 100;
    private DateTime     _lastSweepChartRefresh       = DateTime.MinValue;

    // ── Scale state ───────────────────────────────────────────────────────────

    private double   _physicalPressure;
    private int      _scaleReadingCount;
    private DateTime _scaleRateWindowStart = DateTime.UtcNow;

    // ── Pen rate tracking ─────────────────────────────────────────────────────

    private int      _penPacketCount;
    private DateTime _penRateWindowStart = DateTime.UtcNow;
    private DateTime _lastActiveTime     = DateTime.MinValue;

    // ── Space-pan state ───────────────────────────────────────────────────────

    private bool   _spacePanActive;
    private Point? _lastPanPoint;

    // ── Dot colours ──────────────────────────────────────────────────────────

    private static readonly ISolidColorBrush DotActive   = new SolidColorBrush(Avalonia.Media.Color.FromRgb(34,  197, 94));
    private static readonly ISolidColorBrush DotInactive = new SolidColorBrush(Avalonia.Media.Color.FromRgb(156, 163, 175));

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

        Opened  += OnOpened;
        Loaded  += OnLoaded;
        Closing += OnClosing;

        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        AddHandler(KeyUpEvent,   OnKeyUp,   RoutingStrategies.Tunnel);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent,     OnDrop);

        // PenInputSurface overlays both charts — forward wheel as zoom,
        // track pointer movement for spacebar pan, right-click to reset view.
        PenInputSurface.PointerWheelChanged += OnChartAreaWheel;
        PenInputSurface.PointerMoved        += OnChartAreaPointerMoved;
        PenInputSurface.PointerPressed      += OnChartAreaPointerPressed;

        // Reset pan if the window loses focus while space is held.
        Deactivated += (_, _) => { _spacePanActive = false; _lastPanPoint = null; };
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
            comboBox_axis_range.Items.Add(mode);
        comboBox_axis_range.SelectedIndex = 0;

        foreach (var mode in new[] { AxisDefault, AxisFull, AxisIAF, AxisIAFLarge })
            comboBox_sweep_axis_range.Items.Add(mode);
        comboBox_sweep_axis_range.SelectedIndex = 0;

        _metadata.Date = DateTime.Today.ToString("yyyy-MM-dd");
        _metadata.User = Environment.UserName.ToUpper().Trim();
        _metadata.Os   = "WINDOWS";
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            InitializePlot();
            InitializeSweepPlot();
            UpdateChart();
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
        dot_pen.Fill = DotInactive;
        if (_apis.Count == 0 || ApiCombo.SelectedIndex < 0) return;
        _penManager.Start(_apis[ApiCombo.SelectedIndex]);
        dot_pen.Fill = _penManager.IsRunning ? DotActive : DotInactive;
    }

    private void ApiCombo_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        => StartSession();

    // ── Tab switching ─────────────────────────────────────────────────────────
    // The right-panel tabs also drive which centre chart is visible:
    // Manual → plotView, Auto (Sweep) → sweepPlotView.

    private void btn_right_recording_Click(object? sender, RoutedEventArgs e)
    {
        panel_right_recording.IsVisible = true;
        panel_right_sweep.IsVisible     = false;
        btn_right_recording.Classes.Set("tab-active", true);
        btn_right_sweep.Classes.Set("tab-active", false);

        plotView.IsVisible      = true;
        sweepPlotView.IsVisible = false;
    }

    private void btn_right_sweep_Click(object? sender, RoutedEventArgs e)
    {
        panel_right_recording.IsVisible = false;
        panel_right_sweep.IsVisible     = true;
        btn_right_recording.Classes.Set("tab-active", false);
        btn_right_sweep.Classes.Set("tab-active", true);

        plotView.IsVisible      = false;
        sweepPlotView.IsVisible = true;
        RefreshSweepPlot();
        UpdateSweepData();
    }

    // ── Logging ───────────────────────────────────────────────────────────────

    private void btn_log_toggle_Click(object? sender, RoutedEventArgs e)
        => ToggleLogging();

    private void btn_open_log_folder_Click(object? sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(LogDirectory);
        Process.Start(new ProcessStartInfo(LogDirectory) { UseShellExecute = true });
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        bool textBoxFocused =
            TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is TextBox;

        // Spacebar pan — no modifier required; skip when typing in a text field.
        if (e.Key == Key.Space && !textBoxFocused)
        {
            if (!_spacePanActive) { _spacePanActive = true; _lastPanPoint = null; }
            e.Handled = true;
            return;
        }

        if (e.KeyModifiers != KeyModifiers.Control) return;

        // Don't steal Ctrl+C / Ctrl+A / Ctrl+S when the user is typing in a field.

        switch (e.Key)
        {
            case Key.R:
                btn_record_Click(null, new RoutedEventArgs());
                e.Handled = true;
                break;

            case Key.S when !textBoxFocused:
                btn_save_Click(null, new RoutedEventArgs());
                e.Handled = true;
                break;

            case Key.C when !textBoxFocused:
                btn_clear_last_Click(null, new RoutedEventArgs());
                e.Handled = true;
                break;

            case Key.A when !textBoxFocused:
                btn_clear_all_Click(null, new RoutedEventArgs());
                e.Handled = true;
                break;

            case Key.T:
                btn_scale_record_Click(null, new RoutedEventArgs());
                e.Handled = true;
                break;

            case Key.L:
            case Key.G:                 // keep both — L is the new mnemonic, G is the old one
                ToggleLogging();
                e.Handled = true;
                break;

            case Key.W:
                btn_sweep_clear_Click(null, new RoutedEventArgs());
                e.Handled = true;
                break;
        }
    }

    private void ToggleLogging()
    {
        if (_sessionLogger.IsLogging)
        {
            _sessionLogger.StopLogging();
            btn_log_toggle.Content = "Start Logging";
        }
        else
        {
            _sessionLogger.StartLogging();
            btn_log_toggle.Content = "Stop Logging";
        }
    }

    // ── Scale ─────────────────────────────────────────────────────────────────

    private async void btn_scale_record_Click(object? sender, RoutedEventArgs e)
    {
        if (_scaleManager.IsReading) { _scaleManager.Stop(); return; }
        var port = comboBox_comport.SelectedItem?.ToString();
        if (port is null) return;
        btn_scale_record.Content = "Stop";
        await _scaleManager.StartAsync(port);
        btn_scale_record.Content = "Read";
    }

    private void OnScaleReading(ScaleRecord record)
    {
        _sessionLogger.LogScaleReading(record);
        if (_sweepEnabled) _sweepController.OnScaleData(record.ReadingAsDouble);

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
    }

    // ── Pen data callback ─────────────────────────────────────────────────────

    private void OnPenDataReceived(PenReadingData d)
    {
        _sessionLogger.LogPenReading(d);
        if (_sweepEnabled) _sweepController.OnPenData(d);
        _logicalPressure = d.SmoothedPressure;
        UpdateRibbon(d);
        UpdateCards(d);
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

    private void OnKeyUp(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space)
        {
            _spacePanActive = false;
            _lastPanPoint   = null;
        }
    }

    // ── Chart wheel zoom ─────────────────────────────────────────────────────

    /// <summary>
    /// PenInputSurface overlays both charts and absorbs all pointer events.
    /// Intercept wheel events here and apply zoom directly to the active chart.
    /// Since PenInputSurface and the charts share the same grid cell they have
    /// the same coordinate origin, so cursor positions are interchangeable.
    /// </summary>
    private void OnChartAreaPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_spacePanActive) return;

        var chart = sweepPlotView.IsVisible ? sweepPlotView : plotView;
        var cur   = e.GetPosition(PenInputSurface);

        if (_lastPanPoint is { } prev)
            PanChartByPixelDelta(chart, prev, cur);

        _lastPanPoint = cur;
    }

    private void OnChartAreaPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(PenInputSurface).Properties.IsRightButtonPressed) return;

        // Right-click → reset to the currently selected axis range mode.
        if (sweepPlotView.IsVisible)
            RefreshSweepPlot();   // re-applies ApplySweepAxisRange()
        else
            UpdateChart();        // re-applies ApplyAxisRange()

        e.Handled = true;
    }

    private static void PanChartByPixelDelta(
        ScottPlot.Avalonia.AvaPlot chart, Point from, Point to)
    {
        var plt = chart.Plot;

        // Convert the two pixel positions to data coordinates, then shift the
        // axis limits by the negative of that delta (grab-and-drag semantics).
        var c0 = plt.GetCoordinates((float)from.X, (float)from.Y);
        var c1 = plt.GetCoordinates((float)to.X,   (float)to.Y);

        double dX = -(c1.X - c0.X);
        double dY = -(c1.Y - c0.Y);

        plt.Axes.SetLimits(
            plt.Axes.Bottom.Min + dX,
            plt.Axes.Bottom.Max + dX,
            plt.Axes.Left.Min   + dY,
            plt.Axes.Left.Max   + dY);

        chart.Refresh();
    }

    private void OnChartAreaWheel(object? sender, PointerWheelEventArgs e)
    {
        var chart = sweepPlotView.IsVisible ? sweepPlotView : plotView;
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
        plt.FigureBackground.Color = ScottPlot.Color.FromHex("#FFFFFF");
        plt.DataBackground.Color   = ScottPlot.Color.FromHex("#FAFAFA");
        plotView.UserInputProcessor.IsEnabled = true;
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

        listBox_records.ItemsSource = null;
        listBox_records.ItemsSource = _recordCollection.Items;
        int n = _recordCollection.Count;
        txt_record_count.Text = $"{n} record{(n == 1 ? "" : "s")}";
    }

    private void ApplyAxisRange(double[] dataX, double[] dataY)
    {
        var plt = plotView.Plot;
        switch (_selectedAxisRange)
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

    private void btn_clear_last_Click(object? sender, RoutedEventArgs e)
    { _recordCollection.RemoveLast(); UpdateChart(); }

    private void btn_clear_all_Click(object? sender, RoutedEventArgs e)
    { _recordCollection.Clear(); UpdateChart(); }

    private void comboBox_axis_range_Changed(object? sender, SelectionChangedEventArgs e)
    {
        _selectedAxisRange = comboBox_axis_range.SelectedItem?.ToString() ?? AxisDefault;
        if (plotView?.Plot is not null) UpdateChart();
    }

    private void comboBox_sweep_axis_range_Changed(object? sender, SelectionChangedEventArgs e)
    {
        _selectedSweepAxisRange = comboBox_sweep_axis_range.SelectedItem?.ToString() ?? AxisDefault;
        if (sweepPlotView?.Plot is not null) RefreshSweepPlot();
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
        plt.FigureBackground.Color = ScottPlot.Color.FromHex("#FFFFFF");
        plt.DataBackground.Color   = ScottPlot.Color.FromHex("#FAFAFA");
        sweepPlotView.UserInputProcessor.IsEnabled = true;
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

        // Stable captures (blue, larger dots, sorted by physical pressure)
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

        switch (_selectedSweepAxisRange)
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
        var ordered = _sweepSortAscending
            ? _sweepController.Captures.OrderBy(c => c.PhysicalGf)
            : (IEnumerable<SweepCapture>)_sweepController.Captures.OrderByDescending(c => c.PhysicalGf);

        var rows = ordered
            .Select((c, i) => new SweepCaptureRow(i + 1, c))
            .ToList();

        listBox_sweep_captures.ItemsSource = null;
        listBox_sweep_captures.ItemsSource = rows;
        reading_sweep_unique.Value = _sweepController.Captures.Count.ToString();
        reading_sweep_total.Value  = _sweepController.Captures.Sum(c => c.Count).ToString();
    }

    private void btn_sweep_sort_Click(object? sender, RoutedEventArgs e)
    {
        _sweepSortAscending = !_sweepSortAscending;
        btn_sweep_sort.Content = _sweepSortAscending ? "↑ Force" : "↓ Force";
        UpdateSweepData();
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
            txt_file_status.Text = $"Saved: {file.Name}";
        }
        catch (Exception ex) { txt_file_status.Text = $"Save failed: {ex.Message}"; }
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
            txt_file_status.Text = $"Loaded {_recordCollection.Count} records from {file.Name}";
        }
        catch (Exception ex) { txt_file_status.Text = $"Load failed: {ex.Message}"; }
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
