using Godot;
using System.Collections.Generic;

namespace Render
{
    /// <summary>
    /// Node3D child of Camera3D that hosts a 2×2 spatial quad mesh running
    /// <c>luminous_pass.gdshader</c> — the depth-aware post-process pass for near-star
    /// PSF lens flare (Plan 3 of Phase 5 Rendering Overhaul — narrowed to stars only,
    /// PSF revised for aperture diffraction per play-test feedback).
    ///
    /// Galaxies render entirely in <c>skybox.gdshader</c> via <see cref="SkyboxRenderer"/>
    /// (D-13 decision); galaxy uniforms and the galaxy push block have been removed from
    /// this renderer (Plan 3 narrowing).
    ///
    /// Consumes <see cref="LuminousDescriptorBuilder.Descriptors"/> read-only each frame and
    /// pushes the packed star arrays to <c>luminous_pass.gdshader</c>.
    /// The builder ran earlier this frame (process_priority=-10), so <see cref="Descriptors"/>
    /// is fully populated before this node's <c>_Process</c> executes (Pitfall 3 guard).
    ///
    /// Read-only consumer of GameWorld state — MUST NOT mutate UniVec3 or call TranslatePos.
    /// MUST NOT call BuildDescriptors() — that would double the classify+project loop (Pitfall 5).
    ///
    /// D-05 render ordering: as a Camera3D-child Node3D, this quad renders in the 3D
    /// transparent pass — BEFORE WorldEnvironment glow and BEFORE the CanvasLayer dither.
    /// The additive luminous contribution therefore feeds WorldEnvironment glow naturally.
    ///
    /// PSF knobs (play-test tunable via [Export] properties pushed as uniforms each frame):
    ///   <see cref="PsfCoreScale"/>       — tightness of the inverse-cube core blob.
    ///   <see cref="PsfSpikeLongScale"/>  — elongation factor for diffraction spikes (long axis).
    ///   <see cref="PsfSpikeShortScale"/> — width factor for diffraction spikes (short axis).
    ///   <see cref="PsfIntensity"/>       — overall PSF brightness multiplier.
    ///   <see cref="PsfLodFloor"/>        — LOD weight at which PSF begins to appear.
    ///   <see cref="PsfLodRange"/>        — width of the LOD ramp (wider = gentler fade-in).
    ///   <see cref="PsfDepthEpsilon"/>    — additive depth tolerance at the star mesh surface.
    ///
    /// Removed in Iteration 2 (depth-texture gate):
    ///   star_lin_depths[] array — was hand-computed as distMeters * RenderFactor; replaced by
    ///   a depth texture sample at the star's projected UV so both sides of the depth comparison
    ///   come from the same source and occlusion works correctly at all camera ranges.
    ///   RenderFactor export — no longer needed; star depth is now read from the depth texture.
    /// </summary>
    public partial class LuminousPassRenderer : Node3D
    {
        // ----- Exports --------------------------------------------------------

        /// <summary>NodePath to the LuminousDescriptorBuilder node (read-only descriptor source).</summary>
        [Export] public NodePath BuilderPath { get; set; }

        /// <summary>
        /// NodePath to the TestSetup/GameWorld node. Used to read ship.LocalPos.Scale
        /// each frame for the observer-unit basis conversion in star_view_dists.
        /// If empty, auto-resolved via FindChild("Main") — same pattern as LuminousDescriptorBuilder.
        /// </summary>
        [Export] public NodePath WorldPath { get; set; }

        // ── PSF tuning knobs (pushed as shader uniforms each frame) ──────────────

        /// <summary>
        /// Scale applied to the screen-space pixel delta for the PSF core falloff.
        /// Larger value → smaller, tighter core; smaller → larger, more diffuse blob.
        /// [ASSUMED D-04 play-test calibration knob]
        /// </summary>
        [Export] public float PsfCoreScale       { get; set; } = 80.0f;

        /// <summary>
        /// Long-axis scale for diffraction spikes. Applied to the perpendicular axis so the
        /// spike is elongated along the other axis. Larger → narrower spike.
        /// [ASSUMED D-04 play-test calibration knob]
        /// </summary>
        [Export] public float PsfSpikeLongScale  { get; set; } = 12.0f;

        /// <summary>
        /// Short-axis scale for diffraction spikes (the elongated axis of each spike).
        /// Smaller → fatter along the spike's main direction.
        /// [ASSUMED D-04 play-test calibration knob]
        /// </summary>
        [Export] public float PsfSpikeShortScale { get; set; } = 0.25f;

        /// <summary>
        /// Overall PSF brightness multiplier. Scale up for a more dramatic lens flare;
        /// scale down if the halo blows out the frame.
        /// [ASSUMED D-04 play-test calibration knob]
        /// </summary>
        [Export] public float PsfIntensity       { get; set; } = 1.0f;

        /// <summary>
        /// Minimum LOD weight (0–1) at which PSF begins to appear (start of fade ramp).
        /// The first faint hint of lens flare appears at this lod_weight; it grows over
        /// the range defined by <see cref="PsfLodRange"/>.
        /// [D-04 play-test calibration knob]
        /// </summary>
        [Export] public float PsfLodFloor        { get; set; } = 0.02f;

