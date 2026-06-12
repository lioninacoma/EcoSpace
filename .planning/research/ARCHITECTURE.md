# Architecture Research

**Domain:** First-person retro space sim game layer on a multi-scale universe engine
**Researched:** 2026-06-12
**Confidence:** HIGH

---

## Standard Architecture

### System Overview

```
┌─────────────────────────────────────────────────────────────────────┐
│                        INPUT LAYER                                   │
│  ┌──────────────────────────────────────────────────────────────┐   │
│  │  PlayerShip (Node3D)  —  FlightController.cs                 │   │
│  │  Input → pitch/roll/yaw rotations on Transform.Basis         │   │
│  │  Input → throttle → _forwardSpeed (lerped)                   │   │
│  │  Reads: SpeedScaler output for _maxSpeed ceiling             │   │
│  └──────────────────────────────────────────────────────────────┘   │
├─────────────────────────────────────────────────────────────────────┤
│                     SIMULATION LAYER (existing)                      │
│  ┌─────────────────────┐   ┌──────────────────────────────────┐     │
│  │  GameWorld.cs       │   │  UniObject[]  (array of structs) │     │
│  │  TranslatePos()     │   │  LocalPos: UniVec3               │     │
│  │  TrySpaceTransition │   │  ParentIndex, ChildIndices       │     │
│  │  SOI reparenting    │   │  CurrentSpace, SOIMeters         │     │
│  └─────────┬───────────┘   └──────────────────────────────────┘     │
│            │  reads/writes all UniObject positions                   │
├────────────┼────────────────────────────────────────────────────────┤
│            │          GAME-LAYER BRIDGE                              │
│  ┌─────────▼───────────┐   ┌───────────────────────┐               │
│  │  WorldTranslator.cs │   │  SpeedScaler.cs        │               │
│  │  Converts player    │   │  Reads player parent   │               │
│  │  flight delta →     │   │  SOI radius, returns   │               │
│  │  GameWorld.         │   │  maxSpeed each frame   │               │
│  │  TranslatePos()     │   └───────────────────────┘               │
│  │  Negates for world  │                                             │
│  │  shift (all nodes)  │                                             │
│  └─────────┬───────────┘                                             │
├────────────┼────────────────────────────────────────────────────────┤
│            │          RENDER-SYNC LAYER                              │
│  ┌─────────▼───────────────────────────────────────┐               │
│  │  RenderSync.cs                                   │               │
│  │  Each frame: iterates in-SOI siblings            │               │
│  │  Computes relPos (UniVec3 → Double3 → Vector3)   │               │
│  │  Writes Node3D.Position for each rendered body   │               │
│  └─────────┬───────────────────┬───────────────────┘               │
│            │                   │                                     │
│  ┌─────────▼──────┐  ┌────────▼──────────────────────────────┐     │
│  │  BodyNodes     │  │  SkyboxController.cs                   │     │
│  │  (Node3D with  │  │  Listens for SOI transition event      │     │
│  │  MeshInstance) │  │  Rebuilds star-direction array         │     │
│  │  Planets/stars │  │  Calls sky_material.SetShaderParameter │     │
│  │  in current    │  │  sky.ProcessMode = REALTIME when dirty │     │
│  │  parent space  │  └────────────────────────────────────────┘     │
│  └────────────────┘                                                  │
├─────────────────────────────────────────────────────────────────────┤
│                     RENDERING (existing)                             │
│  ┌──────────────────────────────────────────────────────────────┐   │
│  │  WorldEnvironment (Sky ShaderMaterial)                       │   │
│  │  MeshInstance3D per in-SOI body                              │   │
│  │  UniRenderer (ColorRect dithering post-process)              │   │
│  │  Camera3D child of PlayerShip                                │   │
│  └──────────────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────────┘
```

### Component Responsibilities

