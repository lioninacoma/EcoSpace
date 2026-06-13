using Godot;
using Universe.Math;

namespace Universe
{
	/// <summary>
	/// Hierarchy:
	///   Root    (index 0)
	///   └── Galaxy          (index 1) – Universe space, scale 1e16 m/unit
	///       └── Star        (index 2) – Galaxy space,   scale 1e4  m/unit
	///           ├── PlanetA (index 3) – Star space,     scale 1    m/unit
	///           ├── PlanetB (index 4) – Star space,     scale 1    m/unit
	///           └── Ship    (index 5) – Planet space,   scale 1e-4 m/unit
	///
	/// Real-world distances used (all in metres = Star-space units):
	///   Planet A (Earth-like)  z = 1.496e11 m  (1.00 AU)
	///   Planet B (Mars-like)   z = 2.279e11 m  (1.52 AU)
	///   Star SOI               = 1.5e15 m      (~10 000 AU, beyond Oort cloud)
	///   Planet SOI             = 1.0e9  m      (~1 000 000 km, Earth Hill-sphere)
	/// </summary>
	public partial class TestSetup : GameWorld
	{
		// ----- Object indices -----------------------------------------------
		private int _root;
		private int _galaxy;
		private int _star;
		private int _planetA;
		private int _planetB;
		private int _ship;

		// ----- Orbital parameters -------------------------------------------
		private const double PlanetA_Z = 1.496e11;           // 1.00 AU in metres (Earth)
		private const double PlanetB_Z = 2.279e11;           // 1.52 AU in metres (Mars)
		private const double StarSOI = 1.5e15;               // ~10 000 AU
		private const double PlanetSOI = 1.0e9;              // ~1 000 000 km
		private const double ShipOrbitMeters = 7e6;

		// ----- Skeleton thrust parameters -----------------------------------

		/// <summary>
		/// Placeholder thrust speed in m/s for the walking skeleton.
		/// Exposed as export for in-editor tuning. True context-scaled speed arrives in Plan 02.
		/// </summary>
		[Export] public double SkeletonSpeed { get; set; } = 1e8;   // 1e8 m/s skeleton placeholder

		// ----- Public accessors for RenderBridge / HUD ----------------------

		/// <summary>Index of the player ship in GameObjects. Read by RenderBridge and Hud.</summary>
		public int ShipIndex => _ship;

		// ----- Godot callbacks ----------------------------------------------

		public override void _Ready()
		{
			base._Ready();
			SetupScene();
			PrintState("Initial state");
		}

		public override void _Process(double delta)
		{
			base._Process(delta);

			// Read thrust input from InputMap actions (project.godot defines these)
			float thrustAxis = Input.GetActionStrength("thrust_forward") -
							   Input.GetActionStrength("thrust_back");

			if (thrustAxis != 0f)
			{
				// Placeholder forward motion in ship-local +Z.
				// True attitude-oriented motion (Basis multiply) arrives in Plan 02.
				double thrust = thrustAxis * SkeletonSpeed * delta;
				TranslatePos(_ship, new Double3(0, 0, thrust));
			}
		}

		// ----- Scene setup --------------------------------------------------

		private void SetupScene()
		{
			_root    = AddGameObject(-1,       new Double3(0, 0, 0),         double.MaxValue);
			_galaxy  = AddGameObject(_root,    new Double3(0, 0, 0),         5e3);
			_star    = AddGameObject(_galaxy,  new Double3(0, 0, 0),         StarSOI);
			_planetA = AddGameObject(_star,    new Double3(0, 0, PlanetA_Z), PlanetSOI);
			_planetB = AddGameObject(_star,    new Double3(0, 0, PlanetB_Z), PlanetSOI);
			_ship    = AddGameObject(_planetA, new Double3(0, 0, ShipOrbitMeters), 0);
		}

		// ----- Debug --------------------------------------------------------

		private void PrintState(string label)
		{
			GD.Print($"\n=== {label} ===");
			for (int i = 0; i < GameObjects.Count; i++)
			{
				var o = GameObjects[i];
				if (o == null) continue;
				GD.Print($"  [{i}] {o.CurrentSpace,-10}  parent={o.ParentIndex,2}  " +
						 $"children=[{string.Join(",", o.ChildIndices)}]  " +
						 $"pos={o.LocalPos}");
			}
			GD.Print("");
		}
	}
}