        /// <summary>
        /// Width of the LOD ramp over which PSF grows from zero to full intensity.
        /// smoothstep(PsfLodFloor, PsfLodFloor + PsfLodRange, lod).
        /// Larger value = gentler, more gradual fade-in over a longer portion of the approach.
        /// Default 0.6 spreads the ramp across most of the near-approach range, eliminating
        /// the abrupt "pop" seen with the previous hardcoded 0.2-wide window.
        /// [D-04 play-test calibration knob — Iteration 2]
        /// </summary>
        [Export] public float PsfLodRange        { get; set; } = 0.6f;

        /// <summary>
        /// Additive depth tolerance in view-space render units for the per-pixel analytic depth gate.
        /// PSF is allowed where pixel_view_z &gt;= star_view_z - PsfDepthEpsilon.
        /// A small positive value avoids z-fight shimmer at the star mesh surface without
        /// leaking PSF through foreground geometry (planets, etc.).
        ///
        /// Iteration 4 (corrected units): view-space render units = metres / ship.LocalPos.Scale * StarRenderFactor.
        /// In Planet space (scale=1e-4): star at 1 AU ≈ 1.496e7 render units; a tolerance of 50 render
        /// units ≈ 5e6 m slack — adequate to avoid z-fight shimmer while staying far within the
        /// planet-to-star distance. In Star space (scale=1): star at 1 AU ≈ 1.496e3 render units;
        /// the same 50 render unit tolerance = 5e9 m slack (still fine).
        /// [D-04 play-test calibration knob — Iteration 4]
        /// </summary>
        [Export] public float PsfDepthEpsilon    { get; set; } = 50.0f;

        // ----- Constants (T-05-01 mitigation: fixed-size caps matching the shader) ------

        private const int MaxStars = 128;

        // ----- Pre-allocated uniform arrays (WR-01: never allocate per frame) ──────────
        // Mirrors SkyboxRenderer pre-allocated pattern.
        // Galaxy arrays removed (D-13 / Plan 3): galaxies render in skybox.gdshader.

        private readonly Vector3[] _starDirs       = new Vector3[MaxStars];
        private readonly Color[]   _starColors     = new Color[MaxStars];
        private readonly float[]   _starSizes      = new float[MaxStars];
        private readonly float[]   _starLodWeights = new float[MaxStars];

        /// <summary>
        /// Per-star render-space distance from camera in observer-unit basis:
        ///   distMeters / ship.LocalPos.Scale * WorldRenderer.StarRenderFactor
        /// Matches WorldRenderer.RenderBodyAt units exactly so the depth gate works at all scales.
        /// WorldRenderer.StarRenderFactor (public const 1e-8) is the single source of truth.
        /// ship.LocalPos.Scale read fresh each frame (changes on SOI transitions).
        /// </summary>
        private readonly float[] _starViewDists = new float[MaxStars];

        // ----- Private state --------------------------------------------------

        private ShaderMaterial            _mat;
        private LuminousDescriptorBuilder _builder;
        private TestSetup                 _world;

        // ----- Godot callbacks ------------------------------------------------

        public override void _Ready()
        {
            // --- Resolve descriptor builder reference ---
            if (BuilderPath != null && !BuilderPath.IsEmpty)
                _builder = GetNode<LuminousDescriptorBuilder>(BuilderPath);
            else
                _builder = GetTree().Root.FindChild("LuminousDescriptorBuilder", true, false)
                           as LuminousDescriptorBuilder;

            if (_builder == null)
                GD.PrintErr("[LuminousPassRenderer] Could not resolve LuminousDescriptorBuilder. " +
                            "Set BuilderPath export or ensure Main.tscn hierarchy is correct.");

            // --- Resolve world reference (needed for ship.LocalPos.Scale each frame) ---
            // Same pattern as LuminousDescriptorBuilder._Ready and WorldRenderer._Ready.
            if (WorldPath != null && !WorldPath.IsEmpty)
                _world = GetNode<TestSetup>(WorldPath);
            else
                _world = GetParent<TestSetup>() ?? GetTree().Root.FindChild("Main", true, false) as TestSetup;

            if (_world == null)
                GD.PrintErr("[LuminousPassRenderer] Could not resolve world (TestSetup) node. " +
                            "Set WorldPath export or ensure Main.tscn hierarchy is correct.");

            // --- Create Camera3D-child spatial quad (RESEARCH Pattern 3 / Code Examples) ---
            // QuadMesh 2×2 with FlipFaces=true so the face normal points toward the camera.
            // MaterialOverride on the MeshInstance3D so the spatial shader applies.
            var quad = new QuadMesh { Size = new Vector2(2f, 2f), FlipFaces = true };
            var mesh = new MeshInstance3D { Mesh = quad };

            _mat = new ShaderMaterial
            {
                Shader = GD.Load<Shader>("res://Shaders/luminous_pass.gdshader")
            };
            mesh.MaterialOverride = _mat;

            // AddChild: LuminousPassRenderer is already a Camera3D child (wired in Main.tscn);
            // the quad becomes its child — two levels below Camera3D, still in Camera3D space
            // so the bypass-vertex POSITION trick keeps it screen-filling.
            AddChild(mesh);
        }

