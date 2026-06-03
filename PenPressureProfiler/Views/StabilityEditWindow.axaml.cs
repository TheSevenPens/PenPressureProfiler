using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ScottPlot;

namespace PenPressureProfiler.Views;

/// <summary>
/// Modal dialog for reviewing and deleting individual stability captures.
/// • Orange ⚠ rows / dots  = non-monotonic violations.
/// • Right-click a list row = delete it immediately.
/// • Click (or Ctrl-click) a chart dot = select/deselect the matching row.
/// • Scroll wheel = zoom in/out around the cursor; drag = pan.
/// Returns the surviving captures via ShowDialog, or null on Cancel.
/// </summary>
public partial class StabilityEditWindow : Window
{
    private readonly List<StabilityCapture> _captures;

    // Click-vs-drag detection on the chart.
    private Point? _chartPressPoint;

    public StabilityEditWindow(IEnumerable<StabilityCapture> captures)
    {
        InitializeComponent();

        _captures = captures.OrderBy(c => c.PhysicalGf).ToList();

        // Chart: click-to-select (tunnel so we see it before ScottPlot)
        editPlotView.AddHandler(
            PointerPressedEvent,  OnChartPressed,  RoutingStrategies.Tunnel);
        editPlotView.AddHandler(
            PointerReleasedEvent, OnChartReleased, RoutingStrategies.Tunnel);

        // List: right-click to delete
        listBox_edit.AddHandler(
            PointerPressedEvent, OnListPressed, RoutingStrategies.Tunnel);

        // Selection colour update
        listBox_edit.SelectionChanged += (_, _) => RefreshChart();

        Loaded += (_, _) => Dispatcher.UIThread.Post(
            InitPlot, DispatcherPriority.Background);

        RefreshList();
    }

    // ── Monotonic violation detection ─────────────────────────────────────────

    /// <summary>
    /// A capture is a violator when its logical norm is below the running
    /// maximum of all captures with lower physical force.
    /// </summary>
    private HashSet<StabilityCapture> ComputeViolators()
    {
        var violators = new HashSet<StabilityCapture>();
        double runMax = double.MinValue;

        foreach (var c in _captures)   // already sorted ascending by PhysicalGf
        {
            if (c.LogicalNorm < runMax)
                violators.Add(c);
            runMax = Math.Max(runMax, c.LogicalNorm);
        }

        return violators;
    }

    // ── Chart ─────────────────────────────────────────────────────────────────

    private void InitPlot()
    {
        var plt = editPlotView.Plot;
        plt.XLabel("Physical pressure (gf)");
        plt.YLabel("Logical pressure (%)");
        plt.Title("Blue = clean  |  Orange ⚠ = non-monotonic  |  Red ◆ = selected");
        plt.FigureBackground.Color = ScottPlot.Color.FromHex("#FFFFFF");
        plt.DataBackground.Color   = ScottPlot.Color.FromHex("#FAFAFA");

        // Enable full interactivity: scroll-wheel zoom + drag pan.
        editPlotView.UserInputProcessor.IsEnabled = true;

        // Set initial axis bounds; RefreshChart does NOT reset them so the
        // user's zoom level is preserved across selection changes / deletions.
        if (_captures.Count > 0)
        {
            double xMax = _captures.Max(c => c.PhysicalGf) * 1.15;
            double yMax = Math.Min(_captures.Max(c => c.LogicalNorm * 100) * 1.15, 100);
            plt.Axes.SetLimits(0, Math.Max(xMax, 50), 0, Math.Max(yMax, 10));
        }
        else
        {
            plt.Axes.SetLimits(0, 100, 0, 100);
        }

        RefreshChart();
    }

    /// <summary>
    /// Redraws all scatter series without touching axis limits so that the
    /// user's current zoom / pan position is preserved.
    /// </summary>
    private void RefreshChart()
    {
        var plt = editPlotView.Plot;
        plt.Clear();

        if (_captures.Count == 0)
        {
            editPlotView.Refresh();
            return;
        }

        var violators = ComputeViolators();

        var selectedCaptures = listBox_edit.SelectedItems?
            .OfType<EditCaptureRow>()
            .Select(r => r.Capture)
            .ToHashSet() ?? [];

        // Three groups: selected (red) > violator (orange) > clean (blue)
        var selX   = new List<double>(); var selY   = new List<double>();
        var violX  = new List<double>(); var violY  = new List<double>();
        var cleanX = new List<double>(); var cleanY = new List<double>();

        foreach (var c in _captures)
        {
            double x = c.PhysicalGf, y = c.LogicalNorm * 100;
            if      (selectedCaptures.Contains(c)) { selX.Add(x);   selY.Add(y);   }
            else if (violators.Contains(c))        { violX.Add(x);  violY.Add(y);  }
            else                                    { cleanX.Add(x); cleanY.Add(y); }
        }

        if (cleanX.Count > 0)
        {
            var s = plt.Add.Scatter(cleanX.ToArray(), cleanY.ToArray());
            s.Color = ScottPlot.Color.FromHex("#2563EB");
            s.LineWidth = 0; s.MarkerSize = 7;
        }
        if (violX.Count > 0)
        {
            var s = plt.Add.Scatter(violX.ToArray(), violY.ToArray());
            s.Color = ScottPlot.Color.FromHex("#F97316");
            s.LineWidth = 0; s.MarkerSize = 9;
        }
        if (selX.Count > 0)
        {
            var s = plt.Add.Scatter(selX.ToArray(), selY.ToArray());
            s.Color = ScottPlot.Colors.Red;
            s.LineWidth = 0; s.MarkerSize = 11;
            s.MarkerShape = MarkerShape.FilledDiamond;
        }

        editPlotView.Refresh();
    }

