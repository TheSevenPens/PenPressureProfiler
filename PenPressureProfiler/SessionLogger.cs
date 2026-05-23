using System.IO;

namespace PenPressureProfiler;

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
        _scaleWriter.WriteLine("Timestamp,Force_gf");

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
        _scaleWriter.WriteLine($"{Ts()},{record.ReadingAsString}");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string Ts() =>
        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");

    private static StreamWriter OpenCsv(string path) =>
        new(path, append: false) { AutoFlush = false };
}
