namespace PenPressureProfiler.Model;

public class PressureRecordCollection
{
    private readonly List<PressureRecord> _items = [];

    public IReadOnlyList<PressureRecord> Items => _items;
    public int Count => _items.Count;

    public void Add(double physical, double logical) =>
        _items.Add(new PressureRecord(physical, logical));

    public void RemoveLast()
    {
        if (_items.Count > 0) _items.RemoveAt(_items.Count - 1);
    }

    /// <summary>Removes the record at the given index. Returns true on success.</summary>
    public bool RemoveAt(int index)
    {
        if (index < 0 || index >= _items.Count) return false;
        _items.RemoveAt(index);
        return true;
    }

    public void Clear() => _items.Clear();

    /// <summary>Converts to serialisation format: each entry is [physGf, logPct].</summary>
    public List<double[]> ToRecordArrays() =>
        _items.Select(r => new[] { r.PhysicalPressure, r.LogicalPressure * 100.0 }).ToList();
}
