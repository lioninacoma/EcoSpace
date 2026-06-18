using Godot;
using System.Collections.Generic;

namespace Render
{
	/// <summary>
	/// Per-frame, read-only sky uniform push: classifies bodies via TierClassifier, computes
	/// world-space directions from ship to each NextTierSkybox body using the UniMath
	/// LCA-relative UniVec3 walk (UniMath.RelativePosition), applies the inverse-square
	/// luminosity magnitude model (D-17/D-19), carries each body's BaseColor (D-18), and
	/// pushes star_dirs/star_colors/star_sizes/star_count to the Sky ShaderMaterial each frame.
	/// Brightness values &gt;1 feed the existing WorldEnvironment glow for free (D-20).
	/// Also maintains a per-body sky-direction cache (_skyDirs) as the sky-side of the
	/// RND-07/D-21 handoff baseline; see GetSkyDirection.
	///
	/// Read-only consumer of GameWorld state — MUST NOT mutate UniVec3 or call TranslatePos.
	///
	/// Precision model (LCA-relative UniVec3 walk — now in UniMath):
	///   The ship→body vector is computed entirely in UniVec3 via UniMath.RelativePosition:
	///   both sides are walked to their LCA in UniVec3 form (per-level Convert+add), then
	///   subtracted at the SAME scale for exact integer Units cancellation. ToDouble3() is
	///   called ONCE on the small differenced delta to get metres. The LCA's enormous absolute
	///   offset from root never enters the arithmetic, eliminating catastrophic cancellation
	///   at 1:1 Universe scale.
	///
	///   WARNING — do NOT replace UniMath.RelativePosition with a naive UniVec3 operator-
	///   across spaces: UniVec3.Convert(), operator-, operator+ all route through
	///   EnsureSameScale → Convert → ToDouble3(), which ZEROES the Long3 Units (folds
	///   everything into a single double). A naive bodyUni - shipUni across different scales
	///   collapses to double and discards the precision split the LCA path preserves.
	/// </summary>
	public partial class SkyboxRenderer : Node
	{
		// ----- Exports --------------------------------------------------------

		/// <summary>NodePath to the TestSetup node (GameWorld / world state).</summary>
		[Export] public NodePath WorldPath { get; set; }

		// No per-renderer tuning knobs: a star's size and emitted light are derived ENTIRELY
		// from its own Luminosity / RadiusMeters / BaseColor via the shared StarRendering rules,
		// so the mesh and skybox stay coherent. See StarRendering for the single config point.

		// ----- Private state --------------------------------------------------

		private TestSetup      _world;
		private ShaderMaterial _skyMat;
		private Camera3D       _cam;

		private const int MaxStars = 8;

		/// <summary>Maximum galaxy entries in sky uniform arrays. Matches MAX_GALAXIES in skybox.gdshader.</summary>
		private const int MaxGalaxies = 4;

		/// <summary>Safety cap on a sky point's angular disc radius (radians, ~28°). Next-tier-out
		/// bodies are always far so their true angular size is sub-pixel; this only guards against
		/// a degenerate near body producing a smoothstep edge of 1−cos(θ) → 1 (which breaks the disc).</summary>
		private const float MaxDiscAngle = 0.5f;
		private readonly Vector3[] _dirs   = new Vector3[MaxStars];
		private readonly Color[]   _colors = new Color[MaxStars];
		private readonly float[]   _sizes  = new float[MaxStars];

		// ── Galaxy sky uniform arrays (D-40) ──────────────────────────────────────────
		// Partitioned from NextTierSkybox bodies by ObjectType==Galaxy.
		// galaxy_types[] uses int[] — Task 1 de-risk confirmed int[] path in GLSL uniform.
		// See 03-01-SUMMARY.md for the int[]-vs-float-packing decision contract.
		private readonly Vector3[] _galDirs         = new Vector3[MaxGalaxies];
		private readonly Color[]   _galColors       = new Color[MaxGalaxies];
		private readonly float[]   _galSizes        = new float[MaxGalaxies];
		private readonly int[]     _galTypes        = new int[MaxGalaxies];
		private readonly Vector4[] _galOrientations = new Vector4[MaxGalaxies];

