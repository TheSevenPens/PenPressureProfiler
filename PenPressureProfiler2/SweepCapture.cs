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
);
