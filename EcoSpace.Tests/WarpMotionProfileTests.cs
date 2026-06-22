// WarpMotionProfileTests.cs
// xUnit tests for WarpMotionProfile — closed-form symmetric smoothstep trapezoid.
//
// Source of truth: Scripts/Math/WarpMotionProfile.cs (read before writing these tests).
//
// Key invariant (D-03): integral of Velocity(t) over [0, T_sel] == D (within tolerance).
// Tests cover:
//   (a) Numeric integration equals D at in-system and intergalactic scales, for f = 1/3
//       and an edge near 0.5.
//   (b) Endpoint speeds: Velocity(0) == vLaunch, Velocity(T_sel) == vTerminal.
//   (c) Cruise speed: Velocity(f·T_sel) == VCruise, Velocity((1−f)·T_sel) == VCruise.
//   (d) All outputs finite for small-D and T_sel = 1 s edge cases.

using System;
using Xunit;

public class WarpMotionProfileTests
{
    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Numerically integrates Velocity(t) over [0, T_sel] using the midpoint rule with
    /// <paramref name="steps"/> steps. Sufficient for asserting the area = D invariant.
    /// </summary>
    private static double Integrate(WarpMotionProfile p, int steps = 100_000)
    {
        double dt  = p.TSel / steps;
        double sum = 0.0;
        for (int i = 0; i < steps; i++)
            sum += p.Velocity((i + 0.5) * dt) * dt;
        return sum;
    }

    // Representative launch and terminal speeds (ManualMaxSpeed = 1e6 m/s, D-09).
    private const double VLaunch   = 1e6;
    private const double VTerminal = 1e6;

    // ── (a) Integral equals D (exact-time invariant) ─────────────────────────

    [Theory]
    [InlineData(1e9,  120.0, 1.0 / 3.0)]   // in-system  ~Mars distance, 2 min, f=1/3
    [InlineData(1e9,  120.0, 0.499)]         // in-system, f near 0.5 (near-triangle)
    [InlineData(1e23, 600.0, 1.0 / 3.0)]   // intergalactic ~10 kly, 10 min, f=1/3
    [InlineData(1e23, 600.0, 0.499)]         // intergalactic, f near 0.5
    public void Integral_EqualsD_WithinRelativeTolerance(double d, double tSel, double f)
    {
        var p = WarpMotionProfile.Solve(d, tSel, f, VLaunch, VTerminal);

        double integrated = Integrate(p);

        // Relative tolerance 0.1 % — sufficient to assert the closed-form invariant
        // while allowing numerical integration rounding (midpoint rule, 100 k steps).
        double relativeTol = 1e-3;
        double absTol      = d * relativeTol;
        Assert.True(
            Math.Abs(integrated - d) <= absTol,
            $"Integral {integrated:E3} != D {d:E3} (tol {absTol:E3}) at d={d}, T={tSel}, f={f}");
    }

    // ── (b) Endpoint speeds ──────────────────────────────────────────────────

    [Theory]
    [InlineData(1e9,  120.0, 1.0 / 3.0)]
    [InlineData(1e23, 600.0, 0.25)]
    public void Velocity_AtZero_IsLaunchSpeed(double d, double tSel, double f)
    {
        var p = WarpMotionProfile.Solve(d, tSel, f, VLaunch, VTerminal);
        Assert.Equal(VLaunch, p.Velocity(0.0), precision: 3);
    }

    [Theory]
    [InlineData(1e9,  120.0, 1.0 / 3.0)]
    [InlineData(1e23, 600.0, 0.25)]
    public void Velocity_AtTSel_IsTerminalSpeed(double d, double tSel, double f)
    {
        var p = WarpMotionProfile.Solve(d, tSel, f, VLaunch, VTerminal);
        Assert.Equal(VTerminal, p.Velocity(tSel), precision: 3);
    }

    // ── (c) Cruise speed at phase boundaries ─────────────────────────────────

    [Theory]
    [InlineData(1e9,  120.0, 1.0 / 3.0)]
    [InlineData(1e23, 600.0, 0.25)]
    public void Velocity_AtEndOfAccel_IsCruiseSpeed(double d, double tSel, double f)
    {
        var p = WarpMotionProfile.Solve(d, tSel, f, VLaunch, VTerminal);
        double tEndAccel = f * tSel;
        // Allow tolerance equal to 0.01% of VCruise (smoothstep endpoint is exact).
        Assert.True(
            Math.Abs(p.Velocity(tEndAccel) - p.VCruise) <= Math.Abs(p.VCruise) * 1e-4 + 1e-6,
            $"Velocity at accel end {p.Velocity(tEndAccel):E6} != VCruise {p.VCruise:E6}");
    }

