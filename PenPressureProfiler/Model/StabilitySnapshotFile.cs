using System.Text.Json.Serialization;

namespace PenPressureProfiler.Model;

public sealed class StabilitySnapshotFile
{
    [JsonPropertyName("metadata")]
    public SessionMetadata? Metadata { get; set; }

    [JsonPropertyName("captures")]
    public List<StabilitySnapshotCapture> Captures { get; set; } = [];

    public List<StabilityCapture> ToStabilityCaptures() =>
        Captures.Select(c => new StabilityCapture(
            PhysicalGf:   c.PhysicalGf,
            LogicalNorm:  c.LogicalNorm,
            PenSamples:   c.PenSamples.Select(s =>
                new PenSample(s.Timestamp, s.RawPressure, s.NormalizedPressure, s.Altitude)).ToList(),
            ScaleSamples: (c.ScaleSamples ?? []).Select(s =>
                new ScaleSample(s.Timestamp, s.ForceGf)).ToList()
        ) { Count = c.Count }).ToList();

    public static StabilitySnapshotFile From(
        IEnumerable<StabilityCapture> captures, SessionMetadata? metadata = null) => new()
    {
        Metadata = metadata?.Clone(),
        Captures = captures.Select(c => new StabilitySnapshotCapture
        {
            Count        = c.Count,
            PhysicalGf   = c.PhysicalGf,
            LogicalNorm  = c.LogicalNorm,
            PenSamples   = c.PenSamples.Select(s => new StabilitySnapshotPenSample
                { Timestamp = s.Timestamp, RawPressure = s.RawPressure,
                  NormalizedPressure = s.NormalizedPressure,
                  Altitude = s.Altitude }).ToList()
            // ScaleSamples intentionally not written (see property below).
        }).ToList()
    };
}

public sealed class StabilitySnapshotCapture
{
    [JsonPropertyName("count")]        public int                            Count        { get; set; } = 1;
    [JsonPropertyName("physicalGf")]   public double                         PhysicalGf   { get; set; }
    [JsonPropertyName("logicalNorm")]  public double                         LogicalNorm  { get; set; }
    [JsonPropertyName("penSamples")]   public List<StabilitySnapshotPenSample>   PenSamples   { get; set; } = [];

    // No longer written. Kept nullable + WhenWritingNull so it is omitted from
    // new files but still read back from older snapshots that included it.
    [JsonPropertyName("scaleSamples")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<StabilitySnapshotScaleSample>? ScaleSamples { get; set; }
}

public sealed class StabilitySnapshotPenSample
{
    [JsonPropertyName("timestamp")]          public DateTime Timestamp          { get; set; }
    [JsonPropertyName("rawPressure")]        public uint     RawPressure        { get; set; }
    [JsonPropertyName("normalizedPressure")] public double   NormalizedPressure { get; set; }
    [JsonPropertyName("altitude")]           public double   Altitude           { get; set; }
}

public sealed class StabilitySnapshotScaleSample
{
    [JsonPropertyName("timestamp")] public DateTime Timestamp { get; set; }
    [JsonPropertyName("forceGf")]   public double   ForceGf   { get; set; }
}
