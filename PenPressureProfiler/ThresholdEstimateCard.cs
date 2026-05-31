namespace PenPressureProfiler;

/// <summary>
/// View-model row for the Threshold panel's estimate list. The pre-formatted
/// strings let the XAML data template bind without converters; <see cref="Index"/>
/// is the 0-based offset into the active controller's <c>Estimates</c> list and
/// is used by the per-card delete button.
///
/// <see cref="RawText"/> is the raw driver pressure value at the boundary
/// (always "0" for IAF modes, the driver's MaxPressure integer for MAX);
/// <see cref="LogicalText"/> is the corresponding logical percent
/// ("0%" / "100%").
/// </summary>
public sealed record ThresholdEstimateCard(
    int    Index,
    string Number,
    string PhysicalText,
    string RawText,
    string LogicalText);
