using Godot;
using System.Collections.Generic;
using Universe.Math;

namespace Universe
{
	/// <summary>
	/// Floating-origin render bridge: each frame, computes ship-relative positions
	/// for all bodies in the current parent space and repositions their MeshInstance3D
	/// nodes accordingly. This keeps the camera at the world origin (floating origin)
	/// and avoids single-precision float jitter at large distances (RND-01/RND-02).
	///
	/// Per-body materials and 1:1 true radii added in Plan 01-02 (RND-03/04):
	/// - Planet meshes: default-lit StandardMaterial3D with body.BaseColor, so the
	///   star OmniLight (Star space) or DirectionalLight3D (Planet space) produces a
	///   day/night terminator.
	/// - Star mesh: unshaded emissive StandardMaterial3D (no lighting needed).
	/// - OmniLight3D positioned at the star's ship-relative render transform each frame
	///   (ShadowEnabled=false per RND-04). Its range is in render units and must cover
	///   the full rendered scene; tune StarLightRange accordingly.
	///
	/// Planet-space directional terminator (01-02 revision, RND-02/D-16):
	///   When the ship is in Planet space the star is NOT in the render set (only
	///   parent.ChildIndices bodies are shown — RND-02), so the OmniLight3D is absent.
	///   A DirectionalLight3D (_planetSunLight) is instead activated each frame,
	///   oriented so photons travel FROM the sun TOWARD the planet (computed by walking
	///   ship → planet → star in GameWorld hierarchy and combining LocalPos values in
	///   Star-space meters, then pointing the light's -Z axis in -sun_direction).
	///   This produces a correct day/night terminator on the planet surface whose
	///   dark hemisphere reads as clearly unlit.
	///
	///   The sun is NOT rendered as a mesh in Planet space. At 1:1 scale the sun is
	///   ~1.5e11 m away, which maps to ~1.5e7 render units — ~15× beyond the 1e6 far
	///   plane. The visible sun mesh in Planet space is deferred to the Phase 3 tiered
	///   renderer (skybox/billboard pass for bodies beyond the far plane).
	///
	///   A small ambient floor (Main.tscn) keeps the night hemisphere dark-but-visible
	///   (not pure black), matching the retro aesthetic.
	///
	/// Read-only consumer of GameWorld state — MUST NOT mutate UniVec3 or call TranslatePos.
	/// </summary>
	public partial class RenderBridge : Node3D
	{
		// ----- Exports --------------------------------------------------------

		/// <summary>NodePath to the GameWorld / TestSetup node in the scene.</summary>
		[Export] public NodePath WorldPath { get; set; }

		/// <summary>Far plane for the Camera3D (render units). 1e6 render units = 1 AU at default planet factor.</summary>
		[Export] public float CameraFarPlane { get; set; } = 1e6f;

		/// <summary>
		/// Render units per observer-unit for Planet-space frames (applied after ToLocalDoubleUnits).
		/// Universe/SOI math stays 1:1 meters; only RenderBridge applies this factor.
		/// Planet scale = 0.0001 m/unit → 1 observer-unit = 0.0001 m. Factor=1e-8 → 1 render unit = 1e12 m.
		/// Radius transform: RadiusMeters (true m) / ship.LocalPos.Scale (→ observer units) × factor (→ render units).
		/// </summary>
		[Export] public float PlanetRenderFactor { get; set; } = 1e-8f;

		/// <summary>
		/// Render units per observer-unit for Star-space frames (applied after ToLocalDoubleUnits).
		/// Star scale = 1 m/unit → 1 observer-unit = 1 m. Factor=1e-8 → 1 render unit = 1e8 m.
		/// </summary>
		[Export] public float StarRenderFactor { get; set; } = 1e-8f;

		/// <summary>
		/// Render units per observer-unit for Galaxy-space frames.
		/// Placeholder — Galaxy tier not exercised by MVP scene; tune when reached.
		/// </summary>
		[Export] public float GalaxyRenderFactor { get; set; } = 1e-8f;

