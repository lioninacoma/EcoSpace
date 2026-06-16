using Godot;
using System.Collections.Generic;

namespace Render
{
	/// <summary>
	/// Floating-origin world renderer: each frame, computes ship-relative positions
	/// for all bodies in the current parent space and repositions their MeshInstance3D
	/// nodes accordingly. This keeps the camera at the world origin (floating origin)
	/// and avoids single-precision float jitter at large distances (RND-01/RND-02).
	///
	/// Per-body materials and 1:1 true radii added in Plan 01-02 (RND-03/04):
	/// - Planet meshes: unshaded spatial ShaderMaterial (body_lit.gdshader) that
	///   computes a Lambert day/night terminator in the fragment shader from an
	///   explicit per-body star_dir uniform. Shading is IDENTICAL in every space the
	///   ship occupies (Star space, Planet space, any future tier) because lighting is
	///   purely shader-driven — no dependence on Godot OmniLight3D or DirectionalLight3D,
	///   which would require different node types per space and produce a character
	///   change on cross-space transitions (eliminated by 01-02 shader revision).
	/// - Star mesh: unshaded emissive StandardMaterial3D (no lighting needed — it IS
	///   the light source; bloom/glow via WorldEnvironment).
	///
	/// Per-body star_dir computation each frame:
	///   The nearest star's render-space position is derived from the same cross-frame
	///   hierarchy math used for body positioning (ship-relative floating-origin).
	///   In Star space the star is in the render set and its renderPos is tracked
	///   directly. In Planet space the star is NOT in the render set (RND-02 — only
	///   the planet children are shown), so the star render position is derived by
	///   walking planet→star hierarchy positions in Star-space metres, then applying
	///   the render factor to get the star's render-space vector (direction is what
	///   matters; the star is ~1.5e7 render units out, well beyond the far plane).
	///   star_dir = normalize(starRenderPos - bodyRenderPos) set per body per frame.
	///
	/// Read-only consumer of GameWorld state — MUST NOT mutate UniVec3 or call TranslatePos.
	/// </summary>
	public partial class WorldRenderer : Node3D
	{
		// ----- Exports --------------------------------------------------------

		/// <summary>NodePath to the GameWorld / TestSetup node in the scene.</summary>
		[Export] public NodePath WorldPath { get; set; }

		/// <summary>Far plane for the Camera3D (render units). 1e6 render units = 1 AU at default planet factor.</summary>
		[Export] public float CameraFarPlane { get; set; } = 1e6f;

		/// <summary>
		/// Render units per observer-unit for Planet-space frames (applied after ToLocalDoubleUnits).
		/// Universe/SOI math stays 1:1 meters; only WorldRenderer applies this factor.
		/// Planet scale = 0.0001 m/unit → 1 observer-unit = 0.0001 m. Factor=1e-8 → 1 render unit = 1e12 m.
		/// Radius transform: RadiusMeters (true m) / ship.LocalPos.Scale (→ observer units) × factor (→ render units).
		/// </summary>
		private float PlanetRenderFactor { get; set; } = 1e-8f;

		/// <summary>
		/// Render units per observer-unit for Star-space frames (applied after ToLocalDoubleUnits).
		/// Star scale = 1 m/unit → 1 observer-unit = 1 m. Factor=1e-8 → 1 render unit = 1e8 m.
		/// </summary>
		private float StarRenderFactor { get; set; } = 1e-8f;

		/// <summary>
		/// Render units per observer-unit for Galaxy-space frames.
		/// Placeholder — Galaxy tier not exercised by MVP scene; tune when reached.
		/// </summary>
		private float GalaxyRenderFactor { get; set; } = 1e-8f;

		/// <summary>
		/// Render units per observer-unit for Universe-space frames.
		/// Placeholder — Universe tier not exercised by MVP scene; tune when reached.
		/// </summary>
		private float UniverseRenderFactor { get; set; } = 1e-8f;

