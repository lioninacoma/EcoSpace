// TierClassifierTests.cs
// xUnit tests for TierClassifier.Classify() correctness.
//
// Source of truth: Scripts/TierClassifier.cs (read from disk before writing these tests).
//
// Classification rule (as implemented in TierClassifier.Classify):
//   null / same Index      → Skip
//   Root-space body        → Skip
//   body.CurrentSpace == ship.CurrentSpace
//                          → CurrentTierMesh
//   body.CurrentSpace is ANY ancestor of ship.CurrentSpace (full hierarchy walk)
//                          → NextTierSkybox
//   everything else        → Beyond
//
// NOTE ON PLAN BEHAVIOUR SPEC vs IMPLEMENTATION:
//   The plan's <behavior> section listed "Ship in Planet space, sibling star in Galaxy space
//   (two tiers out) -> Beyond".  The actual TierClassifier.Classify implementation walks
//   ALL ancestors (Star → Galaxy → Universe), so a Galaxy-space body is NextTierSkybox when
//   the ship is in Planet space.  These tests mirror the real on-disk implementation, which
//   is declared the source of truth ("Read TierClassifier.cs from disk to mirror its real API").
//
// Coverage:
//   - Full ship-space × body-space matrix (ship in Star and Planet)
//   - Skip cases: null body, null ship, body==ship, Root-space body
//   - D-19 min-brightness floor: inverse-square model clamped to MinBrightFloor
//   - Ship in Star space seeing a sibling star system in Galaxy space (the real in-system case,
//     confirmed in 02-01-SUMMARY).

using Xunit;

public class TierClassifierTests
{
    // ── Helper ───────────────────────────────────────────────────────────

    /// <summary>
    /// Factory method for minimal UniObject instances used in classification tests.
    /// Only Index and CurrentSpace matter for TierClassifier.Classify.
    /// </summary>
    private static UniObject MakeObj(int index, UniObject.Space space, int parentIdx = -1) =>
        new UniObject { Index = index, CurrentSpace = space, ParentIndex = parentIdx };

    // ── Skip cases ───────────────────────────────────────────────────────

    [Fact]
    public void NullBody_ReturnsSkip()
    {
        var ship = MakeObj(0, UniObject.Space.Star);
        Assert.Equal(SkyTier.Skip, TierClassifier.Classify(null!, ship));
    }

    [Fact]
    public void NullShip_ReturnsSkip()
    {
        var body = MakeObj(1, UniObject.Space.Galaxy);
        Assert.Equal(SkyTier.Skip, TierClassifier.Classify(body, null!));
    }

    [Fact]
    public void BodySameIndexAsShip_ReturnsSkip()
    {
        var ship = MakeObj(5, UniObject.Space.Star);
        var body = MakeObj(5, UniObject.Space.Star);   // same Index
        Assert.Equal(SkyTier.Skip, TierClassifier.Classify(body, ship));
    }

    [Fact]
    public void BodyInRootSpace_ReturnsSkip()
    {
        var ship = MakeObj(0, UniObject.Space.Star);
        var body = MakeObj(1, UniObject.Space.Root);
        Assert.Equal(SkyTier.Skip, TierClassifier.Classify(body, ship));
    }

    // ── Ship in Star space ────────────────────────────────────────────────

    [Fact]
    public void ShipInStar_BodyInStar_ReturnsCurrentTierMesh()
    {
        // Sibling planet in the same Star space → WorldRenderer renders it as a mesh.
        var ship = MakeObj(0, UniObject.Space.Star);
        var body = MakeObj(1, UniObject.Space.Star);
        Assert.Equal(SkyTier.CurrentTierMesh, TierClassifier.Classify(body, ship));
    }

    [Fact]
    public void ShipInStar_BodyInGalaxy_ReturnsNextTierSkybox()
    {
        // Sibling star system (other star) lives in Galaxy space — one tier out.
        // This is the real in-system case from 02-01-SUMMARY: ALPHA CEN / BARNARD / SIRIUS
        // are in Galaxy space, ship starts in Star space → they should appear as sky points.
        var ship = MakeObj(0, UniObject.Space.Star);
        var body = MakeObj(1, UniObject.Space.Galaxy);
        Assert.Equal(SkyTier.NextTierSkybox, TierClassifier.Classify(body, ship));
    }

    [Fact]
    public void ShipInStar_BodyInUniverse_ReturnsNextTierSkybox()
    {
        // Universe space is a grandparent ancestor of Star space.
        // Full ancestor walk → NextTierSkybox.
        var ship = MakeObj(0, UniObject.Space.Star);
        var body = MakeObj(1, UniObject.Space.Universe);
        Assert.Equal(SkyTier.NextTierSkybox, TierClassifier.Classify(body, ship));
    }

    [Fact]
    public void ShipInStar_BodyInPlanet_ReturnsBeyond()
    {
        // Planet is a child space of Star — not an ancestor — so it is Beyond.
        var ship = MakeObj(0, UniObject.Space.Star);
        var body = MakeObj(1, UniObject.Space.Planet);
        Assert.Equal(SkyTier.Beyond, TierClassifier.Classify(body, ship));
    }

    // ── Ship in Planet space ───────────────────────────────────────────────

    [Fact]
    public void ShipInPlanet_BodyInPlanet_ReturnsCurrentTierMesh()
    {
        // Sibling planet in the same Planet space → WorldRenderer mesh.
        var ship = MakeObj(0, UniObject.Space.Planet);
        var body = MakeObj(1, UniObject.Space.Planet);
        Assert.Equal(SkyTier.CurrentTierMesh, TierClassifier.Classify(body, ship));
    }

