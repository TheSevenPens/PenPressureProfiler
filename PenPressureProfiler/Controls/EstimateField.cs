namespace PenPressureProfiler.Controls;

/// <summary>One labeled value inside an <see cref="EstimateCard"/>.</summary>
/// <param name="Label">Caption (e.g. "PHYS:", "LOG%:"). Empty hides the caption.</param>
/// <param name="Value">Pre-formatted value text (e.g. "3.42 gf", "0").</param>
public sealed record EstimateField(string Label, string Value);
