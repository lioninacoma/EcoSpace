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
    /// Galaxy disc LOD weight: 1.0 = far from galaxy (disc visible), 0.0 = inside galaxy
    /// (disc suppressed so the galaxy dissolves into its constituent stars as you enter).
    /// Smooth linear ramp over [0.1 × SOI, 0.5 × SOI] — continuous crossfade, not a SOI
    /// boundary swap (D-03). The fade band is expressed as a fraction of the galaxy's SOI
    /// so it scales correctly regardless of galaxy size.
    ///
    /// <paramref name="galaxySoiMeters"/>: body.SOIMeters from UniObject (the galaxy's sphere
    /// of influence radius in metres).
    ///
    /// Returns 1f when galaxySoiMeters &lt;= 1e-30 (zero-SOI guard, T-05-02).
    /// </summary>
    public static float GalaxyDiscWeight(double distMeters, double galaxySoiMeters)
    {
        if (galaxySoiMeters <= 1e-30) return 1f;
        double fadeStart = 0.1 * galaxySoiMeters;
        double fadeEnd   = 0.5 * galaxySoiMeters;
        double t = System.Math.Clamp(
            (distMeters - fadeStart) / (fadeEnd - fadeStart),
            0.0, 1.0);
        return (float)t;
    }
}