        public override void _Process(double delta)
        {
            if (_mat == null || _builder == null) return;

            // Builder already ran this frame (process_priority=-10, before this node).
            // Read-only: MUST NOT call BuildDescriptors() here — that would double the
            // classify+project loop (Pitfall 5 from RESEARCH.md).
            // Galaxy branch removed (D-13 / Plan 3): galaxies handled by SkyboxRenderer.

            // Read ship.LocalPos.Scale fresh each frame — it changes on SOI transitions.
            // Required for the observer-unit basis conversion in star_view_dists (see below).
            // Bounds-check idiom: (uint) cast catches both < 0 and >= Count in one comparison
            // (WorldRenderer line 206, LuminousDescriptorBuilder line 102).
            var  gameObjs  = _world?.GameObjects;
            int  shipIdx   = _world?.ShipIndex ?? -1;
            var  ship      = (gameObjs != null && (uint)shipIdx < (uint)gameObjs.Count)
                                 ? gameObjs[shipIdx]
                                 : null;

            // If world is not ready yet, skip this frame (star_count=0 keeps the shader quiet).
            if (ship == null)
            {
                _mat?.SetShaderParameter("star_count", 0);
                return;
            }

            // observer-unit scale: metres → observer units (same divisor WorldRenderer uses in
            // RenderBodyAt: r = rawRadiusMeters / ship.LocalPos.Scale * factor).
            double shipScale = ship.LocalPos.Scale;

            int starCount = 0;

            for (int i = 0; i < _builder.DescriptorCount; i++)
            {
                // ref avoids a struct copy on each iteration (WR-01).
                ref var d = ref _builder.Descriptors[i];

                if (d.BodyType == UniObject.Type.Star && starCount < MaxStars)
                {
                    _starDirs[starCount]       = d.Direction;
                    _starColors[starCount]     = d.BaseColor;          // A channel = Brightness
                    _starSizes[starCount]      = d.AngularSize;
                    _starLodWeights[starCount] = d.LodWeight;          // drives PSF intensity

                    // Iteration 4 fix: observer-unit scale divisor added.
                    // Render-space distance (metres / ship.LocalPos.Scale * factor) matches
                    // WorldRenderer.RenderBodyAt: renderPos = relUnits * factor
                    // where relUnits = metres / ship.LocalPos.Scale (observer-unit basis).
                    // Both lin_depth (planet mesh depth) and star_view_dists MUST be in the
                    // SAME render-unit basis so the depth gate fires correctly at all scales.
                    //
                    // Sanity check (Planet space, scale=1e-4, factor=1e-8):
                    //   Star at 1 AU: 1.496e11 / 1e-4 * 1e-8 = 1.496e7 render units (correct)
                    //   Planet 5e6 m away: 5e6 / 1e-4 * 1e-8 = 5e2 render units << 1.496e7 → occludes
                    //
                    // Previous (broken) formula: 1.496e11 * 1e-8 = 1496 render units — 10,000x too
                    // small; appeared comparable to planet surface depth causing range-dependent occlusion.
                    //
                    // WorldRenderer.StarRenderFactor is the single source of truth (public const 1e-8).
                    // Note: all spaces currently share the same factor; if per-space factors ever
                    // diverge, use RenderFactorFor(ship.CurrentSpace) here.
                    _starViewDists[starCount]  = (float)(d.DistanceMeters / shipScale * WorldRenderer.StarRenderFactor);

                    starCount++;
                }
            }

            // Push star uniforms (count-first pattern — SkyboxRenderer lines 239–257).
            // Null-coalescing guard (PostProcessRenderer pattern).
            // Galaxy uniform push removed (D-13 / Plan 3): narrowed to stars only.
            _mat?.SetShaderParameter("star_count", starCount);
            if (starCount > 0)
            {
                _mat?.SetShaderParameter("star_dirs",        _starDirs);
                _mat?.SetShaderParameter("star_colors",      _starColors);
                _mat?.SetShaderParameter("star_sizes",       _starSizes);
                _mat?.SetShaderParameter("star_lod_weights", _starLodWeights);
                // Iteration 3: push analytic render-space distances for per-pixel depth gate.
                _mat?.SetShaderParameter("star_view_dists",  _starViewDists);
            }

            // Push PSF tuning knobs each frame so they update live in the editor.
            _mat?.SetShaderParameter("psf_core_scale",        PsfCoreScale);
            _mat?.SetShaderParameter("psf_spike_long_scale",  PsfSpikeLongScale);
            _mat?.SetShaderParameter("psf_spike_short_scale", PsfSpikeShortScale);
            _mat?.SetShaderParameter("psf_intensity",         PsfIntensity);
            _mat?.SetShaderParameter("psf_lod_floor",         PsfLodFloor);
            _mat?.SetShaderParameter("psf_lod_range",         PsfLodRange);
            _mat?.SetShaderParameter("psf_depth_epsilon",     PsfDepthEpsilon);
        }
    }
}
