---
phase: 01-in-system-flight-mvp
plan: "02"
subsystem: body-rendering
tags: [godot, csharp, shader, dithering, materials, palette, omnilight, emissive, rendering]

requires:
  - "01-01: Walking Skeleton (floating-origin RenderBridge, per-space factors, ToLocalDoubleUnits)"

provides:
  - "UniObject.Name / BaseColor / RadiusMeters per-body presentation fields"
  - "RenderBridge per-body StandardMaterial3D with AlbedoColor + 1:1 RadiusMeters scaling"
  - "Emissive unshaded star material + OmniLight3D (ShadowEnabled=false) with [Export] tuning knobs"
  - "Ambient light floor (ambient_light_energy=0.08) in Environment so planets not black in Planet space"
  - "WorldEnvironment glow enabled for star bloom on approach"
  - "dithering.gdshader hue-preserving per-channel ordered quantize (quantize_levels, default 4)"
  - "UniRenderer [Export] QuantizeLevels with Mathf.Max(1,...) guard"

affects:
  - "03-flight-feel (FlightController speed scaling consumes UniObject.RadiusMeters)"
  - "04-hud-polish (HUD target readout consumes UniObject.Name)"

tech-stack:
  added:
    - "StandardMaterial3D with EmissionEnabled + EmissionEnergyMultiplier for star"
    - "OmniLight3D positioned each frame at star render-transform (ShadowEnabled=false)"
    - "WorldEnvironment glow (glow_enabled=true, bloom) in Main.tscn"
    - "Environment ambient_light_source=2, ambient_light_energy=0.08 ambient floor"
  patterns:
    - "Star identification: body.Name == 'STAR' (explicit name check, space-agnostic)"
    - "Radius transform: RadiusMeters / ship.LocalPos.Scale * factor (meters -> observer-units -> render-units)"
    - "OmniLight follows star render position each frame; hidden when star not in rendered set"
    - "Per-channel quantize: floor(c * levels + bayer_offset) / levels per RGB channel"
    - "Ambient floor as MVP cross-space lighting stand-in (OmniLight absent in Planet space)"

key-files:
  created: []
  modified:
    - "Scripts/Universe/UniObject.cs — added string Name, Godot.Color BaseColor, double RadiusMeters fields"
    - "Scripts/Universe/TestSetup.cs — authored Name/BaseColor/RadiusMeters for Star, Planet A, Planet B, Ship"
    - "Scripts/Render/RenderBridge.cs — per-body materials, 1:1 radii, emissive star, OmniLight3D, ambient note"
    - "Main.tscn — glow enabled, ambient light floor added"
    - "Shaders/dithering.gdshader — 1-bit collapse replaced with per-channel ordered quantize"
    - "Scripts/Universe/UniRenderer.cs — QuantizeLevels [Export] + ApplyAllParameters registration"

key-decisions:
  - "Star identified by body.Name=='STAR' rather than CurrentSpace (star lives in Galaxy space, not Star space)"
  - "Ambient floor (energy=0.08) as MVP stand-in for cross-space lighting; terminator still readable in Star space"
  - "Per-channel quantize with Bayer offset (not a fixed palette) — simplest approach that preserves arbitrary hue"
  - "OmniLight range=1e5 render units (well within 1e6 far plane) with export knob for tuning"
  - "Mesh cache NOT invalidated on material update — meshes are created once with final per-body material (lazy init is final)"

metrics:
  duration: "~12 min"
  completed: "2026-06-13"
  tasks_completed: 3
  tasks_total: 4
  files_modified: 6
---

# Phase 01 Plan 02: Body Rendering Summary

**Per-body hue/radius/emissive/OmniLight/dither — Earth-blue planets, Mars-rust Planet B, emissive blooming star, 1:1 true radii, 8-bit hue-preserving dither**

## Performance

- **Duration:** ~12 min
- **Completed:** 2026-06-13
- **Tasks:** 3 of 4 auto tasks complete (Task 4 = checkpoint:human-verify, pending)
- **Files modified:** 6

