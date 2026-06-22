// WarpMotionProfile.cs
// Pure C# — no Godot dependency. Intentionally kept dependency-free for unit testing.
//
// Closed-form symmetric smoothstep trapezoidal velocity profile for warp drive (D-01/D-04).
//
// The ship accelerates from a launch speed to a cruise velocity v_c, holds cruise,
// then decelerates back to a terminal speed — all governed by a symmetric smoothstep ramp
// (s(u) = 3u² − 2u³) so jerk is zero at both endpoints.
//
// Key invariant (D-03): v_c = D / (T_sel · (1 − f))
//   where D     = warp distance (metres)
//         T_sel = selected travel time (seconds, ≥ 1.0)
//         f     = accel-time fraction ∈ [0, 0.5]
//
// Because each ramp's area = ½·Δv·t_ramp (smoothstep property), the integral of Velocity
// over [0, T_sel] equals D exactly (within floating-point precision).
//
// Ramp phases:
//   Accel:  [0,        f·T_sel]         launch → v_c  via smoothstep
//   Cruise: [f·T_sel, (1−f)·T_sel]     flat v_c
//   Decel:  [(1−f)·T_sel, T_sel]       v_c → terminal via smoothstep
//
// T_sel is the EXACT input; do NOT rescale position to fit (rejects the reference's
// `scale = distance / totalProfileDistance` hack — D-01).

using System;

