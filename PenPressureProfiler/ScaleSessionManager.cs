using System.IO.Ports;

namespace PenPressureProfiler;

/// <summary>
/// Manages serial communication with a digital scale.
/// Opens and closes the COM port per session; callers await <see cref="StartAsync"/>
/// which returns when the session ends (cancelled or on error).
/// </summary>
public sealed class ScaleSessionManager : IDisposable
{
    private const int ReadDelayMs = 10;

    private readonly Action<string>              _onReading;
    private readonly Func<string, string, Task>  _showError;

    private CancellationTokenSource? _cts;

    public bool IsReading { get; private set; }

    public ScaleSessionManager(
        Action<string>             onReading,
        Func<string, string, Task> showError)
    {
        _onReading = onReading;
        _showError = showError;
    }

    /// <summary>
    /// Opens <paramref name="portName"/> and reads until cancelled or an error occurs.
    /// Awaiting this task keeps the button handler alive for the session duration.
    /// </summary>
    public async Task StartAsync(string portName)
    {
        if (IsReading) return;

        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        IsReading = true;

        SerialPort? port = null;
        try
        {
            port = new SerialPort(portName);
            port.Open();
            await ReadLoopAsync(port, _cts.Token);
        }
        catch (UnauthorizedAccessException ex)
        {
            _ = _showError($"Failed to open COM Port - Access Denied\r\n{ex.Message}", "COM Port Error");
        }
        catch (System.IO.IOException ex)
        {
            _ = _showError($"Failed to open COM Port - IO Error\r\n{ex.Message}", "COM Port Error");
        }
        catch (Exception ex)
        {
            _ = _showError($"Failed to open COM Port\r\n{ex.GetType().FullName}\r\n{ex.Message}", "COM Port Error");
        }
        finally
        {
            DisposePort(port);
            IsReading = false;
        }
    }

    public void Stop()
    {
        _cts?.Cancel();
    }

    public void Dispose()
    {
        Stop();
        _cts?.Dispose();
        _cts = null;
    }

    // ── Private ──────────────────────────────────────────────────────────────

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
                    if (parsed.Parsed && parsed.ScaleRecord is not null)
                        _onReading(parsed.ScaleRecord.ReadingAsString);
                }

                await Task.Delay(ReadDelayMs, ct);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal stop — user cancelled via the Stop button. No dialog needed.
        }
        catch (System.IO.IOException ex)
        {
            _ = _showError($"Serial port IO error: {ex.Message}", "Serial Port Error");
        }
        catch (Exception ex)
        {
            _ = _showError($"Serial port error: {ex.Message}", "Serial Port Error");
        }
    }

    private static void DisposePort(SerialPort? port)
    {
        if (port is null) return;
        if (port.IsOpen) port.Close();
        port.Dispose();
    }
}