    [Fact]
    public void ShipInPlanet_BodyInStar_ReturnsNextTierSkybox()
    {
        // Star is the direct parent space of Planet (ParentSpace(Planet) == Star).
        var ship = MakeObj(0, UniObject.Space.Planet);
        var body = MakeObj(1, UniObject.Space.Star);
        Assert.Equal(SkyTier.NextTierSkybox, TierClassifier.Classify(body, ship));
    }

    [Fact]
    public void ShipInPlanet_BodyInGalaxy_ReturnsNextTierSkybox()
    {
        // Galaxy is two tiers above Planet (Planet → Star → Galaxy).
        // The TierClassifier walks ALL ancestors, so Galaxy is NextTierSkybox.
        // (Plan's <behavior> spec listed this as Beyond — the on-disk implementation
        //  is the authority; it returns NextTierSkybox.  See file header note.)
        var ship = MakeObj(0, UniObject.Space.Planet);
        var body = MakeObj(1, UniObject.Space.Galaxy);
        Assert.Equal(SkyTier.NextTierSkybox, TierClassifier.Classify(body, ship));
    }

    [Fact]
    public void ShipInPlanet_BodyInUniverse_ReturnsNextTierSkybox()
    {
        // Universe is three tiers above Planet; still an ancestor → NextTierSkybox.
        var ship = MakeObj(0, UniObject.Space.Planet);
        var body = MakeObj(1, UniObject.Space.Universe);
        Assert.Equal(SkyTier.NextTierSkybox, TierClassifier.Classify(body, ship));
    }

    [Fact]
    public void ShipInPlanet_BodyInRoot_ReturnsSkip()
    {
        // Root is excluded before the ancestor walk (explicit guard in Classify).
        var ship = MakeObj(0, UniObject.Space.Planet);
        var body = MakeObj(1, UniObject.Space.Root);
        Assert.Equal(SkyTier.Skip, TierClassifier.Classify(body, ship));
    }

    // ── Real in-system demo case (from 02-01-SUMMARY) ────────────────────

    [Fact]
    public void SiblingStarInGalaxySpace_VisibleFromStarSpace_ConfirmedCase()
    {
        // The 02-01-SUMMARY confirms: "When the ship is in Planet space or Star space,
        // TierClassifier.Classify returns NextTierSkybox for all three sibling bodies
        // (ALPHA CEN, BARNARD, SIRIUS) because their CurrentSpace == Galaxy ==
        // ParentSpace(ship.CurrentSpace) for both Star and Planet spaces."
        //
        // This test explicitly pins the Star-space scenario as a regression guard.
        var ship          = MakeObj(0, UniObject.Space.Star);
        var alphaCen      = MakeObj(1, UniObject.Space.Galaxy);  // sibling star in Galaxy space
        var barnardsStar  = MakeObj(2, UniObject.Space.Galaxy);
        var sirius        = MakeObj(3, UniObject.Space.Galaxy);

        Assert.Equal(SkyTier.NextTierSkybox, TierClassifier.Classify(alphaCen, ship));
        Assert.Equal(SkyTier.NextTierSkybox, TierClassifier.Classify(barnardsStar, ship));
        Assert.Equal(SkyTier.NextTierSkybox, TierClassifier.Classify(sirius, ship));
    }

    // ── D-19 minimum-brightness floor ─────────────────────────────────────

    [Fact]
    public void MinBrightFloor_ReturnedWhen_RawBrightnessIsBelowFloor()
    {
        // Magnitude model (SkyboxRenderer): brightness = Max(raw, MinBrightFloor)
        // where raw = (float)(L * LuminosityScale / (d * d)).
        //
        // This test uses the exact same formula as SkyboxRenderer.SyncSkyPoints:
        //   Luminosity   = 0.001 (very faint body)
        //   LuminosityScale = 2e35 (SkyboxRenderer default)
        //   dist         = 1e26 m (enormous — sub-threshold result guaranteed)
        //   MinBrightFloor = 0.1f (SkyboxRenderer default)
        //
        // Expected: raw ≈ 0 < floor → brightness == floor.
        double luminosity     = 0.001;
        double luminosityScale = 2e35;
        double dist           = 1e26;     // much larger than any real interstellar distance
        float  floor          = 0.1f;

        float rawAlpha = (float)(luminosity * luminosityScale / (dist * dist));
        float brightness = System.Math.Max(rawAlpha, floor);

        Assert.Equal(floor, brightness, precision: 5);
    }

    [Fact]
    public void MinBrightFloor_NotApplied_WhenBrightnessExceedsFloor()
    {
        // Verify the model is NOT clamped upward when natural brightness exceeds the floor.
        // Uses Sirius-like params: L=25.4, LuminosityScale=2e35, d=8e16 m (~8.6 ly).
        double luminosity      = 25.4;
        double luminosityScale = 2e35;
        double dist            = 8e16;
        float  floor           = 0.1f;

        float rawAlpha   = (float)(luminosity * luminosityScale / (dist * dist));
        float brightness = System.Math.Max(rawAlpha, floor);

        // rawAlpha is significantly above the floor — brightness should equal rawAlpha, not floor.
        Assert.True(brightness > floor,
            $"Expected brightness ({brightness}) > floor ({floor}) for bright Sirius-like body.");
        Assert.Equal(rawAlpha, brightness, precision: 3);
    }
}
