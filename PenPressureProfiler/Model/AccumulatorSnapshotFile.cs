using System.Text.Json.Serialization;

namespace PenPressureProfiler.Model;

/// <summary>
/// On-disk form of an Accumulator run: the force range, the selected bucket
/// width, and the per-bucket 0% / non-zero counts for <b>every</b> width layout
/// (so a loaded file can still switch bucket size without losing data).
/// </summary>
public sealed class AccumulatorSnapshotFile
{
    [JsonPropertyName("metadata")]      public SessionMetadata? Metadata { get; set; }

    [JsonPropertyName("minGf")]         public double MinGf         { get; set; }
    [JsonPropertyName("maxGf")]         public double MaxGf         { get; set; }
    [JsonPropertyName("selectedWidth")] public double SelectedWidth { get; set; }

    [JsonPropertyName("layouts")] public List<AccumulatorLayoutSnapshot> Layouts { get; set; } = [];
}

public sealed class AccumulatorLayoutSnapshot
{
    [JsonPropertyName("width")]        public double      Width        { get; set; }
    [JsonPropertyName("zero")]         public List<long>  Zero         { get; set; } = [];
    [JsonPropertyName("nonZero")]      public List<long>  NonZero      { get; set; } = [];
    [JsonPropertyName("belowZero")]    public long        BelowZero    { get; set; }
    [JsonPropertyName("belowNonZero")] public long        BelowNonZero { get; set; }
    [JsonPropertyName("aboveZero")]    public long        AboveZero    { get; set; }
    [JsonPropertyName("aboveNonZero")] public long        AboveNonZero { get; set; }
}
