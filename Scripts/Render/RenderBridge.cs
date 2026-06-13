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

		/// <summary>Far plane for the Camera3D (render units). 1e6 render units = 1e12 m at k=1e-6.</summary>
		[Export] public float CameraFarPlane { get; set; } = 1e6f;

		/// <summary>
		/// Render units per meter (uniform scale applied to both body positions and radii).
		/// Universe math (UniVec3, TranslatePos, SOI) operates in true 1:1 meters; only
		/// RenderBridge applies this factor so the camera far plane stays reasonable (≤ 1e6
		/// render units). A uniform scale is perspective-invariant — visually identical to 1:1.
		/// Plan 01-02 MUST reuse this same RenderScale when applying true 1:1 body radii.
		/// Default: 1e-6 → 1 render unit = 1,000,000 m; far=1e6 covers ~1e12 m (≥ 1 AU).
		/// </summary>
		[Export] public float RenderScale { get; set; } = 1e-6f;

		/// <summary>Default sphere mesh radius (true meters) for skeleton bodies.</summary>
		[Export] public float DefaultBodyRadius { get; set; } = 6.371e6f;   // Earth-radius default

		// ----- Private state --------------------------------------------------

		private TestSetup _world;

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

			// Track which indices are rendered this frame to hide bodies that left
			var activeIndices = new HashSet<int>();

			// Render the parent body itself (e.g. the planet we orbit) ship-relative
			RenderBodyAt(parentIdx, parent, ship, gameObjects);
			activeIndices.Add(parentIdx);

			// Render siblings: all children of parent except the ship itself
			foreach (int childIdx in parent.ChildIndices)
			{
				if (childIdx == shipIndex) continue;

				var body = (uint)childIdx < (uint)gameObjects.Count ? gameObjects[childIdx] : null;
				if (body == null) continue;

				RenderBodyAt(childIdx, body, ship, gameObjects);
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
		/// Ensures a MeshInstance3D exists for <paramref name="bodyIdx"/>, then
		/// positions it ship-relative using the floating-origin ToLocalDouble.
		/// Anchors on ship.LocalPos (Pitfall 4 — never parent body, or render jitters).
		/// </summary>
		private void RenderBodyAt(
			int bodyIdx,
			UniObject body,
			UniObject ship,
			System.Collections.Generic.List<UniObject> gameObjects)
		{
			var mesh = GetOrCreateMesh(bodyIdx, body);

			// Floating-origin: compute render position relative to ship, then scale to render space.
			// RenderScale (k=1e-6) is applied uniformly — perspective-invariant, keeps camera far ≤ 1e6.
			Double3 rel = body.LocalPos.ToLocalDouble(ship.LocalPos) * RenderScale;
			mesh.Position = new Vector3((float)rel.X, (float)rel.Y, (float)rel.Z);
			mesh.Visible = true;
		}

		/// <summary>
		/// Returns the MeshInstance3D for this body index, creating it lazily on
		/// first encounter. Never spawns or frees per frame.
		/// </summary>
		private MeshInstance3D GetOrCreateMesh(int bodyIdx, UniObject body)
		{
			if (_meshes.TryGetValue(bodyIdx, out var existing))
				return existing;

			// Radius stored in true meters; scaled to render space by RenderScale.
			// Plan 01-02 replaces DefaultBodyRadius with per-body true radii using the same factor.
			var sphereMesh = new SphereMesh
			{
				Radius = DefaultBodyRadius * RenderScale,
				Height = DefaultBodyRadius * 2f * RenderScale,
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
