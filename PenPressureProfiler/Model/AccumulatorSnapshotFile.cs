using System.Text.Json.Serialization;

namespace PenPressureProfiler.Model;

/// <summary>
/// On-disk form of an Accumulator run. v2 stores both targets (IAF and
/// Max pressure), each with its force range, selected bucket width, and the
/// per-bucket off/on counts for <b>every</b> width layout (so a loaded file can
/// still switch bucket size without losing data).
/// <para>v1 files (single IAF target) are still read: they have the legacy
/// top-level <see cref="MinGf"/>/<see cref="MaxGf"/>/<see cref="SelectedWidth"/>/
/// <see cref="Layouts"/> fields and no <see cref="Targets"/>.</para>
/// <para>The JSON wire names for the counters stay <c>"zero"</c>/<c>"nonZero"</c>
/// (etc.) for backward-compatibility with already-saved files; the C# properties
/// use the generalised <c>Under</c>/<c>AtOrOver</c> threshold names.</para>
/// </summary>
public sealed class AccumulatorSnapshotFile
{
    [JsonPropertyName("metadata")]     public SessionMetadata? Metadata { get; set; }

    /// <summary>Format version. Absent/0 = legacy single-target (IAF) file.</summary>
    [JsonPropertyName("version")]      public int     Version      { get; set; }
    /// <summary>Target that was active when saved ("Iaf" / "MaxPressure";
    /// "Saturation" from earlier builds is also accepted on load).</summary>
    [JsonPropertyName("activeTarget")] public string? ActiveTarget { get; set; }

    /// <summary>v2: one entry per target.</summary>
    [JsonPropertyName("targets")]      public List<AccumulatorTargetSnapshot>? Targets { get; set; }

    // ── Legacy v1 top-level fields (still read; written as null in v2) ─────────
    [JsonPropertyName("minGf")]         public double MinGf         { get; set; }
    [JsonPropertyName("maxGf")]         public double MaxGf         { get; set; }
    [JsonPropertyName("selectedWidth")] public double SelectedWidth { get; set; }
    [JsonPropertyName("layouts")]       public List<AccumulatorLayoutSnapshot>? Layouts { get; set; }
}

/// <summary>One target's range, selected width, and per-width layouts.</summary>
public sealed class AccumulatorTargetSnapshot
{
    [JsonPropertyName("target")]        public string Target        { get; set; } = "Iaf";
    [JsonPropertyName("minGf")]         public double MinGf         { get; set; }
    [JsonPropertyName("maxGf")]         public double MaxGf         { get; set; }
    [JsonPropertyName("selectedWidth")] public double SelectedWidth { get; set; }
    [JsonPropertyName("layouts")]       public List<AccumulatorLayoutSnapshot> Layouts { get; set; } = [];
}

public sealed class AccumulatorLayoutSnapshot
{
    [JsonPropertyName("width")]        public double      Width         { get; set; }
    [JsonPropertyName("zero")]         public List<long>  Under         { get; set; } = [];
    [JsonPropertyName("nonZero")]      public List<long>  AtOrOver      { get; set; } = [];
    [JsonPropertyName("belowZero")]    public long        BelowUnder    { get; set; }
    [JsonPropertyName("belowNonZero")] public long        BelowAtOrOver { get; set; }
    [JsonPropertyName("aboveZero")]    public long        AboveUnder    { get; set; }
    [JsonPropertyName("aboveNonZero")] public long        AboveAtOrOver { get; set; }
}
