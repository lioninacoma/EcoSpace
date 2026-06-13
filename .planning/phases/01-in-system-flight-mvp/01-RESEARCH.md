# Phase 1: In-System Flight MVP - Research

**Researched:** 2026-06-13
**Domain:** Godot 4.6 Mono / C# real-time space-sim flight, floating-origin rendering on a custom precision engine, iterative SOI-transition hardening, retro dithered body rendering, virtual-joystick flight, minimal CanvasLayer HUD
**Confidence:** HIGH (codebase-verified) / MEDIUM (Godot rendering specifics)

## Summary

Phase 1 is a Walking Skeleton that wires a playable flight layer onto an **already-working** high-precision universe engine. The hardest parts are not "new tech" — they are *integration* problems: (1) making `GameWorld.TrySpaceTransition` iterative and null-safe (STAB-01); (2) driving the existing `UniVec3`→world-relative-Double3 conversion (`UniVec3.ToLocalDouble`, already present) into Godot `Node3D` transforms every frame so the ship sits at the origin (RND-01/02); (3) making the existing `dithering.gdshader` post-process and per-body colors coexist (RND-03/04); (4) a virtual-joystick mouse model with a *visible* deadzone cursor (FLT-01); and (5) a phosphor-green CanvasLayer HUD following the existing `FPS.gd` pattern (HUD-01..04).

The single most important architectural fact: **the project's `UniVec3` + SOI system is the floating-origin solution. Godot's built-in "Large World Coordinates" engine option is NOT used and MUST NOT be recommended** — it is a compile-time `real_t = double` engine build, irrelevant here because the custom engine already keeps render-space coordinates small. Rendering uses *single-precision* Godot transforms fed camera-relative meters by `ToLocalDouble`, which is exactly the right approach. [VERIFIED: codebase `UniVec3.cs:152-162`] [CITED: docs.godotengine.org/en/stable/tutorials/physics/large_world_coordinates.html]

The second most important fact: the CONTEXT.md (D-01..D-16) has already locked nearly every *design* decision. This research deliberately does **not** re-explore alternatives to locked decisions. It documents the Godot-specific *HOW* and the pitfalls that will bite during implementation.

**Primary recommendation:** Build five thin, ordered slices — (1) STAB-01 iterative transition + a smoke test that crosses two SOIs in one frame; (2) floating-origin sync + render only current-parent-space bodies; (3) dithered/emissive body materials + OmniLight star; (4) virtual-joystick flight + persistent throttle + distance-scaled speed; (5) phosphor-green HUD with adaptive units + target cycling. Each slice should leave the game runnable. Treat all numeric values (speeds, ranges, deadzone, bloom) as tuning knobs exposed via `[Export]`.

## Architectural Responsibility Map

| Capability | Primary Tier | Secondary Tier | Rationale |
|------------|-------------|----------------|-----------|
| SOI transition correctness (STAB-01) | Simulation (`GameWorld`) | — | Pure C# state-machine over `GameObjects`; no Godot nodes involved |
| Floating-origin position math (RND-01) | Math (`UniVec3`) | Simulation (`GameWorld`) | `ToLocalDouble` already computes observer-relative meters; sim owns "who is observer" |
| `UniVec3`→`Node3D` transform sync (RND-01/02) | Render bridge (new node) | Math | A per-frame system reads sim state, writes Godot `Transform3D` |
| Body mesh spawn/update (RND-02/03/04) | Render bridge (new node) | Godot scene | One `MeshInstance3D` per in-space body; mesh/material is Godot-side |
| Dither/8-bit look (RND-03) | Post-process (`UniRenderer` + shader) | — | Already a full-screen `canvas_item` shader; do not move per-body |
| Star lighting (RND-04/D-16) | Godot scene (`OmniLight3D`) | Render bridge | Light is a scene node positioned at the star's render transform |
| Flight input → motion (FLT-01/02/03) | Flight controller (new node) | Simulation | Controller computes a `Double3` delta, calls `GameWorld.TranslatePos` |
| Speed auto-scaling (FLT-03/D-06) | Flight controller | Math (`Distance`) | Controller queries nearest-body distance to set context-max speed |
| HUD (HUD-01..04) | UI (`CanvasLayer`/`Control`) | Flight + Sim (read-only) | Reads ship speed, `CurrentSpace`, nearest body, active target; pure display |

**Why this matters:** The CONTEXT.md integration points imply a clean split: *simulation never touches Godot nodes; the render bridge never mutates `UniVec3`*. Keeping body rendering, lighting, flight, and HUD as separate nodes that all read the one `GameWorld` instance preserves the existing single-threaded `_Process` model and avoids the "everything in TestSetup" anti-pattern.

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions

**Flight Controls & Feel (FLT-01, FLT-02)**
- **D-01: Virtual-joystick mouse model (Wing Commander–style).** Mouse moves a cursor within a screen-center deadzone; the ship continuously rotates *toward* the cursor, turning faster the further the cursor is from center. Centered cursor = no rotation. NOT direct FPS-style 1:1 mouse-delta look.
- **D-02: Hold-attitude stabilization.** When turn input stops, rotation halts cleanly with no residual drift/spin. The ship does **not** auto-level and does **not** recenter toward forward — it keeps whatever orientation it has.
- **D-03: Persistent throttle.** W/S (and/or scroll) raise/lower a throttle level that persists hands-off; a full-stop key (e.g. X) zeroes it. Cockpit-style, not hold-to-thrust.
- **D-04: Roll on Q/E** (left/right). Pitch + yaw stay on the mouse cursor; mouse is never used for roll.
- **D-05: Two reticles.** A fixed center crosshair marks "nose forward" (satisfies HUD-03); a separate moving reticle shows the steering cursor. The on-screen flight cursor is visible.

**Speed Auto-Scaling (FLT-03)**
- **D-06: Max speed scales with distance to the nearest body** (planet/star surface). You naturally slow approaching anything and accelerate leaving it. (Chosen over fixed-per-SOI-scale or hybrid.)
- **D-07: Continuous & smooth envelope.** Max speed eases frame-to-frame with no visible snap when crossing an SOI boundary. Pairs with the distance-based model.
- **D-08: Throttle = fraction of context max.** Actual speed = throttle% × current context-max speed. One control, auto-scaled. No manual mode switch (FLT-03 intent).
- Concrete min/max speed constants, the distance→speed curve shape, and easing rates are **planner/tuning discretion** — only the model is locked here.

