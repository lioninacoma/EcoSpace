// TierClassifier.cs
// Pure C# — no Godot dependency. Intentionally kept dependency-free for unit testing
// (Plan 03 can test this in a plain classlib without GodotSharp).
//
// Source of truth for which bodies SkyboxRenderer renders as sky points vs WorldRenderer
// renders as meshes, per the tiered space model (RND-05, D-22).
//
// Classification rule (ship.CurrentSpace drives the entire partitioning):
//   null or same-Index         → Skip       (self or invalid)
//   Root-space body            → Skip       (Root has no meaningful position)
//   body.CurrentSpace == ship.CurrentSpace
//                              → CurrentTierMesh   (WorldRenderer owns these)
//   body.CurrentSpace == ParentSpace(ship.CurrentSpace)
//                              → NextTierSkybox    (SkyboxRenderer draws these as points)
//   everything else            → Beyond     (two or more tiers out — not rendered)

/// <summary>
/// Classifies each game body relative to the player ship's current space into one of
/// four rendering tiers: Skip, CurrentTierMesh, NextTierSkybox, or Beyond.
/// </summary>
public enum SkyTier
{
    /// <summary>Body is the ship itself, invalid, or in Root space — not rendered.</summary>
    Skip,

    /// <summary>
    /// Body shares the ship's current parent frame — WorldRenderer owns these as meshes.
    /// </summary>
    CurrentTierMesh,

    /// <summary>
    /// Body is one tier out from the ship's current space (i.e. in ParentSpace of ship.CurrentSpace).
    /// SkyboxRenderer projects these as world-fixed light points.
    /// </summary>
    NextTierSkybox,

    /// <summary>Body is two or more tiers out — not rendered in this phase.</summary>
    Beyond,
}

/// <summary>
/// Pure static classifier: maps each body to a <see cref="SkyTier"/> based solely on
/// <c>body.CurrentSpace</c>, <c>body.Index</c>, and <c>UniObject.ParentSpace</c>.
/// No GameWorld mutation, no Godot types.
/// </summary>
public static class TierClassifier
{
    /// <summary>
    /// Classify <paramref name="body"/> relative to <paramref name="ship"/>.
    /// </summary>
    /// <param name="body">The body to classify. May be null → <see cref="SkyTier.Skip"/>.</param>
    /// <param name="ship">The player ship. May be null → <see cref="SkyTier.Skip"/>.</param>
    /// <returns>The rendering tier for this body.</returns>
    public static SkyTier Classify(UniObject body, UniObject ship)
    {
        if (body == null || ship == null)               return SkyTier.Skip;
        if (body.Index == ship.Index)                   return SkyTier.Skip;
        if (body.CurrentSpace == UniObject.Space.Root)  return SkyTier.Skip;

        // WorldRenderer renders all bodies whose CurrentSpace matches the ship's CurrentSpace
        // (they share the same parent coordinate frame as the ship).
        if (body.CurrentSpace == ship.CurrentSpace)     return SkyTier.CurrentTierMesh;

        // Next tier out = the parent space of the ship's current space.
        // e.g. ship in Planet → Star is next-tier; ship in Star → Galaxy is next-tier.
        UniObject.Space nextOut = UniObject.ParentSpace(ship.CurrentSpace);
        if (body.CurrentSpace == nextOut)               return SkyTier.NextTierSkybox;

        return SkyTier.Beyond;
    }
}
