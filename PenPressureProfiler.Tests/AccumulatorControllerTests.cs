using PenPressureProfiler.Detection;

namespace PenPressureProfiler.Tests;

public class AccumulatorControllerTests
{
    // OnPenData only reads RawPressure; the rest can be defaults.
    private static PenReadingData Pen(uint raw) =>
        new(raw, 0, 0, 0, 0, 0, 0, 0, false, false, false, false, PacketCount: 1);

    private static void Feed(AccumulatorController c, uint raw, double gf)
    {
        c.OnPenData(Pen(raw));   // sets the pen state used to classify the next scale sample
        c.OnScaleData(gf);       // buckets the force; raw >= 1 (IAF threshold) → at-or-over
    }

    [Fact]
    public void ClearBucket_ZeroesThatBucketOnly()
    {
        var c = new AccumulatorController();   // IAF default: 0–10 gf, 0.5 gf buckets
        Feed(c, raw: 0, gf: 2.0);              // under, bucket 4 ([2.0, 2.5))
        Feed(c, raw: 5, gf: 2.0);              // at-or-over, same bucket
        Feed(c, raw: 5, gf: 3.0);              // at-or-over, bucket 6 ([3.0, 3.5))

        int b2 = (int)(2.0 / 0.5);  // 4
        int b3 = (int)(3.0 / 0.5);  // 6
        Assert.Equal(1L, c.UnderCounts[b2]);
        Assert.Equal(1L, c.AtOrOverCounts[b2]);
        Assert.Equal(1L, c.AtOrOverCounts[b3]);

        Assert.True(c.ClearBucket(b2));

        Assert.Equal(0L, c.UnderCounts[b2]);
        Assert.Equal(0L, c.AtOrOverCounts[b2]);
        Assert.Equal(1L, c.AtOrOverCounts[b3]);   // other buckets untouched
    }

    [Fact]
    public void ClearBucket_OutOfRangeIndex_ReturnsFalse()
    {
        var c = new AccumulatorController();
        Assert.False(c.ClearBucket(-1));
        Assert.False(c.ClearBucket(c.BucketCount));
    }

    [Fact]
    public void ClearBelow_And_ClearAbove_ZeroOutOfRangeCounts()
    {
        var c = new AccumulatorController();   // 0–10 gf
        Feed(c, raw: 0, gf: -1.0);             // below range, under
        Feed(c, raw: 5, gf: 50.0);             // above range, at-or-over
        Assert.Equal(1L, c.BelowUnder);
        Assert.Equal(1L, c.AboveAtOrOver);

        c.ClearBelow();
        c.ClearAbove();

        Assert.Equal(0L, c.BelowUnder);
        Assert.Equal(0L, c.BelowAtOrOver);
        Assert.Equal(0L, c.AboveUnder);
        Assert.Equal(0L, c.AboveAtOrOver);
    }
}
