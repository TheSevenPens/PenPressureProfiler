using System.Text.Json;
using PenPressureProfiler;

namespace PenPressureProfiler.Tests;

public class PressureTestFileTests
{
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    // ── Round-trip ──────────────────────────────────────────────────────────

    [Fact]
    public void Serialize_ThenDeserialize_PreservesAllFields()
    {
        var original = new PressureTestFile
        {
            Metadata = new SessionMetadata
            {
                Brand       = "WACOM",
                Pen         = "PRO PEN 3",
                PenFamily   = "PRO",
                InventoryId = "--P.0042",
                Date        = "2026-05-22",
                User        = "SEVEN",
                Tablet      = "PTH-860",
                Driver      = "6.4.2",
                Os          = "WINDOWS 11",
                Tags        = "test",
                Notes       = "first run",
            },
            Records     = [[10.0, 5.0], [100.0, 50.0], [500.0, 100.0]]
        };

        string json = JsonSerializer.Serialize(original, WriteOptions);
        var restored = JsonSerializer.Deserialize<PressureTestFile>(json)!;

        Assert.Equal(original.Metadata.Brand,       restored.Metadata.Brand);
        Assert.Equal(original.Metadata.Pen,         restored.Metadata.Pen);
        Assert.Equal(original.Metadata.InventoryId, restored.Metadata.InventoryId);
        Assert.Equal(original.Metadata.Date,        restored.Metadata.Date);
        Assert.Equal(3,                             restored.Records.Count);
        Assert.Equal(10.0,                          restored.Records[0][0]);
        Assert.Equal(5.0,                           restored.Records[0][1]);
    }

    // ── Field name contract ─────────────────────────────────────────────────

    [Fact]
    public void Serialize_UsesNestedMetadataWithLowercaseNames()
    {
        var file = new PressureTestFile
        {
            Metadata = new SessionMetadata { Brand = "WACOM", InventoryId = "X" }
        };
        string json = JsonSerializer.Serialize(file, WriteOptions);
        Assert.Contains("\"metadata\"",    json);
        Assert.Contains("\"brand\"",       json);
        Assert.Contains("\"inventoryid\"", json);
        Assert.Contains("\"penfamily\"",   json);
    }

    // ── ToRecordCollection ──────────────────────────────────────────────────

    [Fact]
    public void ToRecordCollection_ConvertsPercentToFraction()
    {
        var file = new PressureTestFile
        {
            Records = [[100.0, 50.0], [500.0, 100.0]]
        };

        var col = file.ToRecordCollection();

        Assert.Equal(2, col.Count);
        Assert.Equal(100.0, col.Items[0].PhysicalPressure);
        Assert.Equal(0.5,   col.Items[0].LogicalPressure, precision: 10);
        Assert.Equal(500.0, col.Items[1].PhysicalPressure);
        Assert.Equal(1.0,   col.Items[1].LogicalPressure, precision: 10);
    }

    [Fact]
    public void ToRecordCollection_SkipsRecordsWithFewerThanTwoValues()
    {
        var file = new PressureTestFile
        {
            Records = [[100.0], [200.0, 50.0]]
        };

        var col = file.ToRecordCollection();
        Assert.Equal(1, col.Count);
    }

    // ── Deserialization from legacy JSON ────────────────────────────────────

    [Fact]
    public void Deserialize_LegacyJson_ParsesCorrectly()
    {
        // Matches the hand-rolled format produced by older versions of the app.
        const string json = """
            {
                "brand": "WACOM" ,
                "pen": "PRO PEN 3" ,
                "inventoryid": "--P.0001" ,
                "date": "2025-01-01" ,
                "records": [
                    [ 10.0 , 1.2345 ] ,
                    [ 500.0 , 99.9999 ]
                ]
            }
            """;

        var file = JsonSerializer.Deserialize<PressureTestFile>(json)!;
        var meta = file.EffectiveMetadata();

        Assert.Equal("WACOM",    meta.Brand);
        Assert.Equal("--P.0001", meta.InventoryId);
        Assert.Equal(2,          file.Records.Count);
        Assert.Equal(10.0,       file.Records[0][0]);
        Assert.Equal(1.2345,     file.Records[0][1], precision: 4);
    }

    [Fact]
    public void Deserialize_NestedMetadata_TakesPrecedenceOverLegacy()
    {
        // A new-format file: metadata lives in the nested block.
        const string json = """
            {
                "metadata": { "brand": "XENCELABS", "inventoryid": "N-1" },
                "records": [ [ 10.0, 1.0 ] ]
            }
            """;

        var meta = JsonSerializer.Deserialize<PressureTestFile>(json)!.EffectiveMetadata();
        Assert.Equal("XENCELABS", meta.Brand);
        Assert.Equal("N-1",       meta.InventoryId);
    }
}
