namespace PenPressureProfiler;

internal static class StringExtensions
{
    /// <summary>Returns <paramref name="fallback"/> if the string is null or empty.</summary>
    public static string IfEmpty(this string? s, string fallback) =>
        string.IsNullOrEmpty(s) ? fallback : s;
}
