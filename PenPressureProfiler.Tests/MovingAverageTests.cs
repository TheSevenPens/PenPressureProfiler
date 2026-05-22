using PenPressureProfiler;

namespace PenPressureProfiler.Tests;

public class MovingAverageTests
{
    [Fact]
    public void GetAverage_NoSamples_ReturnsZero()
    {
        var ma = new MovingAverage(10);
        Assert.Equal(0.0, ma.GetAverage());
    }

    [Fact]
    public void GetAverage_WithinWindow_IsExactMean()
    {
        var ma = new MovingAverage(5);
        ma.AddSample(1.0);
        ma.AddSample(2.0);
        ma.AddSample(3.0);
        Assert.Equal(2.0, ma.GetAverage(), precision: 10);
    }

    [Fact]
    public void GetAverage_ExceedsWindow_SlidesCorrectly()
    {
        var ma = new MovingAverage(3);
        ma.AddSample(10.0);
        ma.AddSample(20.0);
        ma.AddSample(30.0);
        ma.AddSample(40.0); // evicts 10
        // window = [20, 30, 40] → mean = 30
        Assert.Equal(30.0, ma.GetAverage(), precision: 10);
    }

    [Fact]
    public void SampleCount_TracksAddedSamplesUpToWindow()
    {
        var ma = new MovingAverage(3);
        Assert.Equal(0, ma.SampleCount);
        ma.AddSample(1.0);
        ma.AddSample(2.0);
        Assert.Equal(2, ma.SampleCount);
        ma.AddSample(3.0);
        ma.AddSample(4.0); // window full, stays at 3
        Assert.Equal(3, ma.SampleCount);
    }

    [Fact]
    public void Clear_ResetsAverageAndCount()
    {
        var ma = new MovingAverage(5);
        ma.AddSample(100.0);
        ma.AddSample(200.0);
        ma.Clear();
        Assert.Equal(0, ma.SampleCount);
        Assert.Equal(0.0, ma.GetAverage());
    }
}
