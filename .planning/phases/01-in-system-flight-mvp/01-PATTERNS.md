# Phase 1: In-System Flight MVP - Pattern Map

**Mapped:** 2026-06-13
**Files analyzed:** 9 new/modified
**Analogs found:** 8 / 9 (1 partial — extended palette dither has no in-repo analog beyond the existing 1-bit shader)

## File Classification

| New/Modified File | Role | Data Flow | Closest Analog | Match Quality |
|-------------------|------|-----------|----------------|---------------|
| `Scripts/Universe/GameWorld.cs` (modify: iterative `TrySpaceTransition`) | service / sim | event-driven (SOI boundary) | itself (recursive form, lines 48-117) | exact (refactor in place) |
| `Scripts/Universe/UniObject.cs` (modify: add `Name`, `BaseColor`, `RadiusMeters`) | model | transform | itself (data fields, lines 68-78) | exact (extend in place) |
| `Scripts/Universe/TestSetup.cs` (modify: drop autopilot, author radii/colors) | controller / config | batch (scene build) | itself (`SetupScene`, lines 86-94) | exact (extend in place) |
| `Scripts/Flight/FlightController.cs` (new) | controller | request-response (input→motion) | `TestSetup._Process` (lines 54-82) | role-match |
| `Scripts/Render/RenderBridge.cs` (new) | service / render-bridge | streaming (per-frame sync) | `TestSetup._Process` + `GameWorld.PrintPositions` | role-match |
| `Shaders/dithering.gdshader` (modify: extend toward palette, see Pitfall 6) | shader / config | transform (per-pixel) | itself (lines 1-43) | exact (extend in place) |
| `Scripts/Universe/UniRenderer.cs` (modify: phosphor params / new palette uniforms) | config / provider | event-driven (param push) | itself (`[Export]` pattern, lines 19-106) | exact (extend in place) |
| `Scripts/HUD/Hud.cs` or `Hud.gd` (new) | component / UI | request-response (read+display) | `Scripts/FPS.gd` (lines 1-4) | role-match |
| `Main.tscn` (modify: HUD nodes, `OmniLight3D`, `WorldEnvironment` glow, phosphor label) | config / scene | — | itself (lines 1-46) | exact (extend in place) |

## Pattern Assignments

### `Scripts/Universe/GameWorld.cs` (service, event-driven — STAB-01)

**Analog:** itself — refactor the recursive `TrySpaceTransition` family into an iterative, null-safe loop. Keep all method signatures and the public `TranslatePos` entry points unchanged.

**Entry point to preserve** (`GameWorld.cs:24-40`) — flight motion feeds in here, do NOT change the bounds-check shape:
```csharp
public void TranslatePos(int index, Double3 delta)
{
    if ((uint)index < (uint)GameObjects.Count)
        TranslatePos(GameObjects[index], delta);
}

private void TranslatePos(UniObject obj, Double3 delta)
{
    obj.LocalPos += delta;
    TrySpaceTransition(obj);
}
```

**Current recursive form to REPLACE** (`GameWorld.cs:48-52`) — this is the documented stack-stacking crash source (CONCERNS.md "Recursive Space Transition Stacking"):
```csharp
private void TrySpaceTransition(UniObject obj, int excludeIndex = -1)
{
    if (TryExitParentSOI(obj, out int exitedIndex)) { TrySpaceTransition(obj, exitedIndex); return; }
    if (TryEnterChildSOI(obj, excludeIndex)) { TrySpaceTransition(obj); }
}
```
Replace with a bounded `while` loop that carries `lastExitedIndex` across iterations (RESEARCH Pattern 1; preserves the `excludeIndex` anti-oscillation contract — Pitfall 1). Add an iteration cap (`const int MaxIterations = 32`) so a math oscillation logs an anomaly instead of hanging the frame.

