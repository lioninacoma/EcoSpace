using Godot;

namespace Hud
{
	/// <summary>
	/// Phosphor-green live-speed HUD label (HUD-01 partial).
	///
	/// Read-only consumer of GameWorld state — MUST NOT mutate sim state.
	/// Reads the ship's speed each frame as the per-frame positional delta magnitude
	/// divided by delta time, and writes it to a child Label in m/s.
	/// </summary>
	public partial class Hud : Control
	{
		// ----- Exports --------------------------------------------------------

		/// <summary>NodePath to the GameWorld / TestSetup node in the scene.</summary>
		[Export] public NodePath WorldPath { get; set; }

		/// <summary>Phosphor-green CRT color for the speed label (D-09).</summary>
		[Export] public Color PhosphorGreen { get; set; } = new Color(0.1f, 1.0f, 0.3f);

		// ----- Private state --------------------------------------------------

		private TestSetup _world;
		private Label _speedLabel;
		private UniVec3 _prevPos;
		private bool _prevPosValid = false;

		// ----- Godot callbacks ------------------------------------------------

		public override void _Ready()
		{
			// Resolve world reference
			if (WorldPath != null && !WorldPath.IsEmpty)
				_world = GetNode<TestSetup>(WorldPath);
			else
				_world = GetTree().Root.FindChild("Main", true, false) as TestSetup;

			// Resolve child label
			_speedLabel = GetNodeOrNull<Label>("SpeedLabel");
			if (_speedLabel == null)
			{
				// Fallback: look for sibling SpeedLabel under the same parent
				_speedLabel = GetParent()?.FindChild("SpeedLabel", false, false) as Label;
			}

			// Apply phosphor-green color to label
			if (_speedLabel != null)
				_speedLabel.Modulate = PhosphorGreen;
		}

		public override void _Process(double delta)
		{
			if (_world == null || _speedLabel == null) return;
			if (delta <= 0.0) return;

			var gameObjects = _world.GameObjects;
			int shipIndex = _world.ShipIndex;

			if ((uint)shipIndex >= (uint)(gameObjects?.Count ?? 0)) return;

			var ship = gameObjects[shipIndex];
			if (ship == null) return;

			// Compute speed as per-frame positional change magnitude / delta
			double speedMs = 0.0;
			if (_prevPosValid)
			{
				// Both positions are in the same space during a stable frame;
				// use ToLocalDouble to get the delta in meters.
				Double3 deltaPos = ship.LocalPos.ToLocalDouble(_prevPos);
				double distMeters = System.Math.Sqrt(
					deltaPos.X * deltaPos.X +
					deltaPos.Y * deltaPos.Y +
					deltaPos.Z * deltaPos.Z);
				speedMs = distMeters / delta;
			}

			_prevPos = ship.LocalPos;
			_prevPosValid = true;

			// Display raw m/s — adaptive units arrive in Plan 04
			_speedLabel.Text = $"Speed: {speedMs:F0} m/s";
		}
	}
}
