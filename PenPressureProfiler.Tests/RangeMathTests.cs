using PenPressureProfiler.Model;

namespace PenPressureProfiler.Tests;

public class RangeMathTests
{
    [Theory]
    [InlineData(0.26, 0.5, 0.5)]
    [InlineData(0.24, 0.5, 0.0)]
    [InlineData(7.3, 1.0, 7.0)]
    [InlineData(-3, 0.5, 0.0)]     // clamped to min
    [InlineData(99, 0.5, 30.0)]    // clamped to max
    public void Snap_rounds_to_step_and_clamps(double value, double step, double expected)
        => Assert.Equal(expected, RangeMath.Snap(value, 0, 30, step), 6);

    [Fact]
    public void Snap_removes_floating_point_noise()
        => Assert.Equal(0.3, RangeMath.Snap(0.1 + 0.2, 0, 1, 0.1));

    [Fact]
    public void Value_and_x_round_trip()
    {
        double x = RangeMath.ValueToX(250, 0, 1000, 400);
        Assert.Equal(100, x, 6);
        Assert.Equal(250, RangeMath.XToValue(x, 0, 1000, 400), 6);
    }

    [Fact]
    public void Lower_handle_cannot_pass_upper()
        => Assert.Equal((9.5, 10.0), RangeMath.MoveHandle(true, 12, 0, 10, 0, 30, 0.5));

    [Fact]
    public void Upper_handle_cannot_pass_lower()
        => Assert.Equal((5.0, 5.5), RangeMath.MoveHandle(false, 2, 5, 10, 0, 30, 0.5));

    [Fact]
    public void Moving_a_handle_snaps_it()
        => Assert.Equal((2.5, 10.0), RangeMath.MoveHandle(true, 2.6, 0, 10, 0, 30, 0.5));

    [Fact]
    public void EnsureContains_widens_scale_to_a_loaded_range()
        => Assert.Equal((0.0, 1200.0), RangeMath.EnsureContains(0, 1000, 100, 1180, 25));

    [Fact]
    public void EnsureContains_keeps_scale_that_already_fits()
        => Assert.Equal((0.0, 30.0), RangeMath.EnsureContains(0, 30, 0, 10, 0.5));
}
