
namespace PenPressureProfiler.ViewModels;

/// <summary>
/// View-model row for the Threshold panel's estimate list. <see cref="Index"/>
/// is the 0-based offset into the active controller's <c>Estimates</c> list
/// and is used by the per-card delete button.
/// </summary>
public sealed record ThresholdEstimateCard(
    int                           Index,
    string                        Number,
    IReadOnlyList<ReadingSegment> Segments);
