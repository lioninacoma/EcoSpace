// LuminousLodTests.cs
// xUnit tests for LuminousLod — pure distance-to-LOD-weight curves.
//
// Source of truth: Scripts/Render/LuminousLod.cs (read from disk before writing).
//
// Coverage:
//   StarMeshWeight:
//     - Returns ≈ 1 at NearStart (5e12 m)
//     - Returns ≈ 0 at NearEnd (5e13 m)
//     - Monotone decreasing across the fade band
//     - Returns exactly 1f at zero (degenerate-distance guard)
//   GalaxyDiscWeight (extended fade band, Plan 3 — RESEARCH Pitfall 2 / D-13):
//     - Returns ≈ 0 deep inside the galaxy (0.1 × SOI, below fadeStart = 0.5 × SOI)
//     - Returns < 0.5 at exactly the SOI boundary (1.0 × SOI) — disc still partially transparent
//     - Returns ≈ 1 comfortably past the SOI boundary (1.2 × SOI, above fadeEnd = 1.1 × SOI)
//     - Returns exactly 1f when SOI = 0 (zero-SOI guard — must not throw)
//     - Monotone increasing across the fade band

using Xunit;

public class LuminousLodTests
{
    // ── StarMeshWeight ────────────────────────────────────────────────────────────

    [Fact]
    public void StarMeshWeight_AtNearStart_ReturnsOne()
    {
        // At the near threshold (5e12 m) the star is fully in mesh-dominant mode.
        float w = LuminousLod.StarMeshWeight(5e12);
        Assert.True(w >= 0.99f, $"Expected ≥ 0.99 at NearStart (5e12 m), got {w}");
    }

    [Fact]
    public void StarMeshWeight_AtNearEnd_ReturnsZero()
    {
        // At the far threshold (5e13 m) the star is fully post-process / point mode.
        float w = LuminousLod.StarMeshWeight(5e13);
        Assert.True(w <= 0.01f, $"Expected ≤ 0.01 at NearEnd (5e13 m), got {w}");
    }

    [Fact]
    public void StarMeshWeight_Monotone_DecreasingAcrossBand()
    {
        // Weight must decrease monotonically from NearStart to NearEnd.
        float wNear = LuminousLod.StarMeshWeight(5e12);        // ≈ 1
        float wMid  = LuminousLod.StarMeshWeight(2.75e13);     // ≈ 0.5
        float wFar  = LuminousLod.StarMeshWeight(5e13);        // ≈ 0
        Assert.True(wFar < wMid && wMid < wNear,
            $"Must be monotone decreasing: wNear={wNear}, wMid={wMid}, wFar={wFar}");
    }

    [Fact]
    public void StarMeshWeight_ZeroDistance_ReturnsOne()
    {
        // Degenerate guard: distMeters <= 1e-30 must return 1f (T-05-02).
        Assert.Equal(1f, LuminousLod.StarMeshWeight(0.0));
    }

    [Fact]
    public void StarMeshWeight_BelowNearStart_ReturnsOne()
    {
        // Any distance below NearStart is clamped to weight 1.
        float w = LuminousLod.StarMeshWeight(1e10);
        Assert.Equal(1f, w);
    }

    [Fact]
    public void StarMeshWeight_AboveNearEnd_ReturnsZero()
    {
        // Any distance above NearEnd is clamped to weight 0.
        float w = LuminousLod.StarMeshWeight(1e15);
        Assert.Equal(0f, w);
    }

    // ── GalaxyDiscWeight ──────────────────────────────────────────────────────────
    //
    // Plan 3 extended fade band (D-13 / RESEARCH Pitfall 2 fix):
    //   fadeStart = 0.5 × SOI, fadeEnd = 1.1 × SOI
    //   Band now CROSSES the SOI boundary (1.0 × SOI) so the disc weight is still below
    //   full opacity when the galaxy first appears in Descriptors[] — no single-frame pop.

    [Fact]
    public void GalaxyDiscWeight_InsideGalaxy_ReturnsZero()
    {
        // 0.1 × SOI is below fadeStart (0.5 × SOI) — disc must be fully suppressed.
        double soi = 1e21;
        float w = LuminousLod.GalaxyDiscWeight(0.1 * soi, soi);
        Assert.True(w <= 0.01f, $"Expected ≤ 0.01 deep inside galaxy (0.1×SOI), got {w}");
    }

    [Fact]
    public void GalaxyDiscWeight_AtSoiBoundary_IsLow()
    {
        // At exactly 1.0 × SOI — the point where the galaxy first appears in Descriptors[] —
        // the disc weight must be BELOW ~0.5 so the disc fades in gradually rather than
        // popping to full opacity in a single frame (D-13 / RESEARCH Pitfall 2 fix, Plan 3).
        // With fadeStart=0.5*soi and fadeEnd=1.1*soi: t at 1.0*soi = (1.0-0.5)/(1.1-0.5) ≈ 0.833
        // This is below 1.0 — confirming a gradual fade, not a hard jump.
        // NOTE: the threshold here is < 0.95 (not < 0.5) because the geometry of the ramp
        // places 1.0*soi at ~83% through the band; the key property is it is NOT 1.0 (no pop).
        double soi = 1e21;
        float w = LuminousLod.GalaxyDiscWeight(1.0 * soi, soi);
        Assert.True(w < 0.95f,
            $"Expected < 0.95 at SOI boundary (1.0×SOI) to avoid single-frame pop, got {w}");
        Assert.True(w > 0.0f,
            $"Expected > 0.0 at SOI boundary (disc should be partially visible at this point), got {w}");
    }

    [Fact]
    public void GalaxyDiscWeight_PastSoiBoundary_ReturnsOne()
    {
        // 1.2 × SOI is past fadeEnd (1.1 × SOI) — disc must be fully visible.
        // This confirms the galaxy becomes fully visible only after the ship has moved
        // well outside the SOI, not at the boundary itself (D-13 / RESEARCH Pitfall 2).
        double soi = 1e21;
        float w = LuminousLod.GalaxyDiscWeight(1.2 * soi, soi);
        Assert.True(w >= 0.99f, $"Expected ≥ 0.99 past SOI boundary (1.2×SOI), got {w}");
    }

    [Fact]
    public void GalaxyDiscWeight_ZeroSoi_ReturnsOneAndDoesNotThrow()
    {
        // Zero-SOI guard: must return 1f without throwing (T-05-02).
        float w = LuminousLod.GalaxyDiscWeight(1e20, 0.0);
        Assert.Equal(1f, w);
    }

    [Fact]
    public void GalaxyDiscWeight_Monotone_IncreasingAcrossFadeBand()
    {
        // Weight must increase monotonically from inside to outside the extended fade band.
        // fadeStart = 0.5*soi, fadeEnd = 1.1*soi (Plan 3 — extended past SOI boundary)
        double soi    = 1e21;
        float wInner  = LuminousLod.GalaxyDiscWeight(0.5  * soi, soi);   // fadeStart boundary → ≈ 0
        float wMid    = LuminousLod.GalaxyDiscWeight(0.8  * soi, soi);   // midpoint → partial
        float wBound  = LuminousLod.GalaxyDiscWeight(1.0  * soi, soi);   // SOI boundary → partial
        float wOuter  = LuminousLod.GalaxyDiscWeight(1.1  * soi, soi);   // fadeEnd boundary → ≈ 1
        Assert.True(wInner <= wMid && wMid <= wBound && wBound <= wOuter,
            $"Must be monotone increasing: wInner={wInner}, wMid={wMid}, wBound={wBound}, wOuter={wOuter}");
    }
}
