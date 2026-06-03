using System.Text.Json.Serialization;

namespace PenPressureProfiler.Model;

/// <summary>
/// Serialisation model for a manual pressure-capture JSON file.
/// Metadata lives in a nested "metadata" object (shared shape with stability
/// snapshots). Records are stored as [physical_gf, logical_percent] pairs;
/// logical pressure is a percentage (0–100), not a fraction.
/// </summary>
public class PressureTestFile
{
    [JsonPropertyName("metadata")]
    public SessionMetadata Metadata { get; set; } = new();

    /// <summary>Each element is [physical_gf, logical_percent].</summary>
    [JsonPropertyName("records")] public List<double[]> Records { get; set; } = [];

    // ── Legacy top-level metadata (older files). Read-only fallback: these are
    //    nullable and skipped on write, so new files only ever emit "metadata".
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] [JsonPropertyName("brand")]       public string? LegacyBrand       { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] [JsonPropertyName("pen")]         public string? LegacyPen         { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] [JsonPropertyName("penfamily")]   public string? LegacyPenFamily   { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] [JsonPropertyName("inventoryid")] public string? LegacyInventoryId { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] [JsonPropertyName("date")]        public string? LegacyDate        { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] [JsonPropertyName("user")]        public string? LegacyUser        { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] [JsonPropertyName("tablet")]      public string? LegacyTablet      { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] [JsonPropertyName("driver")]      public string? LegacyDriver      { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] [JsonPropertyName("os")]          public string? LegacyOs          { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] [JsonPropertyName("tags")]        public string? LegacyTags        { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] [JsonPropertyName("notes")]       public string? LegacyNotes       { get; set; }

    /// <summary>Resolved metadata: the nested block if present, otherwise the
    /// legacy top-level fields (for files written before the schema change).</summary>
    public SessionMetadata EffectiveMetadata()
    {
        if (Metadata.HasAny) return Metadata;
        return new SessionMetadata
        {
            Brand = LegacyBrand ?? "", Pen = LegacyPen ?? "", PenFamily = LegacyPenFamily ?? "",
            InventoryId = LegacyInventoryId ?? "", Date = LegacyDate ?? "", User = LegacyUser ?? "",
            Tablet = LegacyTablet ?? "", Driver = LegacyDriver ?? "", Os = LegacyOs ?? "",
            Tags = LegacyTags ?? "", Notes = LegacyNotes ?? "",
        };
    }

    public static PressureTestFile From(SessionMetadata metadata, IEnumerable<double[]> records) => new()
    {
        Metadata = metadata.Clone(),
        Records  = records.ToList(),
    };

    /// <summary>Converts to a collection, scaling logical percent → fraction.</summary>
    public PressureRecordCollection ToRecordCollection()
    {
        var col = new PressureRecordCollection();
        foreach (var r in Records)
            if (r.Length >= 2)
                col.Add(r[0], r[1] / 100.0);
        return col;
    }
}
