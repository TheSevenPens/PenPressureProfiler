namespace PenPressureProfiler;

public class PressureRecordCollection
{
    public List<PressureRecord> items;

    public PressureRecordCollection()
    {
        items = [];
    }

    public int Count => items.Count;

    /// <summary>
    /// Converts to the serialization model, scaling logical fraction → percent.
    /// </summary>
    public List<double[]> ToRecordArrays() =>
        items.Select(r => new[] { r.PhysicalPressure, r.LogicalPressure * 100.0 }).ToList();

    public void Add(double physical, double logical)
    {
        items.Add(new PressureRecord(physical, logical));
    }

    public void Clear()
    {
        items.Clear();
    }

    public void ClearLast()
    {
        items.RemoveAt(items.Count - 1);
    }
}
