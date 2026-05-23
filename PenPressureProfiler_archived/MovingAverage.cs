namespace PenPressureProfiler;

public class MovingAverage
{
    private readonly int windowSize;
    private readonly Queue<double> samples;

    public MovingAverage(int size)
    {
        windowSize = size;
        samples = new Queue<double>(windowSize);
    }

    public void AddSample(double value)
    {
        samples.Enqueue(value);

        if (samples.Count > windowSize)
            samples.Dequeue();
    }

    /// <summary>
    /// Recomputes from the current window on every call — O(windowSize) but
    /// eliminates the floating-point drift that incremental sum accumulation
    /// produces over long sessions.
    /// </summary>
    public double GetAverage() =>
        samples.Count == 0 ? 0.0 : samples.Sum() / samples.Count;

    public int SampleCount => samples.Count;

    public void Clear() => samples.Clear();
}
