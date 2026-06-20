using Avalonia.Controls;
using Avalonia.Threading;
using WinPenKit;
using WinPenKit.Avalonia;

namespace PenPressureProfiler.Sessions;

/// <summary>
/// Owns the WinPenKit pen session and a 60 fps poll timer.
/// Always attaches <see cref="AvaloniaPointerSession"/> to the
/// <paramref name="penInputSurface"/> supplied at construction time —
/// a plain <see cref="Avalonia.Controls.Border"/> with no interactive
/// children — so pointer events are never intercepted by child controls.
/// All callbacks are invoked on the UI thread.
/// </summary>
public sealed class PenSessionManager : IDisposable
{
    private const int MovingAverageWindow = 200;
    private const int PollIntervalMs      = 16;

    private readonly Control                   _penInputSurface;
    private readonly Action<PenReadingData>    _onPenData;
    private readonly Func<string, string, Task> _showError;

    private IPenSession?         _session;
    private ControlCaptureRegion? _captureRegion;
    private DispatcherTimer?     _timer;
    private readonly PenButtonTracker _buttons = new();
    private readonly MovingAverage    _ma      = new(MovingAverageWindow);
    private bool           _prevTip, _prevBarrel1, _prevBarrel2;
    private PenReadingData _lastReading = default;

    public bool IsRunning => _session is not null && _timer is not null;

    /// <summary>
    /// Driver-reported maximum raw pressure value, or 0 if no session is
    /// currently running. The pen reports exactly this raw value when logical
    /// pressure is saturated at 100%.
    /// </summary>
    public int MaxPressure => _session?.MaxPressure ?? 0;

    public PenSessionManager(
        Control                    penInputSurface,
        Action<PenReadingData>     onPenData,
        Func<string, string, Task> showError)
    {
        _penInputSurface = penInputSurface;
        _onPenData       = onPenData;
        _showError       = showError;
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>Start a session for the given API.</summary>
    public void Start(InputApi api)
    {
        try
        {
            IPenSession session;

            if (api == InputApi.AvaloniaPointer)
            {
                // Always attach to PenInputSurface — see class summary.
                session = new AvaloniaPointerSession(_penInputSurface);
            }
            else
            {
                var available = PenSessionFactory.GetAvailableApis();
                if (!available.Contains(api))
                {
                    _ = _showError($"{api} is not available on this system.",
                                   "Initialization Error");
                    return;
                }
                session = PenSessionFactory.Create(api);
            }

            // Scope every backend to the PenInputSurface so the pen behaves
            // identically across APIs: WinTab is desktop-global by default and
            // would otherwise report points anywhere on screen, unlike the
            // control-scoped pointer backends. See [[winpenkit-integration]].
            _captureRegion = new ControlCaptureRegion(_penInputSurface);
            session.CaptureRegion = _captureRegion;

            // WinTab needs the app HWND; AvaloniaPointerSession ignores it.
            var hwnd = GetAppHwnd();
            var error = session.Start(hwnd);
            if (error is not null)
            {
                _ = _showError($"Failed to start pen session: {error}",
                               "Initialization Error");
                session.Dispose();
                _captureRegion?.Dispose();
                _captureRegion = null;
                return;
            }

            _session = session;
            _timer   = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(PollIntervalMs) };
            _timer.Tick += OnTick;
            _timer.Start();
        }
        catch (Exception ex)
        {
            _ = _showError($"Failed to initialize pen session: {ex.Message}",
                           "Initialization Error");
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
        if (_captureRegion is not null)
        {
            _captureRegion.Dispose();
            _captureRegion = null;
        }
        _buttons.Reset();
        _ma.Clear();
        _prevTip = _prevBarrel1 = _prevBarrel2 = false;
        _lastReading = default;
    }

    public void Dispose() => Stop();

    // ── Private ───────────────────────────────────────────────────────────────

    private IntPtr GetAppHwnd()
    {
        try
        {
            if (TopLevel.GetTopLevel(_penInputSurface) is { } tl &&
                tl.TryGetPlatformHandle() is { } ph)
                return ph.Handle;
        }
        catch { }
        return IntPtr.Zero;
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (_session is null) return;

        bool supportsZ = _session.Capabilities.HasFlag(PenCapabilities.ZHeight);
        var points = _session.DrainPoints();

        if (points.Length > 0)
        {
            foreach (var pt in points)
                _buttons.Update(pt);

            bool curTip     = _buttons.IsTipDown;
            bool curBarrel1 = _buttons.IsBarrelDown(1);
            bool curBarrel2 = _buttons.IsBarrelDown(2);

            // Clear MA on button release so next press starts fresh.
            if ((_prevTip && !curTip) ||
                (_prevBarrel1 && !curBarrel1) ||
                (_prevBarrel2 && !curBarrel2))
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

            var last = points[^1];
            _lastReading = new PenReadingData(
                RawPressure:        last.Pressure,
                NormalizedPressure: normalized,
                SmoothedPressure:   _ma.GetAverage(),
                Azimuth:            last.Azimuth,
                Altitude:           last.Altitude,
                TiltX:              last.TiltX,
                TiltY:              last.TiltY,
                Z:                  last.Z,
                SupportsZ:          supportsZ,
                TipDown:            curTip,
                Barrel1Down:        curBarrel1,
                Barrel2Down:        curBarrel2,
                PacketCount:        points.Length
            );
        }
        else
        {
            // No new packets. Preserve last pressure while tip is down (pen
            // pressing but not moving, common with AvaloniaPointerSession
            // which only fires PointerMoved events).
            _lastReading = _lastReading with
            {
                RawPressure        = _lastReading.TipDown ? _lastReading.RawPressure        : 0,
                NormalizedPressure = _lastReading.TipDown ? _lastReading.NormalizedPressure : 0,
                SmoothedPressure   = _ma.GetAverage(),
                SupportsZ          = supportsZ,
                PacketCount        = 0
            };
        }

        _onPenData(_lastReading);
    }
}
