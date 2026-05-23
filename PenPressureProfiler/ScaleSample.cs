namespace PenPressureProfiler;

/// <summary>One scale reading captured inside a stability window.</summary>
public readonly record struct ScaleSample(
    DateTime Timestamp,
    double   ForceGf
);
