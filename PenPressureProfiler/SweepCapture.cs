namespace PenPressureProfiler;

/// <summary>
/// A single auto-captured stable (physical, logical) pair from the sweep mode,
/// together with the timestamped pen and scale samples that made it up.
/// </summary>
public sealed record SweepCapture(
    double                    PhysicalGf,
    double                    LogicalNorm,
    IReadOnlyList<PenSample>   PenSamples,
    IReadOnlyList<ScaleSample> ScaleSamples
);
