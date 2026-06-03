using Avalonia;

namespace PenPressureProfiler;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Match ScottPlot's chart text (axis/tick/label) to the UI font so the
        // plots don't look like a different typeface from the surrounding UI.
        // Set before any plot is created.
        ScottPlot.Fonts.Default = "Segoe UI";

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
