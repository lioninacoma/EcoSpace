using Godot;
using System.Collections.Generic;

namespace Render
{
    /// <summary>
    /// MultiMesh PSF star-point renderer (D-56/D-57). Draws every <see cref="UniObject.Type.Star"/>
    /// body as an additive quad at all tiers, fed per-frame from <see cref="UniObject"/> +
    /// <see cref="StarRendering"/> via the <see cref="UniMath"/> LCA path (floating-origin,
    /// Pitfall 1/2/3). The sphere mesh in <see cref="WorldRenderer"/> layers additively on top
    /// as true distance shrinks, producing the continuous D-53 distance-driven blend.
    ///
    /// Technique adapted from tiffany352/godot-starlight (Copyright 2023 Tiffany Bennett, MIT).
    ///
    /// Read-only consumer of GameWorld state — MUST NOT mutate UniVec3 or call TranslatePos.
    /// </summary>
    public partial class StarPointRenderer : Node3D
    {
        // ----- Exports --------------------------------------------------------

        /// <summary>NodePath to the TestSetup / GameWorld node in the scene.</summary>
        [Export] public NodePath WorldPath { get; set; }

        /// <summary>
        /// Minimum on-screen quad half-size as a fraction of projected.w.
        /// This is the "brightness floor" — even the faintest star at max distance is always
        /// visible as at least this large a point (D-56). Tune in-game; default 0.003.
        /// </summary>
        [Export] public float MinSizeRatio { get; set; } = 0.003f;

        /// <summary>
        /// Maximum per-star brightness in [0,1]. Prevents near-star saturation to white and
        /// keeps BaseColor hue intact. Pushed to the shader as the luminosity_cap uniform.
        /// </summary>
        [Export] public float LuminosityCap { get; set; } = 0.95f;

        /// <summary>
        /// Halo wing width multiplier (D-55). Wider halo → more bloom feed. Pushed to shader.
        /// </summary>
        [Export] public float HaloScale { get; set; } = 0.3f;

        /// <summary>
        /// Overall emission multiplier. Values > 1.0 feed WorldEnvironment bloom (D-55).
        /// Pushed to the shader as emission_energy.
        /// </summary>
        [Export] public float EmissionEnergy { get; set; } = 1.0f;

        // ----- Per-space render factors (verbatim copy from WorldRenderer — D-56 reuses
        //       the same RND-06 render-scale model, no star-specific factor) ----------------

        private float PlanetRenderFactor   { get; } = 1e-8f;
        private float StarRenderFactor     { get; } = 1e-8f;
        private float GalaxyRenderFactor   { get; } = 1e-8f;
        private float UniverseRenderFactor { get; } = 1e-8f;

        /// <summary>Maps observer (ship) space to the appropriate per-space render factor.</summary>
        private float RenderFactorFor(UniObject.Space space) => space switch
        {
            UniObject.Space.Planet   => PlanetRenderFactor,
            UniObject.Space.Star     => StarRenderFactor,
            UniObject.Space.Galaxy   => GalaxyRenderFactor,
            UniObject.Space.Universe => UniverseRenderFactor,
            _                        => StarRenderFactor,
        };

        // ----- Private state --------------------------------------------------

        private TestSetup          _world;
        private MultiMeshInstance3D _mmi;
        private MultiMesh           _mm;
        private int                 _starCount;
        private int[]               _starIndices;

        // ----- Godot callbacks ------------------------------------------------

        public override void _Ready()
        {
            // Resolve world reference — same fallback chain as WorldRenderer._Ready.
            if (WorldPath != null && !WorldPath.IsEmpty)
                _world = GetNode<TestSetup>(WorldPath);
            else
                _world = GetParent<TestSetup>() ?? GetTree().Root.FindChild("Main", true, false) as TestSetup;

            if (_world == null)
            {
                GD.PrintErr("[StarPointRenderer] Could not resolve world (TestSetup). Node will idle.");
                return;
            }

            // Scan GameObjects once to find all Type.Star bodies.
            var objs = _world.GameObjects;
            var starIndicesList = new List<int>();
            for (int i = 0; i < objs.Count; i++)
            {
                var obj = objs[i];
                if (obj != null && obj.ObjectType == UniObject.Type.Star)
                    starIndicesList.Add(i);
            }
            _starIndices = starIndicesList.ToArray();
            _starCount   = _starIndices.Length;

            if (_starCount == 0)
            {
                GD.Print("[StarPointRenderer] No Type.Star bodies found — renderer will idle.");
                return;
            }

            // Build MultiMesh.
            // IMPORTANT: UseColors and UseCustomData must be set BEFORE InstanceCount
            // (GodotSharp 4.6.2 doc: "Can only be set when InstanceCount is 0 or less").
            _mm = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                UseColors       = true,      // A1 resolved: PascalCase in GodotSharp
                UseCustomData   = true,      // A1 resolved: PascalCase in GodotSharp
                Mesh            = new QuadMesh(),
                InstanceCount   = _starCount,
            };