		/// <summary>
		/// Render units per observer-unit for Universe-space frames.
		/// Placeholder — Universe tier not exercised by MVP scene; tune when reached.
		/// </summary>
		[Export] public float UniverseRenderFactor { get; set; } = 1e-8f;

		/// <summary>
		/// Default sphere mesh radius (true meters) used for unnamed/unpresented bodies.
		/// Named bodies use body.RadiusMeters instead (Plan 01-02 RND-03/04).
		/// </summary>
		[Export] public float DefaultBodyRadius { get; set; } = 6.371e6f;   // Earth-radius fallback

		// ----- Star / emissive exports ----------------------------------------

		/// <summary>
		/// Emission energy multiplier for the star's unshaded emissive material.
		/// Values &gt;1 cause glow/bloom when the WorldEnvironment has GlowEnabled=true.
		/// Tune to taste; default provides strong bloom on approach.
		/// </summary>
		[Export] public float StarEmissionEnergy { get; set; } = 3.0f;

		/// <summary>
		/// Energy of the OmniLight3D created at the star's render position.
		/// Set equal to PlanetSunLightEnergy so Star-space and Planet-space lit-side
		/// brightness are comparable. No attenuation (StarLightAttenuation=0) means
		/// this value is the full, unattenuated energy reaching the planet surface.
		/// ShadowEnabled is always false (RND-04).
		/// </summary>
		[Export] public float StarLightEnergy { get; set; } = 1.8f;

		/// <summary>
		/// Range of the OmniLight3D in render units. Must cover the rendered scene.
		/// Scene spans up to ~CameraFarPlane render units; default 1e5 covers
		/// well within the 1e6 far plane.
		/// With StarLightAttenuation=0 the range acts only as a hard cutoff; all
		/// objects within range receive the full StarLightEnergy regardless of distance.
		/// </summary>
		[Export] public float StarLightRange { get; set; } = 1e5f;

		/// <summary>
		/// Attenuation exponent of the OmniLight3D (Godot OmniLight3D.OmniAttenuation).
		/// 0 = constant intensity up to StarLightRange (no distance falloff).
		/// 1 = linear falloff. Default Godot value = 1.
		///
		/// Set to 0 so the planet receives full StarLightEnergy regardless of its
		/// render-space distance from the star. At 1:1 scale the star renders at
		/// ~1500 render units and the planet at ~0–250 render units from the ship;
		/// any non-zero attenuation produces distance-dependent dimming that is hard
		/// to match against the (attenuaton-free) Planet-space DirectionalLight3D.
		/// With attenuation=0 the OmniLight is effectively directional in brightness
		/// while remaining POSITIONAL in direction — the terminator still points from
		/// the real star-mesh position.
		/// </summary>
		[Export] public float StarLightAttenuation { get; set; } = 0.0f;

		// ----- Planet-space directional sun exports ----------------------------

		/// <summary>
		/// Energy of the DirectionalLight3D activated when the ship is in Planet space.
		/// The directional light is oriented along the true sun direction (computed
		/// cross-frame from ship → planet → star hierarchy positions). No attenuation —
		/// correct at 1:1 distances. ShadowEnabled=false (RND-04 cost rule).
		/// Tune so the lit hemisphere is clearly bright against the dark night side.
		/// </summary>
		[Export] public float PlanetSunLightEnergy { get; set; } = 1.8f;

		// ----- Private state --------------------------------------------------

		private TestSetup _world;

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

		/// <summary>Per-body mesh instances: keyed by GameObjects index.</summary>
		private readonly Dictionary<int, MeshInstance3D> _meshes = [];

		/// <summary>
		/// Single OmniLight3D for the star. Created once and repositioned each frame
		/// to the star's ship-relative render position. Hidden when the star is not rendered.
		/// ShadowEnabled=false (RND-04 — no cast shadows, avoids shadow-map cost).
		/// </summary>
		private OmniLight3D _starLight;

