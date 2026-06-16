// UniMathTests.cs
// xUnit tests for UniMath — hierarchy-aware UniVec3 position math.
//
// Source of truth: Scripts/Math/UniMath.cs (read from disk before writing these tests).
//
// Tests mirror a minimal in-memory hierarchy that matches the MVP TestSetup structure:
//
//   Index 0: Root        (Space.Root,     ParentIndex=-1)
//   Index 1: Galaxy      (Space.Universe, ParentIndex=0)  — Galaxy object in Universe space
//   Index 2: Star        (Space.Galaxy,   ParentIndex=1)  — Star object in Galaxy space
//   Index 3: PlanetA     (Space.Star,     ParentIndex=2)  — Planet in Star space
//   Index 4: Ship        (Space.Planet,   ParentIndex=3)  — Ship in Planet space
//   Index 5: AlphaCen    (Space.Galaxy,   ParentIndex=1)  — Sibling star in Galaxy space
//   Index 6: PlanetB     (Space.Star,     ParentIndex=2)  — Second planet in Star space (sibling of PlanetA)
//
// Positions are authored to keep numbers tractable while exercising the precision
// headroom assertion (two objects whose absolute-from-root offset is ~1e17 m but whose
// true separation is exactly 1.0 m).

using System;
using System.Collections.Generic;
using Xunit;

public class UniMathTests
{
    // ── Hierarchy builder ────────────────────────────────────────────────────────

    /// <summary>
    /// Builds the canonical 7-node test hierarchy and returns it as a List&lt;UniObject&gt;
    /// indexed by UniObject.Index (List[i].Index == i for all entries).
    ///
    /// Scales per space (mirrors UniObject.Scale):
    ///   Universe: 1e16   Galaxy: 10000   Star: 1   Planet: 0.0001
    ///
    /// LocalPos for each body is a small non-zero vector so the maths exercises
    /// both Units and Offset rather than hitting degenerate zeros.
    /// </summary>
    private static List<UniObject> BuildHierarchy()
    {
        var objs = new List<UniObject>(new UniObject[7]);

        // Root (Scale = -1, no LocalPos used in the walk)
        objs[0] = new UniObject
        {
            Index        = 0,
            CurrentSpace = UniObject.Space.Root,
            ParentIndex  = -1,
            LocalPos     = new UniVec3(Double3.Zero, 1.0),   // Root scale irrelevant
        };

        // Galaxy object in Universe space (scale 1e16 m/unit)
        double galaxyScale = UniObject.Scale(UniObject.Space.Universe);   // 1e16
        objs[1] = new UniObject
        {
            Index        = 1,
            CurrentSpace = UniObject.Space.Universe,
            ParentIndex  = 0,
            LocalPos     = new UniVec3(new Double3(0, 0, 0), galaxyScale),   // Galaxy at Universe origin
        };

        // Star in Galaxy space (scale 10000 m/unit)
        double starGalaxyScale = UniObject.Scale(UniObject.Space.Galaxy);   // 10000
        objs[2] = new UniObject
        {
            Index        = 2,
            CurrentSpace = UniObject.Space.Galaxy,
            ParentIndex  = 1,
            LocalPos     = new UniVec3(new Double3(500_000.0, 0, 0), starGalaxyScale),   // 5 AU-ish in Galaxy space
        };

        // PlanetA in Star space (scale 1 m/unit)
        double planetStarScale = UniObject.Scale(UniObject.Space.Star);   // 1
        objs[3] = new UniObject
        {
            Index        = 3,
            CurrentSpace = UniObject.Space.Star,
            ParentIndex  = 2,
            LocalPos     = new UniVec3(new Double3(1.496e11, 0, 0), planetStarScale),   // 1 AU from star
        };

        // Ship in Planet space (scale 0.0001 m/unit)
        double shipPlanetScale = UniObject.Scale(UniObject.Space.Planet);   // 0.0001
        objs[4] = new UniObject
        {
            Index        = 4,
            CurrentSpace = UniObject.Space.Planet,
            ParentIndex  = 3,
            LocalPos     = new UniVec3(new Double3(100.0, 0, 0), shipPlanetScale),   // 100 m from planet surface
        };

        // AlphaCen sibling star in Galaxy space (Scale 10000 m/unit)
        // Position: 4.3 light-years ≈ 4.07e16 m; in Galaxy space units = 4.07e16 / 10000 = 4.07e12 units.
        // We encode as Long3 units + small offset so Normalize keeps Units intact.
        double alphaCenMeters = 4.07e16;
        long alphaCenUnits = (long)(alphaCenMeters / starGalaxyScale);   // 4_070_000_000_000 units
        double alphaCenOffset = alphaCenMeters - alphaCenUnits * starGalaxyScale;
        objs[5] = new UniObject
        {
            Index        = 5,
            CurrentSpace = UniObject.Space.Galaxy,
            ParentIndex  = 1,
            LocalPos     = new UniVec3(
                               new Long3(alphaCenUnits, 0, 0),
                               new Double3(alphaCenOffset, 0, 0),
                               starGalaxyScale),
        };

        // PlanetB: second planet in Star space, sibling of PlanetA (same parent = Star index 2)
        objs[6] = new UniObject
        {
            Index        = 6,
            CurrentSpace = UniObject.Space.Star,
            ParentIndex  = 2,
            LocalPos     = new UniVec3(new Double3(2.279e11, 0, 0), planetStarScale),   // ~Mars-like orbit
        };

        // Wire ChildIndices (needed for hierarchy tests that walk children).
        objs[0].ChildIndices.Add(1);
        objs[1].ChildIndices.Add(2);
        objs[1].ChildIndices.Add(5);
        objs[2].ChildIndices.Add(3);
        objs[2].ChildIndices.Add(6);
        objs[3].ChildIndices.Add(4);

        return objs;
    }

