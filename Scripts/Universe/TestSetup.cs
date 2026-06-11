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
		private const double ShipSpeedMetersPerTick = 1e8;

		// ----- Godot callbacks ----------------------------------------------
		private bool _arrived = false;


		public override void _Ready()
		{
			base._Ready();
			SetupScene();
			PrintState("Initial state");
		}

		private int _cellOffset = 0;
		private double _cellOffsetTimer = 0;
		private double _cellOffsetTimeout = 0.01;

		public override void _Process(double delta)
		{
			base._Process(delta);
			if (_arrived) return;

			var ship = GameObjects[_ship];
			var planetB = GameObjects[_planetB];

			TranslatePos(_ship, new Double3(0, 0, ShipSpeedMetersPerTick));

			UniVec3 shipInStar = ship.LocalPos;
			int p = ship.ParentIndex;
			while (p >= 0 && GameObjects[p].CurrentSpace != UniObject.Space.Galaxy)
			{
				shipInStar = ChildPosToParentSpace(shipInStar, GameObjects[p]);
				p = GameObjects[p].ParentIndex;
			}
			double distToB = UniVec3.Distance(shipInStar, planetB.LocalPos);

			GD.Print($"[Ship] space={ship.CurrentSpace,-10}  parentIdx={ship.ParentIndex,2}  " +
					 $"shipPos={ship.LocalPos:e5}  " +
					 $"distToB={distToB:e5} m");

			if (ship.ParentIndex == _planetB)
			{
				_arrived = true;
				PrintState("Arrived at Planet B");
			}
		}

		// ----- Scene setup --------------------------------------------------

		private void SetupScene()
		{
			_root = AddGameObject(-1, new Double3(0, 0, 0), double.MaxValue);
			_galaxy = AddGameObject(_root, new Double3(0, 0, 0), 5e3);
			_star = AddGameObject(_galaxy, new Double3(0, 0, 0), StarSOI);
			_planetA = AddGameObject(_star, new Double3(0, 0, PlanetA_Z), PlanetSOI);
			_planetB = AddGameObject(_star, new Double3(0, 0, PlanetB_Z), PlanetSOI);
			_ship = AddGameObject(_planetA, new Double3(0, 0, ShipOrbitMeters), 0);
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
