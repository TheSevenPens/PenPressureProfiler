namespace PenPressureProfiler;

/// <summary>
/// A snapshot of pen state emitted by <see cref="PenSessionManager"/> each poll tick.
/// </summary>
public readonly record struct PenReadingData(
    uint   RawPressure,
    double NormalizedPressure,
    double SmoothedPressure,
    double Azimuth,
    double Altitude,
    double TiltX,
    double TiltY,
    bool   TipDown,
    bool   Barrel1Down,
    bool   Barrel2Down,
    int    PacketCount   // WinTab packets drained in this poll tick
);
