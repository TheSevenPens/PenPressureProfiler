namespace PenPressureProfiler;

public record class ScaleRecord
{
    public required string Line            { get; set; }
    public required string ReadingAsString { get; set; }
    public double          ReadingAsDouble { get; set; }
}
