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
		/// Plan 01-02 MUST reuse the meters→observer-units→×factor transform for per-body radii.
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

		/// <summary>Default sphere mesh radius (true meters) for skeleton bodies.</summary>
		[Export] public float DefaultBodyRadius { get; set; } = 6.371e6f;   // Earth-radius default

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

			// Render the parent body itself (e.g. the planet we orbit) ship-relative.
			// The parent lives at the ORIGIN of the ship's frame, so its position relative
			// to the ship is simply the negation of the ship's own offset in that frame.
			RenderBodyAt(parentIdx, parent, ship, factor, isParent: true);
			activeIndices.Add(parentIdx);

			// Render siblings: all children of parent except the ship itself
			foreach (int childIdx in parent.ChildIndices)
			{
				if (childIdx == shipIndex) continue;

				var body = (uint)childIdx < (uint)gameObjects.Count ? gameObjects[childIdx] : null;
				if (body == null) continue;

				RenderBodyAt(childIdx, body, ship, factor, isParent: false);
				activeIndices.Add(childIdx);
			}

			// Hide any previously-shown bodies no longer in the current space
			foreach (var kvp in _meshes)
			{
				kvp.Value.Visible = activeIndices.Contains(kvp.Key);
			}
		}

		// ----- Private helpers -----------------------------------------------

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
		/// </summary>
		private void RenderBodyAt(
			int bodyIdx,
			UniObject body,
			UniObject ship,
			float factor,
			bool isParent)
		{
			var mesh = GetOrCreateMesh(bodyIdx, ship, factor);

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
			mesh.Position = new Vector3(
				(float)(relUnits.X * factor),
				(float)(relUnits.Y * factor),
				(float)(relUnits.Z * factor));
			mesh.Visible = true;
		}

		/// <summary>
		/// Returns the MeshInstance3D for this body index, creating it lazily on
		/// first encounter. Never spawns or frees per frame.
		/// Radius transform: DefaultBodyRadius (meters) → ÷ ship.LocalPos.Scale (observer units) → × factor (render units).
		/// Plan 01-02 MUST apply this same meters→observer-units→×factor transform for true per-body radii (RND-03/04).
		/// </summary>
		private MeshInstance3D GetOrCreateMesh(int bodyIdx, UniObject ship, float factor)
		{
			if (_meshes.TryGetValue(bodyIdx, out var existing))
				return existing;

			// Radius: true meters → observer units (÷ ship scale) → render units (× factor).
			// DefaultBodyRadius stays in true meters; plan 01-02 replaces it with per-body RadiusMeters.
			double radiusUnits = DefaultBodyRadius / ship.LocalPos.Scale;
			float r = (float)(radiusUnits * factor);
			var sphereMesh = new SphereMesh
			{
				Radius = r,
				Height = 2f * r,
			};

			// Skeleton placeholder material: unshaded so bodies stay visible with no
			// scene light. Per-body albedo/emissive + 1:1 radii are elaborated in Plan 02 (RND-03/04).
			var mat = new StandardMaterial3D
			{
				ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
				AlbedoColor = new Color(0.75f, 0.85f, 0.75f),   // light gray-green, clearly visible
			};

			var meshInstance = new MeshInstance3D
			{
				Mesh = sphereMesh,
				MaterialOverride = mat,
				Visible = false,
			};

			AddChild(meshInstance);
			_meshes[bodyIdx] = meshInstance;

			return meshInstance;
		}
	}
}
