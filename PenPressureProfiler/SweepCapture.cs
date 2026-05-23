namespace PenPressureProfiler;

/// <summary>
/// A single auto-captured stable (physical, logical) pair from sweep mode,
/// together with the timestamped pen and scale samples that produced it.
/// </summary>
public sealed record SweepCapture(
    double                     PhysicalGf,
    double                     LogicalNorm,
    IReadOnlyList<PenSample>   PenSamples,
    IReadOnlyList<ScaleSample> ScaleSamples
)
{
    /// <summary>
    /// How many times this stable point has been re-confirmed without
    /// adding a duplicate entry to the capture list.
    /// </summary>
    public int Count { get; set; } = 1;
};
