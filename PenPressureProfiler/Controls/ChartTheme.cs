using Avalonia;
using Avalonia.Styling;
using ScottPlot.Avalonia;

namespace PenPressureProfiler.Controls;

/// <summary>
/// Single source of truth for the visual styling shared by every AvaPlot in
/// the app. <see cref="Apply"/> sets the figure / data backgrounds, axis /
/// grid colours (matched to the active light/dark theme) and toggles whether
/// ScottPlot's own pointer input is enabled. Called from each chart's
/// Initialize routine and re-applied on theme changes.
/// </summary>
public static class ChartTheme
{
    private static bool IsDark =>
        Application.Current?.ActualThemeVariant == ThemeVariant.Dark;

    public static void Apply(AvaPlot chart, bool userInputEnabled = true)
    {
        var plt = chart.Plot;

        if (IsDark)
        {
            plt.FigureBackground.Color = ScottPlot.Color.FromHex("#1E1E1E");
            plt.DataBackground.Color   = ScottPlot.Color.FromHex("#252526");
            plt.Axes.Color(ScottPlot.Color.FromHex("#C8C8C8"));
            plt.Grid.MajorLineColor    = ScottPlot.Color.FromHex("#3A3A3A");
        }
        else
        {
            plt.FigureBackground.Color = ScottPlot.Color.FromHex("#FFFFFF");
            plt.DataBackground.Color   = ScottPlot.Color.FromHex("#FAFAFA");
            plt.Axes.Color(ScottPlot.Color.FromHex("#000000"));
            plt.Grid.MajorLineColor    = ScottPlot.Color.FromHex("#E0E0E0");
        }

        // Shrink axis text app-wide: tick labels (the numbers) and the axis
        // titles. ScottPlot's defaults run large for the compact panes here.
        foreach (var axis in plt.Axes.GetAxes())
        {
            axis.TickLabelStyle.FontSize = 10;
            axis.Label.FontSize = 11;
        }

        chart.UserInputProcessor.IsEnabled = userInputEnabled;
    }
}