/// <summary>
/// Closed-form symmetric smoothstep trapezoidal velocity profile (D-01/D-04).
/// T_sel is the EXACT input; v_c is derived: <c>v_c = D / (T_sel·(1−f))</c> (D-03).
/// Accel = decel = <c>f·T_sel</c>; cruise = <c>(1−2f)·T_sel</c>. Ramps use <c>s(u)=3u²−2u³</c>.
/// Pure — no Godot dependency, no mutation; one <see cref="Solve"/> factory + one
/// <see cref="Velocity"/> evaluator (D-10).
/// </summary>
public readonly struct WarpMotionProfile
{
    // ── Constants ──────────────────────────────────────────────────────────────

    private const double EPSILON = 1e-11;

    // ── Solved profile parameters ───────────────────────────────────────────

    /// <summary>Warp distance in metres (D = d0 − target.SOIMeters, D-08).</summary>
    public readonly double D;

    /// <summary>Selected travel time in seconds (player choice, ≥ 1.0 s, D-06).</summary>
    public readonly double TSel;

    /// <summary>Accel-time fraction f ∈ [0, 0.5] (D-02). Default 1/3.</summary>
    public readonly double F;

    /// <summary>Cruise velocity in m/s: v_c = D / (T_sel · (1 − f)) (D-03).</summary>
    public readonly double VCruise;

    /// <summary>Launch speed in m/s (≈ ManualMaxSpeed at warp engage, D-09).</summary>
    public readonly double VLaunch;

    /// <summary>Terminal speed in m/s (= ManualMaxSpeed at SOI arrival, D-07/D-09).</summary>
    public readonly double VTerminal;

    // ── Phase durations ─────────────────────────────────────────────────────

    /// <summary>Accel phase duration in seconds (= f · T_sel).</summary>
    public readonly double TAccel;

    /// <summary>Cruise phase duration in seconds (= (1 − 2f) · T_sel).</summary>
    public readonly double TCruise;

    /// <summary>Decel phase duration in seconds (= f · T_sel; same as TAccel).</summary>
    public readonly double TDecel;

    // ── Constructor (private; use Solve) ────────────────────────────────────

    private WarpMotionProfile(
        double d, double tSel, double f,
        double vCruise, double vLaunch, double vTerminal,
        double tAccel, double tCruise, double tDecel)
    {
        D          = d;
        TSel       = tSel;
        F          = f;
        VCruise    = vCruise;
        VLaunch    = vLaunch;
        VTerminal  = vTerminal;
        TAccel     = tAccel;
        TCruise    = tCruise;
        TDecel     = tDecel;
    }

    // ── Factory ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Solves the closed-form trapezoid profile for the given warp parameters.
    ///
    /// Exact area derivation (smoothstep integral over [0,1] = 0.5):
    ///   Accel area = f·T_sel·(vLaunch + v_c) / 2
    ///   Cruise area = v_c·(1−2f)·T_sel
    ///   Decel area  = f·T_sel·(v_c + vTerminal) / 2
    ///   Total = v_c·T_sel·(1−f) + f·T_sel·(vLaunch + vTerminal) / 2 = D
    ///   → v_c = (D − f·T_sel·(vLaunch + vTerminal)/2) / (T_sel·(1−f))
    ///
    /// When vLaunch == vTerminal == 0 this reduces to the simple D-03 formula:
    ///   v_c = D / (T_sel·(1−f)).
    /// </summary>
    /// <param name="d">Warp distance in metres (D = d0 − target.SOIMeters).</param>
    /// <param name="tSel">Selected travel time in seconds (already clamped to ≥ 1.0 by EngageWarp).</param>
    /// <param name="f">Accel-time fraction ∈ [0, 0.5] (WarpAccelFraction, default 1/3).</param>
    /// <param name="vLaunch">Launch speed in m/s (ship's current speed at EngageWarp, D-09).</param>
    /// <param name="vTerminal">Terminal speed in m/s (ManualMaxSpeed at SOI arrival, D-07).</param>
    /// <returns>A solved <see cref="WarpMotionProfile"/> ready for <see cref="Velocity"/> evaluation.</returns>
    public static WarpMotionProfile Solve(
        double d, double tSel, double f,
        double vLaunch, double vTerminal)
    {
        // Guard: ensure positive time and valid fraction to prevent division by zero (T-08-01).
        tSel = Math.Max(EPSILON, tSel);
        f    = Math.Max(0.0, Math.Min(0.5, f));

        // Exact cruise velocity from area equation:
        //   D = v_c·T_sel·(1−f) + f·T_sel·(vLaunch + vTerminal)/2
        //   v_c = (D − f·T_sel·(vLaunch+vTerminal)/2) / (T_sel·(1−f))
        // Guard denominator to prevent div-by-zero when f→0.5 (triangle) or T_sel→0.
        double rampContribution = f * tSel * (vLaunch + vTerminal) * 0.5;
        double numerator        = d - rampContribution;
        double denominator      = Math.Max(EPSILON, tSel * (1.0 - f));
        double vCruise          = numerator / denominator;

        // Ensure v_c is non-negative (can go negative if D is very small; clamp to 0).
        vCruise = Math.Max(0.0, vCruise);

        double tAccel  = f * tSel;
        double tDecel  = f * tSel;
        double tCruise = Math.Max(0.0, tSel - tAccel - tDecel);

        return new WarpMotionProfile(
            d, tSel, f,
            vCruise, vLaunch, vTerminal,
            tAccel, tCruise, tDecel);
    }

    // ── Velocity evaluator ───────────────────────────────────────────────────

    /// <summary>
    /// Returns the instantaneous velocity in m/s at elapsed warp time <paramref name="t"/> seconds.
    /// Clamps <paramref name="t"/> to [0, T_sel]. Uses smoothstep <c>s(u)=3u²−2u³</c> for jerk-free
    /// ramps; cruise phase is flat at <see cref="VCruise"/>.
    ///
    /// Ramp interpolation uses the numerically stable form <c>v0·(1−s) + v1·s</c> to avoid
    /// catastrophic cancellation when VCruise ≫ VLaunch/VTerminal at intergalactic scales.
    /// </summary>
    /// <param name="t">Elapsed warp time in seconds since EngageWarp.</param>
    public double Velocity(double t)
    {
        // Clamp t to [0, T_sel].
        t = Math.Max(0.0, Math.Min(TSel, t));

        double tEndAccel  = TAccel;
        double tEndCruise = TAccel + TCruise;

        if (t <= tEndAccel)
        {
            // Accel phase: smoothstep from VLaunch → VCruise over [0, TAccel].
            // Numerically stable lerp: v0*(1-s) + v1*s avoids cancellation when v0≫v1 or v1≫v0.
            double u = t / Math.Max(EPSILON, TAccel);
            double s = Smoothstep(u);
            return VLaunch * (1.0 - s) + VCruise * s;
        }
        else if (t <= tEndCruise)
        {
            // Cruise phase: flat at VCruise.
            return VCruise;
        }
        else
        {
            // Decel phase: smoothstep from VCruise → VTerminal over [(1−f)·T_sel, T_sel].
            // Numerically stable lerp: avoids catastrophic cancellation when VCruise ≫ VTerminal
            // (e.g. intergalactic warp: v_c ~ 2e20 m/s, VTerminal ~ 1e6 m/s).
            double u = (t - tEndCruise) / Math.Max(EPSILON, TDecel);
            double s = Smoothstep(u);
            return VCruise * (1.0 - s) + VTerminal * s;
        }
    }

    // ── SIMD helpers ────────────────────────────────────────────────────────

    /// <summary>
    /// Jerk-limited smoothstep: <c>s(u) = 3u² − 2u³</c>.
    /// Has zero first-derivative at u=0 and u=1 (zero accel at both endpoints).
    /// Area over [0,1] = 0.5, so ramp area = 0.5 · Δv · t_ramp (exact closed form).
    /// No Godot/Mathf dependency — hand-rolled for Godot-free compilation.
    /// </summary>
    private static double Smoothstep(double u)
    {
        // Clamp u to [0,1] for safety (should already be clamped by callers).
        u = Math.Max(0.0, Math.Min(1.0, u));
        return u * u * (3.0 - 2.0 * u);
    }
}
