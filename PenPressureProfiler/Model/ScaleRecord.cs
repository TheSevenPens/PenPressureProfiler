namespace PenPressureProfiler.Model;

public record class ScaleRecord
{
    public required string Line            { get; set; }
    public required string ReadingAsString { get; set; }
    public double          ReadingAsDouble { get; set; }

    /// <summary>
    /// Number of fractional digits the scale reported for this reading (e.g. 1
    /// for "50.0", 2 for "0.04"). Reflects the device's resolution so it can be
    /// shown and stored without being rounded away. 0 when the reading had no
    /// decimal point.
    /// </summary>
    public int DecimalPlaces { get; set; }
}
