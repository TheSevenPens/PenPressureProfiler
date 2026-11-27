using PenPressureProfiler.Model;

namespace PenPressureProfiler.Tests;

public class ScaleHealthTests
{
    private static readonly DateTime Now = new(2026, 11, 27, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Not_reading_is_off()
        => Assert.Equal(ScaleHealthState.Off, ScaleHealth.Evaluate(false, false, Now, Now));

    [Fact]
    public void Error_is_off_even_while_reading()
        => Assert.Equal(ScaleHealthState.Off, ScaleHealth.Evaluate(true, true, Now, Now));

    [Fact]
    public void Reading_with_no_data_yet_is_no_data()
        => Assert.Equal(ScaleHealthState.NoData, ScaleHealth.Evaluate(true, false, null, Now));

    [Fact]
    public void Recent_reading_is_live()
        => Assert.Equal(ScaleHealthState.Live,
            ScaleHealth.Evaluate(true, false, Now - TimeSpan.FromMilliseconds(100), Now));

    [Fact]
    public void Reading_older_than_timeout_is_no_data()
        => Assert.Equal(ScaleHealthState.NoData,
            ScaleHealth.Evaluate(true, false, Now - ScaleHealth.DataTimeout - TimeSpan.FromMilliseconds(1), Now));
}
