namespace PenPressureProfiler;

/// <summary>Display row for the Sweep Data DataGrid.</summary>
internal sealed class SweepCaptureRow(int index, SweepCapture capture)
{
    public SweepCapture Capture    { get; } = capture;
    public int          Index      { get; } = index;
    public string       PhysicalGf { get; } = $"{capture.PhysicalGf:F2} gf";
    public string       LogicalPct { get; } = $"{capture.LogicalNorm * 100:F2}%";
    public string       PenRange   { get; } =
        capture.PenSamples.Count > 0
            ? $"{(capture.PenSamples.Max() - capture.PenSamples.Min()) * 100:F2}%"
            : "—";
    public string       ScaleRange { get; } =
        capture.ScaleSamples.Count > 0
            ? $"{capture.ScaleSamples.Max() - capture.ScaleSamples.Min():F2} gf"
            : "—";
}
