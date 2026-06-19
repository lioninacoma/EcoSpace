// LuminousLod.cs
// Pure C# — no Godot dependency. Intentionally kept dependency-free for unit testing
// (mirrors the TierClassifier / StarRendering precedent: global namespace, no namespace
// wrapper, no using Godot).

/// <summary>
/// Pure distance-to-LOD-weight curves for the unified luminous-body descriptor (D-03).
/// No Godot dependency — intentionally kept Godot-free for unit testing.
///
/// Thresholds are [ASSUMED] starting points calibrated against intuition; adjust at
/// play-test gates (D-04). Comments mark each knob.
///
/// Both methods are monotone and clamped to [0,1].
/// Division-by-zero and degenerate-distance guards return a safe sentinel (1f)
/// rather than NaN (T-05-02 mitigation from the threat model).
/// </summary>
public static class LuminousLod
{
    // ── Star LOD ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Distance (metres) at which the star mesh starts to take over from the post-process
    /// point. Below this distance the star is 100% mesh. [ASSUMED play-test calibration knob]
    /// </summary>
    private const double StarNearStart = 5e12;   // ~0.5 ly — tune in play-test

    /// <summary>
    /// Distance (metres) at which the star mesh fully fades to a post-process point.
    /// Above this distance the star is 100% post-process point/glow. [ASSUMED play-test calibration knob]
    /// </summary>
    private const double StarNearEnd   = 5e13;   // ~5 ly — tune in play-test

    /// <summary>
    /// Star mesh LOD weight: 1.0 = near (mesh dominant), 0.0 = far (point/glow dominant).
    /// Smooth linear ramp — no discrete SOI boundary swap (D-03 anti-pattern avoided).
    ///
    /// Returns 1f when distMeters &lt;= 1e-30 (degenerate/zero-distance guard, T-05-02).
    /// </summary>
    public static float StarMeshWeight(double distMeters)
    {
        if (distMeters <= 1e-30) return 1f;
        double t = System.Math.Clamp(
            (distMeters - StarNearStart) / (StarNearEnd - StarNearStart),
            0.0, 1.0);
        return (float)(1.0 - t);
    }

    // ── Galaxy LOD ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Inner distance fraction (multiple of SOI) at which the galaxy disc fade begins.
    /// Below this fraction the disc is fully suppressed (ship still relatively near galaxy).
    /// [ASSUMED play-test calibration knob — RESEARCH Assumptions Log A4]
    /// </summary>
    private const double GalaxyFadeStartFraction = 0.5;   // 0.5 × SOI

    /// <summary>
    /// Outer distance fraction (multiple of SOI) at which the galaxy disc is fully visible.
    /// Must be GREATER than 1.0 so the ramp INCLUDES the SOI boundary (1.0 × SOI):
    /// when the galaxy first appears in Descriptors[] (at dist ≈ SOI), its disc weight
    /// is still below 1.0 and fades in gradually — no single-frame pop (D-13 / RESEARCH Pitfall 2).
    /// [ASSUMED play-test calibration knob — RESEARCH Assumptions Log A4]
    /// </summary>
    private const double GalaxyFadeEndFraction   = 1.1;   // 1.1 × SOI — crosses the SOI boundary

    /// <summary>
    /// Galaxy disc LOD weight: 1.0 = far from galaxy (disc visible), 0.0 = inside galaxy
    /// (disc suppressed so the galaxy dissolves into its constituent stars as you enter).
    /// Smooth linear ramp over [GalaxyFadeStartFraction × SOI, GalaxyFadeEndFraction × SOI] —
    /// continuous crossfade, not a SOI boundary swap (D-03).
    ///
    /// The fade band is expressed as a fraction of the galaxy's SOI so it scales correctly
    /// regardless of galaxy size.  The fade end EXCEEDS 1.0 × SOI so the ramp crosses the
    /// SOI boundary: when the galaxy first becomes visible (dist ≈ SOI), the disc weight is
    /// below 1.0 and grows gradually — removing the "pops out of nowhere" single-frame jump
    /// seen when the ship exits the galaxy SOI (D-13 / RESEARCH Pitfall 2 / Assumptions Log A4).
    ///
    /// <paramref name="galaxySoiMeters"/>: body.SOIMeters from UniObject (the galaxy's sphere
    /// of influence radius in metres).
    ///
    /// Returns 1f when galaxySoiMeters &lt;= 1e-30 (zero-SOI guard, T-05-02 mitigation).
    /// </summary>
    public static float GalaxyDiscWeight(double distMeters, double galaxySoiMeters)
    {
        if (galaxySoiMeters <= 1e-30) return 1f;
        double fadeStart = GalaxyFadeStartFraction * galaxySoiMeters;
        double fadeEnd   = GalaxyFadeEndFraction   * galaxySoiMeters;
        double t = System.Math.Clamp(
            (distMeters - fadeStart) / (fadeEnd - fadeStart),
            0.0, 1.0);
        return (float)t;
    }
}
