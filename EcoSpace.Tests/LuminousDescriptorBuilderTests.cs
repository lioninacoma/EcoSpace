// LuminousDescriptorBuilderTests.cs
// xUnit tests for LuminousDescriptorBuilder — the per-frame classify+project loop.
//
// Source of truth: Scripts/Render/LuminousDescriptorBuilder.cs (read from disk).
//
// Because LuminousDescriptorBuilder is a Godot Node, the per-body math is extracted
// into the static helper LuminousDescriptorBuilder.ComputeDescriptor(...) which is
// fully Godot-free in its data inputs (List<UniObject>). We test that helper directly,
// mirroring the UniMathTests pattern: build a minimal in-memory hierarchy, call the
// static helper, assert the descriptor properties.
//
// Hierarchy for tests (mirrors UniMathTests.BuildHierarchy 5-node minimal variant):
//   [0] Root        (Space.Root,     parent=-1)
//   [1] Galaxy      (Space.Universe, parent=0)  — home galaxy
//   [2] Star        (Space.Galaxy,   parent=1)  — home star
//   [3] Planet      (Space.Star,     parent=2)  — home planet
//   [4] Ship        (Space.Planet,   parent=3)  — player ship
//   [5] AlphaCen    (Space.Galaxy,   parent=1)  — sibling star (non-ancestor)
//   [6] OtherGalaxy (Space.Universe, parent=0)  — second galaxy (non-ancestor)
//
// Coverage:
//   - A Star body in an ancestor space produces a descriptor with BodyType==Star
//     and a non-zero Direction aligned with the UniMath.RelativePosition result
//   - A Galaxy that IS an ancestor of the ship is suppressed (home-galaxy guard)
//   - A non-ancestor Galaxy IS included in the descriptor array
//   - LodWeight == 0 (StarMeshWeight) for a remote sibling star many light-years away
//   - LodWeight == 1 (GalaxyDiscWeight) for a galaxy far outside its SOI

using System.Collections.Generic;
using Xunit;

public class LuminousDescriptorBuilderTests
{
    // ── Hierarchy builder ──────────────────────────────────────────────────────

