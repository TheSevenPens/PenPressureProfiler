using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using MsBox.Avalonia;
using ScottPlot;
using ScottPlot.Avalonia;
using System.IO;
using System.IO.Ports;
using System.Text.Json;

namespace PenPressureProfiler;

public partial class MainWindow : Window
{
    private const int PlotAxisLimit     = 1000;
    private const int PlotPressureLimit = 100;

    private static readonly ISolidColorBrush StatusActiveColor   = new SolidColorBrush(Avalonia.Media.Color.FromRgb(34, 197, 94));
    private static readonly ISolidColorBrush StatusInactiveColor = new SolidColorBrush(Avalonia.Media.Color.FromRgb(156, 163, 175));

    // ── State ─────────────────────────────────────────────────────────────────

    private readonly PenSessionManager   penManager;
    private readonly ScaleSessionManager scaleManager;
    private readonly SessionLogger        sessionLogger;
    private PressureRecordCollection recordCollection = new();

    private static readonly string LogDirectory = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "PenPressureProfiler", "Logs");

    private double physicalPressure;
    private double logicalPressure;

    private int      _scaleReadingCount;
    private DateTime _scaleRateWindowStart = DateTime.UtcNow;

    private int      _penPacketCount;
    private DateTime _penRateWindowStart = DateTime.UtcNow;

    private string? currentLoadedFilePath;
    private string  selectedAxisRangeMode = "Default";
    private ScottPlot.Plottables.Scatter? highlightedPointSeries;

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

        penManager    = new PenSessionManager(OnPenDataReceived, ShowMessageAsync);
        scaleManager  = new ScaleSessionManager(OnScaleReading, ShowMessageAsync);
        sessionLogger = new SessionLogger(LogDirectory);

        this.Loaded  += MainWindow_Loaded;
        this.Closing += MainWindow_Closing;

        AddHandler(KeyDownEvent, Window_KeyDown, RoutingStrategies.Tunnel);
        AddHandler(DragDrop.DragOverEvent, Window_DragOver);
        AddHandler(DragDrop.DropEvent, Window_Drop);

        InitializeUI();
    }

    private void InitializeUI()
    {
        field_date.Text = DateTime.Today.ToString("yyyy-MM-dd");
        field_user.Text = Environment.UserName.ToUpper().Trim();
        field_os.Text   = "WINDOWS";

        foreach (var port in SerialPort.GetPortNames())
            comboBoxcomport.Items.Add(port);

        if (comboBoxcomport.Items.Count > 0)
            comboBoxcomport.SelectedIndex = comboBoxcomport.Items.Count - 1;
    }

    private void InitializePlot()
    {
        var plt = plotView1.Plot;
        plt.XLabel("Physical pressure (gf)");
        plt.YLabel("Logical pressure (%)");
        plt.Title("Pressure response");
        plt.Axes.SetLimits(0, PlotAxisLimit, 0, PlotPressureLimit);
        plt.FigureBackground.Color = ScottPlot.Color.FromHex("#FFFFFF");
        plt.DataBackground.Color   = ScottPlot.Color.FromHex("#FAFAFA");
        plotView1.UserInputProcessor.IsEnabled = false;
        plotView1.Refresh();
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private void MainWindow_Loaded(object? sender, RoutedEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            InitializePlot();
            UpdateChartTitle();
            plotView1.Refresh();
        }, DispatcherPriority.Background);

        penManager.Start();
        dot_pen.Fill = penManager.IsRunning ? StatusActiveColor : StatusInactiveColor;
    }

    private void MainWindow_Closing(object? sender, WindowClosingEventArgs e)
    {
        penManager.Dispose();
        scaleManager.Dispose();
        sessionLogger.Dispose();
    }

    // ── Pen session callbacks ─────────────────────────────────────────────────

    private void OnPenDataReceived(PenReadingData d)
    {
        System.Diagnostics.Debug.Assert(
            Dispatcher.UIThread.CheckAccess(),
            $"{nameof(OnPenDataReceived)} must be called on the UI thread.");

        logicalPressure = d.SmoothedPressure;

        checkBox_tipdown.IsChecked          = d.TipDown;
        checkBox_lowerbuttondown.IsChecked  = d.Barrel1Down;
        checkBox_upperbuttondown.IsChecked  = d.Barrel2Down;

        reading_pressure_raw.Value    = d.RawPressure.ToString();
        reading_pressure_norm.Value   = $"{d.NormalizedPressure * 100.0:00.000}%";
        reading_pressure_smooth.Value = $"{d.SmoothedPressure * 100.0:00.00}%";
        reading_azimuth.Value         = $"{d.Azimuth:F0}°";
        reading_altitude.Value        = $"{d.Altitude:F0}°";
        reading_tiltx.Value           = $"{d.TiltX:00.000}°";
        reading_tilty.Value           = $"{d.TiltY:00.000}°";

        pressureBar.Value = d.SmoothedPressure * 100.0;

        sessionLogger.LogPenReading(d);

        _penPacketCount += d.PacketCount;
        double penElapsed = (DateTime.UtcNow - _penRateWindowStart).TotalSeconds;
        if (penElapsed >= 1.0)
        {
            reading_pen_rate.Value = $"{_penPacketCount / penElapsed:F0} /s";
            _penPacketCount = 0;
            _penRateWindowStart = DateTime.UtcNow;
        }
    }

    // ── Scale session callbacks ───────────────────────────────────────────────

    private void OnScaleReading(string strForce)
    {
        physicalPressure = double.TryParse(strForce, out double v) ? v : physicalPressure;
        reading_force.Value = $"{strForce} gf";
        sessionLogger.LogScaleReading(strForce);

        _scaleReadingCount++;
        double elapsed = (DateTime.UtcNow - _scaleRateWindowStart).TotalSeconds;
        if (elapsed >= 1.0)
        {
            reading_scale_rate.Value = $"{_scaleReadingCount / elapsed:F1} /s";
            _scaleReadingCount = 0;
            _scaleRateWindowStart = DateTime.UtcNow;
        }
    }

    // ── Scale session buttons ─────────────────────────────────────────────────

    private async void button_scale_toggle_Click(object? sender, RoutedEventArgs e)
    {
        if (scaleManager.IsReading)
        {
            scaleManager.Stop();
            return;
        }

        string? portName = GetSelectedComPortName();
        if (portName is null)
        {
            await ShowMessageAsync("No COM port selected.", "COM Port Error");
            return;
        }

        button_scale_toggle.Content = "■  Stop";
        dot_scale.Fill = StatusActiveColor;
        await scaleManager.StartAsync(portName);
        dot_scale.Fill = StatusInactiveColor;
        button_scale_toggle.Content = "▶  Start";
        reading_scale_rate.Value = "—";
        _scaleReadingCount = 0;
        _scaleRateWindowStart = DateTime.UtcNow;
    }

    private void button_logging_toggle_Click(object? sender, RoutedEventArgs e)
    {
        if (sessionLogger.IsLogging)
        {
            sessionLogger.StopLogging();
            button_logging_toggle.Content = "▶  Start Logging";
            dot_logging.Fill = StatusInactiveColor;
        }
        else
        {
            sessionLogger.StartLogging();
            button_logging_toggle.Content = "■  Stop Logging";
            dot_logging.Fill = StatusActiveColor;
        }
    }

    private void button_open_logs_folder_Click(object? sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(LogDirectory);
        System.Diagnostics.Process.Start("explorer.exe", LogDirectory);
    }

    private string? GetSelectedComPortName() =>
        comboBoxcomport.SelectedItem?.ToString()?.ToUpper();

    // ── Chart ─────────────────────────────────────────────────────────────────

    public void UpdateChartTitle()
    {
        if (field_brand is null || field_inventoryid is null || field_date is null || plotView1 is null)
            return;

        var brand = field_brand.Text.Trim().IfEmpty("BRAND");
        var id    = field_inventoryid.Text.Trim().IfEmpty("ID");
        var date  = field_date.Text.Trim().IfEmpty("YYYY-MM-DD");

        string chartTitle = $"{brand}/{id}/{date}";
        plotView1.Plot.Title(chartTitle);
        plotView1.Refresh();

        // Also reflect in the window title bar for multi-instance identification.
        Title = $"PenPressureProfiler — {chartTitle}";
    }

    private void OnChartTitleFieldChanged(object? sender, Avalonia.Controls.TextChangedEventArgs e) =>
        UpdateChartTitle();

    public void UpdateData()
    {
        label_recordcount.Text = recordCollection.Count.ToString();

        dataGrid_records.ItemsSource = null;
        dataGrid_records.ItemsSource = recordCollection.Items;

        var plt = plotView1.Plot;
        plt.Clear();
        highlightedPointSeries = null;

        var dataX = recordCollection.Items.Select(r => r.PhysicalPressure).ToArray();
        var dataY = recordCollection.Items.Select(r => r.LogicalPressure * 100).ToArray();

        if (dataX.Length > 0)
        {
            var line = plt.Add.Scatter(dataX, dataY);
            line.Color      = ScottPlot.Colors.Black;
            line.LineWidth  = 2;
            line.MarkerSize = 6;

            highlightedPointSeries = plt.Add.Scatter(Array.Empty<double>(), Array.Empty<double>());
            highlightedPointSeries.Color      = ScottPlot.Colors.Red;
            highlightedPointSeries.LineWidth  = 0;
            highlightedPointSeries.MarkerSize = 9;
        }

        ApplyAxisRange(dataX, dataY);
        plotView1.Refresh();
    }

    private void ApplyAxisRange(double[] dataX, double[] dataY)
    {
        var plt = plotView1.Plot;

        switch (selectedAxisRangeMode)
        {
            case "Default":
                plt.Axes.SetLimits(0, PlotAxisLimit, 0, PlotPressureLimit);
                break;

            case "Full":
                double xMax = dataX.Length > 0 ? Math.Max(dataX.Max() * 1.1, PlotAxisLimit) : PlotAxisLimit;
                plt.Axes.SetLimits(0, xMax, 0, PlotPressureLimit);
                break;

            case "IAF":
                double iafXMax = dataX.Length > 0 ? dataX.Min() + 2 : 2;
                plt.Axes.SetLimits(0, iafXMax, 0, 5);
                break;

            case "Max":
                if (dataY.Length > 0)
                {
                    var matchingX = dataX
                        .Where((x, i) => i < dataY.Length && dataY[i] >= 95 && dataY[i] <= 100)
                        .ToList();

                    if (matchingX.Count > 0)
                        plt.Axes.SetLimits(Math.Max(0, matchingX.Min() - 0.5), matchingX.Max() + 0.5, 95, 100);
                    else
                        plt.Axes.SetLimits(0, PlotAxisLimit, 95, 100);
                }
                else
                {
                    plt.Axes.SetLimits(0, PlotAxisLimit, 95, 100);
                }
                break;
        }
    }

    private void comboBox_axisRange_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (comboBox_axisRange?.SelectedItem is not ComboBoxItem selectedItem) return;
        selectedAxisRangeMode = selectedItem.Content?.ToString() ?? "Default";
        UpdateData();
    }

    private void dataGrid_records_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (dataGrid_records.SelectedItem is not PressureRecord selected || highlightedPointSeries is null)
            return;

        var plt = plotView1.Plot;
        plt.Remove(highlightedPointSeries);

        highlightedPointSeries = plt.Add.Scatter(
            new[] { selected.PhysicalPressure },
            new[] { selected.LogicalPressure * 100 });
        highlightedPointSeries.Color      = ScottPlot.Colors.Red;
        highlightedPointSeries.LineWidth  = 0;
        highlightedPointSeries.MarkerSize = 9;

        plotView1.Refresh();
    }

    // ── Data recording ────────────────────────────────────────────────────────

    private void button_record_Click(object? sender, RoutedEventArgs e)
    {
        recordCollection.Add(physicalPressure, logicalPressure);
        UpdateData();
    }

    private void button_clearlast_Click(object? sender, RoutedEventArgs e)
    {
        if (recordCollection.Count < 1) return;
        recordCollection.ClearLast();
        UpdateData();
    }

    private void button_clearlog_Click(object? sender, RoutedEventArgs e)
    {
        recordCollection.Clear();
        UpdateData();
    }

    private void button_load_sample_data_Click(object? sender, RoutedEventArgs e)
    {
        recordCollection.Add(10,  0.01);
        recordCollection.Add(100, 0.40);
        recordCollection.Add(150, 0.50);
        recordCollection.Add(400, 0.85);
        recordCollection.Add(500, 1.00);
        UpdateData();
    }

    // ── JSON / file ───────────────────────────────────────────────────────────

    private PressureTestFile BuildPressureTestFile() => new()
    {
        Brand       = field_brand.Text.Trim().ToUpper(),
        Pen         = field_pen.Text.Trim().ToUpper(),
        PenFamily   = field_penfamily.Text.Trim().ToUpper(),
        InventoryId = field_inventoryid.Text.Trim().ToUpper(),
        Date        = field_date.Text.Trim().ToUpper(),
        User        = field_user.Text.Trim().ToUpper(),
        Tablet      = field_tablet.Text.Trim().ToUpper(),
        Driver      = field_driver.Text.Trim().ToUpper(),
        Os          = field_os.Text.Trim().ToUpper(),
        Tags        = field_tags.Text.Trim(),
        Notes       = textBox_notes.Text?.Trim() ?? "",
        Records     = recordCollection.ToRecordArrays()
    };

    private static readonly JsonSerializerOptions JsonWriteOptions = new() { WriteIndented = true };

    private string CreateJSONContent() =>
        JsonSerializer.Serialize(BuildPressureTestFile(), JsonWriteOptions);

    private async void button_copytext_Click(object? sender, RoutedEventArgs e)
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is not null)
            await clipboard.SetTextAsync(CreateJSONContent());
        await ShowMessageAsync("JSON copied to clipboard", "Success");
    }

    private async void button_export_Click(object? sender, RoutedEventArgs e)
    {
        string datestring  = field_date.Text.Trim().ToUpper();
        string inventoryid = field_inventoryid.Text.Trim().ToUpper();
        string filePath    = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            $"{inventoryid}_{datestring}.json");

        try
        {
            System.IO.File.WriteAllText(filePath, CreateJSONContent());
            await ShowMessageAsync($"File saved: {filePath}", "Export Successful");
        }
        catch (System.IO.IOException ex)       { await ShowMessageAsync($"IO Error: {ex.Message}", "Export Error"); }
        catch (UnauthorizedAccessException ex) { await ShowMessageAsync($"Access Denied: {ex.Message}", "Export Error"); }
        catch (Exception ex)                   { await ShowMessageAsync($"Error: {ex.Message}", "Export Error"); }
    }

    private async void button_save_Click(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(currentLoadedFilePath))
        {
            await ShowMessageAsync("No file loaded. Use Export JSON to save to a new file.", "Save Error");
            return;
        }

        try
        {
            System.IO.File.WriteAllText(currentLoadedFilePath, CreateJSONContent());
            await ShowMessageAsync($"File saved: {currentLoadedFilePath}", "Save Successful");
        }
        catch (Exception ex) { await ShowMessageAsync($"Error saving file: {ex.Message}", "Save Error"); }
    }

    private async Task LoadJSONFile(string filePath)
    {
        PressureTestFile data;
        try
        {
            string json = System.IO.File.ReadAllText(filePath);
            data = JsonSerializer.Deserialize<PressureTestFile>(json)
                   ?? throw new System.Text.Json.JsonException("Deserialised to null.");
        }
        catch (System.Text.Json.JsonException ex)
        {
            await ShowMessageAsync($"File is not valid JSON: {ex.Message}", "Load Error");
            return;
        }

        if (data.Records.Count == 0)
        {
            await ShowMessageAsync("No valid records found in JSON file", "Load Error");
            return;
        }

        recordCollection = data.ToRecordCollection();

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

        currentLoadedFilePath = filePath;
        button_save.IsEnabled = true;

        UpdateChartTitle();
        UpdateData();
        await ShowMessageAsync($"Loaded {recordCollection.Count} records from {System.IO.Path.GetFileName(filePath)}", "Load Success");
    }

    // ── Input handling ────────────────────────────────────────────────────────

    private void Window_KeyDown(object? sender, KeyEventArgs e)
    {
        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control)) return;

        switch (e.Key)
        {
            case Key.R: button_record_Click(null, new RoutedEventArgs());           e.Handled = true; break;
            case Key.L: button_load_sample_data_Click(null, new RoutedEventArgs()); e.Handled = true; break;
            case Key.C: button_clearlast_Click(null, new RoutedEventArgs());        e.Handled = true; break;
            case Key.A: button_clearlog_Click(null, new RoutedEventArgs());         e.Handled = true; break;
            case Key.S: button_save_Click(null, new RoutedEventArgs());             e.Handled = true; break;
            case Key.T: scaleManager.Stop();                                        e.Handled = true; break;
        }
    }

    private void Window_DragOver(object? sender, DragEventArgs e)
    {
        var hasJson = e.Data.GetFiles()
            ?.Any(f => f.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) ?? false;

        e.DragEffects = hasJson ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void Window_Drop(object? sender, DragEventArgs e)
    {
        var jsonFile = e.Data.GetFiles()
            ?.FirstOrDefault(f => f.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase));

        if (jsonFile?.Path?.LocalPath is not string jsonPath) return;

        try   { await LoadJSONFile(jsonPath); }
        catch (Exception ex) { await ShowMessageAsync($"Error loading JSON file: {ex.Message}", "Load Error"); }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task ShowMessageAsync(string message, string title)
    {
        var box = MessageBoxManager.GetMessageBoxStandard(title, message);
        await box.ShowWindowDialogAsync(this);
    }
}

internal static class StringExtensions
{
    /// <summary>Returns <paramref name="fallback"/> if the string is null or empty.</summary>
    public static string IfEmpty(this string? s, string fallback) =>
        string.IsNullOrEmpty(s) ? fallback : s;
}
