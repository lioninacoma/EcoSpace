using Godot;
using System.Collections.Generic;

namespace Render
{
	/// <summary>
	/// Per-frame, read-only sky uniform push: reads <see cref="LuminousDescriptorBuilder.Descriptors"/>
	/// and pushes the packed star/galaxy arrays to the Sky ShaderMaterial each frame.
	///
	/// After the D-07 REVERSAL (05-02 replan), SkyboxRenderer is the primary distant-body drawer.
	/// It no longer runs its own classify+project loop — it reads <see cref="LuminousDescriptorBuilder.Descriptors"/>
	/// directly (D-02 refeed). This eliminates the dual classify pass (RESEARCH Pitfall 5) and
	/// makes sky-point positions identical to what WorldRenderer and LuminousPassRenderer would compute.
	///
	/// Display brightness floor (P1/D-09): a <see cref="MinVisibleBrightness"/> floor is applied to
	/// the sky-point alpha when packing <c>star_colors</c> so distant stars are always findable.
	/// This is sky-display-only — <see cref="StarRendering.ApparentBrightness"/> is NEVER changed.
	///
	/// Also maintains a per-body sky-direction cache (_skyDirs) as the sky-side of the
	/// RND-07/D-21 handoff baseline; see <see cref="GetSkyDirection"/>.
	///
	/// Read-only consumer of GameWorld state — MUST NOT mutate UniVec3 or call TranslatePos.
	/// Must NOT call BuildDescriptors() — the builder ran earlier this frame at process_priority=-10.
	/// </summary>
	public partial class SkyboxRenderer : Node
	{
		// ----- Exports --------------------------------------------------------

		/// <summary>NodePath to the LuminousDescriptorBuilder node (read-only descriptor source).</summary>
		[Export] public NodePath BuilderPath { get; set; }

		// ----- Private state --------------------------------------------------

		private ShaderMaterial           _skyMat;
		private LuminousDescriptorBuilder _builder;

		private const int MaxStars    = 128;
		private const int MaxGalaxies = 32;

		/// <summary>Safety cap on a sky point's angular disc radius (radians, ~28°).</summary>
		private const float MaxDiscAngle = 0.5f;

		/// <summary>
		/// Display-only brightness floor for distant star sky points (P1/D-09).
		/// Ensures distant stars in Galaxy/Universe space are always visible as findable
		/// light points even when their physical <see cref="StarRendering.ApparentBrightness"/>
		/// is near zero at light-year distances.
		/// [ASSUMED play-test calibration knob — D-04]
		/// Does NOT change <see cref="StarRendering.ApparentBrightness"/>; the physics stay exact.
		/// </summary>
		private const float MinVisibleBrightness = 0.05f;

		private readonly Vector3[] _dirs   = new Vector3[MaxStars];
		private readonly Color[]   _colors = new Color[MaxStars];
		private readonly float[]   _sizes  = new float[MaxStars];

		// ── Galaxy sky uniform arrays (D-40) ──────────────────────────────────────────
		// Partitioned from descriptors by BodyType==Galaxy.
		// galaxy_types[] uses int[] — int[] path confirmed in GLSL uniform (03-01-SUMMARY.md).
		private readonly Vector3[] _galDirs         = new Vector3[MaxGalaxies];
		private readonly Color[]   _galColors       = new Color[MaxGalaxies];
		private readonly float[]   _galSizes        = new float[MaxGalaxies];
		private readonly int[]     _galTypes        = new int[MaxGalaxies];
		private readonly Vector4[] _galOrientations = new Vector4[MaxGalaxies];

		/// <summary>
		/// GalaxyDiscWeight per galaxy — fed into the galaxy_disc_weights uniform in skybox.gdshader
		/// so the galaxy disc fades in/out continuously (D-13 groundwork for crossfade at SOI boundary).
		/// Populated from <see cref="LuminousBodyDescriptor.LodWeight"/> for each Galaxy descriptor.
		/// </summary>
		private readonly float[] _galDiscWeights = new float[MaxGalaxies];

		/// <summary>
		/// RND-07/D-21 handoff baseline: last-computed world-fixed sky direction per body
		/// (keyed by UniObject.Index). Updated each frame in SyncSkyPoints for every Star descriptor.
		/// Cleared at the start of each frame so stale entries are not retained beyond one frame.
		///
		/// Phase 3 builds ONLY this data structure — it performs NO visible swap or crossfade
		/// (D-21, D-24). A Phase 3 tier-crossing handler will call GetSkyDirection to find the
		/// world-fixed direction for a body just before spawning its mesh.
		/// </summary>
		private readonly Dictionary<int, Vector3> _skyDirs = [];

		// ----- Godot callbacks ------------------------------------------------

		public override void _Ready()
		{
			// Resolve descriptor builder reference — mirrors LuminousPassRenderer._Ready pattern.
			if (BuilderPath != null && !BuilderPath.IsEmpty)
				_builder = GetNode<LuminousDescriptorBuilder>(BuilderPath);
			else
				_builder = GetTree().Root.FindChild("LuminousDescriptorBuilder", true, false)
						   as LuminousDescriptorBuilder;

			if (_builder == null)
				GD.PrintErr("[SkyboxRenderer] Could not resolve LuminousDescriptorBuilder. " +
							"Set BuilderPath export or ensure Main.tscn hierarchy is correct.");

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
			if (_builder == null || _skyMat == null) return;
			SyncSkyPoints();
		}

		// ----- Core sky sync --------------------------------------------------