    // ── FindLca ─────────────────────────────────────────────────────────────────

    [Fact]
    public void FindLca_ShipAndAlphaCen_ReturnsGalaxyIndex()
    {
        // Ship (index 4, Planet space) and AlphaCen (index 5, Galaxy space).
        // LCA walk: Ship→PlanetA→Star→Galaxy(index1)←AlphaCen. LCA = Galaxy = index 1.
        var objs = BuildHierarchy();
        int lca = UniMath.FindLca(objs[4], objs[5], objs);
        Assert.Equal(1, lca);   // Galaxy index
    }

    [Fact]
    public void FindLca_ShipAndPlanetB_ReturnsStarIndex()
    {
        // Ship (index 4, Planet space) and PlanetB (index 6, Star space).
        // Walk: Ship→PlanetA→Star(index2)←PlanetB. LCA = Star = index 2.
        var objs = BuildHierarchy();
        int lca = UniMath.FindLca(objs[4], objs[6], objs);
        Assert.Equal(2, lca);   // Star index
    }

    [Fact]
    public void FindLca_SameObject_ReturnsItsOwnIndex()
    {
        // FindLca(x, x) — the first walk adds x to the ancestor set; the second
        // walk starts at x.Index and immediately finds it in the set → returns x.
        var objs = BuildHierarchy();
        int lca = UniMath.FindLca(objs[4], objs[4], objs);
        Assert.Equal(4, lca);
    }

    // ── RelativePosition ────────────────────────────────────────────────────────

    [Fact]
    public void RelativePosition_ShipAndAlphaCen_ReturnsTrueAndLcaChildScale()
    {
        // RelativePosition must return true and the result must be at the LCA's child-scale.
        // LCA = Galaxy (index 1, Space.Universe). Its children (Star, AlphaCen) are in
        // Space.Galaxy, so child-scale = UniObject.Scale(Space.Galaxy) = 10000.
        var objs = BuildHierarchy();
        bool ok = UniMath.RelativePosition(objs[4], objs[5], objs, out UniVec3 rel);
        Assert.True(ok);
        Assert.Equal(UniObject.Scale(UniObject.Space.Galaxy), rel.Scale, precision: 6);
    }

    [Fact]
    public void RelativePosition_NoCommonAncestor_ReturnsFalse()
    {
        // Construct two objects with no shared ancestor (disjoint hierarchies).
        // Root-disconnected: set ParentIndex such that neither walks to the other's chain.
        var isolated1 = new UniObject { Index = 0, CurrentSpace = UniObject.Space.Star, ParentIndex = -1 };
        var isolated2 = new UniObject { Index = 1, CurrentSpace = UniObject.Space.Star, ParentIndex = -1 };
        var objs = new List<UniObject> { isolated1, isolated2 };
        bool ok = UniMath.RelativePosition(objs[0], objs[1], objs, out _);
        Assert.False(ok);
    }

    // ── RelativeMetres antisymmetry ─────────────────────────────────────────────

    [Fact]
    public void RelativeMetres_IsAntisymmetric_ShipToAlphaCen()
    {
        // RelativeMetres(a, b) = -RelativeMetres(b, a) within floating-point tolerance.
        // Antisymmetry holds because the LCA subtraction is symmetric:
        //   (b_lca - a_lca) = -(a_lca - b_lca).
        // This confirms exact Units cancellation in the LCA frame.
        var objs = BuildHierarchy();
        Double3 ab = UniMath.RelativeMetres(objs[4], objs[5], objs);
        Double3 ba = UniMath.RelativeMetres(objs[5], objs[4], objs);

        const double tol = 1e-3;   // 1 mm tolerance over ~4 light-years
        Assert.InRange(ab.X + ba.X, -tol, tol);
        Assert.InRange(ab.Y + ba.Y, -tol, tol);
        Assert.InRange(ab.Z + ba.Z, -tol, tol);
    }

