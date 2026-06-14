using System.Text.Json.Serialization;

namespace PenPressureProfiler.Model;

/// <summary>
/// On-disk form of a single <see cref="ThresholdRecording"/> — the raw pen and
/// scale samples leading up to one threshold detection, saved from the review
/// dialog for later analysis. Camel-cased property names mirror the stability
/// snapshot files.
/// </summary>
public sealed class ThresholdRecordingFile
{
    [JsonPropertyName("title")]        public string?  Title      { get; set; }
    [JsonPropertyName("detectedAt")]   public DateTime DetectedAt { get; set; }

    [JsonPropertyName("penSamples")]   public List<PenSampleDto>   PenSamples   { get; set; } = [];
    [JsonPropertyName("scaleSamples")] public List<ScaleSampleDto> ScaleSamples { get; set; } = [];

    public static ThresholdRecordingFile From(ThresholdRecording r, string? title) => new()
    {
        Title      = title,
        DetectedAt = r.DetectedAt,
        PenSamples = r.PenSamples.Select(p => new PenSampleDto
        {
            Timestamp          = p.Timestamp,
            RawPressure        = p.RawPressure,
            NormalizedPressure = p.NormalizedPressure,
            SmoothedPressure   = p.SmoothedPressure,
            TipDown            = p.TipDown,
        }).ToList(),
        ScaleSamples = r.ScaleSamples.Select(s => new ScaleSampleDto
        {
            Timestamp = s.Timestamp,
            ForceGf   = s.ForceGf,
        }).ToList(),
    };

    public sealed class PenSampleDto
    {
        [JsonPropertyName("timestamp")]          public DateTime Timestamp          { get; set; }
        [JsonPropertyName("rawPressure")]        public uint     RawPressure        { get; set; }
        [JsonPropertyName("normalizedPressure")] public double   NormalizedPressure { get; set; }
        [JsonPropertyName("smoothedPressure")]   public double   SmoothedPressure   { get; set; }
        [JsonPropertyName("tipDown")]            public bool     TipDown            { get; set; }
    }

    public sealed class ScaleSampleDto
    {
        [JsonPropertyName("timestamp")] public DateTime Timestamp { get; set; }
        [JsonPropertyName("forceGf")]   public double   ForceGf   { get; set; }
    }
}
