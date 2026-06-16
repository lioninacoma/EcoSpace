using Godot;

/// <summary>
/// Hierarchy:
///   Root    (index 0)
///   ├── HOME GALAXY     (index 1) – Universe space, scale 1e16 m/unit  [spiral]
///   │   ├── Star        (index 2) – Galaxy space,   scale 1e4  m/unit  (home star)
///   │   │   ├── PlanetA (index 3) – Star space,     scale 1    m/unit
///   │   │   ├── PlanetB (index 4) – Star space,     scale 1    m/unit
///   │   │   └── Ship    (index 5) – Planet space,   scale 1e-4 m/unit
///   │   ├── Sibling1    (index 6) – Galaxy space    (Alpha Cen-like, 4.2 ly)
///   │   ├── Sibling2    (index 7) – Galaxy space    (Barnard's-like, 5.96 ly, dim M-dwarf)
///   │   └── Sibling3    (index 8) – Galaxy space    (Sirius-like, 8.6 ly, bright A-type)
///   ├── DEST GALAXY     (index 9) – Universe space   [spiral mirror, ~Andromeda at 2.4e22 m]
///   │   ├── Dest Star   (index 10) – Galaxy space
///   │   │   ├── Dest PlanetA (index 11) – Star space
///   │   │   └── Dest PlanetB (index 12) – Star space
///   │   ├── Dest Sib1   (index 13) – Galaxy space
///   │   └── Dest Sib2   (index 14) – Galaxy space
///   └── ELLIPTICAL CLUSTER (index 15) – Universe space [elliptical, ~1.8e22 m at 45°]
///       ├── Cluster Sib1 (index 16) – Galaxy space
///       ├── Cluster Sib2 (index 17) – Galaxy space
///       └── Cluster Sib3 (index 18) – Galaxy space
///
/// Real-world distances used (all in metres = Star-space units):
///   Planet A (Earth-like)  z = 1.496e11 m  (1.00 AU)
///   Planet B (Mars-like)   z = 2.279e11 m  (1.52 AU)
///   Star SOI               = 1.5e15 m      (~10 000 AU, beyond Oort cloud)
///   Planet SOI             = 1.0e9  m      (~1 000 000 km, Earth Hill-sphere)
///   Galaxy SOI             = 5e4 Universe units = 5e20 m (~50 kly radius) (D-34)
///
/// Sibling star distances in Galaxy space (scale = 1e4 m/unit):
///   4.2  ly = 3.97e16 m → 3.97e12 Galaxy units  (Alpha Cen-like)
///   5.96 ly = 5.63e16 m → 5.63e12 Galaxy units  (Barnard's-like)
///   8.6  ly = 8.13e16 m → 8.13e12 Galaxy units  (Sirius-like, offset in X and Z)
///
/// Galaxy positions in Universe space (scale = 1e16 m/unit):
///   DEST GALAXY:         z = 2.4e6 Universe units = 2.4e22 m (~Andromeda scale, D-34)
///   ELLIPTICAL CLUSTER:  x = z = 1.27e6 Universe units → 1.8e22 m at 45° (D-41)
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

    // ----- Galaxy scale constants (D-34) ─────────────────────────────────────────────
    // IMPORTANT: AddGameObject takes its Double3 position and soiMeters in METRES (the
    // UniVec3 ctor stores them as the metres Offset, then Normalize() splits into Universe
    // Units at 1e16 m/unit). These MUST be metres, NOT Universe units — authoring them as
    // units (e.g. 2.4e6) places the galaxies ~1e16× too close, on top of the home system.
    // Long3 range ~9.2e18 Universe units — 2.4e22 m = 2.4e6 units is well within range.
    private const double GalaxySOI           = 5e20;   // metres (~50 kly radius = 5e4 Universe units)
    private const double Galaxy_RadiusMeters = 5e20;   // physical radius for speed envelope (D-36/Pitfall 3)

    // Galaxy 2: destination mirror spiral (~Andromeda), +Z from home galaxy (D-34).
    private const double Galaxy2_Z           = 2.4e22; // metres (= 2.4e6 Universe units)

    // Galaxy 3: elliptical cluster, ~1.8e22 m at 45° from Galaxy 2 for a distinct sky direction.
    // √(1.27e22² + 1.27e22²) ≈ 1.8e22 m (D-41).
    private const double Galaxy3_X           = 1.27e22; // metres
    private const double Galaxy3_Z           = 1.27e22; // metres

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
        _root    = AddGameObject(-1, new Double3(0, 0, 0), double.MaxValue);

        // ── HOME GALAXY (spiral, at origin — Universe space under Root) ───────────────
        // SOI replaces the 5e3 placeholder with the real galaxy-scale value (D-34/Pitfall 2).
        _galaxy  = AddGameObject(_root, new Double3(0, 0, 0), GalaxySOI);
        GameObjects[_galaxy].Name              = "HOME GALAXY";
        GameObjects[_galaxy].ObjectType        = UniObject.Type.Galaxy;
        GameObjects[_galaxy].RadiusMeters      = Galaxy_RadiusMeters;
        GameObjects[_galaxy].Luminosity        = 1e10;  // galaxy-scale luminosity (D-26 / RESEARCH Q3)
        GameObjects[_galaxy].BaseColor         = new Color(0.7f, 0.75f, 1.0f);  // cool blue-white
        GameObjects[_galaxy].GalaxyType        = 0;     // spiral (D-29)
        GameObjects[_galaxy].GalaxySeed        = 0.42f;
        GameObjects[_galaxy].GalaxyOrientation = new Vector3(0f, 1f, 0f); // disc normal → XZ plane

        // ── Home star system ─────────────────────────────────────────────────────────
        _star    = AddGameObject(_galaxy, new Double3(0, 0, 0), StarSOI);
        GameObjects[_star].Name         = "STAR";
        GameObjects[_star].ObjectType   = UniObject.Type.Star;
        GameObjects[_star].BaseColor    = Star_Color;
        GameObjects[_star].RadiusMeters = Star_RadiusMeters;
        GameObjects[_star].Luminosity   = 1.0;  // solar luminosity (baseline, D-26)

        _planetA = AddGameObject(_star, new Double3(0, 0, PlanetA_Z), PlanetSOI);
        GameObjects[_planetA].Name         = "PLANET A";
        GameObjects[_planetA].ObjectType   = UniObject.Type.Planet;
        GameObjects[_planetA].BaseColor    = PlanetA_Color;
        GameObjects[_planetA].RadiusMeters = PlanetA_RadiusMeters;
        GameObjects[_planetA].Luminosity   = 0.0;

        _planetB = AddGameObject(_star, new Double3(0, 0, PlanetB_Z), PlanetSOI);
        GameObjects[_planetB].Name         = "PLANET B";
        GameObjects[_planetB].ObjectType   = UniObject.Type.Planet;
        GameObjects[_planetB].BaseColor    = PlanetB_Color;
        GameObjects[_planetB].RadiusMeters = PlanetB_RadiusMeters;
        GameObjects[_planetB].Luminosity   = 0.0;

        _ship    = AddGameObject(_planetA, new Double3(0, 0, ShipOrbitMeters), 0);
        GameObjects[_ship].Name = "SHIP";

        // ── Sibling star systems (D-23, Phase 2 skybox test data) ────────────────────
        // Galaxy-space children of the home galaxy.
        // TierClassifier classifies them as NextTierSkybox when ship is in Star/Planet space.

        // Sibling 1: Alpha Cen A-like — warm G-type at 4.2 ly
        int _sib1 = AddGameObject(_galaxy, new Double3(Sibling1_GalX, 0, 0), StarSOI);
        GameObjects[_sib1].Name         = "ALPHA CEN";
        GameObjects[_sib1].ObjectType   = UniObject.Type.Star;
        GameObjects[_sib1].BaseColor    = Sibling1_Color;
        GameObjects[_sib1].RadiusMeters = Star_RadiusMeters;
        GameObjects[_sib1].Luminosity   = Sibling1_Luminosity;

        // Sibling 2: Barnard's Star-like — dim M-dwarf at 5.96 ly
        int _sib2 = AddGameObject(_galaxy, new Double3(Sibling2_GalX, 0, 0), StarSOI);
        GameObjects[_sib2].Name         = "BARNARD";
        GameObjects[_sib2].ObjectType   = UniObject.Type.Star;
        GameObjects[_sib2].BaseColor    = Sibling2_Color;
        GameObjects[_sib2].RadiusMeters = Star_RadiusMeters;
        GameObjects[_sib2].Luminosity   = Sibling2_Luminosity;

        // Sibling 3: Sirius-like — very bright blue-white A-type at 8.6 ly, offset angle
        int _sib3 = AddGameObject(_galaxy, new Double3(Sibling3_GalX, 0, Sibling3_GalZ), StarSOI);
        GameObjects[_sib3].Name         = "SIRIUS";
        GameObjects[_sib3].ObjectType   = UniObject.Type.Star;
        GameObjects[_sib3].BaseColor    = Sibling3_Color;
        GameObjects[_sib3].RadiusMeters = Star_RadiusMeters;
        GameObjects[_sib3].Luminosity   = Sibling3_Luminosity;

        // ── DEST GALAXY — full mirror spiral at ~Andromeda distance (D-33/D-34) ───────
        // 2.4e6 Universe units = 2.4e22 m (2.4e6 * 1e16). In +Z direction from home galaxy.
        int _galaxy2 = AddGameObject(_root, new Double3(0, 0, Galaxy2_Z), GalaxySOI);
        GameObjects[_galaxy2].Name              = "DEST GALAXY";
        GameObjects[_galaxy2].ObjectType        = UniObject.Type.Galaxy;
        GameObjects[_galaxy2].RadiusMeters      = Galaxy_RadiusMeters;
        GameObjects[_galaxy2].Luminosity        = 1e10;
        GameObjects[_galaxy2].BaseColor         = new Color(1.0f, 0.85f, 0.7f);  // warm orange-gold
        GameObjects[_galaxy2].GalaxyType        = 0;     // spiral mirror (D-33)
        GameObjects[_galaxy2].GalaxySeed        = 0.73f; // distinct arm pattern from home
        GameObjects[_galaxy2].GalaxyOrientation = new Vector3(0.2f, 0.98f, 0.0f); // slight tilt

        // Destination mirror system: star + 2 planets + 2 sibling stars (D-33/D-41)
        int _dStar = AddGameObject(_galaxy2, new Double3(0, 0, 0), StarSOI);
        GameObjects[_dStar].Name         = "DEST STAR";
        GameObjects[_dStar].ObjectType   = UniObject.Type.Star;
        GameObjects[_dStar].BaseColor    = new Color(1.0f, 0.90f, 0.55f);  // warm K-type
        GameObjects[_dStar].RadiusMeters = Star_RadiusMeters;
        GameObjects[_dStar].Luminosity   = 0.8;  // slightly dimmer than Sol

        int _dPlA = AddGameObject(_dStar, new Double3(0, 0, PlanetA_Z), PlanetSOI);
        GameObjects[_dPlA].Name         = "DEST PLANET A";
        GameObjects[_dPlA].ObjectType   = UniObject.Type.Planet;
        GameObjects[_dPlA].BaseColor    = new Color(0.30f, 0.60f, 0.85f);  // ocean blue
        GameObjects[_dPlA].RadiusMeters = PlanetA_RadiusMeters;
        GameObjects[_dPlA].Luminosity   = 0.0;

        int _dPlB = AddGameObject(_dStar, new Double3(0, 0, PlanetB_Z * 1.5), PlanetSOI);
        GameObjects[_dPlB].Name         = "DEST PLANET B";
        GameObjects[_dPlB].ObjectType   = UniObject.Type.Planet;
        GameObjects[_dPlB].BaseColor    = new Color(0.70f, 0.55f, 0.35f);  // sandy tan
        GameObjects[_dPlB].RadiusMeters = PlanetB_RadiusMeters;
        GameObjects[_dPlB].Luminosity   = 0.0;

        int _dSib1 = AddGameObject(_galaxy2, new Double3(-3.2e12, 0, 0), StarSOI);
        GameObjects[_dSib1].Name         = "DEST SIB 1";
        GameObjects[_dSib1].ObjectType   = UniObject.Type.Star;
        GameObjects[_dSib1].BaseColor    = new Color(1.0f, 0.70f, 0.35f);  // orange M-type
        GameObjects[_dSib1].RadiusMeters = Star_RadiusMeters;
        GameObjects[_dSib1].Luminosity   = 0.2;

        int _dSib2 = AddGameObject(_galaxy2, new Double3(4.5e12, 0, 3.1e12), StarSOI);
        GameObjects[_dSib2].Name         = "DEST SIB 2";
        GameObjects[_dSib2].ObjectType   = UniObject.Type.Star;
        GameObjects[_dSib2].BaseColor    = new Color(0.75f, 0.88f, 1.0f);  // blue-white F-type
        GameObjects[_dSib2].RadiusMeters = Star_RadiusMeters;
        GameObjects[_dSib2].Luminosity   = 3.5;

        // ── ELLIPTICAL CLUSTER — bare star cluster at ~1.8e22 m at 45° (D-41) ─────────
        // x = z = 1.27e6 Universe units → √(2) × 1.27e22 m ≈ 1.80e22 m, 45° from Galaxy 2.
        // Distinct sky direction ensures both galaxies are visible simultaneously from home.
        int _galaxy3 = AddGameObject(_root, new Double3(Galaxy3_X, 0, Galaxy3_Z), GalaxySOI);
        GameObjects[_galaxy3].Name              = "ELLIPTICAL CLUSTER";
        GameObjects[_galaxy3].ObjectType        = UniObject.Type.Galaxy;
        GameObjects[_galaxy3].RadiusMeters      = Galaxy_RadiusMeters;
        GameObjects[_galaxy3].Luminosity        = 6e9;   // slightly dimmer — older stellar population
        GameObjects[_galaxy3].BaseColor         = new Color(1.0f, 0.88f, 0.65f);  // warm golden yellow
        GameObjects[_galaxy3].GalaxyType        = 1;     // elliptical (D-41)
        GameObjects[_galaxy3].GalaxySeed        = 0.17f;
        GameObjects[_galaxy3].GalaxyOrientation = new Vector3(0.3f, 0.95f, 0.1f); // modest tilt

        // Elliptical cluster member stars — 3 stars, no planets (D-41)
        int _cSib1 = AddGameObject(_galaxy3, new Double3(1.5e12, 0, 0), StarSOI);
        GameObjects[_cSib1].Name         = "CLUSTER STAR 1";
        GameObjects[_cSib1].ObjectType   = UniObject.Type.Star;
        GameObjects[_cSib1].BaseColor    = new Color(1.0f, 0.82f, 0.55f);  // warm K-giant
        GameObjects[_cSib1].RadiusMeters = Star_RadiusMeters;
        GameObjects[_cSib1].Luminosity   = 0.6;

        int _cSib2 = AddGameObject(_galaxy3, new Double3(-2.1e12, 0, 1.2e12), StarSOI);
        GameObjects[_cSib2].Name         = "CLUSTER STAR 2";
        GameObjects[_cSib2].ObjectType   = UniObject.Type.Star;
        GameObjects[_cSib2].BaseColor    = new Color(1.0f, 0.92f, 0.75f);  // yellow G-type
        GameObjects[_cSib2].RadiusMeters = Star_RadiusMeters;
        GameObjects[_cSib2].Luminosity   = 1.2;

        int _cSib3 = AddGameObject(_galaxy3, new Double3(0.8e12, 0, -2.5e12), StarSOI);
        GameObjects[_cSib3].Name         = "CLUSTER STAR 3";
        GameObjects[_cSib3].ObjectType   = UniObject.Type.Star;
        GameObjects[_cSib3].BaseColor    = new Color(0.95f, 0.70f, 0.40f);  // orange-red M-type
        GameObjects[_cSib3].RadiusMeters = Star_RadiusMeters;
        GameObjects[_cSib3].Luminosity   = 0.08;
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
