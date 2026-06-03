using System.Text.Json.Serialization;

namespace PenPressureProfiler.Model;

/// <summary>
/// Canonical session metadata, shared by the in-memory session state and the
/// "metadata" block of both file formats (manual captures and stability
/// snapshots) so the two stay structurally identical.
/// </summary>
public sealed class SessionMetadata
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

    public SessionMetadata Clone() => new()
    {
        Brand = Brand, Pen = Pen, PenFamily = PenFamily, InventoryId = InventoryId,
        Date = Date, User = User, Tablet = Tablet, Driver = Driver,
        Os = Os, Tags = Tags, Notes = Notes,
    };

    /// <summary>True when any field carries a non-blank value.</summary>
    public bool HasAny =>
        !string.IsNullOrWhiteSpace(Brand)     || !string.IsNullOrWhiteSpace(Pen)         ||
        !string.IsNullOrWhiteSpace(PenFamily) || !string.IsNullOrWhiteSpace(InventoryId) ||
        !string.IsNullOrWhiteSpace(Date)      || !string.IsNullOrWhiteSpace(User)        ||
        !string.IsNullOrWhiteSpace(Tablet)    || !string.IsNullOrWhiteSpace(Driver)      ||
        !string.IsNullOrWhiteSpace(Os)        || !string.IsNullOrWhiteSpace(Tags)        ||
        !string.IsNullOrWhiteSpace(Notes);
}
