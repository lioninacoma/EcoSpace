using Godot;

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
    ///   <see cref="PsfLodFloor"/>        — minimum LOD weight to receive any PSF.
    ///   <see cref="PsfDepthEpsilon"/>    — depth tolerance at the star mesh surface (0–1).
    ///   <see cref="RenderFactor"/>       — metres-to-render-units scale (matches WorldRenderer).
    /// </summary>
    public partial class LuminousPassRenderer : Node3D
    {
        // ----- Exports --------------------------------------------------------

        /// <summary>NodePath to the LuminousDescriptorBuilder node (read-only descriptor source).</summary>
        [Export] public NodePath BuilderPath { get; set; }

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
        /// Minimum LOD weight (0–1) below which PSF contribution fades to zero.
        /// Prevents far-field sky-shader points from accumulating PSF.
        /// [ASSUMED D-04 play-test calibration knob]
        /// </summary>
        [Export] public float PsfLodFloor        { get; set; } = 0.02f;

        /// <summary>
        /// Depth tolerance multiplier (0–1) for the per-pixel depth gate at the star surface.
        /// PSF is allowed where scene lin_depth >= star_lin_depth * PsfDepthEpsilon.
        /// Values close to 1.0 are strict (only at or beyond the star); smaller values
        /// add tolerance for z-fighting at the star mesh surface.
        /// [ASSUMED D-04 play-test calibration knob]
        /// </summary>
        [Export] public float PsfDepthEpsilon    { get; set; } = 0.8f;

        /// <summary>
        /// Metres-to-render-units scale factor, matching WorldRenderer's per-space render factor.
        /// Used to convert <see cref="LuminousBodyDescriptor.DistanceMeters"/> into view-space
        /// linear depth pushed as <c>star_lin_depths[]</c> to the shader depth gate.
        /// Must match the PlanetRenderFactor/StarRenderFactor set on WorldRenderer (default 1e-8).
        /// [ASSUMED D-04 play-test calibration knob — matches WorldRenderer.StarRenderFactor]
        /// </summary>
        [Export] public float RenderFactor       { get; set; } = 1e-8f;

        // ----- Constants (T-05-01 mitigation: fixed-size caps matching the shader) ------

        private const int MaxStars = 128;

        // ----- Pre-allocated uniform arrays (WR-01: never allocate per frame) ──────────
        // Mirrors SkyboxRenderer pre-allocated pattern.
        // Galaxy arrays removed (D-13 / Plan 3): galaxies render in skybox.gdshader.

        private readonly Vector3[] _starDirs       = new Vector3[MaxStars];
        private readonly Color[]   _starColors     = new Color[MaxStars];
        private readonly float[]   _starSizes      = new float[MaxStars];
        private readonly float[]   _starLodWeights = new float[MaxStars];
        private readonly float[]   _starLinDepths  = new float[MaxStars];

        // ----- Private state --------------------------------------------------

        private ShaderMaterial            _mat;
        private LuminousDescriptorBuilder _builder;

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
            int starCount = 0;

            for (int i = 0; i < _builder.DescriptorCount; i++)
            {
                // ref avoids a struct copy on each iteration (WR-01).
                ref var d = ref _builder.Descriptors[i];

                if (d.BodyType == UniObject.Type.Star && starCount < MaxStars)
                {
                    _starDirs[starCount]      = d.Direction;
                    _starColors[starCount]    = d.BaseColor;          // A channel = Brightness
                    _starSizes[starCount]     = d.AngularSize;
                    _starLodWeights[starCount] = d.LodWeight;         // drives PSF intensity
                    // Convert metric distance to view-space render units for the depth gate.
                    // Matches WorldRenderer per-space render factor (default 1e-8 for Star space).
                    _starLinDepths[starCount] = (float)(d.DistanceMeters * RenderFactor);
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
                _mat?.SetShaderParameter("star_lin_depths",  _starLinDepths);
            }

            // Push PSF tuning knobs each frame so they update live in the editor.
            _mat?.SetShaderParameter("psf_core_scale",        PsfCoreScale);
            _mat?.SetShaderParameter("psf_spike_long_scale",  PsfSpikeLongScale);
            _mat?.SetShaderParameter("psf_spike_short_scale", PsfSpikeShortScale);
            _mat?.SetShaderParameter("psf_intensity",         PsfIntensity);
            _mat?.SetShaderParameter("psf_lod_floor",         PsfLodFloor);
            _mat?.SetShaderParameter("psf_depth_epsilon",     PsfDepthEpsilon);
        }
    }
}
