using Avalonia.Threading;
using System.IO.Ports;

namespace PenPressureProfiler.Sessions;

/// <summary>
/// Manages serial communication with a digital scale.
/// Callers await <see cref="StartAsync"/> which returns when the
/// session ends (cancelled or on error). All <see cref="OnReading"/>
/// callbacks are marshalled onto the UI thread.
/// </summary>
public sealed class ScaleSessionManager : IDisposable
{
    private const int ReadDelayMs = 10;

    /// <summary>
    /// Empirically measured pen → scale response lag (via the Measure Scale Lag
    /// tool): a scale reading reflects the true applied force ~this many ms ago.
    /// <para>
    /// NOT yet applied to any threshold/IAF calculation — stored here so the lag
    /// compensation can reference a single source of truth when we wire it in.
    /// </para>
    /// </summary>
    public const double ResponseLagMs = 245;

    private readonly Action<ScaleRecord>       _onReading;
    private readonly Func<string, string, Task> _showError;
    private CancellationTokenSource?           _cts;

    // The currently open port, held so commands (e.g. TARE) can be written to it
    // while the read loop runs. Null whenever no session is active.
    private SerialPort? _port;

    public bool IsReading { get; private set; }

    /// <summary>True after the most recent <see cref="StartAsync"/> attempt
    /// failed (port open error, IO error, etc). Cleared on the next call.</summary>
    public bool HasError { get; private set; }

    public ScaleSessionManager(
        Action<ScaleRecord>        onReading,
        Func<string, string, Task> showError)
    {
        _onReading = onReading;
        _showError = showError;
    }

    public async Task StartAsync(string portName)
    {
        if (IsReading) return;

        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        IsReading = true;
        HasError  = false;

        SerialPort? port = null;
        try
        {
            port = new SerialPort(portName);
            port.Open();
            _port = port;
            await ReadLoopAsync(port, _cts.Token);
        }
        catch (UnauthorizedAccessException ex)
        {
            HasError = true;
            _ = _showError($"Failed to open {portName} — Access Denied\r\n{ex.Message}",
                           "COM Port Error");
        }
        catch (IOException ex)
        {
            HasError = true;
            _ = _showError($"Failed to open {portName} — IO Error\r\n{ex.Message}",
                           "COM Port Error");
        }
        catch (Exception ex)
        {
            HasError = true;
            _ = _showError($"Failed to open {portName}\r\n{ex.GetType().Name}: {ex.Message}",
                           "COM Port Error");
        }
        finally
        {
            _port = null;
            DisposePort(port);
            IsReading = false;
        }
    }

    /// <summary>
    /// Sends the scale's TARE (zero) command — the bytes <c>"T\r\n"</c> — over the
    /// open port. No-op when no session is active; reports write failures via the
    /// same error path as the read loop. Safe to call from the UI thread while the
    /// background read loop is running (serial reads and writes are independent).
    /// </summary>
    public void SendTare()
    {
        var port = _port;
        if (port is null || !port.IsOpen) return;
        try
        {
            port.Write("T\r\n");
        }
        catch (Exception ex)
        {
            HasError = true;
            _ = _showError($"Failed to send TARE: {ex.Message}", "Serial Port Error");
        }
    }

    public void Stop()    => _cts?.Cancel();
    public void Dispose() { Stop(); _cts?.Dispose(); _cts = null; }

    // ── Private ───────────────────────────────────────────────────────────────

    private async Task ReadLoopAsync(SerialPort port, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                if (port.IsOpen && port.BytesToRead > 0)
                {
                    string line = await Task.Run(() => port.ReadLine(), ct);
                    var parsed = ScaleLineParser.Parse(line);
                    if (parsed.Parsed && parsed.ScaleRecord is { } record)
                        Dispatcher.UIThread.Post(() => _onReading(record));
                }
                await Task.Delay(ReadDelayMs, ct);
            }
        }
        catch (OperationCanceledException) { /* normal stop */ }
        catch (IOException ex)
        {
            HasError = true;
            _ = _showError($"Serial port IO error: {ex.Message}", "Serial Port Error");
        }
        catch (Exception ex)
        {
            HasError = true;
            _ = _showError($"Serial port error: {ex.Message}", "Serial Port Error");
        }
    }

    private static void DisposePort(SerialPort? port)
    {
        if (port is null) return;
        try { if (port.IsOpen) port.Close(); } catch { }
        port.Dispose();
    }
}
