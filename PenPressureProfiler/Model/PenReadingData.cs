namespace PenPressureProfiler.Model;

/// <summary>
/// Snapshot of pen state emitted by <see cref="PenSessionManager"/> each poll tick.
/// </summary>
public readonly record struct PenReadingData(
    uint   RawPressure,
    double NormalizedPressure,
    double SmoothedPressure,
    double Azimuth,
    double Altitude,
    double TiltX,
    double TiltY,
    int    Z,             // WinTab hover height (raw device units); 0 when unsupported
    bool   SupportsZ,     // true only for backends that report Z (WinTab)
    bool   TipDown,
    bool   Barrel1Down,
    bool   Barrel2Down,
    int    PacketCount    // packets drained in this tick; 0 on idle ticks
);