    [Fact]
    public void RelativeMetres_IsAntisymmetric_ShipToPlanetB()
    {
        var objs = BuildHierarchy();
        Double3 ab = UniMath.RelativeMetres(objs[4], objs[6], objs);
        Double3 ba = UniMath.RelativeMetres(objs[6], objs[4], objs);

        const double tol = 1e-6;   // 1 micrometre tolerance over ~1 AU
        Assert.InRange(ab.X + ba.X, -tol, tol);
        Assert.InRange(ab.Y + ba.Y, -tol, tol);
        Assert.InRange(ab.Z + ba.Z, -tol, tol);
    }

    // ── Precision headroom assertion ─────────────────────────────────────────────

    /// <summary>
    /// Two objects separated by exactly 1.0 m in Galaxy space, both placed at
    /// a Galaxy-space offset of ~4e16 m from the Galaxy origin (approximately
    /// Alpha Centauri distance). Their absolute-from-root metre values would be
    /// ~4e16 m; a naive double subtraction at that magnitude loses ~1 digit of
    /// precision per decade → error ~1e1 m at 1e16 m scale (>1e15 relative error).
    ///
    /// UniMath.RelativeMetres uses the LCA-relative UniVec3 walk: the large shared
    /// offset (~4e16 m) cancels exactly in the Long3 integer subtraction, leaving
    /// only the 1.0 m delta. The result must be within 1e-9 m of the true 1.0 m
    /// separation — demonstrating the precision win the old metres walk could not deliver.
    /// </summary>
    [Fact]
    public void PrecisionHeadroom_TinyGapAtGalaxyScale_RecoveredWithHighPrecision()
    {
        // Two Galaxy-space bodies (children of Galaxy at Universe origin, index 1).
        // Base offset: 4e16 m from Galaxy origin (Alpha-Cen-like distance).
        // Separation: exactly 1.0 m.
        double galScale = UniObject.Scale(UniObject.Space.Galaxy);   // 10000 m/unit
        double baseMeters = 4.0e16;
        long   baseUnits  = (long)(baseMeters / galScale);           // 4_000_000_000_000 units
        double baseOffset = baseMeters - baseUnits * galScale;       // residual sub-unit fraction

        // bodyA at baseMeters
        var bodyA = new UniObject
        {
            Index        = 10,
            CurrentSpace = UniObject.Space.Galaxy,
            ParentIndex  = 1,
            LocalPos     = new UniVec3(new Long3(baseUnits, 0, 0),
                                      new Double3(baseOffset, 0, 0),
                                      galScale),
        };

        // bodyB at baseMeters + 1.0 m (exactly one more metre in the same direction)
        double sepOffset = baseOffset + 1.0;   // may overflow one unit; Normalize handles it
        var bodyB = new UniObject
        {
            Index        = 11,
            CurrentSpace = UniObject.Space.Galaxy,
            ParentIndex  = 1,
            LocalPos     = new UniVec3(new Long3(baseUnits, 0, 0),
                                      new Double3(sepOffset, 0, 0),
                                      galScale),
        };

        // Galaxy parent (the LCA for both bodies)
        var galaxy = new UniObject
        {
            Index        = 1,
            CurrentSpace = UniObject.Space.Universe,
            ParentIndex  = 0,
            LocalPos     = new UniVec3(Double3.Zero, UniObject.Scale(UniObject.Space.Universe)),
        };
        var root = new UniObject
        {
            Index        = 0,
            CurrentSpace = UniObject.Space.Root,
            ParentIndex  = -1,
            LocalPos     = new UniVec3(Double3.Zero, 1.0),
        };

        // Build a minimal list (must accommodate indices 0..11)
        var objs = new List<UniObject>(new UniObject[12]);
        objs[0]  = root;
        objs[1]  = galaxy;
        objs[10] = bodyA;
        objs[11] = bodyB;

        // RelativeMetres(bodyA, bodyB) must recover the 1.0 m separation to < 1e-9 m error.
        Double3 delta = UniMath.RelativeMetres(bodyA, bodyB, objs);

        // X component should be +1.0 m (bodyB is bodyA + 1.0 m in +X)
        double err = Math.Abs(delta.X - 1.0);
        Assert.True(err < 1e-9,
            $"Expected 1.0 m separation recovered to < 1e-9 m; actual delta.X={delta.X}, error={err}");
        // Y and Z must be zero (bodies share the same Y/Z)
        Assert.Equal(0.0, delta.Y, precision: 12);
        Assert.Equal(0.0, delta.Z, precision: 12);
    }

    // ── Distance ────────────────────────────────────────────────────────────────

    [Fact]
    public void Distance_IsPositive_ShipToAlphaCen()
    {
        var objs = BuildHierarchy();
        double d = UniMath.Distance(objs[4], objs[5], objs);
        Assert.True(d > 0.0, $"Expected positive distance, got {d}");
    }

    [Fact]
    public void Distance_IsSymmetric_ShipToAlphaCen()
    {
        var objs = BuildHierarchy();
        double ab = UniMath.Distance(objs[4], objs[5], objs);
        double ba = UniMath.Distance(objs[5], objs[4], objs);
        Assert.Equal(ab, ba, precision: 3);   // 1 mm tolerance
    }
}
