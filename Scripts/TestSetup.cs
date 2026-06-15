using Godot;

/// <summary>
/// Hierarchy:
///   Root    (index 0)
///   └── Galaxy          (index 1) – Universe space, scale 1e16 m/unit
///       ├── Star        (index 2) – Galaxy space,   scale 1e4  m/unit  (home star)
///       │   ├── PlanetA (index 3) – Star space,     scale 1    m/unit
///       │   ├── PlanetB (index 4) – Star space,     scale 1    m/unit
///       │   └── Ship    (index 5) – Planet space,   scale 1e-4 m/unit
///       ├── Sibling1    (index 6) – Galaxy space    (Alpha Cen-like, 4.2 ly)
///       ├── Sibling2    (index 7) – Galaxy space    (Barnard's-like, 5.96 ly, dim M-dwarf)
///       └── Sibling3    (index 8) – Galaxy space    (Sirius-like, 8.6 ly, bright A-type)
///
/// Real-world distances used (all in metres = Star-space units):
///   Planet A (Earth-like)  z = 1.496e11 m  (1.00 AU)
///   Planet B (Mars-like)   z = 2.279e11 m  (1.52 AU)
///   Star SOI               = 1.5e15 m      (~10 000 AU, beyond Oort cloud)
///   Planet SOI             = 1.0e9  m      (~1 000 000 km, Earth Hill-sphere)
///
/// Sibling star distances in Galaxy space (scale = 1e4 m/unit):
///   4.2  ly = 3.97e16 m → 3.97e12 Galaxy units  (Alpha Cen-like)
///   5.96 ly = 5.63e16 m → 5.63e12 Galaxy units  (Barnard's-like)
///   8.6  ly = 8.13e16 m → 8.13e12 Galaxy units  (Sirius-like, offset in X and Z)
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

    // ----- Sibling star systems (D-23, Phase 2 skybox test data) --------
    // Galaxy space: 1 unit = 10 000 m → interstellar distances in Galaxy units.
    // Sibling1: Alpha Cen A-like (4.2 ly ≈ 3.97e16 m → 3.97e12 Galaxy units)
    // Warm G-type, slightly brighter than the Sun.
    private const double Sibling1_GalX  = 3.97e12;
    private const double Sibling1_Luminosity = 1.519;
    private static readonly Color Sibling1_Color = new Color(1.0f, 0.92f, 0.70f);

    // Sibling2: Barnard's Star-like (5.96 ly ≈ 5.63e16 m → 5.63e12 Galaxy units)
    // Dim M-dwarf red star — tests the minimum-brightness floor (D-19).
    private const double Sibling2_GalX  = 5.63e12;
    private const double Sibling2_Luminosity = 0.0035;
    private static readonly Color Sibling2_Color = new Color(1.0f, 0.30f, 0.15f);

    // Sibling3: Sirius-like (8.6 ly, bright blue-white A-type)
    // Positioned at an angle so all three siblings appear in different sky directions.
    private const double Sibling3_GalX  = -6.0e12;
    private const double Sibling3_GalZ  =  5.6e12;
    private const double Sibling3_Luminosity = 25.4;
    private static readonly Color Sibling3_Color = new Color(0.70f, 0.85f, 1.0f);

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
        GameObjects[_star].Luminosity   = 1.0;  // solar luminosity (baseline, D-26)

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

        // ----- Sibling star systems (D-23, Phase 2 skybox test data) ----
        // These live directly under _galaxy in Galaxy space.
        // TierClassifier will classify them as NextTierSkybox when the ship is in Star or Planet space.

        // Sibling 1: Alpha Cen A-like — warm G-type at 4.2 ly
        int _sib1 = AddGameObject(_galaxy, new Double3(Sibling1_GalX, 0, 0), StarSOI);
        GameObjects[_sib1].Name         = "ALPHA CEN";
        GameObjects[_sib1].BaseColor    = Sibling1_Color;
        GameObjects[_sib1].RadiusMeters = Star_RadiusMeters;
        GameObjects[_sib1].Luminosity   = Sibling1_Luminosity;

        // Sibling 2: Barnard's Star-like — dim M-dwarf at 5.96 ly
        int _sib2 = AddGameObject(_galaxy, new Double3(Sibling2_GalX, 0, 0), StarSOI);
        GameObjects[_sib2].Name         = "BARNARD";
        GameObjects[_sib2].BaseColor    = Sibling2_Color;
        GameObjects[_sib2].RadiusMeters = Star_RadiusMeters;
        GameObjects[_sib2].Luminosity   = Sibling2_Luminosity;

        // Sibling 3: Sirius-like — very bright blue-white A-type at 8.6 ly, offset angle
        int _sib3 = AddGameObject(_galaxy, new Double3(Sibling3_GalX, 0, Sibling3_GalZ), StarSOI);
        GameObjects[_sib3].Name         = "SIRIUS";
        GameObjects[_sib3].BaseColor    = Sibling3_Color;
        GameObjects[_sib3].RadiusMeters = Star_RadiusMeters;
        GameObjects[_sib3].Luminosity   = Sibling3_Luminosity;
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