## Accomplishments

### Task 1: Per-body presentation data (UniObject + TestSetup)

- Added `public string Name`, `public Godot.Color BaseColor`, `public double RadiusMeters` to `UniObject.cs` (PascalCase data-class convention; matching `using Godot;` added)
- Added radius and color constants to `TestSetup.cs`:
  - `PlanetA_RadiusMeters = 6.371e6` (Earth equatorial radius)
  - `PlanetB_RadiusMeters = 3.390e6` (Mars mean radius)
  - `Star_RadiusMeters = 6.960e8` (Solar radius)
  - `PlanetA_Color = new Color(0.25f, 0.50f, 0.95f)` — Earth-blue
  - `PlanetB_Color = new Color(0.80f, 0.35f, 0.20f)` — Mars-rust
  - `Star_Color = new Color(1.00f, 0.95f, 0.60f)` — solar yellow
- `SetupScene()` extended to set `Name`, `BaseColor`, `RadiusMeters` on each body after `AddGameObject`; colors chosen with distinct luminance AND hue (Pitfall 6 hedge)

### Task 2: Per-body color + 1:1 radius + emissive star + OmniLight

- `RenderBridge.GetOrCreateMesh` now uses `body.RadiusMeters / ship.LocalPos.Scale * factor` for mesh radius (meters → observer-units → render-units, same transform established by Plan 01-01)
- Planet bodies: `StandardMaterial3D` with `AlbedoColor = body.BaseColor` (default-lit, receives OmniLight terminator)
- Star body: `StandardMaterial3D` with `ShadingMode=Unshaded`, `EmissionEnabled=true`, `Emission=body.BaseColor`, `EmissionEnergyMultiplier=StarEmissionEnergy` (export, default 3.0)
- `OmniLight3D` created once in `_Ready`, repositioned to star's render-space position each frame, `ShadowEnabled=false` (RND-04)
- New `[Export]` knobs: `StarEmissionEnergy`, `StarLightEnergy`, `StarLightRange` (in render units, default 1e5)
- `Main.tscn`: glow enabled (`glow_enabled=true`, intensity/strength/bloom), ambient floor added (`ambient_light_energy=0.08`)
- `DirectionalLight3D` was already removed in Plan 01-01 continuation

### Task 3: Hue-preserving dither shader extension

- `dithering.gdshader`: replaced `float avg = ...; COLOR = avg < threshold ? black : white` with per-channel ordered quantize:
  ```glsl
  float bayer_offset = float(dithering_pattern(fragcoord)) / 255.0;
  float levels = float(max(quantize_levels, 1));
  vec3 c = color + vec3(bayer_offset);
  c = floor(c * levels) / (levels - 1.0 + 1e-6);
  c = clamp(c, 0.0, 1.0);
  ```
- Preserved: `screen_texture : hint_screen_texture`, `resolution_scale` block-snapping, 4×4 Bayer `dithering_pattern`, `white`/`black`/`threshold` uniforms (declared; no longer drive a branch)
- Added `uniform int quantize_levels = 4` (default: ~64 colours = 4^3)
- `UniRenderer.cs`: added `[Export] int QuantizeLevels` with `Mathf.Max(1, value)` guard (T-02-04 divide-by-zero mitigation); registered in `ApplyAllParameters()`

## Task Commits

1. **Task 1: Per-body Name/BaseColor/RadiusMeters** - `342e60a` (feat)
2. **Task 2: Per-body color/radius, emissive star, OmniLight, glow + ambient floor** - `9808fb0` (feat)
3. **Task 3: Hue-preserving dither shader extension** - `ef75906` (feat)

## Deviations from Plan

### Architectural clarification: star identification by Name not CurrentSpace

