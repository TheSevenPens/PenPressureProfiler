using PenPressureProfiler.Detection;

namespace PenPressureProfiler.Tests;

public class AccumulatorBucketWidthTests
{
    [Fact]
    public void Max_target_offers_100_and_200_gf_buckets()
    {
        Assert.Contains(100.0, AccumulatorController.MaxPressureBucketWidths);
        Assert.Contains(200.0, AccumulatorController.MaxPressureBucketWidths);
    }

    [Fact]
    public void Width_that_does_not_divide_the_range_gets_a_short_last_bucket()
    {
        var c = new AccumulatorController();
        c.SetTarget(AccumTarget.MaxPressure);   // default range 0–500 gf
        c.SetWidth(200);

        Assert.Equal(3, c.BucketCount);          // [0,200) [200,400) [400,500)
        Assert.Equal(400.0, c.BucketLowerGf(2));
        Assert.Equal(500.0, c.BucketUpperGf(2)); // capped at the range max
        Assert.Equal(200.0, c.BucketUpperGf(0));
    }

    [Fact]
    public void Force_in_the_short_last_bucket_lands_there()
    {
        var c = new AccumulatorController();
        c.SetTarget(AccumTarget.MaxPressure);
        c.SetWidth(200);
        c.OnScaleData(450);                      // pen raw 0 → "under"
        Assert.Equal(1L, c.UnderCounts[2]);
        Assert.Equal(0L, c.UnderCounts[1]);
    }

    [Fact]
    public void Width_that_divides_the_range_is_unchanged()
    {
        var c = new AccumulatorController();
        c.SetTarget(AccumTarget.MaxPressure);
        c.SetWidth(100);
        Assert.Equal(5, c.BucketCount);
        Assert.Equal(500.0, c.BucketUpperGf(4));
    }
}
