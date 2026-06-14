using System.Text.Json.Serialization;

namespace PenPressureProfiler.Model;

/// <summary>
/// On-disk form of every threshold capture in the active mode <b>with</b> its raw
/// pen/scale recording — the verbose counterpart to the lean clipboard/markdown
/// export, written by the threshold pane's "Save Raw…" action for later analysis.
/// </summary>
public sealed class ThresholdRawSnapshotFile
{
    [JsonPropertyName("metadata")] public SessionMetadata?       Metadata { get; set; }
    [JsonPropertyName("mode")]     public string?               Mode     { get; set; }
    [JsonPropertyName("captures")] public List<ThresholdRawCapture> Captures { get; set; } = [];
}

/// <summary>One captured estimate plus the samples leading up to its detection.</summary>
public sealed class ThresholdRawCapture
{
    [JsonPropertyName("number")]     public int      Number     { get; set; }
    /// <summary>The estimate's reported force (IAF or MAX), in gf.</summary>
    [JsonPropertyName("valueGf")]    public double   ValueGf    { get; set; }
    [JsonPropertyName("zeroGf")]     public double?  ZeroGf     { get; set; }
    [JsonPropertyName("nonZeroGf")]  public double?  NonZeroGf  { get; set; }
    [JsonPropertyName("nonZeroRaw")] public uint?    NonZeroRaw { get; set; }
    [JsonPropertyName("detectedAt")] public DateTime DetectedAt { get; set; }

    [JsonPropertyName("penSamples")]
    public List<ThresholdRecordingFile.PenSampleDto> PenSamples { get; set; } = [];
    [JsonPropertyName("scaleSamples")]
    public List<ThresholdRecordingFile.ScaleSampleDto> ScaleSamples { get; set; } = [];
}