**HUD (HUD-01..04)**
- **D-09: Phosphor-green CRT aesthetic** (monochrome green vector-terminal look). Replace the current magenta accent.
- **D-10: Adaptive speed units** — auto-pick the largest readable unit on the ladder **m/s → km/s → AU/s → ly/s** (keep displayed number roughly 1–9999). Real units.
- **D-11: Context label = space level + nearest body**, e.g. `STAR SPACE · nearest: PLANET A`.
- **D-12: Target readout cycles bodies in the current parent space**, showing **name + distance** (adaptive units). Scope limited to renderable-now bodies.

**Body Rendering (RND-03, RND-04)**
- **D-13: Per-body distinct colors + global dither quantize.** Each body gets an authored base color; the existing `dithering.gdshader` post-process quantizes the whole frame.
- **D-14: Stars = emissive sphere + glow/bloom.** Unshaded bright sphere with a Godot glow/bloom halo. No cast shadows (RND-04).
- **D-15: True 1:1 body radii.** Planets/stars at real radii — tiny specks at distance, grow dramatically on close approach.
- **D-16: Star lights planets via a point light** (OmniLight at the star position) for a correct day/night terminator. Range tuned for 1:1 distances; replaces placeholder `DirectionalLight3D`.

### Claude's Discretion
- Iterative `TrySpaceTransition` rewrite shape (while-loop / state machine), null-slot guarding, and related `GameWorld` hardening — implement the safest correct version.
- Floating-origin sync mechanism (which node is origin, how `UniVec3`→`Node3D` transforms are computed each frame to avoid jitter).
- All numeric tuning: speed curve, deadzone size, turn rates, throttle steps, bloom amount, light range, palette specifics, mesh subdivision/LOD.
- HUD implementation tech (GDScript vs C#, `Control` node layout) — match existing `FPS.gd` / `Main.tscn` CanvasLayer pattern unless a better fit emerges.

### Deferred Ideas (OUT OF SCOPE)
- **Dynamic spherical skybox** (RND-05) — Phase 2.
- **Cross-galaxy travel** and galaxy/universe-scale data (TRV-02) — Phase 3.
- **CRT scanline overlay** (`crt.gdshader`) — v2 PRES-01.
- **Boost/afterburner** (FLT-04), **engine/boost audio** (PRES-02), **1-bit/mono toggle** (PRES-03) — v2.
- **'c'-multiple / FTL flavor** on the HUD — cut from v1.
- **All-universe target cycling** and **target relative bearing** — current-space name+distance chosen for v1 (bearing may be revisited per the open tension).
- Deeper `GameWorld` hardening from CONCERNS.md (null-slot compaction / free-list, `DistanceSq`, SOI input validation, SIMD layout asserts) — touch only what STAB-01 requires.
</user_constraints>

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|------------------|
| STAB-01 | SOI transition iterative & null-safe; cross multiple SOIs/frame without crash/corruption | "Iterative SOI Transition" pattern + null-safe accessor; pitfalls #1–#3; CONCERNS.md crash modes mapped |
| WORLD-01 | Hand-authored multi-scale test universe to fly | Reuse `TestSetup.SetupScene()` (verified Root→Galaxy→Star→PlanetA/B→Ship); extend with radii + colors per body |
| RND-01 | Floating origin: player at coordinate origin; `UniVec3`→`Node3D` synced each frame, no jitter | "Floating-Origin Sync" pattern using existing `UniVec3.ToLocalDouble`; pitfall #4 (origin choice) |
| RND-02 | Only current-parent-space objects rendered as geometry | Render bridge iterates ship's parent's `ChildIndices`; pitfall #5 |
| RND-03 | Planets as sphere meshes with dithering + 8-bit palette | `SphereMesh` + `StandardMaterial3D` albedo; existing `dithering.gdshader` quantizes frame; pitfall #6 |
| RND-04 | Stars bright, emissive, light nearby planets, no cast shadows | Unshaded emissive material + glow Environment; `OmniLight3D` with `shadow_enabled=false`; pitfall #7 |
| FLT-01 | Pitch/yaw (mouse) + roll, arcade auto-stabilization | "Virtual-Joystick Steering" pattern; accumulated-relative cursor; pitfall #8 (captured-mouse recenter) |
| FLT-02 | Forward throttle, slow/stop | Persistent throttle state machine; D-03 |
| FLT-03 | Speed auto-scales to SOI surroundings, no mode switch | "Distance-Scaled Speed" pattern; uses `UniVec3.Distance`; pitfall #9 |
| HUD-01 | Speed readout with scale-adaptive unit | "Adaptive Unit Ladder" code example (m/s→km/s→AU/s→ly/s) |
| HUD-02 | Context/location label (space level / nearest body) | Read `CurrentSpace` + nearest-body scan; D-11 format |
| HUD-03 | Center crosshair/reticle | Fixed `Control`/`TextureRect` at viewport center; D-05 fixed reticle |
| HUD-04 | Cycle target readout (name + distance) | Cycle ship-parent `ChildIndices`; name + adaptive distance; open-tension affordance |
| TRV-01 | Fly a single star system, approach dithered bodies | Integration milestone — emerges when slices 1–5 compose |
</phase_requirements>

## Standard Stack

This phase introduces **no external packages**. Everything is built into Godot 4.6.2 Mono / .NET 8. The "stack" is the set of Godot built-in nodes/resources to use.

### Core (Godot built-ins)
| Component | Purpose | Why Standard |
|-----------|---------|--------------|
| `Node3D` (sim host) | Existing `GameWorld`/`TestSetup` lives here | Already the scene root; single `_Process` drives sim |
| `Camera3D` | Player viewpoint at floating origin | Already in `Main.tscn`; becomes the "ship eye" |
| `MeshInstance3D` + `SphereMesh` | Per-body geometry (RND-03/04) | Native, cheap; `SphereMesh` already in `Main.tscn` as a sub-resource |
| `StandardMaterial3D` | Per-body albedo color + emissive | Native PBR material; `emission_enabled` + `shading_mode=Unshaded` for stars |
| `OmniLight3D` | Star point light (RND-04/D-16) | Point light gives correct terminator; `shadow_enabled=false` per RND-04 |
| `WorldEnvironment` + `Environment` | Glow/bloom for emissive stars (D-14) | Glow is an `Environment` post-effect; required for bloom halo |
| `CanvasLayer` + `Control`/`Label`/`TextureRect` | HUD (HUD-01..04) | Matches existing `CanvasLayer`+`FPSLabel` pattern in `Main.tscn` |
| `UniRenderer` + `dithering.gdshader` | 8-bit dither post-process (RND-03) | Already wired to `FullscreenRect`; reuse as-is |

### Supporting (input & math, already present)
| Component | Purpose | When to Use |
|-----------|---------|-------------|
| `Input` / `InputEventMouseMotion` | Mouse + key flight input | Read in `_Input`/`_UnhandledInput`; accumulate into virtual cursor (FLT-01) |
| `InputMap` actions | Throttle/roll/full-stop/target-cycle keybinds | Define named actions in `project.godot` rather than hardcoding scancodes |
| `UniVec3.ToLocalDouble(observer)` | Observer-relative render meters | Per-frame, per-body, to build `Node3D` transforms (RND-01) |
| `UniVec3.Distance` / `MagnitudeSq` | Nearest-body + target distances | FLT-03 speed scaling, HUD-04 distance; prefer `MagnitudeSq` in hot loops |

### Alternatives Considered
| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| Custom `UniVec3` floating origin | Godot "Large World Coordinates" engine build | REJECTED — that is a `real_t=double` engine recompile; the custom engine already solves this and is the locked architecture. Do not enable. |
| `OmniLight3D` star light | `DirectionalLight3D` (current placeholder) | D-16 locks point light for correct terminator at 1:1; directional gives wrong, parallel lighting |
| Per-body shader for 8-bit | Existing full-screen `dithering.gdshader` | D-13 locks global post-process quantize; per-body shading would fight the dither |
| GDScript HUD | C# HUD | Discretion (D); GDScript matches `FPS.gd` and is fine for label updates — choose per planner |

**Installation:** None. `dotnet build` / Godot editor build only. No `nuget`/`npm`/`pip` packages.

**Version verification:** Engine pinned to Godot 4.6.2 Mono in `.csproj` and `project.godot` (`config/features=("4.6","C#","Forward Plus")`). [VERIFIED: codebase `project.godot:15`] No package registry lookups apply.

## Package Legitimacy Audit

**Not applicable.** This phase installs zero external packages — all functionality uses Godot 4.6.2 built-in nodes/resources and the existing in-repo C# engine. No npm/PyPI/crates/NuGet dependencies are added. The only external dependency is the Godot engine itself (already pinned, already in use).

## Architecture Patterns

### System Architecture Diagram

```
                         ┌─────────────────────────────────────────────┐
   Mouse / Keyboard ───► │ FlightController (Node)                      │
   (InputEventMouseMotion│  • accumulate cursor offset (deadzone)       │
    + InputMap actions)  │  • throttle state (persistent)              │
                         │  • roll on Q/E                              │
                         │  • compute attitude (Basis) + speed envelope│
                         └───────────────┬─────────────────────────────┘
                                         │ Double3 delta = forward * speed * dt
                                         ▼
                         ┌─────────────────────────────────────────────┐
   (single _Process) ───►│ GameWorld.TranslatePos(shipIndex, delta)    │
                         │  └─► TrySpaceTransition (ITERATIVE, null-safe)│  ◄── STAB-01
                         │        • exit parent SOI loop                │
                         │        • enter child SOI loop                │
                         │        • mutates UniVec3 + parent/child links│
                         └───────────────┬─────────────────────────────┘
                                         │ reads GameObjects[*].LocalPos (UniVec3)
                                         ▼
                         ┌─────────────────────────────────────────────┐
   ship.LocalPos ───────►│ RenderBridge (Node3D)                       │
   = floating origin     │  for each body in ship.Parent.ChildIndices: │  ◄── RND-01/02
                         │    relMeters = body.ToLocalDouble(ship)     │
                         │    meshInstance.Position = (Vector3)relMeters│
                         │  star MeshInstance: emissive + OmniLight     │  ◄── RND-04
                         └───────────────┬─────────────────────────────┘
                                         ▼
                         ┌──────────────────────┐   screen_texture   ┌──────────────────────┐
   Camera3D @ origin ───►│ 3D viewport render   │ ─────────────────► │ dithering.gdshader    │ ─► 8-bit frame
   (WorldEnvironment glow)└──────────────────────┘                    │ (FullscreenRect)      │
                                         │                            └──────────────────────┘
                                         ▼ (reads sim state, read-only)
                         ┌─────────────────────────────────────────────┐
                         │ HUD (CanvasLayer / Control)                 │  ◄── HUD-01..04
                         │  • speed (adaptive units)   • context label │
                         │  • center crosshair + moving steer reticle  │
                         │  • cycle target (name + distance)           │
                         └─────────────────────────────────────────────┘
```

Trace the primary use case (player nudges mouse, throttles up, approaches Planet B): input → FlightController computes delta → `TranslatePos` moves ship `UniVec3` and may transition SOI → RenderBridge repositions every in-space body relative to the now-moved ship → 3D frame renders → dither post-process quantizes → HUD shows rising speed and shrinking distance to target.

### Recommended Project Structure
```
Scripts/
├── Universe/                 # EXISTING engine — extend, don't replace
│   ├── GameWorld.cs          # STAB-01 edits land here (iterative transition)
│   ├── TestSetup.cs          # WORLD-01 base; remove autopilot _Process, add radii/colors
│   ├── UniObject.cs          # may add: Name, BaseColor, RadiusMeters fields
│   └── Math/UniVec3.cs       # optional: add DistanceSq if hot-loop profiling needs it
├── Flight/
│   └── FlightController.cs    # FLT-01/02/03 — input → TranslatePos
├── Render/
│   └── RenderBridge.cs        # RND-01/02/04 — UniVec3→Node3D, body mesh mgmt, star light
└── HUD/
    └── Hud.cs (or Hud.gd)     # HUD-01..04 — adaptive units, context, reticles, target
Shaders/
└── dithering.gdshader        # EXISTING — unchanged (RND-03)
```

### Pattern 1: Iterative SOI Transition (STAB-01)
**What:** Replace the recursive `TrySpaceTransition` with a bounded `while` loop that null-checks every `GameObjects[index]` lookup and never re-enters via recursion.
**When to use:** The core STAB-01 fix.
**Key shape (illustrative — final form is planner/implementer discretion):**
```csharp
// Source: refactor of existing Scripts/Universe/GameWorld.cs:48-117 [VERIFIED: codebase]
private void TrySpaceTransition(UniObject obj)
{
    const int MaxIterations = 32; // safety cap; hierarchy is 5 deep, this is generous
    int iterations = 0;
    int lastExitedIndex = -1;

    while (iterations++ < MaxIterations)
    {
        if (TryExitParentSOI(obj, out int exitedIndex))
        {
            lastExitedIndex = exitedIndex;   // don't immediately re-enter the SOI we just left
            continue;
        }
        if (TryEnterChildSOI(obj, excludeIndex: lastExitedIndex))
        {
            lastExitedIndex = -1;            // entered something new; reset exclusion
            continue;
        }
        break; // stable: no exit, no entry this pass
    }
}

// Null-safe accessor used by every lookup inside the transition family:
private UniObject Get(int index)
    => (uint)index < (uint)GameObjects.Count ? GameObjects[index] : null;
```
- In `TryExitParentSOI` / `TryEnterChildSOI`, replace direct `GameObjects[i]` with `Get(i)` and early-return `false` on null (CONCERNS.md lines 19-23, 51-55). [VERIFIED: codebase `CONCERNS.md`]
- Preserve the existing `GD.Print` `↑`/`↓` transition logging for debug visibility (CONTEXT.md established pattern). [VERIFIED: codebase `GameWorld.cs:75,111`]

**Anti-Patterns to Avoid**
- **Recursion for transitions:** the existing recursive form stacks O(depth) calls and is the documented stability risk (CONCERNS.md "Recursive Space Transition Stacking"). Replace, don't patch.
- **Unbounded loop:** never `while(true)` without an iteration cap — a math error producing oscillating exit/enter would hang the frame. The cap converts a hang into a logged anomaly.
- **`foreach` over `ChildIndices` while mutating it:** `TryEnterChildSOI` removes/adds child indices *during* iteration of `parent.ChildIndices` (`GameWorld.cs:88-102`). Snapshot to an array/`for` index, or break immediately on first match (it already `return`s on match — keep that). [VERIFIED: codebase]

### Pattern 2: Floating-Origin Sync (RND-01/02)
**What:** The ship is conceptually the origin. Every frame, for each renderable body, compute its position *relative to the ship* in meters and assign that to the body's `MeshInstance3D.Position`. The camera stays near `(0,0,0)`.
**When to use:** Every frame, in the RenderBridge `_Process`, after the sim has updated.
**Why it works:** `UniVec3.ToLocalDouble(observer)` already returns `this - observer` as a `Double3` in meters — exactly the camera-relative vector needed. Because bodies in the same space are within render-friendly distances of the ship (or are far specks at honest 1:1 scale), the resulting `Vector3` fits single-precision render math without jitter. [VERIFIED: codebase `UniVec3.cs:152-162`]
```csharp
// Source: built on existing UniVec3.ToLocalDouble [VERIFIED: codebase UniVec3.cs:157]
UniObject ship = world.GameObjects[shipIndex];
UniObject parent = world.GameObjects[ship.ParentIndex];   // the current render space
foreach (int idx in parent.ChildIndices)                  // RND-02: only in-space bodies
{
    UniObject body = world.GameObjects[idx];
    if (body == null || idx == shipIndex) continue;
    Double3 rel = body.LocalPos.ToLocalDouble(ship.LocalPos); // meters, ship-relative
    meshes[idx].Position = new Vector3((float)rel.X, (float)rel.Y, (float)rel.Z);
}
```
Note: `ship.LocalPos` and `body.LocalPos` share the same `Scale` only when in the same space — they are siblings under the same parent, so this holds. The parent body itself (e.g. the star when in Star space) is at the parent origin; render it from `-ship.LocalPos` converted into the parent frame if it must appear as geometry. [ASSUMED — verify the parent-as-geometry case during implementation]

### Pattern 3: Virtual-Joystick Steering (FLT-01 / D-01, D-02, D-05)
**What:** A *visible* virtual cursor that lives in a screen-center deadzone. Mouse relative motion accumulates into the cursor; the ship rotates toward the cursor at a rate proportional to cursor distance from center; when the cursor is centered (in deadzone), rotation is zero (D-02 hold-attitude).
**Critical Godot subtlety:** Do **not** use `MOUSE_MODE_CAPTURED` naively — captured mode recenters the OS cursor each frame and only yields relative deltas, which destroys the notion of an absolute on-screen cursor position. Two viable approaches:
1. Keep mouse **visible/confined** (`MOUSE_MODE_CONFINED` or default) and read the real cursor position relative to viewport center.
2. Use captured mode for raw input but **accumulate `event.relative` into a software cursor `Vector2`**, clamp it to the deadzone radius, and draw your own reticle. (Captured mode is preferred to stop the OS cursor leaving the window during play.)
[CITED: docs.godotengine.org InputEventMouseMotion; forum.godotengine.org captured-mouse recenter]
```csharp
// Source: synthesized from Godot input docs [CITED]
// In _UnhandledInput:
if (@event is InputEventMouseMotion m)
    _cursor += m.Relative * _sensitivity;          // accumulate
_cursor = _cursor.LimitLength(_maxCursorRadius);   // clamp to deadzone bounds

// In _Process(delta): steer toward cursor
Vector2 steer = _cursor / _maxCursorRadius;        // -1..1 each axis
if (steer.Length() < _deadzoneFraction) steer = Vector2.Zero;  // D-02 hold-attitude
float yaw   = -steer.X * _turnRate * (float)delta;
float pitch = -steer.Y * _turnRate * (float)delta;
float roll  = (Input.GetActionStrength("roll_left") - Input.GetActionStrength("roll_right")) * _rollRate * (float)delta;
// Apply as local rotation so it composes with current attitude (no auto-level, D-02):
shipBasis = shipBasis * new Basis(Vector3.Right, pitch)
                      * new Basis(Vector3.Up, yaw)
                      * new Basis(Vector3.Forward, roll);
shipBasis = shipBasis.Orthonormalized();           // prevent drift accumulation
```
- `Orthonormalized()` each frame prevents the basis from skewing over thousands of multiplications — important for "no uncontrolled drift" (success criterion 1). [CITED: Godot Basis docs]
- Forward delta for `TranslatePos`: `forward = -shipBasis.Z` (Godot's -Z is forward), then `delta = forward * currentSpeed * delta_seconds`, packed into a `Double3`.

### Pattern 4: Distance-Scaled Speed Envelope (FLT-03 / D-06, D-07, D-08)
**What:** `contextMax = curve(distanceToNearestSurface)`; eased frame-to-frame; `actualSpeed = throttle01 * contextMax`.
```csharp
// Source: derived from D-06/07/08 [locked design] + UniVec3.Distance [VERIFIED: codebase]
double nearest = double.MaxValue;
foreach (int idx in parent.ChildIndices) {
    var b = world.GameObjects[idx];
    if (b == null || idx == shipIndex) continue;
    double surfaceDist = UniVec3.Distance(ship.LocalPos, b.LocalPos) - b.RadiusMeters;
    nearest = System.Math.Min(nearest, System.Math.Max(surfaceDist, 0));
}
double targetMax = Mathf.Clamp(nearest * _speedPerMeter, _minSpeed, _maxSpeed); // shape = tuning (D)
_contextMax = Mathf.Lerp(_contextMax, targetMax, _easing * delta);  // D-07 smooth, no SOI snap
double actualSpeed = _throttle01 * _contextMax;                     // D-08
```
- Curve shape, `_speedPerMeter`, `_minSpeed`, `_maxSpeed`, `_easing` are all **tuning discretion** (D). Easing across the frame is what hides SOI-boundary snaps. [locked: D-07]
- `RadiusMeters` is a new per-body field (see WORLD-01 extension).

### Pattern 5: Adaptive Unit Ladder (HUD-01 / D-10)
See Code Examples. Pure formatting; keep displayed number in ~1–9999.

### Anti-Patterns to Avoid (cross-cutting)
- **Mutating sim state from the RenderBridge or HUD.** They are read-only consumers of `GameWorld`. Only `FlightController` (via `TranslatePos`) changes the world.
- **Spawning/destroying `MeshInstance3D` every frame.** Create once per body, reposition each frame; toggle `Visible` when a body leaves the current space (RND-02) rather than freeing nodes.
- **Putting per-body color logic in the dither shader.** D-13 keeps colors in materials; the shader stays a global quantizer.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| High-precision position math | New big-coordinate type | Existing `UniVec3`/`Long3`/`Double3` | Already solved, SIMD-optimized, the locked architecture |
| Floating-origin offset | Manual world-translate bookkeeping | `UniVec3.ToLocalDouble(observer)` | Already computes observer-relative meters [VERIFIED: codebase] |
| 8-bit / dithered look | Per-body quantize logic | `dithering.gdshader` (full-screen) | Already wired, already produces the look (D-13) |
| Sphere geometry | Custom mesh generation | `SphereMesh` primitive | Native, with `radial_segments`/`rings` LOD knobs |
| Bloom/glow halo | Custom blur passes | `Environment.glow_*` | Native HDR glow; emissive >1.0 blooms (D-14) |
| Rotation composition | Euler-angle bookkeeping | `Basis` multiply + `Orthonormalized()` | Avoids gimbal/drift; clean hold-attitude (D-02) |
| Distance compares in hot loops | Repeated `Magnitude()`/`Sqrt` | `MagnitudeSq()` (exists) / add `DistanceSq` | `Sqrt` is the documented hot-loop cost (CONCERNS.md) |
| Keybinding | Hardcoded scancodes | `InputMap` actions in `project.godot` | Rebindable, idiomatic, testable |

**Key insight:** Almost everything Phase 1 needs already exists either in the engine code or in Godot. The work is *wiring and hardening*, not building primitives. The one genuinely new algorithmic piece is the iterative transition loop (STAB-01); everything else composes existing parts.

## Runtime State Inventory

> This phase **rewrites** `TrySpaceTransition` (a refactor) and **modifies** `TestSetup`/`Main.tscn`. It is not a string-rename, but it touches runtime-relevant state, so the inventory is completed below.

| Category | Items Found | Action Required |
|----------|-------------|------------------|
| Stored data | None — game holds no persisted state; `GameObjects` is rebuilt every run by `SetupScene()`. No save files, no DB. (verified: no serialization in codebase; CONCERNS.md "No Persistence") | None |
| Live service config | None — fully offline single-player desktop app; no external services, ports, or daemons | None |
| OS-registered state | None — no scheduled tasks, services, or OS registrations. Only the Godot editor/runtime. | None |
| Secrets/env vars | None — `project.godot` notes "No external environment configuration required" (CLAUDE.md) | None |
| Build artifacts | `EcoSpace` C# assembly rebuilt by Godot.NET.Sdk on build; `.godot/` import cache. Editing `Main.tscn` node UIDs or the `DirectionalLight3D`→`OmniLight3D` swap requires an editor reimport/rebuild. | Rebuild via Godot editor or `dotnet build`; no manual artifact surgery |

**Net:** The only "runtime state" risk is the in-memory `GameObjects` hierarchy *during* a transition — which is exactly what STAB-01 hardens. There is no persisted or external state to migrate.

## Common Pitfalls

### Pitfall 1: Recursion-to-iteration that still re-enters the just-exited SOI
**What goes wrong:** After exiting a parent SOI, the iterative loop immediately re-enters the same SOI (the ship is at the boundary), oscillating forever or for many iterations.
**Why it happens:** The original code threaded an `excludeIndex` precisely to prevent this on the recursive path (`TrySpaceTransition(obj, exitedIndex)`). A naive loop rewrite that drops `excludeIndex` reintroduces the oscillation.
**How to avoid:** Carry `lastExitedIndex` across loop iterations and pass it as `excludeIndex` to `TryEnterChildSOI` (see Pattern 1). Reset it only after a *successful entry into a different* SOI. [VERIFIED: codebase `GameWorld.cs:48-52`]
**Warning signs:** Transition log spams `↑`/`↓` for the same indices in one frame; frame time spikes near boundaries.

### Pitfall 2: Null-slot dereference during transition
**What goes wrong:** `RemoveGameObject` leaves `null` in `GameObjects`; a stale `ParentIndex`/`ChildIndices` entry points at it; the transition dereferences null and crashes.
**Why it happens:** `TryExitParentSOI`/`TryEnterChildSOI` assume non-null lookups (CONCERNS.md lines 19-23).
**How to avoid:** Route every `GameObjects[i]` through a null-safe `Get(i)` and bail to `false` on null. Phase 1 does not remove bodies, but harden anyway — STAB-01 explicitly requires null-safety.
**Warning signs:** `NullReferenceException` in `TryEnterChildSOI`/`TryExitParentSOI` stack frames.

### Pitfall 3: Mutating `ChildIndices` while iterating it
**What goes wrong:** `TryEnterChildSOI` calls `parent.ChildIndices.Remove(...)` and `candidate.ChildIndices.Add(...)` inside `foreach (int siblingIndex in parent.ChildIndices)`. Mutating a `List<int>` mid-`foreach` throws `InvalidOperationException`.
**Why it happens:** The current code gets away with it only because it `return`s immediately after the mutation, exiting the enumerator. Any refactor that continues the loop after a mutation will throw.
**How to avoid:** Keep the "find first, mutate, return" shape, or snapshot `parent.ChildIndices` to a local array before iterating. [VERIFIED: codebase `GameWorld.cs:88-102`]
**Warning signs:** `InvalidOperationException: Collection was modified`.

### Pitfall 4: Choosing the wrong floating-origin anchor → jitter
**What goes wrong:** If body transforms are computed relative to the *parent body* (e.g. the star) instead of the *ship*, the ship can sit thousands of render-units from origin, and single-precision rendering jitters/snaps.
**Why it happens:** RND-01 specifically requires the *player* at origin; it's tempting to anchor on the space's parent.
**How to avoid:** Always pass `ship.LocalPos` as the observer to `ToLocalDouble`. The camera stays at/near origin; everything is ship-relative. [CITED: docs.godotengine.org large_world_coordinates — "increase precision by keeping the player near origin"]
**Warning signs:** Distant geometry shimmers/vibrates; precision worsens the farther the ship travels from a body.

### Pitfall 5: Rendering out-of-space bodies as geometry
**What goes wrong:** Drawing bodies that are not in the ship's current parent space violates RND-02 and (at honest 1:1 scale) puts geometry at absurd distances, causing far-plane clipping and precision blowups.
**Why it happens:** Iterating all `GameObjects` instead of only `ship.Parent.ChildIndices`.
**How to avoid:** Render only the current parent's children (and optionally the parent body itself). Hide/skip everything else (Phase 2's skybox handles out-of-space objects). [VERIFIED: codebase `RND-02` requirement; `GameWorld.cs` ChildIndices model]
**Warning signs:** Z-fighting at the far plane; bodies popping at extreme distances.

### Pitfall 6: Material colors washed out by the dither threshold
**What goes wrong:** `dithering.gdshader` collapses each pixel to pure `white`/`black` based on `avg < threshold` (it is currently a 1-bit quantizer!). Per-body hues (D-13) may all map to the same side of the threshold and become indistinguishable.
**Why it happens:** The existing shader averages RGB and compares to one threshold → effectively monochrome output, not an 8-bit palette. [VERIFIED: codebase `dithering.gdshader:41-42`]
**How to avoid:** This is a real tension to resolve in planning. Options: (a) tune `threshold`/`dithering_size` and pick body base colors with distinct *luminance* so they survive; (b) extend the shader to a small color palette / per-channel quantize so hue survives (D-13 says "bodies stay distinguishable by hue" — the current shader cannot do that as written). Flag for the planner: **achieving "per-body distinct colors" (D-13) likely requires extending the dither shader beyond its current 1-bit black/white output.** [VERIFIED: codebase + locked decision tension]
**Warning signs:** All planets render as identical white blobs on black.

### Pitfall 7: OmniLight range/attenuation wrong at 1:1 distances
**What goes wrong:** A star at 1.496e11 m (1 AU) from a planet needs an enormous `omni_range`; with the wrong `omni_attenuation`, planets receive ~0 light even inside range, or the light is clipped by range entirely.
**Why it happens:** Godot's `omni_range` is a hard cutoff and physical attenuation (`attenuation=1.0`) falls off as inverse-square — at AU scale a planet gets negligible energy. Godot also has documented precision issues for "lights with very long range." [CITED: github.com/godotengine/godot#98655; OmniLight3D docs]
**How to avoid:** This is in the floating-origin render frame, so distances are in render meters (ship-relative), not raw AU — set `omni_range` to comfortably cover the rendered scene, use low `omni_attenuation` (≈0 for near-linear), and crank `light_energy`. Set `shadow_enabled=false` (RND-04). Treat range/energy as tuning (D). [CITED: OmniLight3D docs; locked D-16]
**Warning signs:** Planet fully dark on its lit side, or the terminator missing.

### Pitfall 8: Captured-mouse recenter breaks the visible steering cursor
**What goes wrong:** Using `MOUSE_MODE_CAPTURED` and trying to read absolute cursor position yields a cursor pinned to center — the visible steering reticle (D-05) can't move.
**Why it happens:** Captured mode recenters the OS cursor every frame and only provides `relative` deltas. [CITED: forum.godotengine.org; github.com/godotengine/godot#56669]
**How to avoid:** Accumulate `event.relative` into a software `Vector2` cursor, clamp to the deadzone, and draw your own reticle (Pattern 3). Don't query `GetViewport().GetMousePosition()` for steering in captured mode.
**Warning signs:** Steering reticle stuck at center; ship won't turn.

### Pitfall 9: Speed envelope snapping at SOI boundaries
**What goes wrong:** Because "nearest body" and scale change discontinuously at an SOI crossing, an un-eased `contextMax` jumps, producing a visible speed snap — violating D-07.
**Why it happens:** Computing `contextMax` directly from instantaneous distance without smoothing.
**How to avoid:** Ease `contextMax` toward its target each frame (`Mathf.Lerp`, Pattern 4). The lerp absorbs the discontinuity. [locked: D-07]
**Warning signs:** Ship lurches in/out of warp speed exactly when the transition log prints.

## Code Examples

### Adaptive speed unit ladder (HUD-01 / D-10)
```csharp
// Source: derived from D-10 ladder m/s → km/s → AU/s → ly/s [locked design]
private const double AU = 1.495978707e11;   // meters
private const double LY = 9.4607304725808e15; // meters

public static string FormatSpeed(double metersPerSecond)
{
    double v = System.Math.Abs(metersPerSecond);
    if (v < 1_000.0)          return $"{metersPerSecond:0.#} m/s";
    if (v < AU)               return $"{metersPerSecond / 1_000.0:0.#} km/s";
    if (v < LY)               return $"{metersPerSecond / AU:0.###} AU/s";
    return $"{metersPerSecond / LY:0.###} ly/s";
}
```

### Emissive unshaded star material + glow (RND-04 / D-14)
```csharp
// Source: Godot StandardMaterial3D + Environment glow [CITED: docs.godotengine.org environment_and_post_processing]
var starMat = new StandardMaterial3D
{
    ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, // star isn't lit by anything
    AlbedoColor = new Color(1.0f, 0.95f, 0.6f),
    EmissionEnabled = true,
    Emission = new Color(1.0f, 0.95f, 0.6f),
    EmissionEnergyMultiplier = 4.0f,   // >1 so it blooms; tuning (D)
};
// WorldEnvironment.Environment:
//   GlowEnabled = true; GlowBloom tuned; HDR 2D enabled in project settings for proper threshold
// OmniLight3D at star: ShadowEnabled = false (RND-04); OmniRange/LightEnergy tuned for render-frame distances
```

### Per-body planet material (RND-03 / D-13)
```csharp
// Source: Godot StandardMaterial3D [CITED] — but see Pitfall 6: the dither shader currently 1-bits output
var planetMat = new StandardMaterial3D
{
    AlbedoColor = earthBlue,            // authored per body
    // lit by the star's OmniLight3D; keep default shading so terminator shows (D-16)
};
```

### Iterative transition smoke check (STAB-01 verification)
```csharp
// Source: maps to success criterion 5 [VERIFIED: requirement]
// In a debug/test harness: place the ship just inside PlanetA SOI, apply one large delta that
// carries it past PlanetA's SOI AND into PlanetB's SOI in a single TranslatePos call.
// Expect: exactly one ↑ then one ↓ in the log, no exception, ship.ParentIndex == _planetB,
//         and PrintState shows consistent parent/child links (no orphans, no double-parenting).
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| Recursive SOI transition (current code) | Iterative, null-safe loop | This phase (STAB-01) | Removes stack-stacking crash risk |
| `DirectionalLight3D` placeholder | `OmniLight3D` at star (D-16) | This phase | Correct day/night terminator at 1:1 |
| Magenta HUD accent | Phosphor-green CRT (D-09) | This phase | Aesthetic lock |
| Autopilot `_Process` (A→B) in TestSetup | Player-driven flight controller | This phase | `TestSetup._Process` autopilot removed/replaced |
| Engine "Large World Coordinates" flag | Custom `UniVec3` floating origin (already chosen) | Pre-existing | Do NOT enable the engine flag — redundant and costly |

**Deprecated/outdated for this project:**
- Treating Godot's built-in large-world-coordinates as a solution path — superseded by the in-repo `UniVec3` engine; it is the locked architecture (CLAUDE.md constraint: "do not replace the precision/space system").

## Assumptions Log

| # | Claim | Section | Risk if Wrong |
|---|-------|---------|---------------|
| A1 | Rendering the parent body itself (e.g. star when in Star space) is handled by converting `-ship.LocalPos` into the parent frame | Pattern 2 | Star/parent body mispositioned or missing; needs a render-frame check during implementation |
| A2 | `dithering.gdshader` as written outputs 1-bit B/W, so D-13 "distinct hues" needs shader extension or careful luminance choice | Pitfall 6 | If hues must survive, shader work is in-scope and must be planned; if mono is acceptable, D-13 is reinterpreted — confirm with user |
| A3 | OmniLight range/energy tuning in the *render* (ship-relative) frame is sufficient for a correct terminator | Pitfall 7 | If light must be authored in absolute AU, Godot precision limits at long range may bite |
| A4 | GDScript vs C# for HUD is open; `FPS.gd` pattern is the baseline | Standard Stack | Low — both work; planner picks |
| A5 | No `DistanceSq` on `UniVec3` is needed unless profiling shows the per-frame nearest-body scan is hot | Don't Hand-Roll | Low — `MagnitudeSq` already exists; add only if measured |

**Note:** A2 (the dither shader 1-bit reality vs D-13's "distinguishable by hue") is the highest-impact assumption and should be surfaced to the user/planner before locking the body-rendering plan.

## Open Questions (RESOLVED)

> All three resolved during /gsd-plan-phase 1 and baked into the plans (see per-question RESOLVED notes).

1. **Does D-13 require extending the dither shader, or is luminance-distinct monochrome acceptable?**
   - **RESOLVED:** User chose to **extend the shader to a color palette** (hue-preserving). Planned in Plan 01-02 Task 3. Monochrome-luminance fallback explicitly NOT used.
   - What we know: the shader currently collapses to black/white via one `threshold` (verified in source). Bodies cannot differ by *hue* through it as written.
   - What's unclear: whether "distinguishable" can be met by distinct *brightness* under the existing 1-bit dither, or whether a small palette must be added.
   - Recommendation: plan a small spike — try luminance-distinct base colors first (cheapest, no shader change); if indistinguishable, extend the shader to a 2–4 color palette. Get user confirmation since it changes the look.

2. **Findability of distant bodies at true 1:1 with no skybox (the CONTEXT.md open tension).**
   - **RESOLVED:** Off-screen edge target-direction marker adopted, planned in Plan 01-04 Task 2 (HUD-03/04 scope). Pure 1:1 kept honest; navigational HUD affordance only, no visibility floor on bodies.
   - What we know: D-12/D-15 give name+distance only and tiny specks; no bearing, no skybox until Phase 2.
   - What's unclear: whether players can locate/aim at a cycled target.
   - Recommendation: within HUD-03/04 scope, add a minimal **off-screen edge direction marker / arrow** pointing toward the active target (project the target's render-relative direction onto the viewport edge). Cheap, stays in scope, resolves the tension without violating 1:1. Confirm with user (they chose "pure 1:1" — keep scale honest, only add a navigational HUD affordance, not a visibility floor on the body).

3. **Parent body as geometry vs. skybox.** (links to A1)
   - **RESOLVED:** Parent body rendered as a normal `MeshInstance3D` at its render-relative location for Phase 1 (planned in Plan 01-01 Task 2); out-of-space ancestors deferred to Phase 2.
   - What we know: RND-02 renders current-space objects; the parent (star when in Star space) is special.
   - Recommendation: render the parent body as a normal `MeshInstance3D` positioned at its render-relative location for Phase 1; out-of-space ancestors wait for Phase 2.

## Environment Availability

| Dependency | Required By | Available | Version | Fallback |
|------------|------------|-----------|---------|----------|
| Godot Engine (Mono) | Entire phase | ✓ (project pinned) | 4.6.2 | — |
| .NET SDK | C# build | ✓ (implied by working repo) | 8.0 (net8.0 target) | — |
| DirectX 12 GPU | Forward+ / glow | ✓ (project targets d3d12) | — | Forward+ on Vulkan if cross-platform later |
| AVX2 CPU | SIMD math paths | ✓ recommended | — | Scalar fallback already in `Double3` |

**Missing dependencies with no fallback:** None.
**Missing dependencies with fallback:** None blocking — all are present in the existing working project.

*No external package installation occurs in this phase; the audit is environment-only.*

## Security Domain

> `security_enforcement: true` in config, so this section is included. **Context:** EcoSpace is a single-player, fully offline desktop game with no network, no auth, no user accounts, no persistence, and no untrusted input. The standard web-app threat model (injection, authn/z, session) does not apply. ASVS is largely N/A; the relevant "security" concerns are runtime-robustness (crash safety), which STAB-01 directly addresses.

### Applicable ASVS Categories

| ASVS Category | Applies | Standard Control |
|---------------|---------|-----------------|
| V2 Authentication | no | No users/accounts; offline single-player |
| V3 Session Management | no | No sessions |
| V4 Access Control | no | No multi-user access |
| V5 Input Validation | partial | Input is local keyboard/mouse only — not a security surface, but **SOI/transition input validation matters for crash safety** (CONCERNS.md: negative SOI, null slots). Validate indices/null in the transition family (STAB-01). |
| V6 Cryptography | no | No secrets, no data at rest, no comms |

### Known Threat Patterns for {Godot single-player desktop}

| Pattern | STRIDE | Standard Mitigation |
|---------|--------|---------------------|
| Null-slot dereference crash in transition | Denial of Service (local) | Null-safe `Get(i)` accessor; iteration cap (STAB-01, Pattern 1) |
| Unbounded transition loop hangs frame | Denial of Service (local) | `MaxIterations` cap converts hang → logged anomaly (Pattern 1) |
| Out-of-range numeric input (negative SOI, NaN delta) | Tampering / crash | Validate `soiMeters >= 0` and finite deltas where introduced (CONCERNS.md security note) — minimal, only where STAB-01 touches |
| Far-plane / precision overflow at large coords | Denial of Service (visual corruption) | Floating-origin keeps render coords small (RND-01); render only in-space bodies (RND-02) |

**Net security posture:** No conventional security surface. The only meaningful robustness work is the crash/hang hardening already mandated by STAB-01. No cryptography, secrets, network, or auth in scope.

## Sources

### Primary (HIGH confidence)
- Codebase: `Scripts/Universe/GameWorld.cs`, `TestSetup.cs`, `UniObject.cs`, `Math/UniVec3.cs`, `UniRenderer.cs`, `Shaders/dithering.gdshader`, `Main.tscn`, `project.godot` — verified APIs, hierarchy, transition logic, shader behavior
- `.planning/codebase/CONCERNS.md` — crash modes for STAB-01 (recursion, null slots, double-reparent, ChildIndices mutation)
- `.planning/phases/01-in-system-flight-mvp/01-CONTEXT.md` — locked decisions D-01..D-16
- `.planning/REQUIREMENTS.md`, `.planning/ROADMAP.md` — requirement text + success criteria
- `CLAUDE.md` — stack/architecture constraints (do-not-replace-engine, namespace/style)

### Secondary (MEDIUM confidence)
- docs.godotengine.org — Large World Coordinates, Environment & post-processing (glow), InputEventMouseMotion / mouse capture, OmniLight3D
- github.com/godotengine/godot#98655 — long-distance rendering precision tracker (OmniLight long-range caveat)
- github.com/godotengine/godot#56669 — captured-mouse relative-motion behavior

### Tertiary (LOW confidence)
- forum.godotengine.org / community guides on mouse capture patterns — cross-checked against official docs

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH — all built-in Godot nodes + existing in-repo engine; zero external packages to verify
- Architecture: HIGH — directly grounded in verified codebase APIs and locked CONTEXT decisions
- STAB-01 transition fix: HIGH — exact crash modes read from source + CONCERNS.md
- Rendering specifics (dither/hue, OmniLight at 1:1): MEDIUM — Godot behavior cited from docs/issues, but the dither-hue tension (A2) and light-range tuning (A3) need an implementation spike
- Flight input (captured-mouse subtlety): MEDIUM-HIGH — pitfall confirmed via official docs + issue tracker

**Research date:** 2026-06-13
**Valid until:** 2026-07-13 (stable — Godot 4.6 pinned; revisit only if engine version bumps or Phase 2 skybox changes render assumptions)
