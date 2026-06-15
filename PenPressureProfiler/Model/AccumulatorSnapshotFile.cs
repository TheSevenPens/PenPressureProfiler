using System.Text.Json.Serialization;

namespace PenPressureProfiler.Model;

/// <summary>
/// On-disk form of an Accumulator run: the configured force range / bucket width
/// plus the per-bucket 0% / non-zero counts and the out-of-range tallies.
/// </summary>
public sealed class AccumulatorSnapshotFile
{
    [JsonPropertyName("metadata")]    public SessionMetadata? Metadata { get; set; }

    [JsonPropertyName("minGf")]       public double MinGf       { get; set; }
    [JsonPropertyName("maxGf")]       public double MaxGf       { get; set; }
    [JsonPropertyName("bucketWidth")] public double BucketWidth { get; set; }

    [JsonPropertyName("zero")]    public List<long> Zero    { get; set; } = [];
    [JsonPropertyName("nonZero")] public List<long> NonZero { get; set; } = [];

    [JsonPropertyName("belowZero")]    public long BelowZero    { get; set; }
    [JsonPropertyName("belowNonZero")] public long BelowNonZero { get; set; }
    [JsonPropertyName("aboveZero")]    public long AboveZero    { get; set; }
    [JsonPropertyName("aboveNonZero")] public long AboveNonZero { get; set; }
}
