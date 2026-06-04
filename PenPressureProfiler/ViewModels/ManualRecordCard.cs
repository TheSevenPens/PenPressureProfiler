
namespace PenPressureProfiler.ViewModels;

/// <summary>
/// View-model row for the Manual panel's record list. <see cref="SourceIndex"/>
/// is the 0-based offset into <c>PressureRecordCollection.Items</c> (used by
/// the per-card ✕ delete) and is *not* the same as <see cref="Number"/> (which
/// is the 1-based position in the currently-sorted display order).
/// </summary>
public sealed record ManualRecordCard(
    int                           SourceIndex,
    string                        Number,
    IReadOnlyList<ReadingSegment> Segments);
