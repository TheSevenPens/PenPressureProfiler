using Avalonia;
using Avalonia.Controls;
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
    private const string AxisDefault = "Default";
    private const string AxisFull    = "Full";
    private const string Axis100Pct  = "100%";

    private PressureRecordCollection _recordCollection = new();
    private double                   _logicalPressure;
    private string                   _selectedAxisRange = AxisDefault;

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
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent,     OnDrop);
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

        foreach (var mode in new[] { AxisDefault, AxisFull, Axis100Pct })
            comboBox_axis_range.Items.Add(mode);
        comboBox_axis_range.SelectedIndex = 0;

        field_date.Text = DateTime.Today.ToString("yyyy-MM-dd");
        field_user.Text = Environment.UserName.ToUpper().Trim();
        field_os.Text   = "WINDOWS";
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

    private void btn_center_pressure_Click(object? sender, RoutedEventArgs e)
    {
        plotView.IsVisible      = true;
        sweepPlotView.IsVisible = false;
        btn_center_pressure.Classes.Set("tab-active", true);
        btn_center_sweep.Classes.Set("tab-active", false);
    }

    private void btn_center_sweep_Click(object? sender, RoutedEventArgs e)
    {
        plotView.IsVisible      = false;
        sweepPlotView.IsVisible = true;
        btn_center_pressure.Classes.Set("tab-active", false);
        btn_center_sweep.Classes.Set("tab-active", true);
        RefreshSweepPlot();
    }

    private void btn_right_recording_Click(object? sender, RoutedEventArgs e)
    {
        panel_right_recording.IsVisible = true;
        panel_right_sweep.IsVisible     = false;
        btn_right_recording.Classes.Set("tab-active", true);
        btn_right_sweep.Classes.Set("tab-active", false);
    }

    private void btn_right_sweep_Click(object? sender, RoutedEventArgs e)
    {
        panel_right_recording.IsVisible = false;
        panel_right_sweep.IsVisible     = true;
        btn_right_recording.Classes.Set("tab-active", false);
        btn_right_sweep.Classes.Set("tab-active", true);
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
        if (e.Key == Key.L && e.KeyModifiers == KeyModifiers.Control)
        { ToggleLogging(); e.Handled = true; }
    }

    private void ToggleLogging()
    {
        if (_sessionLogger.IsLogging)
        {
            _sessionLogger.StopLogging();
            btn_log_toggle.Content = "Start Logging";
            txt_log_status.Text    = "Not logging";
        }
        else
        {
            _sessionLogger.StartLogging();
            btn_log_toggle.Content = "Stop Logging";
            txt_log_status.Text    = $"Logging → {LogDirectory}";
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
        btn_scale_record.Content = "Record";
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

        RibbonRawLabel.Text    = $"Raw: {d.RawPressure}";
        RibbonNormLabel.Text   = $"Norm: {d.NormalizedPressure * 100.0:F1}%";
        RibbonSmoothLabel.Text = $"Smooth: {d.SmoothedPressure  * 100.0:F1}%";
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

        reading_azimuth.Value  = $"{d.Azimuth:F1}°";
        reading_altitude.Value = $"{d.Altitude:F1}°";
        reading_tiltx.Value    = $"{d.TiltX:F1}°";
        reading_tilty.Value    = $"{d.TiltY:F1}°";

        check_tip.IsChecked     = d.TipDown;
        check_barrel1.IsChecked = d.Barrel1Down;
        check_barrel2.IsChecked = d.Barrel2Down;
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
        plotView.UserInputProcessor.IsEnabled = false;
        UpdateChartTitle();
        plotView.Refresh();
    }

    private void UpdateChartTitle()
    {
        var brand = BlankTo(field_brand?.Text,       "BRAND");
        var id    = BlankTo(field_inventoryid?.Text, "ID");
        var date  = BlankTo(field_date?.Text,        "DATE");
        plotView?.Plot.Title($"{brand}/{id}/{date}");
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
        UpdateChartTitle();
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
                plt.Axes.SetLimits(0,
                    dataX.Length > 0 ? Math.Max(dataX.Max() * 1.1, PlotAxisLimit) : PlotAxisLimit,
                    0, PlotPressureLimit);
                break;
            case Axis100Pct:
                plt.Axes.SetLimits(0, PlotAxisLimit, 90, 100);
                break;
            default:
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

    private void OnTitleFieldChanged(object? sender, TextChangedEventArgs e)
    {
        if (plotView?.Plot is not null) { UpdateChartTitle(); plotView.Refresh(); }
    }

    // ── Sweep chart ───────────────────────────────────────────────────────────

    private void InitializeSweepPlot()
    {
        var plt = sweepPlotView.Plot;
        plt.XLabel("Physical pressure (gf)");
        plt.YLabel("Logical pressure (%)");
        plt.Title("Sweep");
        plt.Axes.SetLimits(0, PlotAxisLimit, 0, PlotPressureLimit);
        plt.FigureBackground.Color = ScottPlot.Color.FromHex("#FFFFFF");
        plt.DataBackground.Color   = ScottPlot.Color.FromHex("#FAFAFA");
        sweepPlotView.UserInputProcessor.IsEnabled = false;
        sweepPlotView.Refresh();
    }

    private void RefreshSweepPlot()
    {
        var plt = sweepPlotView.Plot;
        plt.Clear();

        // Raw pairs (light grey, small dots, ~10 fps throttled)
        if (_sweepRawX.Count > 0)
        {
            var raw = plt.Add.Scatter(_sweepRawX.ToArray(), _sweepRawY.ToArray());
            raw.Color      = ScottPlot.Color.FromHex("#CCCCCC");
            raw.LineWidth  = 0;
            raw.MarkerSize = 3;
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

        plt.Axes.SetLimits(0, PlotAxisLimit, 0, PlotPressureLimit);
        sweepPlotView.Refresh();
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
        btn_sweep_enable.Content = _sweepEnabled ? "Disable Sweep" : "Enable Sweep";
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

    private void UpdateSweepData()
    {
        var rows = _sweepController.Captures
            .OrderBy(c => c.PhysicalGf)
            .Select((c, i) => new SweepCaptureRow(i + 1, c))
            .ToList();

        listBox_sweep_captures.ItemsSource = null;
        listBox_sweep_captures.ItemsSource = rows;
        reading_sweep_captures.Value = _sweepController.Captures.Count.ToString();
    }

    private string BuildSweepSuggestedFileName()
    {
        var id   = BlankTo(field_inventoryid?.Text, "sweep");
        var date = BlankTo(field_date?.Text, DateTime.Today.ToString("yyyy-MM-dd"));
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

            _recordCollection      = data.ToRecordCollection();
            field_brand.Text       = data.Brand;
            field_pen.Text         = data.Pen;
            field_penfamily.Text   = data.PenFamily;
            field_inventoryid.Text = data.InventoryId;
            field_date.Text        = data.Date;
            field_user.Text        = data.User;
            field_tablet.Text      = data.Tablet;
            field_driver.Text      = data.Driver;
            field_os.Text          = data.Os;
            field_tags.Text        = data.Tags;
            textBox_notes.Text     = data.Notes;

            UpdateChart();
            txt_file_status.Text = $"Loaded {_recordCollection.Count} records from {file.Name}";
        }
        catch (Exception ex) { txt_file_status.Text = $"Load failed: {ex.Message}"; }
    }

    private PressureTestFile BuildTestFile() => new()
    {
        Brand       = field_brand.Text?.Trim()       ?? "",
        Pen         = field_pen.Text?.Trim()          ?? "",
        PenFamily   = field_penfamily.Text?.Trim()    ?? "",
        InventoryId = field_inventoryid.Text?.Trim()  ?? "",
        Date        = field_date.Text?.Trim()         ?? "",
        User        = field_user.Text?.Trim()         ?? "",
        Tablet      = field_tablet.Text?.Trim()       ?? "",
        Driver      = field_driver.Text?.Trim()       ?? "",
        Os          = field_os.Text?.Trim()           ?? "",
        Tags        = field_tags.Text?.Trim()         ?? "",
        Notes       = textBox_notes.Text?.Trim()      ?? "",
        Records     = _recordCollection.ToRecordArrays()
    };

    private string BuildSuggestedFileName()
    {
        var id   = BlankTo(field_inventoryid?.Text, "data");
        var date = BlankTo(field_date?.Text, DateTime.Today.ToString("yyyy-MM-dd"));
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