		/// <summary>
		/// Default sphere mesh radius (true meters) used for unnamed/unpresented bodies.
		/// Named bodies use body.RadiusMeters instead (Plan 01-02 RND-03/04).
		/// </summary>
		[Export] public float DefaultBodyRadius { get; set; } = 6.371e6f;   // Earth-radius fallback

		// ----- Star emissive ---------------------------------------------------

		/// <summary>
		/// THE single global star-brightness knob, shared by the star mesh AND the skybox light
		/// points (it is the editor handle for <see cref="StarRendering.Exposure"/>). In
		/// magnitude/exposure stops: 0 = calibrated default, positive brightens every star
		/// everywhere, negative dims them. There is no separate mesh-only or sky-only brightness.
		/// A star's relative brightness still comes from its own Luminosity + distance.
		/// </summary>
		[Export] public float StarBrightness
		{
			get => StarRendering.Exposure;
			set => StarRendering.Exposure = value;
		}

		// ----- Shader-based body lighting exports (replaces Godot light nodes) ----

		/// <summary>
		/// Sun brightness on the lit hemisphere of body_lit.gdshader.
		/// Applied as the light_energy uniform on each planet/body ShaderMaterial.
		/// Default 1.8 matches the former StarLightEnergy / PlanetSunLightEnergy —
		/// both are now a single value because shading is space-independent.
		/// Tune for desired lit-side vs night-side contrast.
		/// </summary>
		[Export] public float BodyLightEnergy { get; set; } = 1.8f;

		/// <summary>
		/// Ambient floor on the night hemisphere of body_lit.gdshader.
		/// Applied as the ambient uniform on each planet/body ShaderMaterial.
		/// Default 0.03 keeps the night side dark-but-visible (matches the prior
		/// WorldEnvironment ambient_light_energy that lit the night hemisphere when
		/// Godot light nodes were active). Increase slightly if the night side is too dark.
		/// </summary>
		[Export] public float BodyAmbient { get; set; } = 0.03f;

		// ----- Private state --------------------------------------------------

		private TestSetup _world;

		/// <summary>Loaded once in _Ready from res://Shaders/body_lit.gdshader.</summary>
		private Shader _bodyLitShader;

		/// <summary>
		/// Maps the observer (ship) space to the appropriate per-space render factor.
		/// Factor is applied to observer-unit positions (after ToLocalDoubleUnits) to
		/// produce render-space coordinates. Choosing the factor from the ship's space
		/// ensures all bodies in the same frame use a consistent unit basis.
		/// </summary>
		private float RenderFactorFor(UniObject.Space space) => space switch
		{
			UniObject.Space.Planet   => PlanetRenderFactor,
			UniObject.Space.Star     => StarRenderFactor,
			UniObject.Space.Galaxy   => GalaxyRenderFactor,
			UniObject.Space.Universe => UniverseRenderFactor,
			_                        => StarRenderFactor,
		};

		/// <summary>
		/// RND-07/D-21 handoff baseline: last render-space positions from SyncBodies, keyed by
		/// body index. Persisted across frames (rebuilt each frame) so GetRenderPosition can be
		/// called at a tier-crossing event that fires between frames. Cleared at the top of each
		/// SyncBodies call, then repopulated for all bodies in the current render set (T-02-05
		/// mitigation: keyed by validated body Index; no stale entries accumulate beyond one frame).
		///
		/// Phase 2 builds ONLY this data structure — it performs NO visible swap or crossfade
		/// (D-21, D-24). Phase 3 reads it at a tier-crossing to get the mesh's render-space
		/// position, then compares it with SkyboxRenderer.GetSkyDirection to align the promoted
		/// mesh with the sky point on the same screen pixel for an instant exact-match swap.
		/// Color match is automatic: both renderers read body.BaseColor (D-18).
		/// Brightness match is automatic: both derive from body.Luminosity.
		/// </summary>
		private readonly Dictionary<int, Vector3> _lastRenderPositions = [];

