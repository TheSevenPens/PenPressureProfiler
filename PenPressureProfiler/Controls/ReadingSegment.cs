namespace PenPressureProfiler.Controls;

/// <summary>One run of an <see cref="EstimateCard"/> reading line. Numbers are
/// rendered bold and the surrounding units/symbols normal, producing e.g.
/// "3.4 gf → 0.00% (0)" with the numbers emphasised.</summary>
/// <param name="Text">The literal text of this run.</param>
/// <param name="Bold">Whether to render the run bold (used for the numbers).</param>
public sealed record ReadingSegment(string Text, bool Bold);