| Component | Responsibility | File |
|-----------|----------------|------|
| FlightController | Reads input, rotates PlayerShip transform, produces a forward-delta vector each frame | `Scripts/Universe/Player/FlightController.cs` |
| WorldTranslator | Accepts flight delta, calls `GameWorld.TranslatePos(playerIndex, delta)`, then reads updated sibling positions and shifts all active Godot Node3D positions by `-delta` to implement floating origin | `Scripts/Universe/Player/WorldTranslator.cs` |
| SpeedScaler | Reads player's current parent SOI radius each frame, maps it to a maxSpeed value via a curve; feeds maxSpeed ceiling to FlightController | `Scripts/Universe/Player/SpeedScaler.cs` |
| RenderSync | Each frame, iterates UniObjects that are siblings of the player (same parent), converts each `LocalPos` to a `Vector3` relative to the player, writes that to the corresponding Node3D's `Position` | `Scripts/Universe/Rendering/RenderSync.cs` |
| BodyPool | Maintains a pooled set of `Node3D + MeshInstance3D` nodes for in-SOI bodies; activates/deactivates on SOI transition | `Scripts/Universe/Rendering/BodyPool.cs` |
| SkyboxController | On SOI transition, recomputes directions to all out-of-SOI significant objects; encodes them into a texture or uniform array; pushes to the sky `ShaderMaterial` via `SetShaderParameter` | `Scripts/Universe/Rendering/SkyboxController.cs` |
| GameWorld (existing) | World state, SOI transitions, position arithmetic — unchanged | `Scripts/Universe/GameWorld.cs` |
| UniRenderer (existing) | Dithering post-process — unchanged | `Scripts/Universe/UniRenderer.cs` |

---

## Recommended Project Structure

```
Scripts/
└── Universe/
    ├── GameWorld.cs          # (existing) world state & SOI logic
    ├── UniObject.cs          # (existing) object model
    ├── UniRenderer.cs        # (existing) dithering post-process
    ├── TestSetup.cs          # (existing) → eventually replaced by PlayerScene
    │
    ├── Player/               # Player-owned systems
    │   ├── FlightController.cs   # input → rotation + throttle
    │   ├── WorldTranslator.cs    # flight delta → GameWorld + node shift
    │   └── SpeedScaler.cs        # SOI radius → maxSpeed
    │
    ├── Rendering/            # Game-layer render bridge
    │   ├── RenderSync.cs         # UniObject positions → Node3D transforms
    │   ├── BodyPool.cs           # pooled Node3D/MeshInstance nodes
    │   └── SkyboxController.cs   # SOI transition → sky shader update
    │
    └── Math/                 # (existing) UniVec3, Double3, Long3
        ├── UniVec3.cs
        ├── Double3.cs
        └── Long3.cs

Shaders/
├── dithering.gdshader        # (existing) post-process
├── crt.gdshader              # (existing, unused)
├── star_body.gdshader        # NEW: unlit sphere for star geometry
└── skybox.gdshader           # NEW: shader_type sky; encodes star directions
```

### Structure Rationale

