namespace PenPressureProfiler;

public class PressureRecordCollection
{
    private readonly List<PressureRecord> _items = [];

    /// <summary>Read-only view of the recorded pressure pairs.</summary>
    public IReadOnlyList<PressureRecord> Items => _items;

    public int Count => _items.Count;

    /// <summary>
    /// Converts to the serialization model, scaling logical fraction → percent.
    /// </summary>
    public List<double[]> ToRecordArrays() =>
        _items.Select(r => new[] { r.PhysicalPressure, r.LogicalPressure * 100.0 }).ToList();

    public void Add(double physical, double logical) =>
        _items.Add(new PressureRecord(physical, logical));

    public void Clear() => _items.Clear();

    public void ClearLast() => _items.RemoveAt(_items.Count - 1);
}
