namespace PenPressureProfiler;

/// <summary>
/// View-model row for the Auto-capture panel's list. Mirrors the card shape
/// used by the Threshold list. <see cref="SourceIndex"/> is the 0-based offset
/// into <c>SweepController.Captures</c> — used by the per-card ✕ delete —
/// and is *not* the same as <see cref="Number"/> (which is the 1-based
/// position in the currently-sorted display order).
/// </summary>
public sealed record SweepCaptureCard(
    int    SourceIndex,
    string Number,
    string PhysicalText,
    string LogicalText,
    string CountText);