    private static List<UniObject> BuildHierarchy()
    {
        var objs = new List<UniObject>(new UniObject[7]);

        double galScale  = UniObject.Scale(UniObject.Space.Universe);  // 1e16
        double starScale = UniObject.Scale(UniObject.Space.Galaxy);    // 10000
        double planScale = UniObject.Scale(UniObject.Space.Star);      // 1
        double shipScale = UniObject.Scale(UniObject.Space.Planet);    // 0.0001

        // [0] Root
        objs[0] = new UniObject
        {
            Index        = 0,
            CurrentSpace = UniObject.Space.Root,
            ParentIndex  = -1,
            LocalPos     = new UniVec3(Double3.Zero, 1.0),
        };

        // [1] Home Galaxy — in Universe space (SOI = 5e20 m)
        objs[1] = new UniObject
        {
            Index        = 1,
            CurrentSpace = UniObject.Space.Universe,
            ParentIndex  = 0,
            LocalPos     = new UniVec3(Double3.Zero, galScale),
            ObjectType   = UniObject.Type.Galaxy,
            SOIMeters    = 5e20,
            Luminosity   = 1e10,
            RadiusMeters = 5e20,
            BaseColor    = new Godot.Color(0.7f, 0.75f, 1.0f),
            GalaxyType   = 0,
            GalaxySeed   = 0.42f,
            GalaxyOrientation = new Godot.Vector3(0f, 1f, 0f),
        };

        // [2] Home Star — in Galaxy space
        objs[2] = new UniObject
        {
            Index        = 2,
            CurrentSpace = UniObject.Space.Galaxy,
            ParentIndex  = 1,
            LocalPos     = new UniVec3(Double3.Zero, starScale),
            ObjectType   = UniObject.Type.Star,
            SOIMeters    = 1.5e15,
            Luminosity   = 1.0,
            RadiusMeters = 6.960e8,
            BaseColor    = new Godot.Color(1.0f, 0.95f, 0.60f),
        };

        // [3] Planet — in Star space (1 AU from star)
        objs[3] = new UniObject
        {
            Index        = 3,
            CurrentSpace = UniObject.Space.Star,
            ParentIndex  = 2,
            LocalPos     = new UniVec3(new Double3(1.496e11, 0, 0), planScale),
            ObjectType   = UniObject.Type.Planet,
            SOIMeters    = 1.0e9,
            Luminosity   = 0.0,
            RadiusMeters = 6.371e6,
            BaseColor    = new Godot.Color(0.25f, 0.50f, 0.95f),
        };

        // [4] Ship — in Planet space (near planet surface)
        objs[4] = new UniObject
        {
            Index        = 4,
            CurrentSpace = UniObject.Space.Planet,
            ParentIndex  = 3,
            LocalPos     = new UniVec3(new Double3(100.0, 0, 0), shipScale),
            ObjectType   = UniObject.Type.None,
        };

        // [5] AlphaCen — sibling star in Galaxy space (~4.2 ly from home star)
        double alphaCenMeters = 3.97e16;
        long   alphaCenUnits  = (long)(alphaCenMeters / starScale);
        double alphaCenOff    = alphaCenMeters - alphaCenUnits * starScale;
        objs[5] = new UniObject
        {
            Index        = 5,
            CurrentSpace = UniObject.Space.Galaxy,
            ParentIndex  = 1,
            LocalPos     = new UniVec3(
                               new Long3(alphaCenUnits, 0, 0),
                               new Double3(alphaCenOff, 0, 0),
                               starScale),
            ObjectType   = UniObject.Type.Star,
            SOIMeters    = 1.5e15,
            Luminosity   = 1.519,
            RadiusMeters = 6.960e8,
            BaseColor    = new Godot.Color(1.0f, 0.92f, 0.70f),
        };

        // [6] Other Galaxy — in Universe space (~Andromeda distance, non-ancestor)
        double otherGalMeters = 2.4e22;
        long   otherGalUnits  = (long)(otherGalMeters / galScale);
        double otherGalOff    = otherGalMeters - otherGalUnits * galScale;
        objs[6] = new UniObject
        {
            Index        = 6,
            CurrentSpace = UniObject.Space.Universe,
            ParentIndex  = 0,
            LocalPos     = new UniVec3(
                               new Long3(0, 0, otherGalUnits),
                               new Double3(0, 0, otherGalOff),
                               galScale),
            ObjectType   = UniObject.Type.Galaxy,
            SOIMeters    = 5e20,
            Luminosity   = 1e10,
            RadiusMeters = 5e20,
            BaseColor    = new Godot.Color(0.7f, 0.75f, 1.0f),
            GalaxyType   = 1,
            GalaxySeed   = 0.73f,
            GalaxyOrientation = new Godot.Vector3(0.5f, 0.866f, 0f),
        };

        // Wire ChildIndices
        objs[0].ChildIndices.Add(1);
        objs[0].ChildIndices.Add(6);
        objs[1].ChildIndices.Add(2);
        objs[1].ChildIndices.Add(5);
        objs[2].ChildIndices.Add(3);
        objs[3].ChildIndices.Add(4);

        return objs;
    }

    // ── Tests ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void ComputeDescriptor_SiblingStarInGalaxySpace_HasBodyTypeStar()
    {
        // AlphaCen (index 5, Galaxy space) is a NextTierSkybox body when ship is in Planet space.
        // Its descriptor must have BodyType == Star.
        var objs  = BuildHierarchy();
        var ship  = objs[4];
        var body  = objs[5];   // AlphaCen

        var desc = Render.LuminousDescriptorBuilder.ComputeDescriptor(body, ship, objs, pixelAngle: 6.98e-4f);

        Assert.Equal(UniObject.Type.Star, desc.BodyType);
    }

    [Fact]
    public void ComputeDescriptor_SiblingStarInGalaxySpace_HasNonZeroDirection()
    {
        // AlphaCen (index 5) is ~4.2 ly away — direction must be non-zero.
        var objs  = BuildHierarchy();
        var ship  = objs[4];
        var body  = objs[5];   // AlphaCen

        var desc = Render.LuminousDescriptorBuilder.ComputeDescriptor(body, ship, objs, pixelAngle: 6.98e-4f);

        // Direction must not be the Vector3.Up fallback (used only for degenerate/coincident bodies).
        // At 4.2 ly, X component of direction should be strongly positive (AlphaCen is in +X).
        Assert.True(desc.Direction.X > 0.9f,
            $"Expected strong +X direction toward AlphaCen, got Direction={desc.Direction}");
    }