		/// <summary>Per-body mesh instances: keyed by GameObjects index.</summary>
		private readonly Dictionary<int, MeshInstance3D> _meshes = [];

		/// <summary>Reused per-frame scratch (cleared at the top of SyncBodies) to avoid
		/// steady GC pressure from allocating fresh collections every frame (WR-01).</summary>
		private readonly HashSet<int> _activeIndices = [];
		private readonly Dictionary<int, Vector3> _renderPositions = [];

		/// <summary>
		/// Per-body shader materials for lit (non-star) bodies.
		/// Keyed by GameObjects index. star_dir, light_energy, and ambient
		/// are pushed each frame in SyncBodies.
		/// </summary>
		private readonly Dictionary<int, ShaderMaterial> _litMaterials = [];

		// ----- Godot callbacks ------------------------------------------------

		public override void _Ready()
		{
			if (WorldPath != null && !WorldPath.IsEmpty)
				_world = GetNode<TestSetup>(WorldPath);
			else
				_world = GetParent<TestSetup>() ?? GetTree().Root.FindChild("Main", true, false) as TestSetup;

			// Apply camera far plane
			var cam = GetTree().Root.FindChild("Camera3D", true, false) as Camera3D;
			if (cam != null) cam.Far = CameraFarPlane;

			// Load the body lighting shader once. All lit-body ShaderMaterials share
			// this shader object; per-body uniforms are set via ShaderMaterial.SetShaderParameter.
			_bodyLitShader = GD.Load<Shader>("res://Shaders/body_lit.gdshader");
		}

		public override void _Process(double delta)
		{
			if (_world == null) return;

			SyncBodies();
		}

		// ----- Core floating-origin sync -------------------------------------

