using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

public class UniObject
{
    public enum Type
    {
        Orb, Asteroid, Ship, None,
        /// <summary>Emissive body: renders as mesh in current tier; sky-point in ancestor tier (D-38).</summary>
        Star,
        /// <summary>Procedural sky disc only — WorldRenderer skips entirely (D-28/D-38).</summary>
        Galaxy,
        /// <summary>Lit mesh in current tier (D-38).</summary>
        Planet,
    }

    public enum Space
    {
        Root, Universe, Galaxy, Star, Planet
    }

    public static double Scale(Space space)
    {
        return space switch
        {
            Space.Universe => 1e16,
            Space.Galaxy   => 10000,
            Space.Star     => 1,
            Space.Planet   => 0.0001,
            _              => -1
        };
    }

    public static Space ChildSpace(Space parentSpace)
    {
        return parentSpace switch
        {
            Space.Root     => Space.Universe,
            Space.Universe => Space.Galaxy,
            Space.Galaxy   => Space.Star,
            Space.Star     => Space.Planet,
            _              => Space.Planet
        };
    }

    public static Space ParentSpace(Space childSpace)
    {
        return childSpace switch
        {
            Space.Planet   => Space.Star,
            Space.Star     => Space.Galaxy,
            Space.Galaxy   => Space.Universe,
            Space.Universe => Space.Root,
            _              => Space.Root
        };
    }

    public static Space IndexToSpace(int index)
    {
        var spaces = Enum.GetValues(typeof(Space)).Cast<Space>().ToArray();
        return spaces[System.Math.Min(System.Math.Max(index, 0), spaces.Length - 1)];
    }

    public static int SpaceToIndex(Space space)
    {
        var spaces = Enum.GetValues(typeof(Space)).Cast<Space>().ToArray();
        return Array.FindIndex(spaces, s => space.Equals(s));
    }

    public int             Index;
    public Space           CurrentSpace;
    public int             ParentIndex;
    public double          SOIMeters;
    public UniVec3         LocalPos;

    // ── Presentation data ─────────────────────────────────────────────
    /// <summary>Human-readable name displayed by the HUD and target readout.</summary>
    public string          Name;

    /// <summary>
    /// Authored base hue for dithered rendering. Consumed by WorldRenderer when
    /// building the body's <c>StandardMaterial3D.AlbedoColor</c> (Plan 01-02).
    /// </summary>
    public Godot.Color     BaseColor;

    /// <summary>
    /// True 1:1 physical radius in metres (Star-space-equivalent units).
    /// Consumed by WorldRenderer for mesh scaling (meters → observer units → × factor)
    /// and by Plan 03 FLT-03 surface-distance speed scaling.
    /// </summary>
    public double          RadiusMeters;

    /// <summary>
    /// Absolute luminosity in solar luminosity units (L_sun = 3.828e26 W).
    /// Drives the magnitude model in SkyboxRenderer (D-26/D-17).
    /// Default 1.0 = solar luminosity; set to 0.0 for non-emissive bodies.
    /// </summary>
    public double          Luminosity = 1.0;

    // ── Body type + galaxy presentation fields (D-38/D-29) ───────────────

    /// <summary>Body role: drives renderer routing (D-38). Named ObjectType (not Type) to avoid
    /// shadowing the Type enum name (CS0118 pitfall — RESEARCH.md Pitfall 1).</summary>
    public Type            ObjectType = Type.None;

    /// <summary>0 = spiral, 1 = elliptical. Consumed by skybox.gdshader galaxy loop (D-29).</summary>
    public int             GalaxyType;

    /// <summary>Procedural arm/texture seed packed as float. Consumed by skybox.gdshader spiral_galaxy (D-29).</summary>
    public float           GalaxySeed;

    /// <summary>Galaxy disc normal in world space; controls orientation of the rendered disc (D-29).</summary>
    public Vector3         GalaxyOrientation;

    /// <summary>
    /// Indices into GameWorld.GameObjects of all direct children of this object.
    /// Maintained by GameWorld.AddGameObject / RemoveGameObject.
    /// </summary>
    public List<int> ChildIndices = [];
}
