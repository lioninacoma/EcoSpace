using Godot;
using System.Collections.Generic;

namespace Render
{
    /// <summary>
    /// Per-frame, read-only sky uniform push: classifies bodies via TierClassifier, computes
    /// world-space directions from ship to each NextTierSkybox body using a double-precision
    /// hierarchy walk, and pushes star_dirs/star_colors/star_sizes/star_count to the Sky
    /// ShaderMaterial each frame. Plan 02 adds the magnitude model; this plan uses a flat
    /// brightness placeholder so all sky points are equally visible.
    ///
    /// Read-only consumer of GameWorld state — MUST NOT mutate UniVec3 or call TranslatePos.
    /// </summary>
    public partial class SkyboxRenderer : Node
    {
        // ----- Exports --------------------------------------------------------

        /// <summary>NodePath to the TestSetup node (GameWorld / world state).</summary>
        [Export] public NodePath WorldPath { get; set; }

        // ----- Private state --------------------------------------------------

        private TestSetup      _world;
        private ShaderMaterial _skyMat;

        private const int MaxStars = 8;
        private readonly Vector3[] _dirs   = new Vector3[MaxStars];
        private readonly Color[]   _colors = new Color[MaxStars];
        private readonly float[]   _sizes  = new float[MaxStars];

        // ----- Godot callbacks ------------------------------------------------

        public override void _Ready()
        {
            // Resolve world reference — same pattern as WorldRenderer._Ready.
            if (WorldPath != null && !WorldPath.IsEmpty)
                _world = GetNode<TestSetup>(WorldPath);
            else
                _world = GetParent<TestSetup>() ?? GetTree().Root.FindChild("Main", true, false) as TestSetup;

            // Obtain the sky ShaderMaterial from Camera3D's Environment.
            // Walk: Camera3D → Environment → Sky → SkyMaterial (cast to ShaderMaterial).
            // Null-guard each hop so a missing resource only silences the renderer, not crashes.
            var cam = GetTree().Root.FindChild("Camera3D", true, false) as Camera3D;
            var env = cam?.Environment;
            var sky = env?.Sky;
            _skyMat = sky?.SkyMaterial as ShaderMaterial;

            if (_skyMat == null)
                GD.PrintErr("[SkyboxRenderer] Could not obtain Sky ShaderMaterial from Camera3D.Environment.Sky. " +
                            "Ensure Main.tscn has background_mode=2 (Sky) with a ShaderMaterial using skybox.gdshader.");
        }

        public override void _Process(double delta)
        {
            if (_world == null || _skyMat == null) return;
            SyncSkyPoints();
        }

        // ----- Core sky sync --------------------------------------------------

        /// <summary>
        /// Classifies all bodies, computes world-space directions to NextTierSkybox bodies,
        /// and pushes the resulting arrays to the sky ShaderMaterial.
        /// Strictly read-only — never writes to GameObjects, LocalPos, or ChildIndices.
        /// </summary>
        private void SyncSkyPoints()
        {
            var objs    = _world.GameObjects;
            int shipIdx = _world.ShipIndex;

            // Bounds-check idiom from WorldRenderer.
            var ship = (uint)shipIdx < (uint)objs.Count ? objs[shipIdx] : null;
            if (ship == null) return;

            int count = 0;
            for (int i = 0; i < objs.Count && count < MaxStars; i++)
            {
                var body = objs[i];
                if (body == null) continue;
                if (body.Index == shipIdx) continue;

                if (TierClassifier.Classify(body, ship) != SkyTier.NextTierSkybox) continue;

                // Direction: walk hierarchy in double precision to avoid precision loss at
                // interstellar distances (galaxy scale ~1e16 m/unit). Cast to Vector3 only
                // after normalizing the unit direction (Pitfall 5 in RESEARCH.md).
                _dirs[count] = ComputeSkyBodyDirection(body, ship, objs);

                // Plan 01: flat placeholder brightness/size — Plan 02 adds the real magnitude model
                // (D-17 inverse-square law + D-19 min-floor + D-18 BaseColor from body.BaseColor).
                // Using alpha = 1.0 for uniform brightness; size = 0.002f for a tight point.
                _colors[count] = new Color(body.BaseColor.R, body.BaseColor.G, body.BaseColor.B, 1.0f);
                _sizes[count]  = 0.002f;

                count++;
            }

            // Push star_count first, then arrays (only if count > 0 to avoid sending empty arrays).
            _skyMat.SetShaderParameter("star_count", count);
            if (count > 0)
            {
                _skyMat.SetShaderParameter("star_dirs",   _dirs);
                _skyMat.SetShaderParameter("star_colors", _colors);
                _skyMat.SetShaderParameter("star_sizes",  _sizes);
            }
        }

        // ----- Hierarchy direction math (read-only, double-precision) ---------

        /// <summary>
        /// Computes the normalized world-space direction from ship to skyBody in double
        /// precision. Normalizes in double before casting to Vector3 to avoid precision
        /// loss at galaxy-scale distances (Pitfall 5 in RESEARCH.md, A3 in RESEARCH.md).
        /// Returns Vector3.Up as a safe fallback when positions coincide.
        /// Read-only — MUST NOT mutate any UniObject.
        /// </summary>
        private static Vector3 ComputeSkyBodyDirection(UniObject skyBody, UniObject ship, List<UniObject> objs)
        {
            Double3 skyBodyRoot = AbsolutePositionInRoot(skyBody, objs);
            Double3 shipRoot    = AbsolutePositionInRoot(ship, objs);
            Double3 delta       = skyBodyRoot - shipRoot;
            double  len         = delta.Length();

            // Guard coincident positions (should never happen at interstellar scale).
            if (len < 1e-30) return Vector3.Up;

            Double3 dir = delta * (1.0 / len);
            return new Vector3((float)dir.X, (float)dir.Y, (float)dir.Z);
        }

        /// <summary>
        /// Walks the parent chain of <paramref name="obj"/> upward to the root, accumulating
        /// the absolute position in root-space by converting each LocalPos to its parent's scale.
        /// This mirrors <see cref="GameWorld.ChildPosToParentSpace"/> logic but is strictly
        /// read-only — it never modifies any object or calls TranslatePos.
        ///
        /// T-02-02 mitigation (threat register): loop guarded by (uint) bounds check and
        /// null guard; bounded by hierarchy depth; coincident/zero-length delta guarded in caller.
        /// </summary>
        private static Double3 AbsolutePositionInRoot(UniObject obj, List<UniObject> objs)
        {
            // Start from obj's own LocalPos expressed as a raw double (meters in obj's space).
            Double3 pos   = obj.LocalPos.ToDouble3();
            double  scale = obj.LocalPos.Scale;
            int     pIdx  = obj.ParentIndex;

            while ((uint)pIdx < (uint)objs.Count && objs[pIdx] != null)
            {
                var parent = objs[pIdx];

                // Convert pos from child scale to parent scale, then add parent's own position.
                // parent.LocalPos.Scale is the parent's per-unit meter size.
                double parentScale = parent.LocalPos.Scale;
                Double3 inParent   = pos * (scale / parentScale);
                pos   = parent.LocalPos.ToDouble3() + inParent;
                scale = parentScale;
                pIdx  = parent.ParentIndex;
            }

            return pos;
        }
    }
}
