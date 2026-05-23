using System.Text.Json.Serialization;

namespace PenPressureProfiler;

public sealed class SweepSnapshotFile
{
    [JsonPropertyName("captures")]
    public List<SweepSnapshotCapture> Captures { get; set; } = [];

    public List<SweepCapture> ToSweepCaptures() =>
        Captures.Select(c => new SweepCapture(
            PhysicalGf:   c.PhysicalGf,
            LogicalNorm:  c.LogicalNorm,
            PenSamples:   c.PenSamples.Select(s => new PenSample(
                              s.Timestamp, s.RawPressure, s.NormalizedPressure)).ToList(),
            ScaleSamples: c.ScaleSamples.Select(s => new ScaleSample(
                              s.Timestamp, s.ForceGf)).ToList())).ToList();

    public static SweepSnapshotFile From(IEnumerable<SweepCapture> captures) => new()
    {
        Captures = captures.Select(c => new SweepSnapshotCapture
        {
            PhysicalGf   = c.PhysicalGf,
            LogicalNorm  = c.LogicalNorm,
            PenSamples   = c.PenSamples.Select(s => new SweepSnapshotPenSample
                           { Timestamp = s.Timestamp, RawPressure = s.RawPressure,
                             NormalizedPressure = s.NormalizedPressure }).ToList(),
            ScaleSamples = c.ScaleSamples.Select(s => new SweepSnapshotScaleSample
                           { Timestamp = s.Timestamp, ForceGf = s.ForceGf }).ToList()
        }).ToList()
    };
}

public sealed class SweepSnapshotCapture
{
    [JsonPropertyName("physicalGf")]   public double                      PhysicalGf   { get; set; }
    [JsonPropertyName("logicalNorm")]  public double                      LogicalNorm  { get; set; }
    [JsonPropertyName("penSamples")]   public List<SweepSnapshotPenSample>   PenSamples   { get; set; } = [];
    [JsonPropertyName("scaleSamples")] public List<SweepSnapshotScaleSample> ScaleSamples { get; set; } = [];
}

public sealed class SweepSnapshotPenSample
{
    [JsonPropertyName("timestamp")]           public DateTime Timestamp           { get; set; }
    [JsonPropertyName("rawPressure")]         public uint     RawPressure         { get; set; }
    [JsonPropertyName("normalizedPressure")]  public double   NormalizedPressure  { get; set; }
}

public sealed class SweepSnapshotScaleSample
{
    [JsonPropertyName("timestamp")] public DateTime Timestamp { get; set; }
    [JsonPropertyName("forceGf")]   public double   ForceGf   { get; set; }
}
