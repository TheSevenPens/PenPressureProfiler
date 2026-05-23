namespace PenPressureProfiler;

public class PressureRecord
{
    public double PhysicalPressure { get; }
    public double LogicalPressure  { get; }   // 0.0–1.0 fraction

    public PressureRecord(double physical, double logical)
    {
        PhysicalPressure = physical;
        LogicalPressure  = logical;
    }

    public override string ToString() =>
        $"{PhysicalPressure:F0} gf  →  {LogicalPressure * 100.0:F1} %";
}
