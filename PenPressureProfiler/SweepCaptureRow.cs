namespace PenPressureProfiler;

/// <summary>Display row for the sweep captures ListBox.</summary>
internal sealed class SweepCaptureRow(int index, SweepCapture capture)
{
    public SweepCapture Capture    { get; } = capture;
    public int          Index      { get; } = index;
    public string       PhysicalGf { get; } = $"{capture.PhysicalGf:F2} gf";
    public string       LogicalPct { get; } = $"{capture.LogicalNorm * 100:F2}%";
    public string       PenRange   { get; } =
        capture.PenSamples.Count > 0
            ? $"{(capture.PenSamples.Max(s => s.NormalizedPressure) - capture.PenSamples.Min(s => s.NormalizedPressure)) * 100:F2}%"
            : "—";
    public string       ScaleRange { get; } =
        capture.ScaleSamples.Count > 0
            ? $"{capture.ScaleSamples.Max(s => s.ForceGf) - capture.ScaleSamples.Min(s => s.ForceGf):F2} gf"
            : "—";

    public override string ToString() =>
        $"#{Index:D3}  {PhysicalGf,-10}  →  {LogicalPct,-8}  ×{Capture.Count,-3}  pen:±{PenRange}  scale:±{ScaleRange}";
}
