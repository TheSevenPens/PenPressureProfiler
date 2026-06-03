
namespace PenPressureProfiler.ViewModels;

/// <summary>
/// View-model row for the Auto-capture panel's list. <see cref="SourceIndex"/>
/// is the 0-based offset into <c>StabilityController.Captures</c> (used by the
/// per-card ✕ delete) and is *not* the same as <see cref="Number"/> (which
/// is the 1-based position in the currently-sorted display order).
/// </summary>
public sealed record StabilityCaptureCard(
    int                          SourceIndex,
    string                       Number,
    IReadOnlyList<EstimateField> Fields);