**Null-safe accessor to ADD and route every lookup through** (CONCERNS.md lines 19-23, "Missing Validation in UniObject Space Transitions"):
```csharp
private UniObject Get(int index)
    => (uint)index < (uint)GameObjects.Count ? GameObjects[index] : null;
```
Replace the raw `GameObjects[obj.ParentIndex]` (line 59), `GameObjects[siblingIndex]` (line 93), and `GameObjects[parent.ParentIndex]` (line 69) lookups with `Get(...)` + early `return false` on null.

**Bounds-check convention to copy** — the project's idiomatic unsigned-cast guard (`GameWorld.cs:20, 136, 194`, CLAUDE.md "Error Handling"):
```csharp
if ((uint)index < (uint)GameObjects.Count) // checks >= 0 AND < Count in one compare
```

**`foreach`-while-mutating hazard to keep working** (`GameWorld.cs:88-114`, Pitfall 3) — the current code mutates `parent.ChildIndices` and `candidate.ChildIndices` then `return`s immediately, exiting the enumerator. Keep the "find first → mutate → return" shape, or snapshot `ChildIndices` to a local array before iterating. Never `continue` the loop after a mutation.

**Transition logging to PRESERVE** (`GameWorld.cs:75, 111`, CLAUDE.md "Logging") — keep the `↑`/`↓` `GD.Print` markers for debug visibility through the rewrite:
```csharp
GD.Print($"[Transition ↑] Exited SOI of {parent.CurrentSpace}, now in {obj.CurrentSpace}");
GD.Print($"[Transition ↓] Entered SOI of {candidate.CurrentSpace} (index {siblingIndex}), now in {obj.CurrentSpace}");
```

---

### `Scripts/Universe/UniObject.cs` (model, transform — WORLD-01 extension)

**Analog:** itself — add per-body presentation fields alongside the existing public data fields.

**Public-field block to extend** (`UniObject.cs:68-78`) — follow the exact PascalCase public-field convention (CLAUDE.md "public fields use PascalCase"):
```csharp
public int             Index;
public Space           CurrentSpace;
public int             ParentIndex;
public double          SOIMeters;
public UniVec3         LocalPos;
public List<int> ChildIndices = [];
```
Add (per RESEARCH "Recommended Project Structure" and Patterns 2/4): `public string Name;`, `public Color BaseColor;` (Godot `Color` for D-13 authored hue), `public double RadiusMeters;` (D-15 true 1:1 radius; consumed by RenderBridge mesh scale and FLT-03 surface-distance). Keep them mutable public fields — `UniObject` is the project's data-class (CLAUDE.md "No public fields except in simple data types").

---

### `Scripts/Universe/TestSetup.cs` (controller/config, batch — WORLD-01)

**Analog:** itself — keep `SetupScene()` as the WORLD-01 base; remove the autopilot `_Process` loop (lines 54-82, the A→B flight that the player replaces).

**Scene-authoring pattern to reuse and extend** (`TestSetup.cs:86-94`):
```csharp
private void SetupScene()
{
    _root    = AddGameObject(-1,      new Double3(0, 0, 0),         double.MaxValue);
    _galaxy  = AddGameObject(_root,   new Double3(0, 0, 0),         5e3);
    _star    = AddGameObject(_galaxy, new Double3(0, 0, 0),         StarSOI);
    _planetA = AddGameObject(_star,   new Double3(0, 0, PlanetA_Z), PlanetSOI);
    _planetB = AddGameObject(_star,   new Double3(0, 0, PlanetB_Z), PlanetSOI);
    _ship    = AddGameObject(_planetA,new Double3(0, 0, ShipOrbitMeters), 0);
}
```
Extend: after each `AddGameObject`, set the new `Name` / `BaseColor` / `RadiusMeters` on `GameObjects[idx]` (e.g. Earth-blue + Earth radius on `_planetA`, Mars-rust on `_planetB`, yellow + emissive star on `_star`). Keep the named constants convention (`UPPER_SNAKE`/`PascalCase` consts, lines 32-37) for any new radii/colors.

