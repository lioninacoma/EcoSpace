using Godot;

namespace Render
{
    /// <summary>
    /// Per-body descriptor built once per frame by <see cref="LuminousDescriptorBuilder"/>.
    /// Consumed read-only by WorldRenderer (mesh side) and LuminousPassRenderer (post-process side).
    /// Replaces the dual <c>_skyDirs</c> / <c>_lastRenderPositions</c> caches (D-02):
    /// every luminous body is described exactly once, and all drawers share the same direction,
    /// brightness, color, and LOD weight — making representation crossfades mathematically
    /// pop-free by construction.
    /// </summary>
    public struct LuminousBodyDescriptor
    {
        /// <summary>Index into GameWorld.GameObjects for this body.</summary>
        public int              BodyIndex;

        /// <summary>
        /// World-space unit vector from ship to body, computed via UniMath.RelativePosition
        /// (LCA-relative path — NEVER formed as absolute-from-root metres to avoid catastrophic
        /// cancellation at Universe scale ~1e30 m). Falls back to Vector3.Up when the LCA walk
        /// fails or the distance is degenerate (&lt; 1e-30 m).
        /// </summary>
        public Vector3          Direction;

        /// <summary>
        /// Angular apparent size of the body in smoothstep space: (1 - cos theta_eff), where
        /// theta_eff is the physical angular radius clamped to [PixelAngularSize, MaxDiscAngle].
        /// Matches the SkyboxRenderer convention so post-process and mesh use identical sizes.
        /// </summary>
        public float            AngularSize;

        /// <summary>Apparent display brightness in [0,1] from StarRendering.ApparentBrightness.</summary>
        public float            Brightness;

        /// <summary>
        /// Body base color with alpha = Brightness, matching the <c>star_colors[i].a</c>
        /// packing convention in <c>skybox.gdshader</c> and <see cref="SkyboxRenderer"/>.
        /// </summary>
        public Color            BaseColor;

        /// <summary>
        /// Continuous distance-driven LOD weight from <see cref="LuminousLod"/>:
        /// 0 = far (post-process point/disc only); 1 = near (mesh dominant).
        /// Never a discrete SOI-boundary flag (D-03 anti-pattern).
        /// </summary>
        public float            LodWeight;

        /// <summary>Body classification: Star, Galaxy, etc. (UniObject.Type).</summary>
        public UniObject.Type   BodyType;

        /// <summary>Galaxy subtype: 0 = spiral, 1 = elliptical. Galaxies only.</summary>
        public int              GalaxyType;

        /// <summary>
        /// Galaxy disc orientation and procedural seed: xyz = disc_normal (world space),
        /// w = GalaxySeed. Galaxies only. Matches the <c>galaxy_orientations</c> uniform
        /// packing in <c>skybox.gdshader</c>.
        /// </summary>
        public Vector4          GalaxyOrientation;

        /// <summary>
        /// True metric distance in metres (ship to body), used to compute LodWeight.
        /// NOT pushed to a shader uniform — kept here for LOD and debugging only.
        /// </summary>
        public double           DistanceMeters;
    }
}
