namespace PenPressureProfiler.Model;

/// <summary>
/// UI-free maths for the two-handle range slider: snapping to a step, mapping
/// between values and pixels, and keeping the handles in order.
/// </summary>
public static class RangeMath
{
    /// <summary>Snaps <paramref name="value"/> to the nearest multiple of
    /// <paramref name="step"/> (counted from <paramref name="min"/>) and clamps it
    /// to [min, max].</summary>
    public static double Snap(double value, double min, double max, double step)
    {
        if (step > 0) value = min + Math.Round((value - min) / step) * step;
        // Tidy floating-point noise (0.30000000000000004 → 0.3).
        value = Math.Round(value, 6);
        return Math.Clamp(value, min, max);
    }

    /// <summary>Maps a value in [min, max] to an x position across a track of
    /// <paramref name="width"/> pixels.</summary>
    public static double ValueToX(double value, double min, double max, double width)
        => max <= min ? 0 : (Math.Clamp(value, min, max) - min) / (max - min) * width;

    /// <summary>Maps an x position on the track back to a value in [min, max].</summary>
    public static double XToValue(double x, double min, double max, double width)
        => width <= 0 ? min : min + Math.Clamp(x / width, 0, 1) * (max - min);

    /// <summary>
    /// Moves one handle to <paramref name="proposed"/> (snapped), keeping it at
    /// least one step away from the other handle. Returns the new (lower, upper).
    /// </summary>
    public static (double Lower, double Upper) MoveHandle(
        bool movingLower, double proposed, double lower, double upper,
        double min, double max, double step)
    {
        double gap = step > 0 ? step : 0;
        var v = Snap(proposed, min, max, step);
        if (movingLower) return (Math.Min(v, upper - gap), upper);
        return (lower, Math.Max(v, lower + gap));
    }

    /// <summary>Extends a scale so it contains [lower, upper] (e.g. a loaded file
    /// with a wider range than the default scale), rounding outward to a step.</summary>
    public static (double Min, double Max) EnsureContains(
        double scaleMin, double scaleMax, double lower, double upper, double step)
    {
        double min = Math.Min(scaleMin, lower);
        double max = Math.Max(scaleMax, upper);
        if (step > 0)
        {
            min = Math.Floor(min / step) * step;
            max = Math.Ceiling(max / step) * step;
        }
        return (min, max);
    }
}