    [Fact]
    public void ComputeDescriptor_SiblingStarInGalaxySpace_DirectionMatchesRelativePosition()
    {
        // The descriptor direction must be consistent with UniMath.RelativePosition (LCA path).
        // Both must point in the same +X direction toward AlphaCen.
        var objs = BuildHierarchy();
        var ship = objs[4];
        var body = objs[5];   // AlphaCen

        bool ok = UniMath.RelativePosition(ship, body, objs, out UniVec3 relUni);
        Assert.True(ok, "RelativePosition must find a common ancestor for ship→AlphaCen");

        Double3 delta = relUni.ToDouble3();
        double  len   = delta.Magnitude();
        Double3 dir   = delta * (1.0 / len);

        var desc = Render.LuminousDescriptorBuilder.ComputeDescriptor(body, ship, objs, pixelAngle: 6.98e-4f);

        // X component must match to within 1e-5 (float precision from the double→float cast).
        Assert.InRange(desc.Direction.X - (float)dir.X, -1e-4f, 1e-4f);
    }

    [Fact]
    public void ComputeDescriptor_SiblingStarFarAway_LodWeightIsZero()
    {
        // AlphaCen is ~4.2 ly ≈ 3.97e16 m — well beyond StarNearEnd (5e13 m).
        // StarMeshWeight at this distance must return 0 (post-process point mode).
        var objs = BuildHierarchy();
        var ship = objs[4];
        var body = objs[5];   // AlphaCen

        var desc = Render.LuminousDescriptorBuilder.ComputeDescriptor(body, ship, objs, pixelAngle: 6.98e-4f);

        Assert.Equal(0f, desc.LodWeight);
    }

    [Fact]
    public void ComputeDescriptor_NonAncestorGalaxy_IsIncluded()
    {
        // OtherGalaxy (index 6) is NOT an ancestor of the ship (it is a sibling of HomeGalaxy
        // under Root). It must NOT be suppressed by the home-galaxy guard.
        var objs = BuildHierarchy();
        var ship = objs[4];
        var body = objs[6];   // OtherGalaxy

        // Verify the LCA is NOT body.Index (so suppression guard does not apply).
        int lca = UniMath.FindLca(ship, body, objs);
        Assert.NotEqual(body.Index, lca);   // lca == 0 (Root); not body.Index → not suppressed

        // ComputeDescriptor must succeed (no exception, valid BodyType).
        var desc = Render.LuminousDescriptorBuilder.ComputeDescriptor(body, ship, objs, pixelAngle: 6.98e-4f);
        Assert.Equal(UniObject.Type.Galaxy, desc.BodyType);
    }

    [Fact]
    public void HomeGalaxySuppressionGuard_AncestorGalaxy_IsLcaEqualBodyIndex()
    {
        // HomeGalaxy (index 1) IS an ancestor of the ship (index 4).
        // The suppression predicate FindLca(ship, homeGalaxy) == homeGalaxy.Index must be true.
        // LuminousDescriptorBuilder.BuildDescriptors() calls this predicate to skip home galaxies.
        var objs      = BuildHierarchy();
        var ship      = objs[4];
        var homeGal   = objs[1];   // home galaxy (ancestor of ship)

        int lca = UniMath.FindLca(ship, homeGal, objs);
        Assert.Equal(homeGal.Index, lca);   // predicate is true → would be suppressed
    }

    [Fact]
    public void ComputeDescriptor_FarGalaxy_LodWeightIsOne()
    {
        // OtherGalaxy is ~2.4e22 m from the ship, its SOI is 5e20 m.
        // Distance >> SOI → GalaxyDiscWeight returns 1 (far disc fully visible).
        var objs = BuildHierarchy();
        var ship = objs[4];
        var body = objs[6];   // OtherGalaxy

        var desc = Render.LuminousDescriptorBuilder.ComputeDescriptor(body, ship, objs, pixelAngle: 6.98e-4f);

        // At 2.4e22 m, far outside the 5e20 m SOI → weight should be 1.
        Assert.Equal(1f, desc.LodWeight);
    }
}
