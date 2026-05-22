namespace PenPressureProfiler;

public class MovingAverage
{
    private readonly int windowSize;
    private readonly Queue<double> samples;
    private double sum;

    public MovingAverage(int size)
    {
        windowSize = size;
        samples = new Queue<double>(windowSize);
        sum = 0.0;
    }

    public void AddSample(double value)
    {
        samples.Enqueue(value);
        sum += value;

        if (samples.Count > windowSize)
        {
            sum -= samples.Dequeue();
        }
    }

    public double GetAverage() => samples.Count == 0 ? 0.0 : sum / samples.Count;

    public int SampleCount => samples.Count;

    public void Clear()
    {
        samples.Clear();
        sum = 0.0;
    }
}
