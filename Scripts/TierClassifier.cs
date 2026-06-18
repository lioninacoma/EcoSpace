// TierClassifier.cs
// Pure C# — no Godot dependency. Intentionally kept dependency-free for unit testing
// (Plan 03 can test this in a plain classlib without GodotSharp).
//
// Source of truth for which bodies SkyboxRenderer renders as sky points vs WorldRenderer
// renders as meshes, per the tiered space model (RND-05, D-22).
//
// Classification rule (ship.CurrentSpace drives the entire partitioning):
//   null or same-Index         → Skip             (self or invalid)
//   Root-space body            → Skip             (Root has no meaningful position)
//   body.ObjectType == Galaxy  → NextTierSkybox   (D-49: always sky, regardless of tier)
//   body.CurrentSpace == ship.CurrentSpace
//                              → CurrentTierMesh  (WorldRenderer owns these)
//   body.CurrentSpace is any ancestor of ship.CurrentSpace
//                              → NextTierSkybox   (SkyboxRenderer draws these as points)
//   everything else            → Beyond           (sibling or child spaces — not rendered)

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
    /// Body is in any ancestor space of the ship's current space.
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

        // D-49: Galaxy-typed bodies are ALWAYS sky entities, regardless of tier match.
        // Prevents Galaxy-in-Universe-space from returning CurrentTierMesh, which WorldRenderer
        // skips entirely (D-28/D-38 guard), leaving nothing drawn (Pitfall 5 in RESEARCH.md).
        if (body.ObjectType == UniObject.Type.Galaxy)   return SkyTier.NextTierSkybox;

        // WorldRenderer renders all bodies whose CurrentSpace matches the ship's CurrentSpace
        // (they share the same parent coordinate frame as the ship).
        if (body.CurrentSpace == ship.CurrentSpace)     return SkyTier.CurrentTierMesh;

        // Any body whose space is an ancestor of the ship's current space is a sky point.
        // e.g. ship in Planet → Star and Galaxy bodies are all sky points.
        var s = ship.CurrentSpace;
        while (s != UniObject.Space.Root)
        {
            s = UniObject.ParentSpace(s);
            if (body.CurrentSpace == s) return SkyTier.NextTierSkybox;
        }

        return SkyTier.Beyond;
    }
}
