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
    private bool         _prevTip, _prevBarrel1, _prevBarrel2;
    private PenReadingData _lastReading = default;

    /// <summary>True when the session is active and polling.</summary>
    public bool IsRunning => _session is not null && _timer is not null;

    public PenSessionManager(
        Action<PenReadingData>     onPenData,
        Func<string, string, Task> showError)
    {
        _onPenData = onPenData;
        _showError = showError;
    }

    /// <summary>Start a factory-based WinTab session.</summary>
    public void Start(InputApi api = InputApi.WintabSystem)
    {
        try
        {
            var available = PenSessionFactory.GetAvailableApis();
            if (!available.Contains(api))
            {
                _ = _showError($"{api} is not available on this system.", "Initialization Error");
                return;
            }

            var session = PenSessionFactory.Create(api);
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

    /// <summary>Start an Avalonia-pointer session attached to <paramref name="element"/>.</summary>
    public void StartAvalonia(Avalonia.Controls.Control element)
    {
        try
        {
            var session = new WinPenKit.Avalonia.AvaloniaPointerSession(element);
            var error = session.Start();
            if (error is not null)
            {
                _ = _showError($"Failed to start Avalonia pointer session: {error}", "Initialization Error");
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
            _ = _showError($"Failed to initialize Avalonia pointer session: {ex.Message}", "Initialization Error");
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

        if (points.Length > 0)
        {
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
            double normalized  = 0;
            foreach (var pt in points)
            {
                normalized = pt.Pressure / maxPressure;
                _ma.AddSample(normalized);
            }

            var last = points[points.Length - 1];
            _lastReading = new PenReadingData(
                RawPressure:        last.Pressure,
                NormalizedPressure: normalized,
                SmoothedPressure:   _ma.GetAverage(),
                Azimuth:            last.Azimuth,
                Altitude:           last.Altitude,
                TiltX:              last.TiltX,
                TiltY:              last.TiltY,
                TipDown:            curTip,
                Barrel1Down:        curBarrel1,
                Barrel2Down:        curBarrel2,
                PacketCount:        points.Length
            );
        }
        else
        {
            // No new packets this tick. When the tip is down (pen pressing but
            // not moving — common with AvaloniaPointerSession which only fires
            // on PointerMoved), preserve the last raw/normalized pressure so the
            // log and UI don't flicker to zero between motion events.
            // When the tip is up, reset to zero for a clean idle baseline.
            _lastReading = _lastReading with
            {
                RawPressure        = _lastReading.TipDown ? _lastReading.RawPressure        : 0,
                NormalizedPressure = _lastReading.TipDown ? _lastReading.NormalizedPressure : 0,
                SmoothedPressure   = _ma.GetAverage(),
                PacketCount        = 0
            };
        }

        _onPenData(_lastReading);
    }
}