		/// <summary>
		/// DirectionalLight3D that stands in for the sun when the ship is in Planet space.
		/// The star is not in the render set in Planet space (RND-02), so the OmniLight3D
		/// is absent. This light is oriented along the true sun direction each frame
		/// (derived from ship → planet → star cross-frame hierarchy math in Star-space meters).
		/// No attenuation (directional), ShadowEnabled=false (RND-04).
		/// Active only in Planet space; disabled in Star space where the OmniLight is used.
		/// </summary>
		private DirectionalLight3D _planetSunLight;

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

			// Create the star OmniLight3D once; it will be repositioned and shown/hidden per frame.
			// OmniAttenuation=0 gives constant brightness up to OmniRange — no distance falloff —
			// so lit-side energy is independent of render-unit distance and matches Planet-space
			// DirectionalLight3D brightness (cross-space shading consistency, D-16 fix).
			_starLight = new OmniLight3D
			{
				ShadowEnabled    = false,               // RND-04: no cast shadows
				OmniRange        = StarLightRange,
				OmniAttenuation  = StarLightAttenuation, // 0 = no falloff; terminator direction still from star position
				LightEnergy      = StarLightEnergy,
				Visible          = false,
			};
			AddChild(_starLight);

			// Create the planet-space directional sun light once; activated only in Planet space.
			// Orientation is recomputed every frame from the true cross-frame sun direction.
			_planetSunLight = new DirectionalLight3D
			{
				ShadowEnabled = false,    // RND-04: no cast shadows
				LightEnergy   = PlanetSunLightEnergy,
				Visible       = false,
			};
			AddChild(_planetSunLight);
		}

		public override void _Process(double delta)
		{
			if (_world == null) return;

			SyncBodies();
		}

		// ----- Core floating-origin sync -------------------------------------

		/// <summary>
		/// Repositions all in-parent-space body meshes relative to the ship each frame.
		/// Only bodies in the ship's current parent space are shown (RND-02).
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

			// Track which indices are rendered this frame to hide bodies that left
			var activeIndices = new HashSet<int>();

			// Star light hidden by default each frame; shown only when the star is rendered.
			bool starRendered = false;

			// Render the parent body itself (e.g. the planet we orbit) ship-relative.
			// The parent lives at the ORIGIN of the ship's frame, so its position relative
			// to the ship is simply the negation of the ship's own offset in that frame.
			RenderBodyAt(parentIdx, parent, ship, factor, isParent: true, out bool parentIsStar);
			activeIndices.Add(parentIdx);
			if (parentIsStar) starRendered = true;

			// Render siblings: all children of parent except the ship itself
			foreach (int childIdx in parent.ChildIndices)
			{
				if (childIdx == shipIndex) continue;

				var body = (uint)childIdx < (uint)gameObjects.Count ? gameObjects[childIdx] : null;
				if (body == null) continue;

				RenderBodyAt(childIdx, body, ship, factor, isParent: false, out bool siblingIsStar);
				activeIndices.Add(childIdx);
				if (siblingIsStar) starRendered = true;
			}

			// Hide any previously-shown bodies no longer in the current space
			foreach (var kvp in _meshes)
			{
				kvp.Value.Visible = activeIndices.Contains(kvp.Key);
			}

			// Keep star light in sync: only visible when the star mesh is rendered.
			// OmniAttenuation re-applied each frame so runtime Export changes take effect.
			_starLight.Visible          = starRendered;
			_starLight.OmniRange        = StarLightRange;
			_starLight.OmniAttenuation  = StarLightAttenuation;
			_starLight.LightEnergy      = StarLightEnergy;

