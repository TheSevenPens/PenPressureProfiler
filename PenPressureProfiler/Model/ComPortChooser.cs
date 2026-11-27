namespace PenPressureProfiler.Model;

/// <summary>
/// Decides which COM port the scale picker should select, given the ports that
/// exist right now. UI-free so it can be unit-tested.
/// </summary>
public static class ComPortChooser
{
    /// <summary>
    /// Returns the port to select, or <c>null</c> when no ports exist.
    /// Order of preference: the remembered port (if it is still present), then
    /// the currently selected port (if still present), then the last port in
    /// the list (the most recently added device is usually listed last).
    /// Port names compare case-insensitively, as Windows treats them.
    /// </summary>
    public static string? Choose(IReadOnlyList<string> available, string? remembered, string? current = null)
    {
        if (available.Count == 0) return null;
        return Find(available, remembered)
            ?? Find(available, current)
            ?? available[^1];
    }

    /// <summary>Cleans up a raw port list: drops blanks and duplicates, keeps order.</summary>
    public static IReadOnlyList<string> Normalize(IEnumerable<string> ports) =>
        ports.Where(p => !string.IsNullOrWhiteSpace(p))
             .Select(p => p.Trim())
             .Distinct(StringComparer.OrdinalIgnoreCase)
             .ToList();

    private static string? Find(IReadOnlyList<string> available, string? name) =>
        string.IsNullOrWhiteSpace(name)
            ? null
            : available.FirstOrDefault(p => string.Equals(p, name.Trim(), StringComparison.OrdinalIgnoreCase));
}