		/// <summary>
		/// Repositions all in-parent-space body meshes relative to the ship each frame
		/// and pushes per-body star_dir uniforms to the body_lit ShaderMaterials so the
		/// Lambert terminator always faces the nearest star in every space.
		/// Only bodies in the ship's current parent space are shown (RND-02).
		/// Also persists render positions into _lastRenderPositions for RND-07/D-21 handoff.
		/// </summary>
		public void SyncBodies()
		{
			var gameObjects = _world?.GameObjects;
			if (gameObjects == null) return;

			int shipIndex = _world.ShipIndex;
			var ship = (uint)shipIndex < (uint)gameObjects.Count ? gameObjects[shipIndex] : null;
			if (ship == null) return;

			int parentIdx = ship.ParentIndex;
			var parent = (uint)parentIdx < (uint)gameObjects.Count ? gameObjects[parentIdx] : null;
			if (parent == null) return;

			// Compute render factor once per frame from the ship's current space.
			// All bodies in this frame share the same observer-unit basis (ship.LocalPos.Scale).
			float factor = RenderFactorFor(ship.CurrentSpace);

			// Track which indices are rendered this frame to hide bodies that left the set.
			// Reused scratch fields (WR-01) — cleared each frame instead of reallocated.
			var activeIndices = _activeIndices;
			activeIndices.Clear();

			// Collect per-body render positions for this frame (used for star_dir computation
			// and persisted to _lastRenderPositions for RND-07/D-21 handoff baseline).
			var renderPositions = _renderPositions;
			renderPositions.Clear();

			// Clear persisted positions — rebuilt fresh each frame (T-02-05 mitigation).
			_lastRenderPositions.Clear();

			// Track whether the star is in the current render set.
			bool starRendered = false;
			int  starIdx      = -1;

			// Render the parent body itself (e.g. the planet we orbit) ship-relative.
			// The parent lives at the ORIGIN of the ship's frame, so its position relative
			// to the ship is simply the negation of the ship's own offset in that frame.
			// D-28/T-03-06: if the parent is a Galaxy (ship in Universe space) skip mesh render —
			// galaxies are sky-only and must never appear as world-space sphere meshes.
			if (parent.ObjectType != UniObject.Type.Galaxy)
			{
				RenderBodyAt(parentIdx, parent, ship, factor, isParent: true, out bool parentIsStar, out Vector3 parentRenderPos);
				activeIndices.Add(parentIdx);
				renderPositions[parentIdx] = parentRenderPos;
				if (parentIsStar) { starRendered = true; starIdx = parentIdx; }
			}

			// Render siblings: all children of parent except the ship itself
			foreach (int childIdx in parent.ChildIndices)
			{
				if (childIdx == shipIndex) continue;

				var body = (uint)childIdx < (uint)gameObjects.Count ? gameObjects[childIdx] : null;
				if (body == null) continue;
				// D-28/T-03-06: galaxies are sky-only — never render as a mesh (Pitfall 4)
				if (body.ObjectType == UniObject.Type.Galaxy) continue;

				RenderBodyAt(childIdx, body, ship, factor, isParent: false, out bool siblingIsStar, out Vector3 siblingRenderPos);
				activeIndices.Add(childIdx);
				renderPositions[childIdx] = siblingRenderPos;
				if (siblingIsStar) { starRendered = true; starIdx = childIdx; }
			}

			// Hide any previously-shown bodies no longer in the current space.
			foreach (var kvp in _meshes)
			{
				kvp.Value.Visible = activeIndices.Contains(kvp.Key);
			}

			// Determine the star's render-space position for star_dir computation.
			// If the star is in the render set, use its tracked renderPos directly.
			// If the star is NOT in the render set (Planet space), compute the direction
			// from ship→planet→star cross-frame hierarchy in Star-space metres.
			Vector3 starRenderPos;
			if (starRendered && starIdx >= 0)
			{
				starRenderPos = renderPositions[starIdx];
			}
			else
			{
				// Planet space: star is the grandparent (planet's parent).
				// Derive the star's render-space position even though it is beyond the far plane;
				// only the direction matters for star_dir.
				starRenderPos = ComputeStarRenderPosFromHierarchy(ship, parent, gameObjects, factor);
			}

			// Push star_dir, light_energy, and ambient to each lit (non-star) body's shader.
			foreach (int bodyIdx in activeIndices)
			{
				if (!_litMaterials.TryGetValue(bodyIdx, out var mat)) continue;

				Vector3 bodyRenderPos = renderPositions.TryGetValue(bodyIdx, out var p) ? p : Vector3.Zero;
				Vector3 toStar = starRenderPos - bodyRenderPos;
				float   mag    = toStar.Length();
				Vector3 starDir = mag > 1e-6f ? toStar / mag : Vector3.Up;

				mat.SetShaderParameter("star_dir",     starDir);
				mat.SetShaderParameter("light_energy", BodyLightEnergy);
				mat.SetShaderParameter("ambient",      BodyAmbient);
			}

			// Persist render positions for the RND-07/D-21 handoff baseline accessor.
			// Copied after all bodies are rendered so the full render set is captured.
			foreach (var kvp in renderPositions)
				_lastRenderPositions[kvp.Key] = kvp.Value;
		}

		// ----- RND-07/D-21 handoff baseline accessor ---------------------------

		/// <summary>
		/// Returns the last render-space position for the body currently tracked as a mesh.
		/// Returns <c>true</c> and populates <paramref name="pos"/> when the body was in the
		/// render set during the most-recent SyncBodies call; returns <c>false</c> (pos=default)
		/// if the body is not currently rendered as a mesh.
		///
		/// This is the mesh-side of the two RND-07/D-21 baseline data sources. Phase 3 reads it
		/// at a tier-crossing to get a mesh body's current render-space position, then compares it
		/// with <see cref="SkyboxRenderer.GetSkyDirection"/> (the sky-point direction) so the
		/// promoted sky point and the newly spawned mesh can be placed on the same screen pixel
		/// for an instant exact-match swap with no crossfade (D-21). Phase 2 ONLY builds and
		/// exposes this accessor; no visible swap is performed here (D-24).
		/// Color match is automatic: both renderers read body.BaseColor (D-18).
		/// Brightness match is automatic: both derive from body.Luminosity.
		/// </summary>
		/// <param name="bodyIdx">The body index (UniObject.Index / GameObjects list key).</param>
		/// <param name="pos">Receives the last render-space Vector3 position if found.</param>
		/// <returns>True if the body was in the render set during the most-recent frame.</returns>
		public bool GetRenderPosition(int bodyIdx, out Vector3 pos)
		{
			if (_lastRenderPositions.TryGetValue(bodyIdx, out pos)) return true;
			pos = default;
			return false;
		}