- **Player/:** Flight and input are owned entirely by the player subsystem. They read from GameWorld but never mutate UniObject fields except through `GameWorld.TranslatePos`. Clean boundary.
- **Rendering/:** RenderSync and BodyPool are the translation surface between the simulation's UniVec3 positions and Godot's `float`-based scene graph. Grouping them together makes it obvious they are the only code that writes to Godot Node3D positions (except WorldTranslator's world-shift pass).
- **Shaders/:** Sky shader separate from dithering post-process — they live on different node types (`WorldEnvironment/Sky` vs `CanvasLayer/ColorRect`).

---

## Architectural Patterns

### Pattern 1: Floating Origin via World Translation (not player movement)

**What:** The PlayerShip Node3D stays at `Vector3.Zero` in Godot's scene graph at all times. Each frame, instead of moving the player forward, the world moves backward. All active Godot Node3D positions are offset by `-delta` to simulate the player's motion. The player's UniVec3 position in the simulation layer is the authoritative position; the Godot node is decorative.

**When to use:** Always, for the entire game. This is the foundational pattern.

**Trade-offs:** 
- Eliminates float-precision drift that would occur if the player Node3D moved far from origin.
- Requires that *every* rendered Node3D position is managed by RenderSync (you cannot rely on Godot's position for anything except the current frame's visual).
- Physics CharacterBody3D is not used for the player — pure transform manipulation is correct here because this is a space sim with no terrain collision. Use `Node3D` (not `CharacterBody3D`) for the PlayerShip scene root.

**Implementation sketch:**
```csharp
// WorldTranslator._Process(delta)
var flightDelta = _flightController.GetDeltaVector(delta);  // in meters, ship-forward
GameWorld.TranslatePos(_playerIndex, (Double3)flightDelta);  // updates player UniVec3

// After SOI transition resolves, shift all active Godot nodes:
var godotShift = -(Vector3)flightDelta;  // cast to single-precision for Godot
foreach (var node in _bodyPool.ActiveNodes)
    node.Position += godotShift;
// Player node stays at Vector3.Zero — do NOT move it.
```

The cast `(Vector3)flightDelta` loses precision below ~1e-7 m, which is acceptable because no rendered body is ever more than a few AU from the player within a single SOI (positions are relative within the parent space).

### Pattern 2: RenderSync — UniVec3 → Vector3 per frame

**What:** Each frame, RenderSync iterates the siblings of the player (objects with the same `ParentIndex`). For each, it computes the relative position in the parent frame: `relPos = sibling.LocalPos - player.LocalPos` (using UniVec3 subtraction), converts to `Double3` via `ToDouble3()`, then casts to `Vector3`. This `Vector3` is assigned to the corresponding pooled `Node3D.Position`.

**When to use:** Every frame, for every in-SOI rendered body.

**Trade-offs:**
- Precision is bounded by the SOI diameter. A star SOI in this codebase can be ~1e13 m. In double precision, sub-millimeter accuracy is maintained throughout. The final cast to float introduces ~1 m error at star-SOI scale, which is imperceptible at those distances.
- Siblings list is small (typically 2–10 bodies per SOI level) so the per-frame cost is negligible.

**Implementation sketch:**
```csharp
// RenderSync._Process(double delta)
var player = GameWorld.GameObjects[_playerIndex];
foreach (int siblingIdx in GameWorld.GameObjects[player.ParentIndex].ChildIndices)
{
    if (siblingIdx == _playerIndex) continue;
    var sibling = GameWorld.GameObjects[siblingIdx];
    if (sibling == null) continue;
    Double3 relPos = (sibling.LocalPos - player.LocalPos).ToDouble3();
    _bodyPool.GetNode(siblingIdx).Position = new Vector3(
        (float)relPos.X, (float)relPos.Y, (float)relPos.Z);
}
```

### Pattern 3: SOI Transition Event → SkyboxController Rebuild

**What:** GameWorld's `TrySpaceTransition` currently logs to console. Add an event/callback that fires when a transition completes. `SkyboxController` subscribes; on firing, it recalculates directions to all bodies *outside* the new SOI and pushes them to the sky shader.

**When to use:** On every SOI boundary crossing (both upward exit and downward entry).

**Trade-offs:**
- Rebuilding only on transitions (not per-frame) is correct — out-of-SOI objects do not move relative to the player's new frame in a meaningful way within a single play session.
- A transition is a rare event (seconds to minutes between them), so even an O(N) rebuild over all universe objects is cheap.

**Encoding strategy:** Pass star/galaxy directions as a `uniform vec3[MAX_STARS]` in the sky shader. For a small hand-authored universe (v1), MAX_STARS = 64 is sufficient. For each out-of-SOI significant body, convert its UniVec3 position to a direction from the player origin in the new frame, normalize, pack into a `Vector3[]`, and push via `SetShaderParameter("star_directions", array)`.

### Pattern 4: SpeedScaler — SOI Radius → maxSpeed Curve

**What:** SpeedScaler reads `GameWorld.GameObjects[player.ParentIndex].SOIMeters` each frame and maps it to a maxSpeed value using a monotonic mapping. A simple exponential ratio works: `maxSpeed = BASE_SPEED * (soiRadius / REFERENCE_SOI)^EXPONENT`.

**When to use:** Every frame (cheap read + one multiply).

**Trade-offs:**
- Purely reactive — no manual mode switching needed.
- The exponent (~0.8–1.0) must be tuned. Too steep → speed jumps feel jarring at SOI transitions. Too shallow → cross-galaxy travel feels impossibly slow.
- Recommend exposing BASE_SPEED, REFERENCE_SOI, EXPONENT as `[Export]` fields on SpeedScaler for in-editor tuning.

**Example values:** In a star SOI (~1e13 m), maxSpeed ~= 1e6 m/s (Frontier-scale sub-light). In a galaxy SOI (~1e21 m), maxSpeed ~= 1e14 m/s (FTL-equivalent). The scaling handles this naturally.

**Implementation sketch:**
```csharp
// SpeedScaler.GetMaxSpeed()
double soiRadius = GameWorld.GameObjects[
    GameWorld.GameObjects[_playerIndex].ParentIndex].SOIMeters;
return BaseSpeed * Math.Pow(soiRadius / ReferenceSoi, Exponent);
```

---

## Data Flow

### Per-Frame Flight Loop

```
Input.GetAxis("pitch_up"/"pitch_down") ─────────────────────────────┐
Input.GetAxis("yaw_left"/"yaw_right")                               │
Input.GetAxis("roll_left"/"roll_right")  → FlightController         │
Input.GetAxis("throttle_up"/"throttle_down")                        │
                                              │                      │
                         SpeedScaler ─────────┤ clamps forward speed │
                         (reads SOI radius)   │                      │
                                              ▼                      │
                                   _forwardSpeed (lerped float)       │
                                   rotation delta (Basis change)     │
                                              │
                                              ▼
                                   WorldTranslator._Process()
                                              │
                         ┌────────────────────┴─────────────────────┐
                         │                                           │
                         ▼                                           ▼
              GameWorld.TranslatePos(                  shift all active Godot
               playerIndex, flightDelta)               Node3D positions by
                         │                             -flightDelta (Vector3)
                         ▼
              GameWorld.TrySpaceTransition()
                         │
              ┌──────────┴──────────────┐
              │  no transition          │  transition fired
              │                         │
              ▼                         ▼
         RenderSync._Process()     SkyboxController.OnTransition()
         (write sibling           (rebuild out-of-SOI star directions,
          positions to            push to sky ShaderMaterial,
          Node3D.Position)        reassign BodyPool active set)
                                         │
                                         ▼
                                  RenderSync._Process()
                                  (with new sibling set)
```

### SOI Transition Detail

```
TrySpaceTransition fires OnSOITransition(newParentIndex) event
          │
          ├─► SkyboxController
          │     ├─ Iterate ALL GameWorld.GameObjects
          │     ├─ For each NOT in new parent's ChildIndices:
          │     │    compute direction = Normalize(obj.LocalPos_in_newFrame)
          │     │    encode as Vector3 in out-of-SOI list
          │     └─ SetShaderParameter("star_directions", array)
          │        SetShaderParameter("star_count", count)
          │
          └─► BodyPool
                ├─ Deactivate all currently-active nodes
                └─ Activate nodes for new parent's ChildIndices
                   (excluding player itself)
```

### Rendering Path (per frame)

```
Godot _Process() ──► FlightController ──► WorldTranslator ──► GameWorld
                                                │
                          ┌─────────────────────┘
                          │
                          ▼
                    RenderSync
                    (for each in-SOI sibling)
                          │  relPos = sibling.LocalPos - player.LocalPos
                          │  → ToDouble3() → cast to Vector3
                          ▼
                    BodyPool.GetNode(idx).Position = relPos
                          │
                          ▼
                    Godot renders scene:
                    - PlayerShip at (0,0,0)
                    - Bodies at float-precision relative positions
                    - Sky shader: EYEDIR-based star rendering (uniform array)
                    - UniRenderer ColorRect: dithering post-process overlay
```

---

## Floating Origin and SOI Reparenting: Coexistence

This is the most nuanced part of the architecture. Three things happen when a SOI boundary is crossed:

**1. GameWorld reparents the player** (existing code). The player's `LocalPos` is re-expressed in the new parent's coordinate space. This is already handled by `ChildPosToParentSpace` / the inverse.

**2. WorldTranslator's node-shift logic is suspended for that frame.** On a transition frame, the flightDelta is applied to the simulation first (via `TranslatePos`, which includes the transition). The Godot node shift should be skipped or zeroed for that one frame because the body configuration has changed. SkyboxController and BodyPool rebuild synchronously before RenderSync runs.

**3. RenderSync recomputes from scratch.** After the transition, `player.ParentIndex` has changed, so the siblings list has changed. RenderSync iterates the new parent's `ChildIndices`. All Node3D positions are rewritten from the new relative positions. This happens naturally each frame — no special case is needed in RenderSync because it always reads the current state.

**Key rule:** WorldTranslator must never accumulate a Godot-space delta across a transition frame. The simulation is the source of truth. After any `TrySpaceTransition`, the Godot positions should be rebuilt from scratch via RenderSync, not shifted incrementally. Practically: if `OnSOITransition` fires in a given frame, skip the `-delta` node shift for that frame.

---

## Component Boundaries (what talks to what)

```
FlightController
  READS: Input (Godot)
  READS: SpeedScaler.GetMaxSpeed()
  WRITES: rotation to its own Node3D.Transform (PlayerShip basis)
  WRITES: _forwardSpeed (private)
  PRODUCES: GetDeltaVector() → Double3

SpeedScaler
  READS: GameWorld.GameObjects[playerIndex].ParentIndex → SOIMeters
  PRODUCES: GetMaxSpeed() → double
  NO writes to GameWorld or Godot nodes

WorldTranslator
  READS: FlightController.GetDeltaVector()
  CALLS: GameWorld.TranslatePos(playerIndex, delta)
  CALLS: GameWorld.TrySpaceTransition (implicit via TranslatePos)
  WRITES: Node3D.Position for all active BodyPool nodes (world shift)
  RAISES: OnSOITransition event (forwarded from GameWorld)

RenderSync
  READS: GameWorld.GameObjects (player + siblings)
  WRITES: BodyPool.GetNode(idx).Position each frame
  NO direct Godot Input reads
  NO GameWorld mutations

BodyPool
  OWNS: pooled Node3D + MeshInstance3D nodes
  RESPONDS TO: WorldTranslator.OnSOITransition (activate/deactivate set)
  READS: GameWorld.GameObjects[newParentIndex].ChildIndices

SkyboxController
  READS: GameWorld.GameObjects (all objects)
  WRITES: sky ShaderMaterial.SetShaderParameter (on transition only)
  RESPONDS TO: WorldTranslator.OnSOITransition
  NO per-frame writes (only on transition)

GameWorld (existing — not modified)
  OWNS: GameObjects list, SOI logic, position arithmetic
  PROVIDES: TranslatePos, AddGameObject, RemoveGameObject
```

**The boundary rule:** Only `WorldTranslator` and `RenderSync` write to Godot Node3D positions. Everything else either mutates only the simulation state (GameWorld) or reads it.

---

## Build Order

The components have clear dependencies. Build in this order, with the in-system milestone gating the cross-galaxy work.

### Phase 1 — Render Sync Foundation (no input yet)

1. **BodyPool** — pooled Node3D + MeshInstance3D nodes, star body shader (`star_body.gdshader`: unlit emissive sphere). No dependencies on player systems. Validates that bodies appear in the right positions.
2. **RenderSync** — wire it to the existing TestSetup simulation. Point it at the existing TestSetup ship as the "player" index. Bodies should correctly track their relative positions as TestSetup's ship moves. This proves the UniVec3 → Vector3 conversion path without touching flight or input.

**Validation gate:** Planets visible as sphere meshes orbiting a star, correctly positioned as TestSetup moves the ship. Dithering post-process works on them.

### Phase 2 — Player Flight (in-system)

3. **FlightController** — rotation (pitch/roll/yaw) on a Node3D `PlayerShip`. Hard-code a maxSpeed constant initially.
4. **WorldTranslator** — connect FlightController to GameWorld.TranslatePos. Implement the node-shift pass. Replace TestSetup's automatic movement with player input.
5. **SpeedScaler** — read parent SOI, compute maxSpeed. Wire into FlightController.

**Validation gate:** Player can fly around the star system, approach planets, planets get larger as approached. Speed scales with SOI context. SOI transitions fire correctly.

### Phase 3 — Skybox

6. **SkyboxController + skybox.gdshader** — implement the sky shader with a `uniform vec3[64] star_directions` array. On startup, populate with directions to all out-of-SOI bodies. On SOI transition, rebuild.

**Validation gate:** Stars visible in skybox. On entering a planet's SOI, the star moves to correct position in sky. On exiting to star level, the galaxy appears as a skybox object.

### Phase 4 — Cross-Galaxy Travel

7. **Extend hand-authored data** to include galaxy, universe objects. Validate SOI transitions across all levels (planet → star → galaxy → universe). Skybox rebuilds correctly at each level. SpeedScaler provides FTL-scale velocity at galaxy SOI.

**Validation gate:** Player can fly from inside a star system to another galaxy, with correct skybox representation at each scale level.

---

## Anti-Patterns

### Anti-Pattern 1: Moving the PlayerShip Node3D

**What people do:** Set `PlayerShip.Position += velocity * delta` each frame, like a normal Godot character controller.

**Why it's wrong:** Godot's scene graph uses single-precision floats. After ~4096 m, positions lose sub-millimeter precision. At star-system scale, the player's position becomes meaningless jitter. The whole point of the existing UniVec3 system is to keep precision in the simulation, with the Godot scene anchored at origin.

**Do this instead:** Keep PlayerShip at `Vector3.Zero`. Move the world (shift all active nodes by `-delta`). Let RenderSync set absolute positions each frame from the simulation state.

### Anti-Pattern 2: Storing Positions in Godot Node3D

**What people do:** Read `someBody.GlobalPosition` to compute distances or AI decisions.

**Why it's wrong:** `GlobalPosition` is a float-precision Godot value, valid only for the current frame after RenderSync has written it. It encodes only the relative position within the current SOI. Using it for anything persistent or for distance calculations to out-of-SOI bodies gives wrong results.

**Do this instead:** All gameplay distance/position logic reads from `GameWorld.GameObjects[idx].LocalPos` (UniVec3). Godot positions are write-only outputs of RenderSync, used only for rendering.

### Anti-Pattern 3: Rebuilding the Skybox Every Frame

**What people do:** Recalculate star directions and call `SetShaderParameter` for each star in every `_Process` call.

**Why it's wrong:** Sky ShaderMaterial `SetShaderParameter` flushes the shader uniform buffer and potentially triggers a GPU upload each call. Doing this for 64 stars every frame at 60fps is unnecessary overhead. Out-of-SOI objects do not move relative to the player within a session.

**Do this instead:** Rebuild only on SOI transition (event-driven). The sky shader itself handles the view-dependent projection via `EYEDIR` — the C# side only needs to supply static direction vectors.

### Anti-Pattern 4: Bypassing GameWorld for Position Updates

**What people do:** Directly mutate `GameWorld.GameObjects[i].LocalPos = ...` from flight or render code.

**Why it's wrong:** Bypasses `TrySpaceTransition`, breaking SOI logic. Also violates the existing null-slot convention in GameObjects list.

**Do this instead:** All position updates go through `GameWorld.TranslatePos(index, delta)`. This is the only correct mutation path for object positions.

---

## Integration Points

### Internal Boundaries

| Boundary | Communication | Notes |
|----------|---------------|-------|
| FlightController ↔ WorldTranslator | `GetDeltaVector()` return value (Double3) | Called once per frame; FlightController is a child component of WorldTranslator's scene or a sibling node |
| WorldTranslator ↔ GameWorld | `GameWorld.TranslatePos()` method call | The only mutation path; WorldTranslator holds `_playerIndex` (int) |
| WorldTranslator ↔ BodyPool/SkyboxController | C# event `Action<int> OnSOITransition(newParentIndex)` | Fired after `TrySpaceTransition` returns; subscribers rebuild their state |
| RenderSync ↔ BodyPool | `BodyPool.GetNode(objectIndex)` → `Node3D` | RenderSync writes `Position`; BodyPool owns node lifecycle |
| SkyboxController ↔ sky ShaderMaterial | `ShaderMaterial.SetShaderParameter(name, value)` | Only on transition; material must be `.Duplicate()`d so it is not shared |
| SpeedScaler ↔ GameWorld | Direct read of `GameWorld.GameObjects[...].SOIMeters` | Read-only; no mutation |

### Godot Scene Structure

```
Main.tscn (Node3D)
├── WorldEnvironment          ← hosts Sky with skybox ShaderMaterial
├── PlayerShip (Node3D)       ← stays at (0,0,0)
│   ├── Camera3D              ← child of ship, inherits rotation
│   ├── FlightController.cs   ← attached to PlayerShip
│   └── ShipMesh (MeshInstance3D)
├── BodyPool (Node3D)         ← parent of all pooled body nodes
│   ├── Body_0 (Node3D + MeshInstance3D)
│   ├── Body_1 ...
│   └── ...
├── WorldTranslator.cs        ← attached to Main or a Manager node
├── RenderSync.cs             ← attached to Main or a Manager node
├── SpeedScaler.cs            ← attached to Main or a Manager node
├── SkyboxController.cs       ← attached to Main or a Manager node
└── CanvasLayer
    └── UniRenderer (ColorRect) ← existing dithering post-process
```

---

## Scalability Considerations

| Concern | In-System (v1) | Cross-Galaxy (v2) |
|---------|----------------|-------------------|
| Bodies to render per frame | 2–5 siblings | 2–5 siblings (same — only in-SOI rendered) |
| RenderSync cost | O(siblings) ~trivial | O(siblings) ~trivial |
| Skybox star count | ~5–20 directions | ~50–64 directions (still one array upload per transition) |
| SpeedScaler precision | double arithmetic | double arithmetic — no change |
| SOI transition frequency | Rare (minutes) | Rare (minutes) |
| UniVec3 range | Sufficient for star system (~1e13 m) | Sufficient for universe scale (~1e26 m) |

The architecture does not need to change between v1 and v2. Only data (hand-authored universe objects) and tuning parameters (SpeedScaler curve) change.

---

## Sources

- Godot 4 Large World Coordinates documentation: [https://docs.godotengine.org/en/stable/tutorials/physics/large_world_coordinates.html](https://docs.godotengine.org/en/stable/tutorials/physics/large_world_coordinates.html)
- Godot 4 Sky Shaders reference: [https://docs.godotengine.org/en/stable/tutorials/shaders/shader_reference/sky_shader.html](https://docs.godotengine.org/en/stable/tutorials/shaders/shader_reference/sky_shader.html)
- Frozen Fractal floating origin implementation blog: [https://frozenfractal.com/blog/2024/4/11/around-the-world-14-floating-the-origin/](https://frozenfractal.com/blog/2024/4/11/around-the-world-14-floating-the-origin/)
- Godot 4 arcade spaceship recipe: [https://kidscancode.org/godot_recipes/4.x/3d/spaceship/index.html](https://kidscancode.org/godot_recipes/4.x/3d/spaceship/index.html)
- Godot 4 custom sky shader article: [https://godotengine.org/article/custom-sky-shaders-godot-4-0/](https://godotengine.org/article/custom-sky-shaders-godot-4-0/)
- Existing codebase: `Scripts/Universe/GameWorld.cs`, `UniObject.cs`, `UniVec3.cs` (analysis date 2026-06-12)

---

*Architecture research for: EcoSpace game layer (player flight, floating origin, render sync, skybox, speed scaling)*
*Researched: 2026-06-12*
