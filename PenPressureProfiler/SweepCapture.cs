namespace PenPressureProfiler;

/// <summary>
/// A single auto-captured stable (physical, logical) pair from the sweep mode,
/// together with the raw pen and scale samples that made it up.
/// </summary>
public sealed record SweepCapture(
    double                PhysicalGf,
    double                LogicalNorm,
    IReadOnlyList<double> PenSamples,   // normalised pressure values in the stability window
    IReadOnlyList<double> ScaleSamples  // force (gf) values in the stability window
);