		/// <summary>
		/// Derives the star's render-space position when the star is NOT in the current
		/// render set (e.g. Planet space). Uses UniMath.RelativeMetres for the cross-frame
		/// hierarchy math — the same LCA-relative UniVec3 strategy as SkyboxRenderer —
		/// expressed as a render-space Vector3 for star_dir shading.
		///
		/// Sign derivation (why no negation):
		///   UniMath.RelativeMetres(ship, star, …) returns star − ship in metres via the LCA
		///   path (from=ship, to=star → result = star position − ship position in LCA frame).
		///   This is exactly the star-relative-to-ship vector we need as a render-space direction.
		///   The old code computed shipInStar then applied -shipInStar; UniMath.RelativeMetres
		///   gives us star-ship directly, so we use +starRelToShip (NO negation).
		///
		/// The star is at ~1.5e11 m (1 AU) in Star-space metres, mapping to ~1.5e7 render
		/// units — far beyond the 1e6 far plane. The DIRECTION is what matters; the vector
		/// is far out but normalised correctly in SyncBodies.
		///
		/// Fallback: returns Vector3.Up * 1e7f when the hierarchy is unexpected (star not found
		/// above the planet) OR when UniMath.RelativeMetres returns Double3.Zero (no LCA).
		/// </summary>
		private Vector3 ComputeStarRenderPosFromHierarchy(
			UniObject ship, UniObject planet, List<UniObject> gameObjects, float factor)
		{
			// Walk up from planet to find the star.
			int grandparentIdx = planet.ParentIndex;
			var star = (uint)grandparentIdx < (uint)gameObjects.Count ? gameObjects[grandparentIdx] : null;
			if (star == null || !IsStarBody(star))
				return Vector3.Up * 1e7f;   // fallback: point light straight up if hierarchy unexpected

			// LCA-relative cross-frame position: star − ship in metres.
			// UniMath.RelativeMetres(from=ship, to=star) walks both to their LCA in UniVec3,
			// subtracts at the same scale for exact integer Units cancellation, then collapses
			// the small delta to metres via a single ToDouble3(). This is the same precision
			// strategy used by SkyboxRenderer — the star's enormous absolute-from-root offset
			// never enters the arithmetic.
			// Sign: result is already star-relative-to-ship (to − from = star − ship),
			// so NO negation is needed here. The old code computed -shipInStar; we get +starRelToShip.
			Double3 starRelToShip = UniMath.RelativeMetres(ship, star, gameObjects);

			// Fallback if UniMath found no common ancestor (should not occur in a valid hierarchy).
			if (starRelToShip.X == 0.0 && starRelToShip.Y == 0.0 && starRelToShip.Z == 0.0)
				return Vector3.Up * 1e7f;

			// Convert to observer units (÷ ship.LocalPos.Scale) then to render units (× factor).
			double obsFactor = factor / ship.LocalPos.Scale;
			return new Vector3(
				(float)(starRelToShip.X * obsFactor),
				(float)(starRelToShip.Y * obsFactor),
				(float)(starRelToShip.Z * obsFactor));
		}

		// ----- Private helpers -----------------------------------------------

		/// <summary>
		/// Determines whether the given body is a star. Identified by ObjectType (D-38) so
		/// it works for any star across any galaxy, not just the home-system "STAR" by name.
		/// </summary>
		private static bool IsStarBody(UniObject body) =>
			body.ObjectType == UniObject.Type.Star;

