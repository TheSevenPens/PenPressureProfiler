using PenPressureProfiler;

namespace PenPressureProfiler.Tests;

public class PressureRecordCollectionTests
{
    [Fact]
    public void Add_IncreasesCount()
    {
        var col = new PressureRecordCollection();
        col.Add(100.0, 0.5);
        Assert.Equal(1, col.Count);
    }

    [Fact]
    public void Add_StoresValuesCorrectly()
    {
        var col = new PressureRecordCollection();
        col.Add(123.4, 0.75);
        Assert.Equal(123.4, col.Items[0].PhysicalPressure);
        Assert.Equal(0.75, col.Items[0].LogicalPressure);
    }

    [Fact]
    public void Clear_RemovesAllRecords()
    {
        var col = new PressureRecordCollection();
        col.Add(10.0, 0.1);
        col.Add(20.0, 0.2);
        col.Clear();
        Assert.Equal(0, col.Count);
    }

    [Fact]
    public void ClearLast_RemovesMostRecentRecord()
    {
        var col = new PressureRecordCollection();
        col.Add(10.0, 0.1);
        col.Add(20.0, 0.2);
        col.ClearLast();
        Assert.Equal(1, col.Count);
        Assert.Equal(10.0, col.Items[0].PhysicalPressure);
    }

    [Fact]
    public void ToRecordArrays_ConvertsLogicalToPercent()
    {
        var col = new PressureRecordCollection();
        col.Add(50.0, 0.5);   // logical fraction 0.5 → percent 50.0
        col.Add(200.0, 1.0);  // logical fraction 1.0 → percent 100.0
        var arrays = col.ToRecordArrays();
        Assert.Equal(2, arrays.Count);
        Assert.Equal(50.0,  arrays[0][0]);
        Assert.Equal(50.0,  arrays[0][1]);
        Assert.Equal(200.0, arrays[1][0]);
        Assert.Equal(100.0, arrays[1][1]);
    }
}
