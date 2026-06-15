using Godot;
using System.Collections.Generic;

namespace Render
{
	/// <summary>
	/// Per-frame, read-only sky uniform push: classifies bodies via TierClassifier, computes
	/// world-space directions from ship to each NextTierSkybox body using a double-precision
	/// hierarchy walk, applies the inverse-square luminosity magnitude model (D-17/D-19), carries
	/// each body's BaseColor (D-18), and pushes star_dirs/star_colors/star_sizes/star_count to
	/// the Sky ShaderMaterial each frame. Brightness values &gt;1 feed the existing WorldEnvironment
	/// glow for free (D-20). Also maintains a per-body sky-direction cache (_skyDirs) as the
	/// sky-side of the RND-07/D-21 handoff baseline; see GetSkyDirection.
	///
	/// Read-only consumer of GameWorld state — MUST NOT mutate UniVec3 or call TranslatePos.
	/// </summary>
	public partial class SkyboxRenderer : Node
	{
		// ----- Exports --------------------------------------------------------

		/// <summary>NodePath to the TestSetup node (GameWorld / world state).</summary>
		[Export] public NodePath WorldPath { get; set; }

		/// <summary>Scale factor mapping L/D² (solar-lum / m²) to sky shader alpha.
		/// Calibrated so Alpha-Cen-like stars (~4 ly, L=1.5) land around alpha=200.
		/// Exported for in-editor tuning without recompile.</summary>
		[Export] public double LuminosityScale { get; set; } = 2e35;

		/// <summary>Minimum alpha for any sky point regardless of distance/luminosity.
		/// Ensures even faint stars (Barnard's, dim M-dwarfs) remain a visible pixel.</summary>
		[Export] public float MinBrightFloor { get; set; } = 0.1f;

		/// <summary>Minimum smoothstep disc half-width in SkyboxRenderer coords.
		/// ~3 screen pixels at 75° FOV / 1920 wide — prevents sub-pixel invisibility
		/// for any star regardless of true angular size.</summary>
		[Export] public float MinStarSize { get; set; } = 3e-6f;

		/// <summary>Maximum smoothstep disc half-width. Clamps even very bright/close
		/// points so they do not fill the sky unrealistically.</summary>
		[Export] public float MaxStarSize { get; set; } = 0.005f;

		/// <summary>Disc radius per unit of apparent brightness (alpha). Larger values
		/// make brighter stars appear as wider discs, producing magnitude-ranked sizing
		/// consistent with Elite/Frontier. Default 2e-6 keeps even Sirius (alpha≈200)
		/// within a visually pleasing ~5 px disc at 1920 wide.</summary>
		[Export] public float SizePerBright { get; set; } = 2e-6f;

		// ----- Private state --------------------------------------------------

		private TestSetup      _world;
		private ShaderMaterial _skyMat;

		private const int MaxStars = 8;
		private readonly Vector3[] _dirs   = new Vector3[MaxStars];
		private readonly Color[]   _colors = new Color[MaxStars];
		private readonly float[]   _sizes  = new float[MaxStars];

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
		/// Also updates _skyDirs (RND-07/D-21 handoff cache) for each sky-visible body.
		/// Strictly read-only — never writes to GameObjects, LocalPos, or ChildIndices.
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

			// Cache ship's root-frame position once — reused for every body's direction.
			Double3 shipRoot = AbsolutePositionInRoot(ship, objs);

			int count = 0;
			for (int i = 0; i < objs.Count && count < MaxStars; i++)
			{
				var body = objs[i];
				if (body == null) continue;
				if (body.Index == shipIdx) continue;

				if (TierClassifier.Classify(body, ship) != SkyTier.NextTierSkybox) continue;

				// Direction and distance: walk hierarchy in double precision using the full
				// UniVec3 position (Units * Scale + Offset). Casting only the normalized
				// unit vector to Vector3 avoids precision loss at interstellar distances
				// (Pitfall 5 in RESEARCH.md).
				Double3 bodyRoot = AbsolutePositionInRoot(body, objs);
				Double3 delta    = bodyRoot - shipRoot;
				double  len      = delta.Magnitude();

				Vector3 dir3;
				if (len < 1e-30)
				{
					dir3         = Vector3.Up;
					_sizes[count] = MinStarSize;
				}
				else
				{
					Double3 dir  = delta * (1.0 / len);
					dir3         = new Vector3((float)dir.X, (float)dir.Y, (float)dir.Z);
				}
				_dirs[count] = dir3;

				// Cache world-fixed sky direction per body for RND-07/D-21 handoff baseline.
				// Phase 3 reads this to align a newly spawned mesh with the sky point's
				// screen position for a pop-free instant swap.
				_skyDirs[body.Index] = dir3;

				// Inverse-square luminosity model (D-17/D-18/D-19).
				// body.Luminosity = 0 → reflected-light body (planet) → floors to MinBrightFloor.
				// Values >> 1.0 drive Forward+ HDR bloom; tone mapper handles the upper range.
				float rawAlpha = body.Luminosity > 0 && len >= 1e-30
					? (float)(body.Luminosity * LuminosityScale / (len * len))
					: 0f;
				float alpha = Mathf.Max(rawAlpha, MinBrightFloor);
				_colors[count] = new Color(body.BaseColor.R, body.BaseColor.G, body.BaseColor.B, alpha);

				// Magnitude-ranked disc size (D-17): brighter → larger disc, clamped to
				// [MinStarSize, MaxStarSize] so faint stars are always ≥1 pixel and bright
				// ones don't fill the sky unrealistically (SizePerBright tunable in inspector).
				if (len >= 1e-30)
					_sizes[count] = Mathf.Clamp(alpha * SizePerBright, MinStarSize, MaxStarSize);

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

		// ----- Hierarchy position math (read-only, double-precision) ------------

		/// <summary>
		/// Returns the absolute position of <paramref name="obj"/> in metres, measured from
		/// the top-level ancestor's origin. Uses the full UniVec3 (Units * Scale + Offset) so
		/// bodies at interstellar distances (e.g. Galaxy-scale siblings) are not collapsed to
		/// the origin by a ToDouble3() call that would lose the integer Units part.
		///
		/// Stops walking when a parent's Scale is ≤ 0 (Root object has Scale = −1).
		///
		/// T-02-02 mitigation: loop guarded by (uint) bounds check and null guard; bounded by
		/// hierarchy depth; coincident/zero-length delta guarded at the call sites.
		/// Read-only — MUST NOT mutate any UniObject.
		/// </summary>
		private static Double3 AbsolutePositionInRoot(UniObject obj, List<UniObject> objs)
		{
			// Full position in metres from the parent's origin: Units * Scale + Offset.
			double  scale = obj.LocalPos.Scale;
			var     u     = obj.LocalPos.Units;
			Double3 pos   = obj.LocalPos.Offset
							+ new Double3((double)u.X * scale, (double)u.Y * scale, (double)u.Z * scale);
			int pIdx = obj.ParentIndex;

			while ((uint)pIdx < (uint)objs.Count && objs[pIdx] != null)
			{
				var parent = objs[pIdx];
				double pScale = parent.LocalPos.Scale;
				if (pScale <= 0) break;   // Root has Scale = -1; stop before corrupting pos.

				// Accumulate parent's full position in metres and add our metres-from-parent.
				var pu = parent.LocalPos.Units;
				pos = parent.LocalPos.Offset
					  + new Double3((double)pu.X * pScale, (double)pu.Y * pScale, (double)pu.Z * pScale)
					  + pos;
				pIdx = parent.ParentIndex;
			}

			return pos;   // metres, absolute from the top-level ancestor origin
		}
	}
}