		/// <summary>
		/// Ensures a MeshInstance3D exists for <paramref name="bodyIdx"/>, then positions it
		/// ship-relative in render space. Uses observer-unit basis positions multiplied by the
		/// per-space <paramref name="factor"/> so positions stay within camera far range.
		/// Anchors on ship.LocalPos (Pitfall 4 — never parent body, or render jitters).
		///
		/// <paramref name="isParent"/>: when true the body is the parent of the ship's frame.
		/// Its LocalPos is in the GRANDPARENT frame and cannot be diffed against ship.LocalPos
		/// (which is in the PARENT frame). Instead, the parent sits at the ORIGIN of the ship's
		/// frame, so ship-relative position = -(ship's own offset in that frame).
		/// For siblings (isParent=false) both body and ship share the same parent frame, so
		/// ToLocalDoubleUnits is correct.
		/// All paths yield observer-unit positions on ship.LocalPos.Scale basis before × factor.
		///
		/// <paramref name="isStar"/>: set to true when this body is the star (for star_dir sync).
		/// <paramref name="renderPos"/>: the computed render-space position (returned for star_dir).
		/// </summary>
		private void RenderBodyAt(
			int bodyIdx,
			UniObject body,
			UniObject ship,
			float factor,
			bool isParent,
			out bool isStar,
			out Vector3 renderPos)
		{
			isStar = IsStarBody(body);
			var mesh = GetOrCreateMesh(bodyIdx, body);

			// Per-frame radius: true metres → observer units (÷ ship.LocalPos.Scale) → render units (× factor).
			// Recomputed every frame so the radius is correct after SOI transitions change ship.LocalPos.Scale.
			// Verified math (Star space, factor=1e-8):
			//   Planet A: 6.371e6 / 1 × 1e-8 = 0.0637 render units (correct tiny speck)
			//   Star:     6.96e8  / 1 × 1e-8 = 6.96 render units
			// Verified math (Planet space, scale=1e-4 m/unit, factor=1e-8):
			//   Planet A: 6.371e6 / 0.0001 × 1e-8 = 637 render units (correct fills view close-up)
			double rawRadiusMeters = body.RadiusMeters > 0.0 ? body.RadiusMeters : DefaultBodyRadius;
			float r = (float)((rawRadiusMeters / ship.LocalPos.Scale) * factor);
			mesh.Scale = new Vector3(r, r, r);

			Double3 relUnits;
			if (isParent)
			{
				// Parent body sits at the origin of the ship's frame; its position relative
				// to the ship is the negation of the ship's offset within that frame.
				// ship.LocalPos.ToDouble3Units() is already on the ship-scale (observer) basis.
				relUnits = ship.LocalPos.ToDouble3Units() * -1.0;
			}
			else
			{
				// Sibling: both body and ship share the same parent frame → direct delta.
				// Floating-origin: metres → ÷ ship.LocalPos.Scale → observer units.
				relUnits = body.LocalPos.ToLocalDoubleUnits(ship.LocalPos);
			}

			// × factor → render units (consistent basis for positions and radii).
			renderPos = new Vector3(
				(float)(relUnits.X * factor),
				(float)(relUnits.Y * factor),
				(float)(relUnits.Z * factor));

			mesh.Position = renderPos;
			mesh.Visible  = true;

			// Star mesh emitted light: driven each frame by the SAME shared StarRendering curve
			// the skybox uses (inverse-square flux of the star's own Luminosity at its true
			// distance, through the magnitude curve + global Exposure). distMeters = observer-unit
			// distance × ship scale. Updating per frame keeps mesh and sky coherent under live
			// Exposure tuning and as the star's distance changes.
			if (isStar && mesh.MaterialOverride is StandardMaterial3D starMat)
			{
				double distMeters = relUnits.Magnitude() * ship.LocalPos.Scale;
				starMat.EmissionEnergyMultiplier = StarRendering.ApparentBrightness(body.Luminosity, distMeters);
			}
		}

