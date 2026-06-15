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

				if (len < 1e-30) { _dirs[count] = Vector3.Up; _sizes[count] = MinStarSize; }
				else
				{
					Double3 dir  = delta * (1.0 / len);
					_dirs[count] = new Vector3((float)dir.X, (float)dir.Y, (float)dir.Z);
					// Angular disc size: (R/D)^2 * 0.5 — matches the body's true angular radius
					// so the sky point shrinks/grows with distance and matches the mesh at the
					// SOI transition boundary (Finding 2 fix). Clamped to [1e-7, 0.01].
					float angR   = (float)(body.RadiusMeters / len);
					_sizes[count] = Mathf.Clamp(angR * angR * 0.5f, MinStarSize, 0.01f);
				}

				// Inverse-square luminosity model (D-17/D-18/D-19).
				// body.Luminosity = 0 → reflected-light body (planet) → floors to MinBrightFloor.
				// Values >> 1.0 drive Forward+ HDR bloom; tone mapper handles the upper range.
				float rawAlpha = body.Luminosity > 0
				    ? (float)(body.Luminosity * LuminosityScale / (len * len))
				    : 0f;
				float alpha = MathF.Max(rawAlpha, MinBrightFloor);
				_colors[count] = new Color(body.BaseColor.R, body.BaseColor.G, body.BaseColor.B, alpha);

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
