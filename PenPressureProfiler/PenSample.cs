namespace PenPressureProfiler;

/// <summary>
/// One pen reading captured inside a stability window.
/// </summary>
public readonly record struct PenSample(
    DateTime Timestamp,
    uint     RawPressure,
    double   NormalizedPressure
);
