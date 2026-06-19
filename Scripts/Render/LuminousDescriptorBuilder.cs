using Godot;
using System.Collections.Generic;

namespace Render
{
    /// <summary>
    /// Per-frame single classify→project→appearance loop that produces one
    /// <see cref="LuminousBodyDescriptor"/> per luminous body, exposing them via
    /// <see cref="Descriptors"/> and <see cref="DescriptorCount"/>.
    ///
    /// All downstream drawers (WorldRenderer mesh path, LuminousPassRenderer post-process)
    /// consume this array read-only — D-02 "one descriptor, shared by all drawers" principle.
    /// This eliminates the dual <c>_skyDirs</c> / <c>_lastRenderPositions</c> caches and
    /// makes representation crossfades pop-free by construction (same numbers, different drawer).
    ///
    /// Read-only consumer of GameWorld state — MUST NOT mutate UniVec3 or call TranslatePos.
    ///
    /// Process priority: must be set LOWER (runs earlier) than SkyboxRenderer and WorldRenderer
    /// in Main.tscn so <see cref="Descriptors"/> is populated before any consumer reads it.
    /// (Pitfall 3 from RESEARCH.md — never run the classify+project loop twice per frame.)
    /// </summary>
    public partial class LuminousDescriptorBuilder : Node
    {
        // ----- Exports --------------------------------------------------------

        /// <summary>NodePath to the TestSetup/GameWorld node. If empty, auto-resolved.</summary>
        [Export] public NodePath WorldPath { get; set; }

        // ----- Constants ------------------------------------------------------

        private const int MaxStars    = 128;
        private const int MaxGalaxies = 32;

        /// <summary>
        /// Render units per observer-unit (metres / ship.LocalPos.Scale → observer units, × this factor → render units).
        /// Shared with <see cref="WorldRenderer.StarRenderFactor"/> — both must always be 1e-8f.
        /// Defined here (in the test-compiled set) so <see cref="ComputeDescriptor"/> can use it
        /// without pulling the full Godot-Node WorldRenderer into the test project.
        /// </summary>
        internal const float StarRenderFactor = 1e-8f;

        /// <summary>Safety cap on a sky-point disc angular radius (radians, ~28°).</summary>
        private const float MaxDiscAngle = 0.5f;

        // ----- Pre-allocated output (WR-01: never allocate per frame) --------

        private readonly LuminousBodyDescriptor[] _descriptors =
            new LuminousBodyDescriptor[MaxStars + MaxGalaxies];

        private int _descriptorCount = 0;

        // ----- Private state --------------------------------------------------

        private TestSetup _world;
        private Camera3D  _cam;

        // ----- Public read-only output ----------------------------------------

        /// <summary>
        /// Descriptors produced by the most-recent <see cref="BuildDescriptors"/> call.
        /// Read by downstream renderers. Length is always <see cref="MaxStars"/> + <see cref="MaxGalaxies"/>;
        /// only the first <see cref="DescriptorCount"/> entries are valid.
        /// </summary>
        public LuminousBodyDescriptor[] Descriptors => _descriptors;

        /// <summary>Number of valid entries in <see cref="Descriptors"/> after the last <see cref="BuildDescriptors"/> call.</summary>
        public int DescriptorCount => _descriptorCount;

        // ----- Godot callbacks ------------------------------------------------

        public override void _Ready()
        {
            // Resolve world reference — same pattern as SkyboxRenderer._Ready.
            if (WorldPath != null && !WorldPath.IsEmpty)
                _world = GetNode<TestSetup>(WorldPath);
            else
                _world = GetParent<TestSetup>() ?? GetTree().Root.FindChild("Main", true, false) as TestSetup;

            // Cache camera for PixelAngularSize.
            _cam = GetTree().Root.FindChild("Camera3D", true, false) as Camera3D;

            if (_world == null)
                GD.PrintErr("[LuminousDescriptorBuilder] Could not resolve world (TestSetup) node. " +
                            "Set WorldPath export or ensure Main.tscn hierarchy is correct.");
        }

        public override void _Process(double delta)
        {
            if (_world == null) return;
            BuildDescriptors();
        }

        // ----- Core classify+project loop ------------------------------------

        /// <summary>
        /// Runs one classify→project→appearance loop over all GameObjects and populates
        /// <see cref="Descriptors"/> with one <see cref="LuminousBodyDescriptor"/> per
        /// visible luminous body. Safe to call from tests via the static <see cref="ComputeDescriptor"/>
        /// helper when the Node is not instantiated.
        ///
        /// Only updates <see cref="DescriptorCount"/>; never allocates.
        /// Strictly read-only — never writes to GameObjects, LocalPos, or ChildIndices.
        /// </summary>
        public void BuildDescriptors()
        {
            var objs    = _world.GameObjects;
            int shipIdx = _world.ShipIndex;

            // Bounds-check idiom (WorldRenderer line 206, SkyboxRenderer line 136).
            var ship = (uint)shipIdx < (uint)objs.Count ? objs[shipIdx] : null;
            if (ship == null) { _descriptorCount = 0; return; }

            float pixelAngle = PixelAngularSize();
            _descriptorCount = 0;

            for (int i = 0; i < objs.Count; i++)
            {
                // Capacity guard (T-05-01 mitigation: pre-allocated array capped at MaxStars+MaxGalaxies).
                if (_descriptorCount >= _descriptors.Length) break;

                var body = objs[i];
                if (body == null) continue;                  // null-slot guard (T-05-EP)
                if (body.Index == shipIdx) continue;         // skip the ship itself

                // TierClassifier: skip bodies with no meaningful sky presence.
                var tier = TierClassifier.Classify(body, ship);
                if (tier == SkyTier.Skip || tier == SkyTier.Beyond) continue;

                // Home-galaxy suppression guard (SkyboxRenderer lines 207-209 / must-have truth #2):
                // While the ship is inside this galaxy's SOI (galaxy is an ancestor of the ship),
                // the galaxy must NOT render — only non-ancestor galaxies appear.
                // FindLca(ship, body) == body.Index is exactly "body is an ancestor of the ship".
                if (body.ObjectType == UniObject.Type.Galaxy
                    && UniMath.FindLca(ship, body, objs) == body.Index)
                    continue;

                // Build the descriptor for this body.
                var desc = ComputeDescriptor(body, ship, objs, pixelAngle);

                _descriptors[_descriptorCount++] = desc;
            }
        }

