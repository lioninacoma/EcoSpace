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
	///   star OmniLight produces a day/night terminator.
	/// - Star mesh: unshaded emissive StandardMaterial3D (no lighting needed).
	/// - OmniLight3D positioned at the star's ship-relative render transform each frame
	///   (ShadowEnabled=false per RND-04). Its range is in render units and must cover
	///   the full rendered scene; tune StarLightRange accordingly.
	///
	/// Ambient lighting note: when the ship is in Planet space the star is NOT in the
	/// render set (only parent.ChildIndices bodies are shown — RND-02). The OmniLight3D
	/// is therefore absent from the frame, and a purely lit planet material would render
	/// black. A small ambient light floor on the Environment (Main.tscn) ensures planets
	/// are never fully black while keeping the day/night terminator visible when the
	/// OmniLight IS in frame (Star space). This is the MVP stand-in; true cross-space
	/// directional lighting is not implemented in this phase.
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
		/// Tune so lit-side planets are well-lit and the terminator is distinct.
		/// ShadowEnabled is always false (RND-04).
		/// </summary>
		[Export] public float StarLightEnergy { get; set; } = 2.0f;

		/// <summary>
		/// Range of the OmniLight3D in render units. Must cover the rendered scene.
		/// Scene spans up to ~CameraFarPlane render units; default 1e5 covers
		/// well within the 1e6 far plane. The light uses quadratic attenuation by
		/// default — tune so the planet (hundreds of render units away) is adequately lit.
		/// PITFALL: attenuation at long render-unit distances can make planets dim;
		/// increase range or reduce attenuation if the terminator is too subtle.
		/// </summary>
		[Export] public float StarLightRange { get; set; } = 1e5f;

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
			_starLight = new OmniLight3D
			{
				ShadowEnabled = false,    // RND-04: no cast shadows
				OmniRange     = StarLightRange,
				LightEnergy   = StarLightEnergy,
				Visible       = false,
			};
			AddChild(_starLight);
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

			// Keep star light in sync: only visible when the star mesh is rendered
			_starLight.Visible      = starRendered;
			_starLight.OmniRange    = StarLightRange;
			_starLight.LightEnergy  = StarLightEnergy;
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
			var mesh = GetOrCreateMesh(bodyIdx, body, ship, factor);

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
		/// Radius transform: body.RadiusMeters (true m) → ÷ ship.LocalPos.Scale (observer units)
		/// → × factor (render units). Falls back to DefaultBodyRadius for bodies with no
		/// authored radius (Root, Galaxy — not rendered as bodies in this phase).
		///
		/// Material assignment (RND-03/04):
		/// - Star: ShadingMode=Unshaded + EmissionEnabled + EmissionEnergyMultiplier=StarEmissionEnergy.
		///   No lighting needed — it IS the light source.
		/// - Planets/other: default-lit StandardMaterial3D with AlbedoColor=body.BaseColor.
		///   Receives the OmniLight terminator (D-16). Note: when in Planet space the OmniLight
		///   is not in frame; the Environment ambient floor (set in Main.tscn) keeps these
		///   lit enough to be visible (MVP stand-in for cross-space lighting).
		/// </summary>
		private MeshInstance3D GetOrCreateMesh(int bodyIdx, UniObject body, UniObject ship, float factor)
		{
			if (_meshes.TryGetValue(bodyIdx, out var existing))
				return existing;

			// Radius: true meters → observer units (÷ ship scale) → render units (× factor).
			double rawRadiusMeters = body.RadiusMeters > 0.0 ? body.RadiusMeters : DefaultBodyRadius;
			double radiusUnits     = rawRadiusMeters / ship.LocalPos.Scale;
			float  r               = (float)(radiusUnits * factor);

			var sphereMesh = new SphereMesh
			{
				Radius = r,
				Height = 2f * r,
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
				// Lit planet: receives the OmniLight terminator (D-16).
				// When in Planet space no OmniLight is in frame; the Environment ambient
				// floor (Main.tscn) ensures the planet is not fully black.
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
