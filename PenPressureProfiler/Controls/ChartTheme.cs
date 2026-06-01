using ScottPlot.Avalonia;

namespace PenPressureProfiler.Controls;

/// <summary>
/// Single source of truth for the visual styling shared by every AvaPlot in
/// the app. <see cref="Apply"/> sets the figure / data backgrounds and
/// toggles whether ScottPlot's own pointer input is enabled — every chart
/// in the app calls this from its Initialize routine.
/// </summary>
public static class ChartTheme
{
    private static readonly ScottPlot.Color FigureBackground = ScottPlot.Color.FromHex("#FFFFFF");
    private static readonly ScottPlot.Color DataBackground   = ScottPlot.Color.FromHex("#FAFAFA");

    public static void Apply(AvaPlot chart, bool userInputEnabled = true)
    {
        var plt = chart.Plot;
        plt.FigureBackground.Color = FigureBackground;
        plt.DataBackground.Color   = DataBackground;
        chart.UserInputProcessor.IsEnabled = userInputEnabled;
    }
}
