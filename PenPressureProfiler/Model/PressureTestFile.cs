using System.Text.Json.Serialization;

namespace PenPressureProfiler.Model;

/// <summary>
/// Serialisation model for a pressure-test JSON file.
/// Records are stored as [physical_gf, logical_percent] pairs.
/// Logical pressure is a percentage (0–100), not a fraction.
/// </summary>
public class PressureTestFile
{
    [JsonPropertyName("brand")]       public string Brand       { get; set; } = "";
    [JsonPropertyName("pen")]         public string Pen         { get; set; } = "";
    [JsonPropertyName("penfamily")]   public string PenFamily   { get; set; } = "";
    [JsonPropertyName("inventoryid")] public string InventoryId { get; set; } = "";
    [JsonPropertyName("date")]        public string Date        { get; set; } = "";
    [JsonPropertyName("user")]        public string User        { get; set; } = "";
    [JsonPropertyName("tablet")]      public string Tablet      { get; set; } = "";
    [JsonPropertyName("driver")]      public string Driver      { get; set; } = "";
    [JsonPropertyName("os")]          public string Os          { get; set; } = "";
    [JsonPropertyName("tags")]        public string Tags        { get; set; } = "";
    [JsonPropertyName("notes")]       public string Notes       { get; set; } = "";

    /// <summary>Each element is [physical_gf, logical_percent].</summary>
    [JsonPropertyName("records")] public List<double[]> Records { get; set; } = [];

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
