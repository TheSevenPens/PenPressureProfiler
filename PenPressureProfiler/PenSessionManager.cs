using Avalonia.Threading;
using WinPenKit;

namespace PenPressureProfiler;

/// <summary>
/// Manages the WinTab pen input session and emits <see cref="PenReadingData"/>
/// snapshots on a ~60fps poll timer. All callbacks are invoked on the UI thread.
/// </summary>
public sealed class PenSessionManager : IDisposable
{
    private const int MovingAverageWindow = 200;
    private const int PollIntervalMs = 16;

    private readonly Action<PenReadingData>      _onPenData;
    private readonly Func<string, string, Task>  _showError;

    private IPenSession?     _session;
    private DispatcherTimer? _timer;
    private readonly PenButtonTracker _buttons = new();
    private readonly MovingAverage    _ma = new(MovingAverageWindow);
    private bool _prevTip, _prevBarrel1, _prevBarrel2;

    public PenSessionManager(
        Action<PenReadingData>     onPenData,
        Func<string, string, Task> showError)
    {
        _onPenData = onPenData;
        _showError = showError;
    }

    public void Start()
    {
        try
        {
            var apis = PenSessionFactory.GetAvailableApis();
            if (!apis.Contains(InputApi.WintabSystem))
            {
                _ = _showError("WinTab is not available. Is the tablet driver installed?", "Initialization Error");
                return;
            }

            var session = PenSessionFactory.Create(InputApi.WintabSystem);
            var error = session.Start();
            if (error is not null)
            {
                _ = _showError($"Failed to start pen session: {error}", "Initialization Error");
                session.Dispose();
                return;
            }

            _session = session;
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(PollIntervalMs) };
            _timer.Tick += OnTick;
            _timer.Start();
        }
        catch (Exception ex)
        {
            _ = _showError($"Failed to initialize WinPenKit: {ex.Message}", "Initialization Error");
        }
    }

    public void Stop()
    {
        if (_timer is not null)
        {
            _timer.Stop();
            _timer.Tick -= OnTick;
            _timer = null;
        }

        if (_session is not null)
        {
            _session.Stop();
            _session.Dispose();
            _session = null;
        }

        _buttons.Reset();
        _ma.Clear();
        _prevTip = _prevBarrel1 = _prevBarrel2 = false;
    }

    public void Dispose() => Stop();

    // ── Private ──────────────────────────────────────────────────────────────

    private void OnTick(object? sender, EventArgs e)
    {
        if (_session is null) return;

        var points = _session.DrainPoints();
        if (points.Length == 0) return;

        foreach (var pt in points)
            _buttons.Update(pt);

        bool curTip     = _buttons.IsTipDown;
        bool curBarrel1 = _buttons.IsBarrelDown(1);
        bool curBarrel2 = _buttons.IsBarrelDown(2);

        // Clear the moving average on any button release so the smoothed value
        // resets when the pen lifts, giving a clean reading for the next press.
        if ((_prevTip && !curTip) || (_prevBarrel1 && !curBarrel1) || (_prevBarrel2 && !curBarrel2))
            _ma.Clear();

        _prevTip     = curTip;
        _prevBarrel1 = curBarrel1;
        _prevBarrel2 = curBarrel2;

        double maxPressure = _session.MaxPressure;
        double normalized = 0;
        foreach (var pt in points)
        {
            normalized = pt.Pressure / maxPressure;
            _ma.AddSample(normalized);
        }

        var last = points[points.Length - 1];
        _onPenData(new PenReadingData(
            RawPressure:       last.Pressure,
            NormalizedPressure: normalized,
            SmoothedPressure:  _ma.GetAverage(),
            Azimuth:           last.Azimuth,
            Altitude:          last.Altitude,
            TiltX:             last.TiltX,
            TiltY:             last.TiltY,
            TipDown:           curTip,
            Barrel1Down:       curBarrel1,
            Barrel2Down:       curBarrel2
        ));
    }
}
