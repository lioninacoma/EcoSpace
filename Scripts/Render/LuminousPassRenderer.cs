using Godot;

namespace Render
{
    /// <summary>
    /// Node3D child of Camera3D that hosts a 2×2 spatial quad mesh running
    /// <c>luminous_pass.gdshader</c> — the depth-aware post-process pass for star
    /// glow/halo and galaxy disc drawing (Plan 2 of Phase 5 Rendering Overhaul).
    ///
    /// Consumes <see cref="LuminousDescriptorBuilder.Descriptors"/> read-only each frame and
    /// pushes the packed star/galaxy arrays to <c>luminous_pass.gdshader</c>.
    /// The builder ran earlier this frame (process_priority=-10), so <see cref="Descriptors"/>
    /// is fully populated before this node's <c>_Process</c> executes (Pitfall 3 guard).
    ///
    /// Read-only consumer of GameWorld state — MUST NOT mutate UniVec3 or call TranslatePos.
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

        private const int MaxStars    = 8;
        private const int MaxGalaxies = 4;

        // ----- Pre-allocated uniform arrays (WR-01: never allocate per frame) ──────────
        // Mirrors SkyboxRenderer pre-allocated pattern (lines 58–70).

        private readonly Vector3[] _starDirs        = new Vector3[MaxStars];
        private readonly Color[]   _starColors      = new Color[MaxStars];
        private readonly float[]   _starSizes       = new float[MaxStars];
        private readonly float[]   _starLodWeights  = new float[MaxStars];

        private readonly Vector3[] _galDirs         = new Vector3[MaxGalaxies];
        private readonly Color[]   _galColors       = new Color[MaxGalaxies];
        private readonly float[]   _galSizes        = new float[MaxGalaxies];
        private readonly float[]   _galDiscWeights  = new float[MaxGalaxies];
        private readonly int[]     _galTypes        = new int[MaxGalaxies];
        private readonly Vector4[] _galOrientations = new Vector4[MaxGalaxies];

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
            // classify+project loop (Pitfall 3 from RESEARCH.md).
            int starCount = 0;
            int galCount  = 0;

            for (int i = 0; i < _builder.DescriptorCount; i++)
            {
                // ref avoids a struct copy on each iteration (WR-01).
                ref var d = ref _builder.Descriptors[i];

                if (d.BodyType == UniObject.Type.Star && starCount < MaxStars)
                {
                    _starDirs[starCount]       = d.Direction;
                    _starColors[starCount]     = d.BaseColor;       // A channel = Brightness
                    _starSizes[starCount]      = d.AngularSize;
                    _starLodWeights[starCount] = d.LodWeight;
                    starCount++;
                }
                else if (d.BodyType == UniObject.Type.Galaxy && galCount < MaxGalaxies)
                {
                    _galDirs[galCount]         = d.Direction;
                    _galColors[galCount]       = d.BaseColor;
                    _galSizes[galCount]        = d.AngularSize;
                    _galDiscWeights[galCount]  = d.LodWeight;       // GalaxyDiscWeight
                    _galTypes[galCount]        = d.GalaxyType;
                    _galOrientations[galCount] = d.GalaxyOrientation;
                    galCount++;
                }
            }

            // Push star uniforms (count-first pattern — SkyboxRenderer lines 239–257).
            // Null-coalescing guard (PostProcessRenderer pattern).
            _mat?.SetShaderParameter("star_count", starCount);
            if (starCount > 0)
            {
                _mat?.SetShaderParameter("star_dirs",        _starDirs);
                _mat?.SetShaderParameter("star_colors",      _starColors);
                _mat?.SetShaderParameter("star_sizes",       _starSizes);
                _mat?.SetShaderParameter("star_lod_weights", _starLodWeights);
            }

            // Push galaxy uniforms.
            // Galaxy disc logic is a STUB in Plan 2 (galaxy_disc_weights fed; Plan 3 finishes).
            _mat?.SetShaderParameter("galaxy_count", galCount);
            if (galCount > 0)
            {
                _mat?.SetShaderParameter("galaxy_dirs",         _galDirs);
                _mat?.SetShaderParameter("galaxy_colors",       _galColors);
                _mat?.SetShaderParameter("galaxy_sizes",        _galSizes);
                _mat?.SetShaderParameter("galaxy_disc_weights", _galDiscWeights);
                _mat?.SetShaderParameter("galaxy_types",        _galTypes);
                _mat?.SetShaderParameter("galaxy_orientations", _galOrientations);
            }
        }
    }
}