        // ----- Per-body computation (static helper for testability) ----------

        /// <summary>
        /// Computes a <see cref="LuminousBodyDescriptor"/> for a single body relative to the ship.
        /// Extracted as a static method so unit tests can exercise the pure math without
        /// instantiating a Godot Node.
        ///
        /// All direction math uses the UniMath LCA-relative path (MANDATORY per CLAUDE.md
        /// §"Position Math" — NEVER form absolute-from-root metres then subtract).
        /// </summary>
        /// <param name="body">The body to describe. Must not be null.</param>
        /// <param name="ship">The player ship. Must not be null.</param>
        /// <param name="objs">Full GameObjects list.</param>
        /// <param name="pixelAngle">Minimum angular size in radians (one screen pixel).</param>
        public static LuminousBodyDescriptor ComputeDescriptor(
            UniObject body, UniObject ship, List<UniObject> objs, float pixelAngle)
        {
            // LCA-relative direction — MANDATORY path (CLAUDE.md §Position Math).
            // UniMath.RelativePosition returns body − ship in the LCA child-frame as a UniVec3;
            // exact integer Units cancellation at any scale. ToDouble3() is called ONCE on the
            // small differenced delta to get metres (never on an absolute-from-root value).
            bool hasLca = UniMath.RelativePosition(ship, body, objs, out UniVec3 relUni);
            Double3 delta = hasLca ? relUni.ToDouble3() : Double3.Zero;
            double  len   = hasLca ? delta.Magnitude() : 0.0;

            Vector3 dir3;
            if (!hasLca || len < 1e-30)
                dir3 = Vector3.Up;   // safe fallback for degenerate/coincident bodies
            else
            {
                Double3 dir = delta * (1.0 / len);
                dir3 = new Vector3((float)dir.X, (float)dir.Y, (float)dir.Z);
            }

            // Appearance via StarRendering — single source of truth (D-01, StarRendering.cs).
            // Angular radius: same rule as mesh sphere, floored at one pixel, capped at MaxDiscAngle.
            double theta = StarRendering.AngularRadius(body.RadiusMeters, len);
            float  eff   = Mathf.Clamp((float)theta, pixelAngle, MaxDiscAngle);
            float  size  = 1f - Mathf.Cos(eff);

            // Apparent brightness: inverse-square magnitude curve clamped to [0,1].
            float  alpha = StarRendering.ApparentBrightness(body.Luminosity, len);

            // LOD weight: smooth distance function from LuminousLod (D-03, never a SOI flag).
            float lodWeight = body.ObjectType == UniObject.Type.Galaxy
                ? LuminousLod.GalaxyDiscWeight(len, body.SOIMeters)
                : LuminousLod.StarMeshWeight(len);

            // Render-space distance: mirrors WorldRenderer's metres→render conversion exactly.
            // WorldRenderer.RenderBodyAt: r = rawRadiusMeters / ship.LocalPos.Scale * factor
            // WorldRenderer.ComputeStarRenderPosFromHierarchy: obsFactor = factor / ship.LocalPos.Scale
            // All three use metres / ship.LocalPos.Scale * StarRenderFactor — single source of truth.
            // StarRenderFactor is defined here (accessible to tests) and must equal WorldRenderer.StarRenderFactor.
            // Defensive guard: ship.LocalPos.Scale is a valid non-zero universe scale in practice,
            // but guard against a degenerate zero to avoid a division-by-zero crash.
            double renderDistance = ship.LocalPos.Scale != 0.0
                ? len / ship.LocalPos.Scale * StarRenderFactor
                : 0.0;

            // BaseColor.A = Brightness matches the star_colors[i].a packing in skybox.gdshader.
            var desc = new LuminousBodyDescriptor
            {
                BodyIndex         = body.Index,
                Direction         = dir3,
                AngularSize       = size,
                Brightness        = alpha,
                BaseColor         = new Color(body.BaseColor.R, body.BaseColor.G, body.BaseColor.B, alpha),
                LodWeight         = lodWeight,
                BodyType          = body.ObjectType,
                GalaxyType        = body.GalaxyType,
                GalaxyOrientation = new Vector4(
                    body.GalaxyOrientation.X, body.GalaxyOrientation.Y, body.GalaxyOrientation.Z,
                    body.GalaxySeed),
                DistanceMeters    = len,
                RenderDistance    = renderDistance,
            };

            return desc;
        }

        // ----- Helpers -------------------------------------------------------

        /// <summary>
        /// Angular size of a single screen pixel in radians, from the camera's vertical FOV
        /// and the viewport height. Used as the minimum disc size — a star cannot render
        /// smaller than the display can resolve.
        /// Copied verbatim from SkyboxRenderer (same camera/viewport pattern).
        /// </summary>
        private float PixelAngularSize()
        {
            float fovRad = Mathf.DegToRad(_cam?.Fov ?? 75f);
            float height = GetViewport()?.GetVisibleRect().Size.Y ?? 1080f;
            if (height < 1f) height = 1080f;
            return fovRad / height;
        }
    }
}
