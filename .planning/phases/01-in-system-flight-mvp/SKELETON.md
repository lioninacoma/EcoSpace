# Walking Skeleton — EcoSpace

**Phase:** 1
**Generated:** 2026-06-13

## Capability Proven End-to-End

> One sentence: the smallest user-visible capability that exercises the full stack.

A player can launch `Main.tscn`, sit at the floating-origin coordinate while one sphere body (Planet A) is rendered ship-relative, press a thrust key to move the ship forward via `GameWorld.TranslatePos` — crossing SOI boundaries iteratively and crash-free — and watch a phosphor-green HUD value update, proving the **input → simulation (UniVec3/SOI) → floating-origin render → HUD** loop works against the existing precision engine.

## Architectural Decisions

| Decision | Choice | Rationale |
|---|---|---|
| Precision / coordinate system | Existing `UniVec3` + `Long3` + `Double3` + SOI hierarchy (`GameWorld`) | Locked architecture (CLAUDE.md): do NOT replace it; all new code consumes it read-mostly. Godot "Large World Coordinates" engine flag is NOT enabled (RESEARCH: redundant + costly). |
| Floating origin | Ship is the observer; `UniVec3.ToLocalDouble(ship.LocalPos)` produces ship-relative meters fed to `Node3D.Position` each frame | `ToLocalDouble` already exists and returns observer-relative `Double3` (`UniVec3.cs:157-162`). Camera stays at world origin. |
| Simulation mutation point | Only `GameWorld.TranslatePos(shipIndex, Double3)` mutates the world; RenderBridge + HUD are read-only consumers | Preserves single-threaded `_Process` model; avoids the "everything in TestSetup" anti-pattern (RESEARCH Architectural Responsibility Map). |
| Node topology | `Main` (Node3D, `TestSetup : GameWorld`) hosts child nodes `FlightController`, `RenderBridge`, `Hud`; `Camera3D` is the ship eye at origin | Each system owns its own `_Process`; matches existing derived-`_Process` driver pattern (`TestSetup.cs:54`). |
| Render look | Per-body `StandardMaterial3D` colors + full-screen `dithering.gdshader` post-process (extended to a color palette) | D-13 locked: colors live in materials, shader is a global quantizer (extended from 1-bit to palette so hue survives). |
| HUD | Phosphor-green `CanvasLayer` + `Control`/`Label` following the existing `FPS.gd` pattern | Discretion D; matches `Main.tscn` `CanvasLayer`+`FPSLabel`. C# chosen for HUD (sim reads are C# types). |
| Input | `InputMap` named actions in `project.godot`; mouse via accumulated `event.relative` software cursor (NOT captured-mouse absolute query) | Rebindable, idiomatic; captured-mouse breaks the visible steering reticle (RESEARCH Pitfall 8). |
| Build / run | `dotnet build EcoSpace.csproj`; run `Main.tscn` headless via Godot for log-smoke verification, F5 in editor for visual | No test runner exists (TESTING.md); verification = build + headless `GD.Print` log assertions + human-verify checkpoints. |

## Directory Layout

```
Scripts/
├── Universe/                 # EXISTING engine — extend in place, never replace
│   ├── GameWorld.cs          # STAB-01 iterative + null-safe transition lands here
│   ├── TestSetup.cs          # WORLD-01 base; autopilot _Process removed; child nodes wired
│   ├── UniObject.cs          # + Name, BaseColor, RadiusMeters fields
│   └── Math/UniVec3.cs        # consumed read-only (ToLocalDouble, Distance)
├── Flight/
│   └── FlightController.cs    # FLT-01/02/03 — input → TranslatePos
├── Render/
│   └── RenderBridge.cs        # RND-01/02/04 — UniVec3→Node3D, body meshes, star light
└── HUD/
    └── Hud.cs                 # HUD-01..04 — adaptive units, context, reticles, target
Shaders/
└── dithering.gdshader         # EXISTING — extended to color palette (RND-03/D-13)
```

## Stack Touched in Phase 1 (Skeleton = Plan 01)

- [x] Project scaffold — existing Godot project; new `Scripts/Flight`, `Scripts/Render`, `Scripts/HUD` folders + `FlightController`/`RenderBridge`/`Hud` nodes wired into `Main.tscn`
- [x] "Routing" equiv — `Main.tscn` is the single scene; child-node `_Process` loop is the runtime entry path
- [x] Real state read+write — write: `TranslatePos` mutates ship `UniVec3` + may reparent across SOI; read: RenderBridge reads `GameObjects[*].LocalPos` each frame
- [x] UI wired to sim — phosphor-green HUD label reads live ship state each frame
- [x] Full-stack run — `dotnet build` + run `Main.tscn`; thrust key moves ship and a body renders ship-relative

## Out of Scope (Deferred to Later Slices / Phases)

> Explicit so later work does not re-litigate Phase 1 minimalism.

- Virtual-joystick mouse steering, roll, throttle, distance-scaled speed (Plan 02 — FLT-01/02/03)
- Per-body distinct colors, true 1:1 radii, emissive star + glow + OmniLight, palette dither extension (Plan 03 — RND-03/04)
- Adaptive speed units, context label, target cycling, off-screen target marker (Plan 04 — HUD-01/02/04)
- Dynamic spherical skybox / out-of-space projection (Phase 2 — RND-05)
- Cross-galaxy travel + galaxy/universe-scale authored data (Phase 3 — TRV-02)
- CRT scanline overlay, boost/afterburner, audio, 1-bit/mono toggle (v2)
- Deeper `GameWorld` hardening from CONCERNS.md beyond what STAB-01 requires (null-slot compaction / free-list, `DistanceSq`, SIMD layout asserts)

## Subsequent Slice Plan (within Phase 1)

Each later plan adds one vertical slice on top of this skeleton without altering its architectural decisions:

- **Plan 02 — Flight feel:** virtual-joystick steering + persistent throttle + roll + hold-attitude + distance-scaled speed envelope + two reticles (FLT-01/02/03, HUD-03)
- **Plan 03 — Body rendering:** per-body colors + 1:1 radii + emissive star + glow + OmniLight + color-palette dither extension (RND-03/04, WORLD-01 authoring, completes RND-02)
- **Plan 04 — HUD:** adaptive speed units + context label + target cycling + off-screen direction marker (HUD-01/02/04, TRV-01 integration milestone)

Subsequent Phases build on the same skeleton:

- **Phase 2 — Dynamic Skybox:** out-of-SOI stars/galaxies projected onto a stable spherical skybox (RND-05)
- **Phase 3 — Cross-Galaxy Travel:** galaxy/universe-scale authored data + full SOI chain at intergalactic scale (TRV-02)