    // ── Chart pointer: click-to-select ────────────────────────────────────────

    private void OnChartPressed(object? sender, PointerPressedEventArgs e)
    {
        var pt = e.GetCurrentPoint(editPlotView);

        if (pt.Properties.IsRightButtonPressed)
        {
            ResetEditPlotLimits();
            RefreshChart();
            e.Handled = true;
            return;
        }

        if (pt.Properties.IsLeftButtonPressed)
            _chartPressPoint = e.GetPosition(editPlotView);
    }

    /// <summary>Resets the edit chart to a fit-to-data view of the current captures.</summary>
    private void ResetEditPlotLimits()
    {
        if (_captures.Count == 0)
        {
            editPlotView.Plot.Axes.SetLimits(0, 100, 0, 100);
            return;
        }

        double xMax = _captures.Max(c => c.PhysicalGf) * 1.15;
        double yMax = Math.Min(_captures.Max(c => c.LogicalNorm * 100) * 1.15, 100);
        editPlotView.Plot.Axes.SetLimits(
            0, Math.Max(xMax, 50),
            0, Math.Max(yMax, 10));
    }

    private void OnChartReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_chartPressPoint is null) return;
        var rel    = e.GetPosition(editPlotView);
        var press  = _chartPressPoint.Value;
        _chartPressPoint = null;

        // Only treat it as a click if the pointer barely moved (not a pan drag).
        double moved = Math.Sqrt(
            Math.Pow(rel.X - press.X, 2) + Math.Pow(rel.Y - press.Y, 2));
        if (moved >= 5.0) return;

        SelectNearestChartPoint(rel, (e.KeyModifiers & KeyModifiers.Control) != 0);
    }

    private void SelectNearestChartPoint(Point clickPos, bool ctrlHeld)
    {
        const double PixelThreshold = 15.0;
        StabilityCapture? closest = null;
        double minDist = PixelThreshold + 1;

        foreach (var c in _captures)
        {
            try
            {
                var pix = editPlotView.Plot.GetPixel(
                    new Coordinates(c.PhysicalGf, c.LogicalNorm * 100));
                double d = Math.Sqrt(
                    Math.Pow(pix.X - clickPos.X, 2) +
                    Math.Pow(pix.Y - clickPos.Y, 2));
                if (d < minDist) { minDist = d; closest = c; }
            }
            catch { /* plot not yet rendered */ }
        }

        if (closest is null) return;

        var rows   = listBox_edit.ItemsSource?.Cast<EditCaptureRow>().ToList();
        var target = rows?.FirstOrDefault(r => r.Capture == closest);
        if (target is null) return;

        if (ctrlHeld)
        {
            if (listBox_edit.SelectedItems?.Contains(target) == true)
                listBox_edit.SelectedItems.Remove(target);
            else
                listBox_edit.SelectedItems?.Add(target);
        }
        else
        {
            listBox_edit.SelectedItem = target;
        }

        listBox_edit.ScrollIntoView(target);
    }

    // ── List pointer: right-click to delete ───────────────────────────────────

    private void OnListPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(listBox_edit).Properties.IsRightButtonPressed)
            return;

        var row = (e.Source as Visual)
            ?.FindAncestorOfType<ListBoxItem>(includeSelf: true)
            ?.DataContext as EditCaptureRow;

        if (row is null) return;

        _captures.Remove(row.Capture);
        RefreshList();
        e.Handled = true;
    }

    // ── List management ───────────────────────────────────────────────────────

    private void RefreshList()
    {
        var violators = ComputeViolators();

        listBox_edit.ItemsSource = _captures
            .Select((c, i) => new EditCaptureRow(i + 1, c, violators.Contains(c)))
            .ToList();

        RefreshStatus();
        RefreshChart();
    }

    private void RefreshStatus()
    {
        int sel        = listBox_edit.SelectedItems?.Count ?? 0;
        int total      = _captures.Count;
        int violCount  = ComputeViolators().Count;

        string violPart = violCount > 0
            ? $"  |  {violCount} ⚠ non-monotonic"
            : "";

        txt_status.Text = sel > 0
            ? $"{sel} selected (red) — click Delete Selected to remove{violPart}"
            : $"{total} capture{(total == 1 ? "" : "s")}{violPart} — select items to delete";

        btn_delete_selected.Content = sel > 0
            ? $"Delete Selected ({sel})" : "Delete Selected";
    }

    // ── Delete handlers ───────────────────────────────────────────────────────

    private void DeleteSelected_Click(object? sender, RoutedEventArgs e)
    {
        var toRemove = listBox_edit.SelectedItems?
            .OfType<EditCaptureRow>()
            .Select(r => r.Capture)
            .ToList();

        if (toRemove is null || toRemove.Count == 0) return;

        foreach (var c in toRemove)
            _captures.Remove(c);

        RefreshList();
    }

    private void Done_Click(object? sender, RoutedEventArgs e)
        => Close(_captures);

    private void Cancel_Click(object? sender, RoutedEventArgs e)
        => Close(null);
}
