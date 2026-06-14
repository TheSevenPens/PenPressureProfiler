namespace PenPressureProfiler.Model;

/// <summary>One pen reading captured in a threshold recording buffer. Carries the
/// fields useful for debugging a detection — raw / normalized / smoothed pressure
/// and whether the tip was down.</summary>
public readonly record struct ThresholdPenSample(
    DateTime Timestamp,
    uint     RawPressure,
    double   NormalizedPressure,
    double   SmoothedPressure,
    bool     TipDown
);

/// <summary>
/// The timestamped pen and scale samples leading up to a single threshold
/// detection. Captured from a rolling ~10 s buffer that runs while Threshold mode
/// is active; the snapshot is taken at the moment the estimate is recorded.
/// <para>
/// This is debug/analysis data only — it is intentionally excluded from the normal
/// capture save/clipboard output and is persisted separately on request.
/// </para>
/// </summary>
public sealed record ThresholdRecording(
    DateTime                          DetectedAt,
    IReadOnlyList<ThresholdPenSample> PenSamples,
    IReadOnlyList<ScaleSample>        ScaleSamples
);
