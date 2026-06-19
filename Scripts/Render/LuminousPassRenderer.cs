using Godot;

namespace Render
{
    /// <summary>
    /// Node3D child of Camera3D that hosts a 2×2 spatial quad mesh running
    /// <c>luminous_pass.gdshader</c> — the depth-aware post-process pass for near-star
    /// glow/halo drawing (Plan 3 of Phase 5 Rendering Overhaul — narrowed to stars only).
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
    /// </summary>
    public partial class LuminousPassRenderer : Node3D
    {
        // ----- Exports --------------------------------------------------------

        /// <summary>NodePath to the LuminousDescriptorBuilder node (read-only descriptor source).</summary>
        [Export] public NodePath BuilderPath { get; set; }

        // ----- Constants (T-05-01 mitigation: fixed-size caps matching the shader) ------

        private const int MaxStars = 8;

        // ----- Pre-allocated uniform arrays (WR-01: never allocate per frame) ──────────
        // Mirrors SkyboxRenderer pre-allocated pattern.
        // Galaxy arrays removed (D-13 / Plan 3): galaxies render in skybox.gdshader.

        private readonly Vector3[] _starDirs       = new Vector3[MaxStars];
        private readonly Color[]   _starColors     = new Color[MaxStars];
        private readonly float[]   _starSizes      = new float[MaxStars];
        private readonly float[]   _starLodWeights = new float[MaxStars];

        // ----- Private state --------------------------------------------------

        private ShaderMaterial           _mat;
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
                    _starDirs[starCount]       = d.Direction;
                    _starColors[starCount]     = d.BaseColor;       // A channel = Brightness
                    _starSizes[starCount]      = d.AngularSize;
                    _starLodWeights[starCount] = d.LodWeight;       // drives is_near + lod_fade
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
            }
        }
    }
}
