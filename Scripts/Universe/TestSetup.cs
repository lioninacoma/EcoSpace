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
		// Skeleton VIEWING distance: chosen so planet renders as a clear disc in the 75° FOV,
		// not a screen-filling surface. At 2.5e7 m the planet subtends ~29° (radius 637 render
		// units, ship at 2500 render units) — well inside PlanetSOI (1e9 m) so SOI transitions
		// are unaffected. True orbital distances arrive in Plan 01-02 (RND-03/04).
		private const double ShipOrbitMeters = 2.5e7;

		// ----- Per-body presentation data (D-13 authored hues, D-15 true 1:1 radii) ------
		// Radii in true metres (Star-space units); consumed by WorldRenderer + Plan 03 FLT-03.
		private const double PlanetA_RadiusMeters = 6.371e6;  // Earth equatorial radius
		private const double PlanetB_RadiusMeters = 3.390e6;  // Mars mean radius
		private const double Star_RadiusMeters    = 6.960e8;  // Solar radius

		// Authored hues: distinct luminance AND hue so they survive the dither (Pitfall 6).
		// Earth-blue: bright enough to not collapse to black in quantize.
		private static readonly Color PlanetA_Color = new Color(0.25f, 0.50f, 0.95f);
		// Mars-rust: warm orange-red, clearly different from blue and yellow.
		private static readonly Color PlanetB_Color = new Color(0.80f, 0.35f, 0.20f);
		// Solar yellow: very bright so it reads as emissive and blooms.
		private static readonly Color Star_Color    = new Color(1.00f, 0.95f, 0.60f);

		// ----- Public accessors for WorldRenderer / FlightController / HUD ----

		/// <summary>Index of the player ship in GameObjects. Read by WorldRenderer, FlightController, and Hud.</summary>
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
			// FlightController owns ship motion (Plan 03). TestSetup no longer drives
			// placeholder thrust here — the skeleton's SkeletonSpeed / thrust_forward
			// / thrust_back inputs have been replaced by FlightController.
			base._Process(delta);
		}

		// ----- Scene setup --------------------------------------------------

		private void SetupScene()
		{
			_root    = AddGameObject(-1,       new Double3(0, 0, 0),         double.MaxValue);
			_galaxy  = AddGameObject(_root,    new Double3(0, 0, 0),         5e3);

			_star    = AddGameObject(_galaxy,  new Double3(0, 0, 0),         StarSOI);
			GameObjects[_star].Name         = "STAR";
			GameObjects[_star].BaseColor    = Star_Color;
			GameObjects[_star].RadiusMeters = Star_RadiusMeters;

			_planetA = AddGameObject(_star,    new Double3(0, 0, PlanetA_Z), PlanetSOI);
			GameObjects[_planetA].Name         = "PLANET A";
			GameObjects[_planetA].BaseColor    = PlanetA_Color;
			GameObjects[_planetA].RadiusMeters = PlanetA_RadiusMeters;

			_planetB = AddGameObject(_star,    new Double3(0, 0, PlanetB_Z), PlanetSOI);
			GameObjects[_planetB].Name         = "PLANET B";
			GameObjects[_planetB].BaseColor    = PlanetB_Color;
			GameObjects[_planetB].RadiusMeters = PlanetB_RadiusMeters;

			_ship    = AddGameObject(_planetA, new Double3(0, 0, ShipOrbitMeters), 0);
			GameObjects[_ship].Name = "SHIP";
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
