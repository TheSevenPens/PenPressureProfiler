using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using MsBox.Avalonia;
using ScottPlot;
using ScottPlot.Avalonia;
using System.IO.Ports;
using System.Text.Json;

namespace PenPressureProfiler;

public partial class MainWindow : Window
{
    private const int PlotFontSize     = 14;
    private const int PlotAxisLimit    = 1000;
    private const int PlotPressureLimit = 100;

    // ── State ─────────────────────────────────────────────────────────────────

    private readonly PenSessionManager   penManager;
    private readonly ScaleSessionManager scaleManager;
    private PressureRecordCollection recordCollection = new();

    private double physicalPressure;
    private double logicalPressure;   // smoothed value from pen manager

    private string? currentLoadedFilePath;
    private string  selectedAxisRangeMode = "Default";
    private ScottPlot.Plottables.Scatter? highlightedPointSeries;

    // ── Construction ─────────────────────────────────────────────────────────

    public MainWindow()
    {
        InitializeComponent();

        penManager   = new PenSessionManager(OnPenDataReceived, ShowMessageAsync);
        scaleManager = new ScaleSessionManager(OnScaleReading, ShowMessageAsync);

        this.Loaded  += MainWindow_Loaded;
        this.Closing += MainWindow_Closing;

        AddHandler(KeyDownEvent, Window_KeyDown, RoutingStrategies.Tunnel);
        AddHandler(DragDrop.DragOverEvent, Window_DragOver);
        AddHandler(DragDrop.DropEvent, Window_Drop);

        InitializeUI();
    }

    private void InitializeUI()
    {
        textBox_date.Text = DateTime.Today.ToString("yyyy-MM-dd");
        textBox_User.Text = Environment.UserName.ToUpper().Trim();

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
    }

    private void MainWindow_Closing(object? sender, WindowClosingEventArgs e)
    {
        penManager.Dispose();
        scaleManager.Dispose();
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

        label_pressure_raw.Text         = d.RawPressure.ToString();
        label_normalized_pressure.Text  = $"{d.NormalizedPressure * 100.0:00.000}%";
        label_normalizedpressure_ma.Text = $"{d.SmoothedPressure * 100.0:00.00}%";
        label_or_azimuth.Text           = $"{d.Azimuth:F0}°";
        label_or_altitude.Text          = $"{d.Altitude:F0}°";
        label_tiltx.Text                = $"{d.TiltX:00.000}°";
        label_tilty.Text                = $"{d.TiltY:00.000}°";
    }

    // ── Scale session callbacks ───────────────────────────────────────────────

    private void OnScaleReading(string strForce)
    {
        physicalPressure = double.TryParse(strForce, out double v) ? v : physicalPressure;
        label_force.Text = $"{strForce} gf";
    }

    // ── Scale session buttons ─────────────────────────────────────────────────

    private async void button_start_Click(object? sender, RoutedEventArgs e)
    {
        if (scaleManager.IsReading) return;

        string? portName = GetSelectedComPortName();
        if (portName is null)
        {
            await ShowMessageAsync("No COM port selected.", "COM Port Error");
            return;
        }

        await scaleManager.StartAsync(portName);
    }

    private void button_stop_Click(object? sender, RoutedEventArgs e) =>
        scaleManager.Stop();

    private string? GetSelectedComPortName() =>
        comboBoxcomport.SelectedItem?.ToString()?.ToUpper();

    // ── Chart ─────────────────────────────────────────────────────────────────

    public void UpdateChartTitle()
    {
        if (textBox_brand is null || textBox_inventoryid is null || textBox_date is null || plotView1 is null)
            return;

        var brand = textBox_brand.Text?.Trim()       ?? "BRAND";
        var id    = textBox_inventoryid.Text?.Trim()  ?? "ID";
        var date  = textBox_date.Text?.Trim()         ?? "YYYY-MM-DD";
        plotView1.Plot.Title($"{brand}/{id}/{date}");
        plotView1.Refresh();
    }

    private void OnChartTitleFieldChanged(object? sender, TextChangedEventArgs e) =>
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
        Brand       = textBox_brand.Text?.Trim().ToUpper()       ?? "",
        Pen         = textBox_Pen.Text?.Trim().ToUpper()         ?? "",
        PenFamily   = textBox_penfamily.Text?.Trim().ToUpper()   ?? "",
        InventoryId = textBox_inventoryid.Text?.Trim().ToUpper() ?? "",
        Date        = textBox_date.Text?.Trim().ToUpper()        ?? "",
        User        = textBox_User.Text?.Trim().ToUpper()        ?? "",
        Tablet      = textBox_Tablet.Text?.Trim().ToUpper()      ?? "",
        Driver      = textBox_driver.Text?.Trim().ToUpper()      ?? "",
        Os          = textBox_OS.Text?.Trim().ToUpper()          ?? "",
        Tags        = textBox_tags.Text?.Trim()                  ?? "",
        Notes       = textBox_notes.Text?.Trim()                 ?? "",
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
        string datestring   = textBox_date.Text?.Trim().ToUpper()        ?? "";
        string inventoryid  = textBox_inventoryid.Text?.Trim().ToUpper() ?? "";
        string filePath     = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            $"{inventoryid}_{datestring}.json");

        try
        {
            System.IO.File.WriteAllText(filePath, CreateJSONContent());
            await ShowMessageAsync($"File saved: {filePath}", "Export Successful");
        }
        catch (System.IO.IOException ex)  { await ShowMessageAsync($"IO Error: {ex.Message}", "Export Error"); }
        catch (UnauthorizedAccessException ex) { await ShowMessageAsync($"Access Denied: {ex.Message}", "Export Error"); }
        catch (Exception ex) { await ShowMessageAsync($"Error: {ex.Message}", "Export Error"); }
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

        textBox_brand.Text       = data.Brand;
        textBox_Pen.Text         = data.Pen;
        textBox_penfamily.Text   = data.PenFamily;
        textBox_inventoryid.Text = data.InventoryId;
        textBox_date.Text        = data.Date;
        textBox_User.Text        = data.User;
        textBox_Tablet.Text      = data.Tablet;
        textBox_driver.Text      = data.Driver;
        textBox_OS.Text          = data.Os;
        textBox_tags.Text        = data.Tags;
        textBox_notes.Text       = data.Notes;

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
            case Key.R: button_record_Click(null, new RoutedEventArgs());          e.Handled = true; break;
            case Key.L: button_load_sample_data_Click(null, new RoutedEventArgs()); e.Handled = true; break;
            case Key.C: button_clearlast_Click(null, new RoutedEventArgs());        e.Handled = true; break;
            case Key.A: button_clearlog_Click(null, new RoutedEventArgs());         e.Handled = true; break;
            case Key.S: button_save_Click(null, new RoutedEventArgs());             e.Handled = true; break;
            case Key.T: button_stop_Click(null, new RoutedEventArgs());             e.Handled = true; break;
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