            // Load the PSF shader and build a ShaderMaterial.
            var psfShader = GD.Load<Shader>("res://Shaders/star_point.gdshader");
            var mat = new ShaderMaterial { Shader = psfShader };

            // Push [Export] knobs as shader uniforms.
            mat.SetShaderParameter("min_size_ratio",  MinSizeRatio);
            mat.SetShaderParameter("luminosity_cap",  LuminosityCap);
            mat.SetShaderParameter("emission_energy", EmissionEnergy);
            mat.SetShaderParameter("halo_scale",      HaloScale);

            // Create and mount the MultiMeshInstance3D as a child of this node.
            // This node itself is a child of Main at world origin (Pitfall 2 — do NOT move it).
            _mmi = new MultiMeshInstance3D
            {
                Multimesh        = _mm,
                MaterialOverride = mat,
            };
            AddChild(_mmi);

            GD.Print($"[StarPointRenderer] Ready — {_starCount} star(s) instanced.");
        }

        public override void _Process(double delta)
        {
            if (_world == null || _mm == null || _starCount == 0) return;

            var objs     = _world.GameObjects;
            int shipIdx  = _world.ShipIndex;

            // Bounds-check idiom (WorldRenderer.cs lines 206-207).
            var ship = (uint)shipIdx < (uint)objs.Count ? objs[shipIdx] : null;
            if (ship == null) return;

            float factor = RenderFactorFor(ship.CurrentSpace);
            double scale = ship.LocalPos.Scale;

            for (int i = 0; i < _starIndices.Length; i++)
            {
                int starObjIdx = _starIndices[i];
                var star = (uint)starObjIdx < (uint)objs.Count ? objs[starObjIdx] : null;
                if (star == null) continue;

                // MANDATORY: LCA-relative cross-frame position (CLAUDE.md §Position Math,
                // Pitfall 1). Never raw ToDouble3() on an absolute position at galaxy scale.
                // UniMath.RelativeMetres(ship, star) = star − ship in the LCA frame (metres).
                Double3 relM = UniMath.RelativeMetres(ship, star, objs);

                // Fallback if no LCA found (should never occur in a valid hierarchy).
                if (relM.X == 0.0 && relM.Y == 0.0 && relM.Z == 0.0)
                {
                    _mm.SetInstanceTransform(i, new Transform3D(Basis.Identity, Vector3.Up * 1e7f));
                    continue;
                }

                // Convert to render-space: metres → ÷ ship scale (observer units) → × factor.
                // Verbatim from WorldRenderer lines 392-403 (ComputeStarRenderPosFromHierarchy).
                double obsFactor = factor / scale;
                var rp = new Vector3(
                    (float)(relM.X * obsFactor),
                    (float)(relM.Y * obsFactor),
                    (float)(relM.Z * obsFactor));

                // Apparent brightness from the shared StarRendering model (no drift from mesh).
                float bright = Mathf.Min(
                    StarRendering.ApparentBrightness(star.Luminosity, relM.Magnitude()),
                    LuminosityCap);

                // Set per-instance data:
                //   Transform — ship-relative render-space position, identity basis (Pitfall 2).
                //   Color     — BaseColor.rgb, brightness in alpha channel (shader reads COLOR.a).
                //   CustomData — luminosity in red channel (A2: INSTANCE_CUSTOM.x in shader).
                _mm.SetInstanceTransform(i,
                    new Transform3D(Basis.Identity, rp));
                _mm.SetInstanceColor(i,
                    new Color(star.BaseColor.R, star.BaseColor.G, star.BaseColor.B, bright));
                _mm.SetInstanceCustomData(i,
                    new Color((float)star.Luminosity, 0f, 0f, 0f));
            }

            // Keep shader uniforms in sync with [Export] knobs (editor can change them live).
            if (_mmi.MaterialOverride is ShaderMaterial liveMat)
            {
                liveMat.SetShaderParameter("min_size_ratio",  MinSizeRatio);
                liveMat.SetShaderParameter("luminosity_cap",  LuminosityCap);
                liveMat.SetShaderParameter("emission_energy", EmissionEnergy);
                liveMat.SetShaderParameter("halo_scale",      HaloScale);
            }
        }
    }
}