		/// <summary>
		/// Reads <see cref="LuminousDescriptorBuilder.Descriptors"/> and pushes the packed
		/// star/galaxy arrays to the sky ShaderMaterial. Also updates the _skyDirs cache
		/// (RND-07/D-21 handoff baseline) for each Star descriptor.
		///
		/// STRICTLY READ-ONLY: contains NO UniMath.RelativePosition, NO TierClassifier.Classify,
		/// and NO GameObjects iteration (RESEARCH Pitfall 5). The classify+project work was done
		/// by LuminousDescriptorBuilder at process_priority=-10 earlier this frame.
		///
		/// Brightness floor (P1): star sky-point alphas are floored at MinVisibleBrightness
		/// so distant stars are always findable. This floor is sky-display-only; it does NOT
		/// alter LuminousBodyDescriptor.Brightness (which remains physically correct).
		/// </summary>
		private void SyncSkyPoints()
		{
			// Clear the RND-07 cache each frame so stale entries don't persist (T-02-05 mitigation).
			_skyDirs.Clear();

			int count    = 0;
			int galCount = 0;

			for (int i = 0; i < _builder.DescriptorCount; i++)
			{
				// Early exit once both arrays are full (T-03-01: bounded by MaxStars + MaxGalaxies)
				if (count >= MaxStars && galCount >= MaxGalaxies) break;

				// ref avoids a struct copy on each iteration (WR-01 no-per-frame-alloc pattern).
				ref var d = ref _builder.Descriptors[i];

				if (d.BodyType == UniObject.Type.Star && count < MaxStars)
				{
					_dirs[count]  = d.Direction;
					_sizes[count] = d.AngularSize;

					// Display brightness floor (P1/D-09): floor the alpha pushed to the sky shader
					// so distant stars are always findable as light points. Physics untouched.
					// [ASSUMED play-test calibration knob — D-04]
					float displayAlpha = Mathf.Max(d.Brightness, MinVisibleBrightness);
					_colors[count] = new Color(d.BaseColor.R, d.BaseColor.G, d.BaseColor.B, displayAlpha);

					// Cache world-fixed sky direction per body for RND-07/D-21 handoff baseline.
					// (RESEARCH Pitfall 6: must be rebuilt in the refeed loop)
					_skyDirs[d.BodyIndex] = d.Direction;

					count++;
				}
				else if (d.BodyType == UniObject.Type.Galaxy && galCount < MaxGalaxies)
				{
					// Home-galaxy suppression is already handled by LuminousDescriptorBuilder.
					// Do NOT add a second ancestor check here — it is dead code and adds confusion.
					// (RESEARCH anti-pattern / PLAN prohibitions)
					_galDirs[galCount]         = d.Direction;
					_galSizes[galCount]        = d.AngularSize;
					_galColors[galCount]       = d.BaseColor;
					_galTypes[galCount]        = d.GalaxyType;
					_galOrientations[galCount] = d.GalaxyOrientation;
					// D-13 groundwork: GalaxyDiscWeight crossfade value for galaxy_disc_weights uniform.
					_galDiscWeights[galCount]  = d.LodWeight;
					galCount++;
				}
				// else: non-Star/Galaxy descriptor — skip (future-proof guard)
			}

			// Push star_count first, then arrays (only if count > 0 to avoid sending empty arrays).
			_skyMat.SetShaderParameter("star_count", count);
			if (count > 0)
			{
				_skyMat.SetShaderParameter("star_dirs",   _dirs);
				_skyMat.SetShaderParameter("star_colors", _colors);
				_skyMat.SetShaderParameter("star_sizes",  _sizes);
			}

			// Push galaxy uniforms.
			_skyMat.SetShaderParameter("galaxy_count", galCount);
			if (galCount > 0)
			{
				_skyMat.SetShaderParameter("galaxy_dirs",         _galDirs);
				_skyMat.SetShaderParameter("galaxy_colors",       _galColors);
				_skyMat.SetShaderParameter("galaxy_sizes",        _galSizes);
				_skyMat.SetShaderParameter("galaxy_types",        _galTypes);
				_skyMat.SetShaderParameter("galaxy_orientations", _galOrientations);
				// D-13 groundwork: push galaxy_disc_weights for crossfade in skybox.gdshader.
				_skyMat.SetShaderParameter("galaxy_disc_weights", _galDiscWeights);
			}
		}

		// ----- RND-07/D-21 handoff baseline accessors ----------------------------

		/// <summary>
		/// Returns the last-computed world-fixed sky direction for the body with the given index.
		/// Returns <c>true</c> and populates <paramref name="dir"/> when the body was visible in
		/// the sky set during the most-recent frame; returns <c>false</c> (and dir=default) if the
		/// body is not currently a Star descriptor or the cache is empty.
		///
		/// This is the sky-side of the RND-07/D-21 handoff baseline. Phase 3 reads it at a
		/// tier-crossing to obtain the sky point's world direction, enabling a pop-free instant swap.
		/// </summary>
		/// <param name="bodyIdx">The UniObject.Index of the body to look up.</param>
		/// <param name="dir">Receives the world-fixed direction vector (unit length) if found.</param>
		/// <returns>True if the body is currently tracked in the sky direction cache.</returns>
		public bool GetSkyDirection(int bodyIdx, out Vector3 dir)
		{
			if (_skyDirs.TryGetValue(bodyIdx, out dir)) return true;
			dir = default;
			return false;
		}
	}
}
