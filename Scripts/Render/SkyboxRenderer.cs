using Godot;
using System.Collections.Generic;

namespace Render
{
	/// <summary>
	/// Per-frame, read-only sky uniform push: classifies bodies via TierClassifier, computes
	/// world-space directions from ship to each NextTierSkybox body using a double-precision
	/// LCA-relative hierarchy walk, applies the inverse-square luminosity magnitude model
	/// (D-17/D-19), carries each body's BaseColor (D-18), and pushes
	/// star_dirs/star_colors/star_sizes/star_count to the Sky ShaderMaterial each frame.
	/// Brightness values &gt;1 feed the existing WorldEnvironment glow for free (D-20).
	/// Also maintains a per-body sky-direction cache (_skyDirs) as the sky-side of the
	/// RND-07/D-21 handoff baseline; see GetSkyDirection.
	///
	/// Read-only consumer of GameWorld state — MUST NOT mutate UniVec3 or call TranslatePos.
	///
	/// Precision model (LCA-relative walk):
	///   The ship→body vector is formed by walking each side up to their lowest-common ancestor
	///   (LCA) and accumulating metres in the LCA's frame; the final subtraction is a small-vector
	///   operation bounded by the inter-body separation in that frame. The LCA's (potentially
	///   enormous) absolute offset from root never enters the arithmetic, eliminating catastrophic
	///   cancellation of two large nearly-equal vectors at 1:1 Universe scale.
	///
	///   WARNING — do NOT replace the LCA walk with a naive UniVec3 operator- across spaces:
	///   UniVec3.Convert(), operator-, operator+ all route through EnsureSameScale → Convert →
	///   ToDouble3(), which ZEROES the Long3 Units (folds everything into a single double).
	///   A naive bodyUni - shipUni across different scales collapses to double and discards the
	///   precision split, negating the entire point of the LCA approach.
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

		/// <summary>Safety cap on a sky point's angular disc radius (radians, ~28°). Next-tier-out
		/// bodies are always far so their true angular size is sub-pixel; this only guards against
		/// a degenerate near body producing a smoothstep edge of 1−cos(θ) → 1 (which breaks the disc).</summary>
		private const float MaxDiscAngle = 0.5f;
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
		/// Direction math uses the LCA-relative walk (see RelativePosition). The large
		/// common-ancestor offset is never formed; subtraction operates on small LCA-frame
		/// metre vectors bounded by the inter-body separation, not by the absolute distance
		/// from root.
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