**[Rule 2 - Missing Critical Functionality] Plan suggested `CurrentSpace == Space.Star` to identify the star, but this is incorrect**
- **Found during:** Task 2 implementation
- **Issue:** The star body (`AddGameObject(_galaxy, ...)`) gets `CurrentSpace = Space.Galaxy` (it lives IN galaxy space, not star space). `Space.Star` is the space that bodies inside the star's SOI (planets) inhabit. Using `CurrentSpace == Space.Star` would match planets, not the star.
- **Fix:** Identify the star via `body.Name == "STAR"` — unambiguous and space-agnostic. A `private static bool IsStarBody(UniObject body)` helper centralises the check.
- **Files modified:** `Scripts/Render/RenderBridge.cs`
- **No additional commit** — handled inline in Task 2

### Ambient lighting floor added (cross-space lighting caveat)

**[Critical Architecture Update] Planet-space renders have no OmniLight in frame — planets would be black without mitigation**
- **Issue:** RND-02 mandates rendering only `parent.ChildIndices` bodies. When the ship is in Planet space, the star is not a child of the planet and is NOT in the rendered set. The OmniLight3D (owned by RenderBridge, positioned at the star) is therefore hidden — a default-lit planet material renders fully black.
- **Fix applied:** `ambient_light_source = 2` + `ambient_light_energy = 0.08` added to the `Environment` sub-resource in `Main.tscn`. This provides a dim but clearly visible base illumination. Value is intentionally low (8% of max) so that when the star's OmniLight IS in frame (Star space), the day/night terminator gradient is still clearly visible.
- **Tuning:** `ambient_light_energy` is in the `.tscn` and is easily adjusted in the Godot editor. Users wanting a darker ambient can lower it; users finding planets too dim in Planet space can raise it.
- **MVP limitation noted:** True cross-space directional lighting (e.g. computing the real direction from the planet to the star and applying a directional light in Planet space) is NOT implemented. The ambient floor is the MVP stand-in. This surfaces at the human-verify checkpoint so the user can decide if more sophistication is needed before advancing.

## Known Stubs

- **Mesh materials are lazy-init-final:** meshes are created once with materials baked at creation time. If `StarEmissionEnergy`/`StarLightEnergy`/`StarLightRange` exports are changed at runtime in the editor, the OmniLight parameters update each frame (live), but the star's emissive material `EmissionEnergyMultiplier` does NOT update — would require mesh recreation. This is acceptable for MVP tuning via editor reload.
- **SkeletonSpeed = 1e8 m/s** in `TestSetup.cs` — placeholder; Plan 03 context-scaled speed
- **Forward-only thrust** in `TestSetup._Process` — attitude-oriented motion arrives in Plan 03

## Threat Flags

No new threat surface beyond the plan's threat model. T-02-04 (quantize_levels divide-by-zero) mitigated by `Mathf.Max(1, value)` guard in UniRenderer.

## Lighting Architecture Note for Checkpoint

The user will see the following lighting behavior during verification:

- **In Planet space** (default start): planet lit by ambient floor only (soft, directionless). No terminator visible. This is by design — the star is not in the render set when orbiting a planet.
- **In Star space** (after SOI exit): both planets appear as tiny specks but now the OmniLight3D from the star is active. The star glows yellow and blooms. Planets (if close enough to resolve) would show a terminator.
- **On approach to star**: star bloom grows dramatically; OmniLight range=1e5 render units covers the visible scene.

The checkpoint asks whether the user is satisfied with the ambient-floor approach or wants true cross-space directional lighting before proceeding to Plan 03.

## Self-Check: PASSED

Files exist:
- Scripts/Universe/UniObject.cs: FOUND
- Scripts/Universe/TestSetup.cs: FOUND
- Scripts/Render/RenderBridge.cs: FOUND
- Shaders/dithering.gdshader: FOUND
- Scripts/Universe/UniRenderer.cs: FOUND
- Main.tscn: FOUND

Commits verified:
- 342e60a (Task 1 — feat: per-body Name/BaseColor/RadiusMeters): FOUND
- 9808fb0 (Task 2 — feat: per-body color/radius, emissive star, OmniLight, glow + ambient floor): FOUND
- ef75906 (Task 3 — feat: hue-preserving dither shader extension): FOUND

Build: 0 errors, 0 warnings confirmed (all tasks).
