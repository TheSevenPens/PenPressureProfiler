using System.Text.Json.Serialization;

namespace PenPressureProfiler;

/// <summary>
/// Root object for a saved sweep session — a list of stable captures
/// with their underlying raw samples.
/// </summary>
public sealed class SweepSnapshotFile
{
    [JsonPropertyName("captures")]
    public List<SweepSnapshotCapture> Captures { get; set; } = [];

    /// <summary>Converts to a list of <see cref="SweepCapture"/> for
    /// reloading into <see cref="SweepController"/>.</summary>
    public List<SweepCapture> ToSweepCaptures() =>
        Captures.Select(c => new SweepCapture(
            PhysicalGf:   c.PhysicalGf,
            LogicalNorm:  c.LogicalNorm,
            PenSamples:   c.PenSamples,
            ScaleSamples: c.ScaleSamples)).ToList();

    /// <summary>Builds a file model from the current capture list.</summary>
    public static SweepSnapshotFile From(IEnumerable<SweepCapture> captures) => new()
    {
        Captures = captures.Select(c => new SweepSnapshotCapture
        {
            PhysicalGf   = c.PhysicalGf,
            LogicalNorm  = c.LogicalNorm,
            PenSamples   = c.PenSamples.ToList(),
            ScaleSamples = c.ScaleSamples.ToList()
        }).ToList()
    };
}

public sealed class SweepSnapshotCapture
{
    [JsonPropertyName("physicalGf")]   public double       PhysicalGf   { get; set; }
    [JsonPropertyName("logicalNorm")]  public double       LogicalNorm  { get; set; }
    [JsonPropertyName("penSamples")]   public List<double> PenSamples   { get; set; } = [];
    [JsonPropertyName("scaleSamples")] public List<double> ScaleSamples { get; set; } = [];
}