			int count = 0;
			for (int i = 0; i < objs.Count && count < MaxStars; i++)
			{
				var body = objs[i];
				if (body == null) continue;
				if (body.Index == shipIdx) continue;

				if (TierClassifier.Classify(body, ship) != SkyTier.NextTierSkybox) continue;

				// Direction and distance: LCA-relative hierarchy walk in double precision.
				// RelativePosition returns body − ship in metres anchored at their LCA so
				// the large common-ancestor offset never enters the subtraction
				// (eliminates catastrophic cancellation at 1:1 Universe scale).
				// Casting only the normalized unit vector to Vector3 avoids precision loss
				// at interstellar distances (Pitfall 5 in RESEARCH.md).
				bool hasCommonAncestor = RelativePosition(body, ship, objs, out Double3 delta);

				double  len  = hasCommonAncestor ? delta.Magnitude() : 0.0;

				Vector3 dir3;
				if (!hasCommonAncestor || len < 1e-30)
					dir3 = Vector3.Up;
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

				// SIZE — purely physical: the disc subtends the star's true angular radius
				// θ = RadiusMeters / distance (the SAME rule the mesh sphere obeys, so the
				// handoff cannot pop), floored at the pixel footprint because nothing can render
				// smaller than one pixel. The smoothstep disc param is (1 − cos θ_eff).
				double theta = StarRendering.AngularRadius(body.RadiusMeters, len);
				// Floor at one pixel (resolution limit) and cap at MaxDiscAngle so a degenerate
				// near body cannot drive 1−cos toward 1 (which collapses the shader smoothstep).
				// Next-tier-out bodies are always far, so this cap is a safety bound, not a tuning knob.
				float  eff   = Mathf.Clamp((float)theta, pixelAngle, MaxDiscAngle);
				_sizes[count] = 1f - Mathf.Cos(eff);

				// BRIGHTNESS — the SAME shared rule the mesh uses: inverse-square flux through a
				// magnitude (log) curve, shifted by the one global StarRendering.Exposure. Result
				// is in [0,1] so the star's BaseColor hue is preserved instead of washing out.
				float alpha = StarRendering.ApparentBrightness(body.Luminosity, len);
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

		// ----- Hierarchy position math (read-only, double-precision, LCA-relative) ----

		/// <summary>
		/// Returns the ship→body vector in metres, computed via the lowest-common ancestor (LCA).
		/// Each side is accumulated relative to the LCA in metres (stopping the walk at the LCA);
		/// the final subtraction operates on two small LCA-frame vectors whose magnitude is bounded
		/// by inter-body separation — the LCA's large absolute offset from root is never formed.
		///
		/// Returns <c>true</c> and populates <paramref name="delta"/> on success.
		/// Returns <c>false</c> (delta = Double3.Zero) if no common ancestor is found — callers
		/// treat this as the coincident/zero-length case (fall back to Vector3.Up).
		///
		/// T-02-02 mitigation: loops guarded by (uint) bounds check and null guard; bounded
		/// by hierarchy depth (≤ 5); coincident/zero-length delta guarded at the call site.
		/// Read-only — MUST NOT mutate any UniObject.
		///
		/// WARNING — do NOT replace with a naive UniVec3 operator- across spaces: Convert()
		/// routes through ToDouble3(), which zeroes the Long3 Units, collapsing everything to
		/// a single double and throwing away the precision split that this walk preserves.
		/// </summary>
		private static bool RelativePosition(
			UniObject body, UniObject ship, List<UniObject> objs, out Double3 delta)
		{
			int lcaIdx = FindLca(body, ship, objs);
			if (lcaIdx < 0)
			{
				delta = Double3.Zero;
				return false;
			}

			Double3 bodyFromLca = PositionRelativeToAncestor(body, lcaIdx, objs);
			Double3 shipFromLca = PositionRelativeToAncestor(ship, lcaIdx, objs);

			// body position relative to ship, both expressed in LCA-frame metres.
			// Both operands are small (bounded by inter-body separation from the LCA),
			// so no catastrophic cancellation of large nearly-equal values.
			delta = bodyFromLca - shipFromLca;
			return true;
		}

		/// <summary>
		/// Finds the lowest-common ancestor index of <paramref name="a"/> and
		/// <paramref name="b"/> in the object hierarchy. Returns -1 if no common
		/// ancestor can be found (should be impossible — all objects share Root).
		///
		/// Algorithm: build a set of all ancestor indices (including a itself) by
		/// walking a's ParentIndex chain; then walk b's chain upward until the first
		/// index that is in that set — that index is the LCA.
		///
		/// Every loop step is guarded by (uint)idx &lt; (uint)objs.Count and a null
		/// check (T-02-02 mitigation). Hierarchy depth is ≤ 5.
		/// Read-only — MUST NOT mutate any UniObject.
		/// </summary>
		private static int FindLca(UniObject a, UniObject b, List<UniObject> objs)
		{
			// Build the ancestor set for 'a' (including 'a' itself).
			// Using a small fixed-size approach — hierarchy depth ≤ 5, so a HashSet is fine.
			var aAncestors = new HashSet<int>();
			int idx = a.Index;
			while ((uint)idx < (uint)objs.Count && objs[idx] != null)
			{
				aAncestors.Add(idx);
				int parentIdx = objs[idx].ParentIndex;
				if (parentIdx == idx) break;   // cycle guard (Root points to -1 normally)
				idx = parentIdx;
			}

			// Walk 'b' upward until we find an index in aAncestors.
			idx = b.Index;
			while ((uint)idx < (uint)objs.Count && objs[idx] != null)
			{
				if (aAncestors.Contains(idx)) return idx;
				int parentIdx = objs[idx].ParentIndex;
				if (parentIdx == idx) break;   // cycle guard
				idx = parentIdx;
			}

			return -1;   // no common ancestor found (should not happen in a valid hierarchy)
		}

		/// <summary>
		/// Accumulates the position of <paramref name="node"/> relative to the ancestor at
		/// <paramref name="lcaIdx"/> in metres, walking child→parent and stopping when the
		/// walk reaches the LCA (or the node IS the LCA — returns Double3.Zero).
		///
		/// At each step the node's full position in its own frame is formed as:
		///   pos = Units * Scale + Offset    (metres in the child frame)
		/// then added to the running total expressed in the LCA frame. This is the same
		/// per-step accumulation pattern used in WorldRenderer.ComputeStarRenderPosFromHierarchy,
		/// generalized to walk to an arbitrary ancestor.
		///
		/// The defensive Scale ≤ 0 break ensures Root (Scale = -1) is never multiplied in,
		/// even though the LCA walk normally stops well above Root.
		///
		/// T-02-02 mitigation: loop guarded by (uint) bounds check and null guard; bounded
		/// by hierarchy depth (≤ 5). Read-only — MUST NOT mutate any UniObject.
		/// </summary>
		private static Double3 PositionRelativeToAncestor(
			UniObject node, int lcaIdx, List<UniObject> objs)
		{
			// If the node IS the LCA it contributes zero offset from the LCA frame.
			if (node.Index == lcaIdx) return Double3.Zero;

			// Full position of 'node' in its own frame (metres from its parent's origin):
			//   Units * Scale + Offset
			double  scale = node.LocalPos.Scale;
			var     u     = node.LocalPos.Units;
			Double3 pos   = node.LocalPos.Offset
							+ new Double3((double)u.X * scale, (double)u.Y * scale, (double)u.Z * scale);

			int pIdx = node.ParentIndex;

			// Walk upward, accumulating each parent's in-frame position, until we reach
			// the LCA. The walk terminates once pIdx becomes the LCA index — at that
			// point `pos` already contains the node's metres-from-LCA (the LCA's own
			// position relative to itself is zero, so we do not need to add it).
			while ((uint)pIdx < (uint)objs.Count && objs[pIdx] != null)
			{
				if (pIdx == lcaIdx) break;   // reached the LCA — stop here

				var parent = objs[pIdx];
				double pScale = parent.LocalPos.Scale;
				if (pScale <= 0) break;   // Root has Scale = -1; never multiply Root's position in

				// Accumulate parent's own in-frame position (metres from its parent's origin).
				var pu = parent.LocalPos.Units;
				pos = parent.LocalPos.Offset
					  + new Double3((double)pu.X * pScale, (double)pu.Y * pScale, (double)pu.Z * pScale)
					  + pos;
				pIdx = parent.ParentIndex;
			}

			return pos;   // metres from the LCA's origin, in LCA-frame coordinates
		}
	}
}