			// Planet-space directional sun: active only when the star is NOT in the render set.
			// Mutually exclusive with the OmniLight — one or the other is active, never both.
			bool inPlanetSpace = ship.CurrentSpace == UniObject.Space.Planet;
			if (inPlanetSpace && !starRendered)
				SyncPlanetSunLight(ship, parent, gameObjects);
			else
				_planetSunLight.Visible = false;
		}

		/// <summary>
		/// Orients and activates the planet-space DirectionalLight3D each frame so it points
		/// along the true sun direction. Called only when the ship is in Planet space and the
		/// star is not in the current render set.
		///
		/// Direction math (all in Star-space meters, scale = 1 m/unit):
		///   planet_in_star = planet.LocalPos.ToDouble3()
		///   ship_in_star   = planet_in_star + ship.LocalPos.ToDouble3()
		///                    (ship.LocalPos is in Planet space, scale=1e-4 m/unit;
		///                     ToDouble3() returns meters = Units*1e-4 + Offset, then
		///                     we add to the planet's Star-frame meters)
		///   sun_direction_to_ship = normalize(ship_in_star - starPos)
		///                         = normalize(ship_in_star)   (star is at origin)
		///
		/// The DirectionalLight3D emits photons along its local -Z axis. The photons must
		/// travel FROM the sun (at +sun_direction) TOWARD the planet (at -sun_direction).
		/// Photon travel direction = -sun_direction_to_ship.
		/// LookAt(pos - sun_dir, up) orients -Z toward (pos - sun_dir) = -sun_dir,
		/// making photons travel in the -sun_dir direction, illuminating the hemisphere
		/// that faces the sun (normals pointing toward +sun_dir = toward the star).
		///
		/// The ship term (~2.5e7 m) is tiny compared to 1 AU (~1.496e11 m) but is included
		/// for correctness; it prevents the terminator from drifting as the ship orbits.
		/// </summary>
		private void SyncPlanetSunLight(UniObject ship, UniObject planet, List<UniObject> gameObjects)
		{
			// Walk up from planet to find the star.
			int starIdx = planet.ParentIndex;
			var star = (uint)starIdx < (uint)gameObjects.Count ? gameObjects[starIdx] : null;

			// Verify the grandparent is the star (named "STAR"); if hierarchy is unexpected, disable.
			if (star == null || !IsStarBody(star))
			{
				_planetSunLight.Visible = false;
				return;
			}

			// Planet position in Star frame (scale = 1 m/unit → ToDouble3() gives meters directly).
			Double3 planetInStar = planet.LocalPos.ToDouble3();

			// Ship position in Planet frame (scale = 1e-4 m/unit → ToDouble3() gives meters).
			// Adding to planetInStar gives the ship's position in Star-frame meters.
			Double3 shipInStar = planetInStar + ship.LocalPos.ToDouble3();

			// Direction from star (origin) toward ship. Normalize in double precision.
			double mag = shipInStar.Magnitude();
			if (mag < 1.0)
			{
				// Degenerate: ship at star origin — disable light rather than NaN.
				_planetSunLight.Visible = false;
				return;
			}

			Double3 dir = shipInStar * (1.0 / mag);   // unit vector from star toward ship/planet

			// Godot's DirectionalLight3D emits photons along its local -Z axis.
			// Photons must travel FROM the sun (at +dir) TOWARD the planet (at -dir),
			// so the photon travel direction = -dir.
			// LookAt(target, up) orients -Z toward target, so we point at (lightPos - dir),
			// making -Z = -dir = the correct photon direction (sun → planet).
			// This ensures dot(N, L_from_surface) > 0 for the hemisphere facing the sun.
			Vector3 sunDir    = new Vector3((float)dir.X, (float)dir.Y, (float)dir.Z);
			Vector3 lightPos  = Vector3.Zero;    // position irrelevant for DirectionalLight3D
			Vector3 lookTarget = lightPos - sunDir;   // -sunDir = photon travel direction

			// Avoid gimbal issues if sunDir is nearly parallel to Vector3.Up
			Vector3 up = (System.Math.Abs(dir.Y) > 0.99) ? Vector3.Back : Vector3.Up;
			_planetSunLight.LookAt(lookTarget, up);

			_planetSunLight.LightEnergy = PlanetSunLightEnergy;
			_planetSunLight.Visible     = true;
		}

		// ----- Private helpers -----------------------------------------------

		/// <summary>
		/// Determines whether the given body is the star. Identified by name so
		/// it works regardless of which CurrentSpace the ship is in.
		/// </summary>
		private static bool IsStarBody(UniObject body) => body.Name == "STAR";

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
		/// <paramref name="isStar"/>: set to true when this body is the star (for OmniLight sync).
		/// </summary>
		private void RenderBodyAt(
			int bodyIdx,
			UniObject body,
			UniObject ship,
			float factor,
			bool isParent,
			out bool isStar)
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
				// Floating-origin: meters → ÷ ship.LocalPos.Scale → observer units.
				relUnits = body.LocalPos.ToLocalDoubleUnits(ship.LocalPos);
			}

			// × factor → render units (consistent basis for positions and radii).
			var renderPos = new Vector3(
				(float)(relUnits.X * factor),
				(float)(relUnits.Y * factor),
				(float)(relUnits.Z * factor));

			mesh.Position = renderPos;
			mesh.Visible  = true;

			// If this is the star, move the OmniLight to the same render-space position.
			if (isStar)
				_starLight.Position = renderPos;
		}

		/// <summary>
		/// Returns the MeshInstance3D for this body index, creating it lazily on
		/// first encounter. Never spawns or frees per frame.
		///
		/// Mesh geometry: a UNIT sphere (Radius=1, Height=2) is created once. The rendered
		/// size is applied each frame by setting MeshInstance3D.Scale in RenderBodyAt so
		/// that the radius correctly reflects the current observer scale and render factor
		/// after every SOI transition (fixes stale-radius bug where baking the radius at
		/// creation time caused bodies to stay at their creation-frame size across transitions).
		///
		/// Radius computation (per-frame in RenderBodyAt):
		///   rawRadiusMeters / ship.LocalPos.Scale  →  observer units
		///   observer units × factor                 →  render units (applied as mesh.Scale)
		/// Falls back to DefaultBodyRadius for bodies with no authored radius.
		///
		/// Material assignment (RND-03/04):
		/// - Star: ShadingMode=Unshaded + EmissionEnabled + EmissionEnergyMultiplier=StarEmissionEnergy.
		///   No lighting needed — it IS the light source.
		/// - Planets/other: default-lit StandardMaterial3D with AlbedoColor=body.BaseColor.
		///   In Star space: receives the OmniLight3D terminator (D-16) from the star mesh position.
		///   In Planet space: receives the DirectionalLight3D (_planetSunLight) oriented along the
		///   true cross-frame sun direction, producing a day/night terminator (01-02 revision).
		///   The sun is NOT rendered as a mesh in Planet space — at 1 AU the sun is ~1.5e7 render
		///   units, well beyond the 1e6 far plane. Visible sun mesh in Planet space is deferred to
		///   the Phase 3 tiered/skybox renderer.
		///   A small ambient floor (Main.tscn) keeps the night hemisphere dark-but-visible.
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

			// Build per-body material.
			StandardMaterial3D mat;
			if (IsStarBody(body))
			{
				// Emissive unshaded star (D-14). No shading pass needed — it is the source.
				mat = new StandardMaterial3D
				{
					ShadingMode             = BaseMaterial3D.ShadingModeEnum.Unshaded,
					AlbedoColor             = body.BaseColor,
					EmissionEnabled         = true,
					Emission                = body.BaseColor,
					EmissionEnergyMultiplier = StarEmissionEnergy,
				};
			}
			else
			{
				// Lit planet: receives OmniLight (Star space) or DirectionalLight3D (Planet space).
				// In Planet space the DirectionalLight3D is oriented along the real sun direction
				// each frame; a small ambient floor (Main.tscn) keeps the night side dark-but-visible.
				mat = new StandardMaterial3D
				{
					AlbedoColor = body.BaseColor.IsEqualApprox(default) ? new Color(0.75f, 0.85f, 0.75f) : body.BaseColor,
				};
			}

			var meshInstance = new MeshInstance3D
			{
				Mesh             = sphereMesh,
				MaterialOverride = mat,
				Visible          = false,
			};

			AddChild(meshInstance);
			_meshes[bodyIdx] = meshInstance;

			return meshInstance;
		}
	}
}
