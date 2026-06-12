using PenPressureProfiler.Detection;
using PenPressureProfiler.Model;

namespace PenPressureProfiler.Tests;

/// <summary>
/// Covers the scale-bracketed IAF capture: the estimate is the midpoint between
/// the last 0%-reading scale force and the first non-zero-reading scale force,
/// captured at scale-update boundaries.
/// </summary>
public class IafBracketTests
{
    private static PenReadingData Pen(uint raw) =>
        new(RawPressure: raw,
            NormalizedPressure: raw / 8192.0,
            SmoothedPressure:   raw / 8192.0,
            Azimuth: 0, Altitude: 0, TiltX: 0, TiltY: 0,
            TipDown: raw > 0, Barrel1Down: false, Barrel2Down: false,
            PacketCount: 1);

    // ── From below (push sweep) ────────────────────────────────────────────

    [Fact]
    public void Below_SlowPress_BracketsActivationAtMidpoint()
    {
        var c = new IafBelowController();
        c.OnScaleData(0.05);   // arm (≤ MaxRestingGf)
        c.OnPenData(Pen(0));   // pen still 0
        c.OnScaleData(0.40);   // 0%-side force = 0.40
        c.OnPenData(Pen(1));   // pen activates
        c.OnScaleData(0.60);   // first on-side scale sample → fires

        var e = Assert.Single(c.Estimates);
        Assert.Equal(0.40, e.FirstZeroGf,   3);
        Assert.Equal(0.60, e.LastNonZeroGf, 3);
        Assert.Equal(0.50, e.IafGf,         3);   // midpoint
        Assert.Equal(1u,   e.LastNonZeroRaw);
    }

    [Fact]
    public void Below_NoCaptureUntilScaleSampleAfterActivation()
    {
        var c = new IafBelowController();
        c.OnScaleData(0.05);
        c.OnPenData(Pen(0));
        c.OnScaleData(0.40);
        c.OnPenData(Pen(1));   // activation, but no scale sample yet
        Assert.Empty(c.Estimates);

        c.OnScaleData(0.60);   // now it fires
        Assert.Single(c.Estimates);
    }

    [Fact]
    public void Below_PressWithoutArming_RejectedAndNoEstimate()
    {
        var c = new IafBelowController();
        bool rejected = false;
        c.SweepRejected += () => rejected = true;

        c.OnScaleData(5.0);    // never dips to the rest floor → not armed
        c.OnPenData(Pen(0));
        c.OnPenData(Pen(1));   // activation edge without arming

        Assert.True(rejected);
        Assert.Empty(c.Estimates);
    }

    [Fact]
    public void Below_ArmIsConsumed_NoSecondCaptureWhileStillPressed()
    {
        var c = new IafBelowController();
        c.OnScaleData(0.05);
        c.OnPenData(Pen(0));
        c.OnScaleData(0.40);
        c.OnPenData(Pen(1));
        c.OnScaleData(0.60);   // fires once
        Assert.Single(c.Estimates);

        c.OnScaleData(0.70);   // still pressed — must not add more
        c.OnScaleData(0.80);
        Assert.Single(c.Estimates);
    }

    // ── From above (release sweep) ─────────────────────────────────────────

    [Fact]
    public void Above_SlowRelease_BracketsAtMidpoint()
    {
        var c = new IafController();
        c.OnPenData(Pen(2000));    // pressing hard
        c.OnScaleData(50.0);       // peak ≥ MinPeakGf → armed; on-force 50
        c.OnPenData(Pen(3));       // still on, now light
        c.OnScaleData(2.0);        // last on-force = 2.0 (< MinPeakGf → clean)
        c.OnPenData(Pen(0));       // released
        c.OnScaleData(0.5);        // first 0% force = 0.5 → fires

        var e = Assert.Single(c.Estimates);
        Assert.Equal(0.5,  e.FirstZeroGf,   3);
        Assert.Equal(2.0,  e.LastNonZeroGf, 3);
        Assert.Equal(1.25, e.IafGf,         3);   // midpoint
        Assert.Equal(3u,   e.LastNonZeroRaw);
    }

    [Fact]
    public void Above_NotArmed_NoEstimate()
    {
        var c = new IafController();
        c.OnPenData(Pen(100));
        c.OnScaleData(5.0);    // peak 5 < MinPeakGf → never armed
        c.OnPenData(Pen(0));
        c.OnScaleData(0.5);
        Assert.Empty(c.Estimates);
    }

    [Fact]
    public void Above_ReleaseUnderLoad_RejectedAsJump()
    {
        var c = new IafController();
        bool rejected = false;
        c.SweepRejected += () => rejected = true;

        c.OnPenData(Pen(2000));
        c.OnScaleData(50.0);   // armed; last on-force stays heavy
        c.OnPenData(Pen(0));   // jumped straight to zero while loaded
        c.OnScaleData(0.5);    // on-force was 50 ≥ MinPeakGf → reject

        Assert.True(rejected);
        Assert.Empty(c.Estimates);
    }
}
