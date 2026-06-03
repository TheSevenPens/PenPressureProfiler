namespace PenPressureProfiler.Model;

public class MovingAverage
{
    private readonly int _windowSize;
    private readonly Queue<double> _samples;

    public MovingAverage(int size)
    {
        _windowSize = size;
        _samples    = new Queue<double>(_windowSize);
    }

    public void AddSample(double value)
    {
        _samples.Enqueue(value);
        if (_samples.Count > _windowSize)
            _samples.Dequeue();
    }

    /// <summary>
    /// Recomputes from the current window — eliminates incremental float drift.
    /// </summary>
    public double GetAverage() =>
        _samples.Count == 0 ? 0.0 : _samples.Sum() / _samples.Count;

    public int SampleCount => _samples.Count;
    public void Clear()    => _samples.Clear();
}
