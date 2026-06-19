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
//   GalaxyDiscWeight:
//     - Returns ≈ 0 deep inside the galaxy (0.05 × SOI, below fadeStart = 0.1 × SOI)
//     - Returns ≈ 1 far outside the galaxy (0.6 × SOI, above fadeEnd = 0.5 × SOI)
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

    [Fact]
    public void GalaxyDiscWeight_InsideGalaxy_ReturnsZero()
    {
        // 0.05 × SOI is inside the galaxy (below fadeStart = 0.1 × SOI) — disc must be hidden.
        double soi = 1e21;
        float w = LuminousLod.GalaxyDiscWeight(0.05 * soi, soi);
        Assert.True(w <= 0.01f, $"Expected ≤ 0.01 deep inside galaxy (0.05×SOI), got {w}");
    }

    [Fact]
    public void GalaxyDiscWeight_FarFromGalaxy_ReturnsOne()
    {
        // 0.6 × SOI is beyond fadeEnd (0.5 × SOI) — disc must be fully visible.
        double soi = 1e21;
        float w = LuminousLod.GalaxyDiscWeight(0.6 * soi, soi);
        Assert.True(w >= 0.99f, $"Expected ≥ 0.99 far outside galaxy (0.6×SOI), got {w}");
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
        // Weight must increase monotonically from inside to outside the fade band.
        double soi = 1e21;
        float wInner = LuminousLod.GalaxyDiscWeight(0.1  * soi, soi);   // fadeStart boundary → ≈ 0
        float wMid   = LuminousLod.GalaxyDiscWeight(0.3  * soi, soi);   // midpoint → ≈ 0.5
        float wOuter = LuminousLod.GalaxyDiscWeight(0.5  * soi, soi);   // fadeEnd boundary → ≈ 1
        Assert.True(wInner <= wMid && wMid <= wOuter,
            $"Must be monotone increasing: wInner={wInner}, wMid={wMid}, wOuter={wOuter}");
    }
}