		/// <summary>
		/// Returns the MeshInstance3D for this body index, creating it lazily on
		/// first encounter. Never spawns or frees per frame.
		///
		/// Mesh geometry: a UNIT sphere (Radius=1, Height=2) is created once. The rendered
		/// size is applied each frame by setting MeshInstance3D.Scale in RenderBodyAt so
		/// that the radius correctly reflects the current observer scale and render factor
		/// after every SOI transition.
		///
		/// Material assignment (RND-03/04, 01-02 shader revision):
		/// - Star: ShadingMode=Unshaded + EmissionEnabled; EmissionEnergyMultiplier is set each
		///   frame in RenderBodyAt from StarRendering.ApparentBrightness — the shared star light
		///   rule (identical curve to the skybox points).
		///   No lighting needed — it IS the light source. Bloom via WorldEnvironment glow.
		/// - Planets/other: ShaderMaterial using body_lit.gdshader (unshaded spatial shader).
		///   Albedo is set from body.BaseColor at creation. star_dir, light_energy, and ambient
		///   are pushed each frame in SyncBodies so the Lambert terminator always faces the
		///   nearest star, in every space, with identical shading character. A separate
		///   ShaderMaterial instance per body allows independent per-body star_dir uniforms
		///   (directions differ slightly per body at planetary separation distances).
		/// </summary>
		private MeshInstance3D GetOrCreateMesh(int bodyIdx, UniObject body)
		{
			if (_meshes.TryGetValue(bodyIdx, out var existing))
				return existing;

			// Unit sphere — scale is applied per-frame in RenderBodyAt to handle SOI transitions.
			var sphereMesh = new SphereMesh
			{
				Radius = 1f,
				Height = 2f,
			};

			MeshInstance3D meshInstance;

			if (IsStarBody(body))
			{
				// Emissive unshaded star (D-14). No shading pass needed — it is the source.
				// Bloom is produced by the WorldEnvironment glow settings in Main.tscn.
				var mat = new StandardMaterial3D
				{
					ShadingMode              = BaseMaterial3D.ShadingModeEnum.Unshaded,
					AlbedoColor              = body.BaseColor,
					EmissionEnabled          = true,
					Emission                 = body.BaseColor,
					// Emitted light comes from the shared StarRendering curve (same as the skybox);
					// updated each frame in RenderBodyAt from the star's Luminosity + distance.
					EmissionEnergyMultiplier = 1f,
				};

				meshInstance = new MeshInstance3D
				{
					Mesh             = sphereMesh,
					MaterialOverride = mat,
					Visible          = false,
				};
			}
			else
			{
				// Lit planet/body: unshaded spatial ShaderMaterial (body_lit.gdshader).
				// A SEPARATE ShaderMaterial instance per body is mandatory — ShaderMaterial
				// is a Resource with reference semantics; if bodies shared one instance,
				// setting star_dir on one would overwrite all others. Each body needs its
				// own uniform set because star_dir differs per body.
				var mat = new ShaderMaterial
				{
					Shader = _bodyLitShader,
				};

				// Set albedo at creation; it does not change frame-to-frame.
				Color baseColor = body.BaseColor.IsEqualApprox(default) ? new Color(0.75f, 0.85f, 0.75f) : body.BaseColor;
				mat.SetShaderParameter("albedo", baseColor);

				// star_dir, light_energy, ambient are set each frame in SyncBodies.
				// Initialise to safe defaults so the shader is never in an undefined state.
				mat.SetShaderParameter("star_dir",     Vector3.Up);
				mat.SetShaderParameter("light_energy", BodyLightEnergy);
				mat.SetShaderParameter("ambient",      BodyAmbient);

				_litMaterials[bodyIdx] = mat;

				meshInstance = new MeshInstance3D
				{
					Mesh             = sphereMesh,
					MaterialOverride = mat,
					Visible          = false,
				};
			}

			AddChild(meshInstance);
			_meshes[bodyIdx] = meshInstance;

			return meshInstance;
		}
	}
}