**Derived-`_Process` contract** (`TestSetup.cs:54-56`, CONCERNS.md "Missing _Process Implementation") — base `GameWorld._Process` is intentionally empty; the derived class drives the loop. The new `FlightController` / `RenderBridge` either live as child nodes with their own `_Process`, or `TestSetup._Process` calls into them. Always `base._Process(delta)` first:
```csharp
public override void _Process(double delta)
{
    base._Process(delta);
    // ... per-frame work ...
}
```

---

### `Scripts/Flight/FlightController.cs` (controller, request-response — FLT-01/02/03)

**Analog:** `TestSetup._Process` (the existing per-frame sim driver that calls `TranslatePos`) for the call shape; `UniRenderer` for the `[Export]` tuning-knob convention.

**Motion call to copy** (`TestSetup.cs:62`) — the controller computes a `Double3` delta from attitude × throttle × context-max-speed and feeds the same entry point:
```csharp
TranslatePos(_ship, new Double3(0, 0, ShipSpeedMetersPerTick)); // replace the constant with forward * speed * delta
```

**Tuning knobs as `[Export]` fields** — mirror `UniRenderer`'s export-with-backing-field style (`UniRenderer.cs:41-50`) for deadzone, turn rate, throttle steps, `_speedPerMeter`, `_minSpeed`, `_maxSpeed`, `_easing` (all RESEARCH-flagged as tuning discretion). Use `Mathf.Clamp` / `Mathf.Max` guards as that file does:
```csharp
set { _resolutionScale = Mathf.Max(1, value); _material?.SetShaderParameter(...); }
```

**Distance-scaled speed math** (RESEARCH Pattern 4) — use the existing static distance helper (`UniVec3.cs:207`) over the ship's parent's children; prefer `MagnitudeSq` in the hot scan:
```csharp
public static double Distance(UniVec3 a, UniVec3 b) => (a - b).Magnitude();
```
Ease `contextMax` with `Mathf.Lerp` each frame to hide SOI snaps (Pitfall 9, D-07). Apply local rotation via `Basis` multiply + `Orthonormalized()` each frame (Pattern 3, D-02 hold-attitude); forward = `-shipBasis.Z`.

**Naming/style:** PascalCase methods, camelCase locals, `_underscore` private fields, Godot lifecycle `_Process`/`_UnhandledInput` (CLAUDE.md "Naming Patterns").

---

### `Scripts/Render/RenderBridge.cs` (service, streaming — RND-01/02/04)

**Analog:** `TestSetup._Process` (read sim state each frame) + `GameWorld.PrintPositions` (the existing pattern of walking `ParentIndex` / `ChildIndices` and converting positions).

**Floating-origin sync** (RESEARCH Pattern 2; built on `UniVec3.ToLocalDouble`, `UniVec3.cs:157-162`):
```csharp
public Double3 ToLocalDouble(in UniVec3 observer)
{
    UniVec3 delta = this - observer;
    return delta.ToDouble3();
}
```
Per frame, for each `idx` in `ship.Parent.ChildIndices` (RND-02 — only current-space bodies), compute `body.LocalPos.ToLocalDouble(ship.LocalPos)` and assign to that body's `MeshInstance3D.Position`. Always anchor on `ship.LocalPos` (Pitfall 4 — never the parent body, or render jitters).

**Hierarchy-walk convention to copy** (`GameWorld.PrintPositions`, `GameWorld.cs:134-150`, and `TestSetup._Process` lines 64-70) — iterate via integer indices with null-skip:
```csharp
int currentParent = obj.ParentIndex;
while (currentParent >= 0)
{
    var parent = GameObjects[currentParent];
    // ...
    currentParent = parent.ParentIndex;
}
```
Add `if (o == null) continue;` guards as `TestSetup.PrintState` does (`TestSetup.cs:104`).