    [Theory]
    [InlineData(1e9,  120.0, 1.0 / 3.0)]
    [InlineData(1e23, 600.0, 0.25)]
    public void Velocity_AtStartOfDecel_IsCruiseSpeed(double d, double tSel, double f)
    {
        var p = WarpMotionProfile.Solve(d, tSel, f, VLaunch, VTerminal);
        double tStartDecel = (1.0 - f) * tSel;
        Assert.True(
            Math.Abs(p.Velocity(tStartDecel) - p.VCruise) <= Math.Abs(p.VCruise) * 1e-4 + 1e-6,
            $"Velocity at decel start {p.Velocity(tStartDecel):E6} != VCruise {p.VCruise:E6}");
    }

    // ── (d) Edge cases: all outputs finite ───────────────────────────────────

    [Fact]
    public void SmallD_AllOutputsFinite()
    {
        // Very small distance (e.g. nudge 1 m within SOI).
        var p = WarpMotionProfile.Solve(d: 1.0, tSel: 1.0, f: 1.0 / 3.0, VLaunch, VTerminal);
        Assert.True(double.IsFinite(p.VCruise),          "VCruise not finite");
        Assert.True(double.IsFinite(p.Velocity(0.0)),    "Velocity(0) not finite");
        Assert.True(double.IsFinite(p.Velocity(0.5)),    "Velocity(0.5) not finite");
        Assert.True(double.IsFinite(p.Velocity(p.TSel)), "Velocity(TSel) not finite");
    }

    [Fact]
    public void TSelAtFloor_AllOutputsFinite()
    {
        // T_sel = 1 s (minimum allowed by EngageWarp D-06 clamp).
        var p = WarpMotionProfile.Solve(d: 1e9, tSel: 1.0, f: 1.0 / 3.0, VLaunch, VTerminal);
        Assert.True(double.IsFinite(p.VCruise),          "VCruise not finite at T=1s");
        Assert.True(double.IsFinite(p.Velocity(0.0)),    "Velocity(0) not finite at T=1s");
        Assert.True(double.IsFinite(p.Velocity(0.5)),    "Velocity(0.5s) not finite at T=1s");
        Assert.True(double.IsFinite(p.Velocity(1.0)),    "Velocity(T_sel) not finite at T=1s");
    }

    [Fact]
    public void FAtZero_AllOutputsFinite()
    {
        // f = 0: no accel/decel ramp — pure cruise (cruise = T_sel).
        var p = WarpMotionProfile.Solve(d: 1e9, tSel: 120.0, f: 0.0, VLaunch, VTerminal);
        Assert.True(double.IsFinite(p.VCruise),            "VCruise not finite at f=0");
        Assert.True(double.IsFinite(p.Velocity(0.0)),      "Velocity(0) not finite at f=0");
        Assert.True(double.IsFinite(p.Velocity(60.0)),     "Velocity(mid) not finite at f=0");
        Assert.True(double.IsFinite(p.Velocity(120.0)),    "Velocity(T_sel) not finite at f=0");
    }

    [Fact]
    public void FAtHalf_AllOutputsFinite()
    {
        // f = 0.5: no cruise phase — pure triangle profile.
        var p = WarpMotionProfile.Solve(d: 1e9, tSel: 120.0, f: 0.5, VLaunch, VTerminal);
        Assert.True(double.IsFinite(p.VCruise),            "VCruise not finite at f=0.5");
        Assert.True(double.IsFinite(p.Velocity(0.0)),      "Velocity(0) not finite at f=0.5");
        Assert.True(double.IsFinite(p.Velocity(60.0)),     "Velocity(mid) not finite at f=0.5");
        Assert.True(double.IsFinite(p.Velocity(120.0)),    "Velocity(T_sel) not finite at f=0.5");
    }

    // ── Additional sanity: VCruise is derived correctly ──────────────────────

    [Fact]
    public void VCruise_DerivedCorrectly()
    {
        // The exact formula accounting for non-zero launch/terminal speeds (D-03 corrected):
        //   v_c = (D - f·T_sel·(vLaunch+vTerminal)/2) / (T_sel·(1−f))
        // This reduces to D / (T_sel·(1−f)) only when vLaunch == vTerminal == 0.
        double d = 1e9, tSel = 120.0, f = 1.0 / 3.0;
        var p = WarpMotionProfile.Solve(d, tSel, f, VLaunch, VTerminal);
        double rampContrib = f * tSel * (VLaunch + VTerminal) * 0.5;
        double expected = (d - rampContrib) / (tSel * (1.0 - f));
        Assert.Equal(expected, p.VCruise, precision: 6);
    }

    // ── No scale = distance / totalProfileDistance hack ──────────────────────

    [Fact]
    public void Solve_DoesNotRescalePosition()
    {
        // If TSel on the solved struct equals the input (not rescaled), the T field == tSel.
        double d = 1e9, tSel = 300.0, f = 1.0 / 3.0;
        var p = WarpMotionProfile.Solve(d, tSel, f, VLaunch, VTerminal);
        Assert.Equal(tSel, p.TSel);
    }
}