		/// <summary>
		/// RND-07/D-21 handoff baseline: last-computed world-fixed sky direction per body
		/// (keyed by UniObject.Index). Updated each frame in SyncSkyPoints for every body
		/// classified as NextTierSkybox. Cleared at the start of each frame so stale entries
		/// from bodies that left the sky set are not retained beyond one frame.
		///
		/// Phase 2 builds ONLY this data structure — it performs NO visible swap or crossfade
		/// (D-21, D-24). A Phase 3 tier-crossing handler will call GetSkyDirection to find the
		/// world-fixed direction for a body just before spawning its mesh, and compare it with
		/// WorldRenderer.GetRenderPosition to align the point and mesh on one screen pixel for a
		/// pop-free instant swap. Color match is automatic: both renderers read body.BaseColor.
		/// Brightness match is automatic: both derive from body.Luminosity.
		/// </summary>
		private readonly Dictionary<int, Vector3> _skyDirs = [];

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
			_cam = GetTree().Root.FindChild("Camera3D", true, false) as Camera3D;
			var env = _cam?.Environment;
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
		/// Also updates _skyDirs (RND-07/D-21 handoff cache) for each sky-visible body.
		/// Strictly read-only — never writes to GameObjects, LocalPos, or ChildIndices.
		///
		/// Direction math delegates to UniMath.RelativePosition (LCA-relative UniVec3 walk).
		/// The ship→body UniVec3 is differenced at the LCA child-scale (exact integer Units
		/// cancellation), then ToDouble3() is called ONCE on the small resulting delta.
		/// The large common-ancestor offset is never formed; subtraction operates on the
		/// small inter-body delta in the LCA frame.
		/// </summary>
		private void SyncSkyPoints()
		{
			var objs    = _world.GameObjects;
			int shipIdx = _world.ShipIndex;

			// Bounds-check idiom from WorldRenderer.
			var ship = (uint)shipIdx < (uint)objs.Count ? objs[shipIdx] : null;
			if (ship == null) return;

			// Clear stale entries — rebuilt fresh each frame (T-02-05 mitigation).
			_skyDirs.Clear();

			// Angular size of one screen pixel (radians). A star can never render smaller than
			// the screen can resolve, so this is the floor on a sky point's disc — the only
			// "minimum" and it is a physical resolution limit, not artificial size enhancement.
			float pixelAngle = PixelAngularSize();

			int count    = 0;
			int galCount = 0;
			for (int i = 0; i < objs.Count; i++)
			{
				// Early exit once both arrays are full (T-03-01: loop bounded by MaxStars + MaxGalaxies)
				if (count >= MaxStars && galCount >= MaxGalaxies) break;

				var body = objs[i];
				if (body == null) continue;
				if (body.Index == shipIdx) continue;

				if (TierClassifier.Classify(body, ship) != SkyTier.NextTierSkybox) continue;

				// Direction and distance: LCA-relative UniVec3 walk via UniMath.
				// RelativePosition(ship, body) returns body − ship as a UniVec3 in the LCA
				// child-frame (exact integer Units cancellation at any scale). ToDouble3() is
				// called once on the small differenced delta to get metres. Casting only the
				// normalized unit vector to Vector3 avoids precision loss at interstellar
				// distances (Pitfall 5 in RESEARCH.md).
				// MANDATORY for galaxies: at intergalactic distances (~2.4e22 m) naive cross-scale
				// UniVec3 subtraction catastrophically loses precision — only LCA path is safe (CLAUDE.md).
				bool hasCommonAncestor = UniMath.RelativePosition(ship, body, objs, out UniVec3 relUni);
				Double3 delta = hasCommonAncestor ? relUni.ToDouble3() : Double3.Zero;

				double  len  = hasCommonAncestor ? delta.Magnitude() : 0.0;

				Vector3 dir3;
				if (!hasCommonAncestor || len < 1e-30)
					dir3 = Vector3.Up;
				else
				{
					Double3 dir  = delta * (1.0 / len);
					dir3         = new Vector3((float)dir.X, (float)dir.Y, (float)dir.Z);
				}

				// SIZE — purely physical: the disc subtends the body's true angular radius
				// θ = RadiusMeters / distance (the SAME rule the mesh sphere obeys, so the
				// handoff cannot pop), floored at the pixel footprint because nothing can render
				// smaller than one pixel. The smoothstep disc param is (1 − cos θ_eff).
				double theta = StarRendering.AngularRadius(body.RadiusMeters, len);
				// Floor at one pixel (resolution limit) and cap at MaxDiscAngle so a degenerate
				// near body cannot drive 1−cos toward 1 (which collapses the shader smoothstep).
				float  eff   = Mathf.Clamp((float)theta, pixelAngle, MaxDiscAngle);
				float  size  = 1f - Mathf.Cos(eff);

				// BRIGHTNESS — the SAME shared rule the mesh uses: inverse-square flux through a
				// magnitude (log) curve, shifted by the one global StarRendering.Exposure. Result
				// is in [0,1] so the body's BaseColor hue is preserved instead of washing out.
				// Galaxies reuse this identical path so point↔disc brightness auto-matches (D-30).
				float alpha = StarRendering.ApparentBrightness(body.Luminosity, len);

				// ── Partition by ObjectType (D-40) ────────────────────────────────────
				if (body.ObjectType == UniObject.Type.Galaxy && galCount < MaxGalaxies)
				{
					// ── Home-galaxy suppression guard (must-have truth #2, 03-01-SUMMARY.md line 135) ──
					// While the ship is inside this galaxy's SOI (i.e. the galaxy is an ancestor of
					// the ship), the galaxy must NOT render as a disc — only the 2 OTHER (non-ancestor)
					// galaxies appear from inside the home system.
					// FindLca(ship, body) == body.Index is exactly "body is an ancestor of (or equal to)
					// the ship" — the ship-self case is excluded by the body.Index==shipIdx continue above.
					// This call is strictly read-only: FindLca walks ParentIndex chains without mutation.
					if (UniMath.FindLca(ship, body, objs) == body.Index)
						continue;

					// Galaxy: route to procedural-disc uniform arrays
					_galDirs[galCount]         = dir3;
					_galSizes[galCount]        = size;
					_galColors[galCount]       = new Color(body.BaseColor.R, body.BaseColor.G, body.BaseColor.B, alpha);
					_galTypes[galCount]        = body.GalaxyType;
					_galOrientations[galCount] = new Vector4(
						body.GalaxyOrientation.X, body.GalaxyOrientation.Y, body.GalaxyOrientation.Z,
						body.GalaxySeed);
					galCount++;
				}
				else if (body.ObjectType == UniObject.Type.Star && count < MaxStars)
				{
					// Star: route to existing star-point uniform arrays
					_dirs[count]   = dir3;
					_sizes[count]  = size;
					_colors[count] = new Color(body.BaseColor.R, body.BaseColor.G, body.BaseColor.B, alpha);

					// Cache world-fixed sky direction per body for RND-07/D-21 handoff baseline.
					// Phase 3 reads this to align a newly spawned mesh with the sky point's
					// screen position for a pop-free instant swap. Star branch only — galaxy
					// bodies are sky-only and have no mesh counterpart (D-28).
					_skyDirs[body.Index] = dir3;

					count++;
				}
				// else: non-Star/Galaxy NextTierSkybox body — skip (future-proof guard)
			}

			// Push star_count first, then arrays (only if count > 0 to avoid sending empty arrays).
			_skyMat.SetShaderParameter("star_count", count);
			if (count > 0)
			{
				_skyMat.SetShaderParameter("star_dirs",   _dirs);
				_skyMat.SetShaderParameter("star_colors", _colors);
				_skyMat.SetShaderParameter("star_sizes",  _sizes);
			}

			// Push galaxy uniforms (T-03-01: galaxy_count clamped to MaxGalaxies by galCount guard above)
			_skyMat.SetShaderParameter("galaxy_count", galCount);
			if (galCount > 0)
			{
				_skyMat.SetShaderParameter("galaxy_dirs",         _galDirs);
				_skyMat.SetShaderParameter("galaxy_colors",       _galColors);
				_skyMat.SetShaderParameter("galaxy_sizes",        _galSizes);
				_skyMat.SetShaderParameter("galaxy_types",        _galTypes);
				_skyMat.SetShaderParameter("galaxy_orientations", _galOrientations);
			}
		}

		// ----- RND-07/D-21 handoff baseline accessors ----------------------------

		/// <summary>
		/// Returns the last-computed world-fixed sky direction for the body with the given index.
		/// Returns <c>true</c> and populates <paramref name="dir"/> when the body was visible in
		/// the sky set during the most-recent frame; returns <c>false</c> (and dir=default) if the
		/// body is not currently a NextTierSkybox body or the cache is empty.
		///
		/// This is one of the two RND-07/D-21 baseline data sources. Phase 3 reads it at a
		/// tier-crossing to obtain the sky point's world direction, then compares it with
		/// <see cref="WorldRenderer.GetRenderPosition"/> (the mesh-side position) to align the
		/// promoted mesh on the same screen pixel — enabling an instant exact-match swap with
		/// no crossfade (D-21). Phase 2 ONLY builds and exposes this cache; no visible swap
		/// is performed here (D-24).
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

		/// <summary>
		/// Angular size of a single screen pixel in radians, from the camera's vertical FOV and
		/// the viewport height. Used as the minimum disc size for a sky point (a star cannot
		/// render smaller than the display can resolve). Falls back to a sane default if the
		/// camera or viewport is unavailable.
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
