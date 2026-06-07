using System.Globalization;
using System.IO;

namespace PenPressureProfiler.Sessions;

/// <summary>
/// Writes real-time pen and scale readings to two timestamped CSV files
/// in <see cref="LogDirectory"/>. Create once, call StartLogging/StopLogging as needed.
/// </summary>
public sealed class SessionLogger : IDisposable
{
    public string LogDirectory { get; }
    public bool   IsLogging    { get; private set; }

    private StreamWriter? _penWriter;
    private StreamWriter? _scaleWriter;

    public SessionLogger(string logDirectory)
    {
        LogDirectory = logDirectory;
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public void StartLogging()
    {
        if (IsLogging) return;

        Directory.CreateDirectory(LogDirectory);
        string stamp = DateTime.Now.ToString("yyyy-MM-dd_HHmmss");

        _penWriter = OpenCsv(Path.Combine(LogDirectory, $"pen_{stamp}.csv"));
        _penWriter.WriteLine(
            "Timestamp," +
            "RawPressure,NormalizedPressure,SmoothedPressure," +
            "Azimuth,Altitude,TiltX,TiltY," +
            "TipDown,Barrel1Down,Barrel2Down");

        _scaleWriter = OpenCsv(Path.Combine(LogDirectory, $"scale_{stamp}.csv"));
        _scaleWriter.WriteLine("Timestamp,Force_gf,RawLine");

        IsLogging = true;
    }

    public void StopLogging()
    {
        if (!IsLogging) return;

        _penWriter?.Flush(); _penWriter?.Dispose(); _penWriter = null;
        _scaleWriter?.Flush(); _scaleWriter?.Dispose(); _scaleWriter = null;

        IsLogging = false;
    }

    public void Dispose() => StopLogging();

    // ── Write ─────────────────────────────────────────────────────────────────

    public void LogPenReading(PenReadingData d)
    {
        if (!IsLogging || _penWriter is null) return;
        _penWriter.WriteLine(
            $"{Ts()}," +
            $"{d.RawPressure},{d.NormalizedPressure:F6},{d.SmoothedPressure:F6}," +
            $"{d.Azimuth:F2},{d.Altitude:F2},{d.TiltX:F2},{d.TiltY:F2}," +
            $"{d.TipDown},{d.Barrel1Down},{d.Barrel2Down}");
    }

    public void LogScaleReading(ScaleRecord record)
    {
        if (!IsLogging || _scaleWriter is null) return;
        // Force_gf is the signed parsed value, formatted to the scale's own
        // resolution (min 2 dp so old single-decimal logs are unchanged) so a
        // finer scale's digits aren't rounded away. RawLine is the verbatim
        // serial line (quoted — it can contain commas, e.g. "ST,GS -50.00g").
        int dp = Math.Max(2, record.DecimalPlaces);
        _scaleWriter.WriteLine(
            $"{Ts()}," +
            $"{record.ReadingAsDouble.ToString("F" + dp, CultureInfo.InvariantCulture)}," +
            $"{CsvQuote(record.Line)}");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string Ts() =>
        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");

    /// <summary>Wraps a field in double quotes (escaping embedded quotes) for CSV.</summary>
    private static string CsvQuote(string s) =>
        $"\"{s.Replace("\"", "\"\"")}\"";

    private static StreamWriter OpenCsv(string path) =>
        new(path, append: false) { AutoFlush = false };
}