**Mesh lifecycle** (Pitfall: don't spawn/free per frame) — create one `MeshInstance3D` + `SphereMesh` per body once, reposition each frame, toggle `Visible` when a body leaves the current space. The star gets `ShadingModeEnum.Unshaded` + emissive material + an `OmniLight3D` (`ShadowEnabled=false`, RND-04/D-16) positioned at the star's render transform (RESEARCH "Emissive unshaded star material").

---

### `Shaders/dithering.gdshader` (shader, transform — RND-03 / D-13)

**Analog:** itself. CRITICAL TENSION (Pitfall 6 / Assumption A2): the shader as written is a **1-bit black/white quantizer**, not an 8-bit palette — it cannot preserve per-body hue (D-13) without extension.

**Current quantize logic** (`dithering.gdshader:35-42`):
```glsl
ivec3 c = ivec3(round(color * 255.0));
if (dithering) { c += ivec3(dithering_pattern(fragcoord)); }
float avg = float(c.x + c.y + c.z) / 3.0;
COLOR = avg < threshold ? black : white;   // <-- collapses every pixel to black or white
```

**Uniform-declaration + ordered-Bayer pattern to preserve** (`dithering.gdshader:1-22`) — keep `screen_texture` hint, `resolution_scale` block-snapping, and the 4×4 `dithering_pattern` when extending:
```glsl
uniform sampler2D screen_texture : hint_screen_texture, filter_nearest;
uniform vec4 white: source_color = vec4(1,1,1,1);
uniform vec4 black: source_color = vec4(0,0,0,1);
```
**Planner decision required (Open Question 1):** either (a) author body base colors with distinct *luminance* so they survive the 1-bit threshold (no shader change), or (b) extend `COLOR = avg < threshold ? black : white;` to a small per-channel / palette quantize so hue survives. Surface to user before locking — it changes the look. Match the GLSL comment style already in-file (German block comments) only if matching; new comments may be English per CLAUDE.md.

---

### `Scripts/Universe/UniRenderer.cs` (config/provider, event-driven — D-09/D-13)

**Analog:** itself — add any new palette/phosphor uniforms following the existing export-property-with-shader-push pattern.

**Export-property + backing-field + null-safe push pattern to copy** (`UniRenderer.cs:30-39` and `_Ready` lines 85-95):
```csharp
[Export]
public int DitheringSize
{
    get => _ditheringSize;
    set { _ditheringSize = value; _material?.SetShaderParameter("dithering_size", value); }
}
```
For new `white`/`black` (phosphor green) or palette colors, mirror the `Color White` / `Color Black` exports (lines 63-83) and register them in `ApplyAllParameters()` (lines 98-106). Note the hardcoded shader path (`UniRenderer.cs:89`, CONCERNS.md) — extend cautiously, the load has no fallback.

---

### `Scripts/HUD/Hud.cs` or `Hud.gd` (component/UI, request-response — HUD-01..04)

**Analog:** `Scripts/FPS.gd` — the existing per-frame label-update UI script on the `CanvasLayer`.

**Per-frame label-update pattern** (`FPS.gd:1-4`):
```gdscript
extends Label

func _process(_delta):
    text = "FPS: " + str(Engine.get_frames_per_second())
```
HUD reads (read-only, never mutates sim — RESEARCH anti-pattern) ship throttle/speed, `CurrentSpace`, nearest-body distance, and active target each frame and writes label text. If implemented in C#, follow `UniRenderer` namespace/`partial class`/`_Process` conventions; GDScript is acceptable per Discretion D.

**Adaptive unit ladder** (RESEARCH "Code Examples", HUD-01/D-10) — keep displayed number ~1–9999 across m/s → km/s → AU/s → ly/s.

**Context label** (D-11): `CurrentSpace` tier + nearest body, e.g. `STAR SPACE · nearest: PLANET A`. Source the space tier from `UniObject.Space` (`UniObject.cs:15-18`).

**Target cycle** (HUD-04/D-12): cycle `ship.Parent.ChildIndices`, show `Name` + adaptive distance via `UniVec3.Distance`. Consider the off-screen direction-marker affordance (RESEARCH Open Question 2 / CONTEXT open tension) — confirm with user.

---

### `Main.tscn` (config/scene)

**Analog:** itself — extend the existing scene graph.

**Existing nodes to build on** (`Main.tscn:12-46`): `Main` (Node3D, `TestSetup` script), `CanvasLayer` (holds `FullscreenRect` dither + `FPSLabel`), `Camera3D` (player eye at floating origin), `MeshInstance3D`+`SphereMesh` (ready sphere primitive), `DirectionalLight3D` (placeholder to REPLACE with `OmniLight3D` per D-16), `WorldEnvironment`/`Environment` (currently `background_mode = 1`; add `GlowEnabled` for D-14 bloom).

**Phosphor-green change** (D-09) — the magenta accent to replace (`Main.tscn:29`):
```
modulate = Color(1, 0, 1, 1)   # FPSLabel magenta → phosphor green
```
Add HUD `Control`/`Label`/`TextureRect` nodes under `CanvasLayer` (crosshair HUD-03, steer reticle D-05, speed/context/target labels). Editing node UIDs / swapping the light requires an editor reimport/rebuild (RESEARCH Runtime State Inventory).

## Shared Patterns

### Integer-index object model + null-safe bounds check
**Source:** `GameWorld.cs:20, 136, 194` (`(uint)index < (uint)GameObjects.Count`); `TestSetup.cs:104` (`if (o == null) continue;`)
**Apply to:** GameWorld (STAB-01), RenderBridge, FlightController, HUD — every `GameObjects[i]` access.
```csharp
if ((uint)index < (uint)GameObjects.Count) { /* safe */ }
// and inside the transition family:
private UniObject Get(int index) => (uint)index < (uint)GameObjects.Count ? GameObjects[index] : null;
```

### Floating-origin position conversion
**Source:** `UniVec3.cs:157-162` (`ToLocalDouble`), `:207` (`Distance`), `:167-174` (`MagnitudeSq`/`Magnitude`)
**Apply to:** RenderBridge (per-body render transform), FlightController (nearest-body speed scaling), HUD (target distance). Prefer `MagnitudeSq` in per-frame scans; the engine math is the locked floating-origin solution — never enable Godot "Large World Coordinates".

### `[Export]` tuning knob with backing field + null-safe shader push
**Source:** `UniRenderer.cs:19-50` (export get/set + `_material?.SetShaderParameter`), `_Ready` + `ApplyAllParameters` (lines 85-106)
**Apply to:** UniRenderer (palette/phosphor), FlightController (speed/turn/throttle tuning), RenderBridge (light range/energy, mesh subdivision). All RESEARCH "tuning discretion" values exposed here, guarded with `Mathf.Clamp`/`Mathf.Max`.

### `↑`/`↓` `GD.Print` transition + state logging
**Source:** `GameWorld.cs:75, 111`; `TestSetup.cs:73-75, 98-110` (`PrintState`)
**Apply to:** GameWorld (keep through STAB-01 rewrite) and the STAB-01 smoke test (expect exactly one `↑` then one `↓`, no exception, consistent parent/child links).

### Single-threaded derived-`_Process` driver
**Source:** `GameWorld.cs:203` (empty base) + `TestSetup.cs:54-56` (`base._Process(delta)` then work)
**Apply to:** FlightController, RenderBridge, HUD — all per-frame work hangs off `_Process(double delta)`; sim mutates only via `TranslatePos`; render/HUD are read-only consumers.

## No Analog Found

| File | Role | Data Flow | Reason |
|------|------|-----------|--------|
| Extended palette / per-channel quantize in `dithering.gdshader` | shader | transform | The only in-repo shader is the existing 1-bit B/W dither. Preserving per-body *hue* (D-13) has no codebase precedent — it is the highest-risk open question (A2/Open Q1). Planner must decide luminance-distinct colors (no change) vs. shader extension, with user confirmation. Reference RESEARCH Pitfall 6 patterns rather than a codebase analog. |

## Metadata

**Analog search scope:** `Scripts/Universe/` (GameWorld, TestSetup, UniObject, UniRenderer, Math/UniVec3), `Scripts/FPS.gd`, `Shaders/dithering.gdshader`, `Main.tscn`, `.planning/codebase/CONCERNS.md`
**Files scanned:** 8 source/scene files + CONCERNS.md
**Pattern extraction date:** 2026-06-13
