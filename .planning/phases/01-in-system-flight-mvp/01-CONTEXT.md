# Phase 1: In-System Flight MVP - Context

**Gathered:** 2026-06-12
**Status:** Ready for planning

<domain>
## Phase Boundary

Build the **playable flight layer** on top of the existing universe engine so a player can fly a single star system, approach dithered planets/stars, and read a minimal retro HUD. Concretely, Phase 1 delivers:

1. **Stability fix (STAB-01):** Convert `GameWorld.TrySpaceTransition` from recursion to an iterative, null-safe loop so the ship can cross multiple SOI boundaries in one frame without crashing or corrupting the hierarchy.
2. **Floating-origin rendering (RND-01/02):** Hold the player ship at the coordinate origin, translate the world around it, sync in-space objects' `UniVec3` positions to Godot `Node3D` transforms each frame. Render the current star system's bodies — its planet(s) and star(s)/sun(s) — as geometry whenever the player is within the system (both Star space and Planet space, so the sun stays a mesh while flying near a planet); out-of-system bodies are not rendered as meshes.
3. **Body rendering (RND-03/04):** Planets and stars as dithered sphere meshes with an 8-bit palette look; stars emissive and lighting nearby planets.
4. **Arcade flight (FLT-01/02/03):** Mouse virtual-joystick control, persistent throttle, context-auto-scaling speed.
5. **Minimal HUD (HUD-01..04):** Speed w/ adaptive units, context label, crosshair, cycle-able target readout.
6. **Hand-authored world (WORLD-01) + in-system travel goal (TRV-01).**

**Out of scope (later phases):** dynamic skybox / out-of-space projection (RND-05, Phase 2); galaxy-space star meshes + visually continuous skybox↔mesh handoff (RND-07, Phase 2); cross-galaxy travel (TRV-02, Phase 3); procedural generation, cockpit art, economy, combat, CRT scanline overlay (v2 PRES-01), boost/afterburner (v2 FLT-04), audio (v2).

</domain>

<decisions>
## Implementation Decisions

### Flight Controls & Feel (FLT-01, FLT-02)
- **D-01: Virtual-joystick mouse model (Wing Commander–style).** Mouse moves a cursor within a screen-center deadzone; the ship continuously rotates *toward* the cursor, turning faster the further the cursor is from center. Centered cursor = no rotation. NOT direct FPS-style 1:1 mouse-delta look.
- **D-02: Hold-attitude stabilization.** When turn input stops, rotation halts cleanly with no residual drift/spin. The ship does **not** auto-level and does **not** recenter toward forward — it keeps whatever orientation it has.
- **D-03: Persistent throttle.** W/S (and/or scroll) raise/lower a throttle level that persists hands-off; a full-stop key (e.g. X) zeroes it. Cockpit-style, not hold-to-thrust.
- **D-04: Roll on Q/E** (left/right). Pitch + yaw stay on the mouse cursor; mouse is never used for roll.
- **D-05: Two reticles.** A fixed center crosshair marks "nose forward" (satisfies HUD-03); a separate moving reticle shows the steering cursor. The on-screen flight cursor is visible.

### Speed Auto-Scaling (FLT-03)
- **D-06: Max speed scales with distance to the nearest body** (planet/star surface). You naturally slow approaching anything and accelerate leaving it. (Chosen over fixed-per-SOI-scale or hybrid.)
- **D-07: Continuous & smooth envelope.** Max speed eases frame-to-frame with no visible snap when crossing an SOI boundary. Pairs with the distance-based model.
- **D-08: Throttle = fraction of context max.** Actual speed = throttle% × current context-max speed. One control, auto-scaled: full throttle near a planet is a crawl; full throttle in deep space is enormous. No manual mode switch (FLT-03 intent).
- Concrete min/max speed constants, the distance→speed curve shape, and easing rates are **planner/tuning discretion** — only the model is locked here.

### HUD (HUD-01..04)
- **D-09: Phosphor-green CRT aesthetic** (monochrome green vector-terminal look). Replace the current magenta accent. (Chosen over amber / multi-color.)
- **D-10: Adaptive speed units** — auto-pick the largest readable unit on the ladder **m/s → km/s → AU/s → ly/s** (keep the displayed number roughly 1–9999). Real units, reinforcing the 1:1 scale. (No abstract %/c-multiples in v1.)
- **D-11: Context label = space level + nearest body**, e.g. `STAR SPACE · nearest: PLANET A`. Shows both the SOI scale tier and the closest named body.
- **D-12: Target readout cycles bodies in the current parent space**, showing **name + distance** (adaptive units). Scope is limited to what's renderable now (not all-universe bodies — distant ones come with the Phase 2 skybox).

### Body Rendering (RND-03, RND-04)
- **D-13: Per-body distinct colors + global dither quantize.** Each body gets an authored base color (e.g. Earth-blue, Mars-rust, yellow star); the existing `dithering.gdshader` post-process quantizes the whole frame to the 8-bit/dithered look. Bodies stay distinguishable by hue.
- **D-14: Stars = emissive sphere + glow/bloom.** Unshaded bright sphere with a Godot glow/bloom halo that blooms on approach. No cast shadows (RND-04).
- **D-15: True 1:1 body radii.** Planets/stars at real radii — they are tiny specks at distance and grow dramatically only on close approach. Honors the 1:1 universe. (See risk in Specific Ideas re: findability.)
  - **Distance reframe (→ RND-06):** the earlier "honest 1:1 *render distances*" framing is superseded. 1:1 is preserved in both calculation and rendering, but rendering happens in uniformly scaled **unit-space** (observer-scale unit basis × per-space render factor, far ≤ 1e6) — uniform scaling *is* the 1:1 model, not a violation. True 1:1 *radii* (this D-15) are unchanged.
- **D-16: Star lights planets via a point light** (OmniLight at the star position) for a correct day/night terminator. Range must be tuned for 1:1 distances; replaces the placeholder `DirectionalLight3D` as the star's light source.

### Claude's Discretion
- Iterative `TrySpaceTransition` rewrite shape (while-loop / state machine), null-slot guarding, and any related `GameWorld` hardening (the CONCERNS.md null-deref / double-reparent items) — implement the safest correct version.
- Floating-origin sync mechanism (which node is origin, how `UniVec3`→`Node3D` transforms are computed each frame to avoid jitter).
- All numeric tuning: speed curve, deadzone size, turn rates, throttle steps, bloom amount, light range, palette specifics, mesh subdivision/LOD.
- HUD implementation tech (GDScript vs C#, `Control` node layout) — match existing `FPS.gd` / `Main.tscn` CanvasLayer pattern unless a better fit emerges.

</decisions>

<canonical_refs>
## Canonical References

**Downstream agents MUST read these before planning or implementing.**

### Phase requirements & scope
- `.planning/ROADMAP.md` §"Phase 1: In-System Flight MVP" — goal, success criteria, requirement list (STAB-01, WORLD-01, RND-01..04, FLT-01..03, HUD-01..04, TRV-01).
- `.planning/REQUIREMENTS.md` §Stability/Flight/World/Rendering/HUD/Travel — full text of each requirement ID owned by this phase.
- `.planning/PROJECT.md` §Context, §Key Decisions — floating-origin, arcade-flight, auto-scaling-speed, 8-bit palette decisions and rationale.

### Engine code to build on (do not replace — consume)
- `Scripts/Universe/GameWorld.cs` — `TranslatePos(int, Double3)` is the flight-input entry point; `TrySpaceTransition` / `TryExitParentSOI` / `TryEnterChildSOI` are the recursive transition logic to make iterative + null-safe (STAB-01).
- `Scripts/Universe/TestSetup.cs` — existing hand-authored `Root→Galaxy→Star→PlanetA/B→Ship` hierarchy with real distances; its auto-pilot `_Process` loop (A→B) is replaced by player input. Reuse `SetupScene()` as the WORLD-01 base.
- `Scripts/Universe/UniObject.cs` — `Space` enum, scale factors, `ChildSpace`/`ParentSpace`; source of the context-label space tier.
- `Scripts/Universe/Math/UniVec3.cs` — position math (`Magnitude`, `Distance`, `Convert`); needed for distance-to-body speed scaling and target distances. (CONCERNS notes no `DistanceSq` — may want one.)
- `Scripts/Universe/UniRenderer.cs` + `Shaders/dithering.gdshader` — the dithering post-process that produces the 8-bit look; already wired to `Main.tscn`'s `FullscreenRect`.
- `Main.tscn` — has `Camera3D`, a `SphereMesh` MeshInstance3D, `DirectionalLight3D`, the dithering `FullscreenRect`, and `FPSLabel` (`Scripts/FPS.gd`). HUD and body meshes integrate here.

### Codebase maps
- `.planning/codebase/CONCERNS.md` — known crash sources directly relevant to STAB-01: recursive `TrySpaceTransition` stacking, null-slot fragility in `GameObjects`, missing null checks in `TryExitParentSOI`/`TryEnterChildSOI`, double-reparenting in `TryEnterChildSOI`.
- `.planning/codebase/ARCHITECTURE.md`, `.planning/codebase/STRUCTURE.md`, `.planning/codebase/CONVENTIONS.md` — namespace/layer conventions and where new code (flight controller, HUD, body renderer) should live.

</canonical_refs>

<code_context>
## Existing Code Insights

### Reusable Assets
- `GameWorld.TranslatePos(index, Double3 delta)` — feed per-frame ship movement here; SOI transitions happen automatically inside it.
- `TestSetup.SetupScene()` + the documented hierarchy/distances — the hand-authored star system for WORLD-01; extend with authored radii + colors per body.
- `UniRenderer` `[Export]` pattern + `dithering.gdshader` — the 8-bit dither look is already running on a full-screen `ColorRect`; per-body colors flow through it.
- `Main.tscn` `Camera3D` / `SphereMesh` / `CanvasLayer`+`FPSLabel` — camera, a ready sphere mesh, and the HUD canvas already exist.
- `Scripts/FPS.gd` — existing GDScript UI label; HUD can follow the same CanvasLayer + script pattern.

### Established Patterns
- Single-threaded `_Process(double delta)` simulation loop; `TestSetup : GameWorld` overrides `_Process` (base `GameWorld._Process` is intentionally empty — a derived class must drive it).
- Integer-index object model (`ParentIndex`/`ChildIndices`) with `(uint)i < (uint)Count` bounds checks; positions as `UniVec3 = Long3 units + Double3 offset + scale`.
- `GD.Print` transition logging with `↑`/`↓` markers — keep for debug visibility during the iterative rewrite.

### Integration Points
- **Flight controller** → calls `TranslatePos(_ship, delta)` with a velocity derived from throttle × context-max-speed, oriented by the ship's attitude.
- **Floating origin** → ship at world origin; every other in-space object's `Node3D` transform = its `UniVec3` relative to the ship, recomputed each frame.
- **Body renderer** → spawns/updates one `MeshInstance3D` per body of the current star system (RND-02) — every planet plus the system's sun(s), visible in both Star and Planet space — sized by 1:1 radius, colored per-body; each star gets emissive material + OmniLight.
- **HUD** → reads ship throttle/speed, `CurrentSpace`, nearest-body distance, and the active target each frame; renders on the existing `CanvasLayer`.

</code_context>

<specifics>
## Specific Ideas

- **Reference feel:** Wing Commander for cockpit/flight + HUD; Elite/Frontier for the vast traversable cosmos.
- **Open tension (flagged for planner, not a blocker):** True 1:1 radii (D-15) + no skybox yet (Phase 2) + a target readout that shows only name + distance (no bearing, D-12) could make distant planets genuinely hard to *find and aim at* during a Phase 1 flight. The player starts in-system but may stare at empty space. Consider, within HUD-04/HUD-03 scope, a minimal affordance: an off-screen/edge target-direction marker or arrow toward the currently-cycled target, or confirm that name+distance + the deadzone steering is sufficient to navigate. Resolve during planning; do not silently ship an unfindable world. (A "1:1 with visibility floor" option was offered and the user chose pure 1:1 — keep scale honest.)
- The current `FPSLabel` magenta is explicitly being replaced by phosphor green (D-09).

</specifics>

<deferred>
## Deferred Ideas

- **Dynamic spherical skybox** for out-of-space stars/galaxies (RND-05) — Phase 2.
- **Cross-galaxy travel** and galaxy/universe-scale authored data (TRV-02) — Phase 3.
- **CRT scanline overlay** (wire up the existing unused `crt.gdshader`) — v2 PRES-01.
- **Boost/afterburner** (FLT-04), **engine/boost audio** (PRES-02), **1-bit/mono render toggle** (PRES-03) — v2.
- **'c'-multiple / FTL speed flavor** on the HUD — considered but cut from v1 in favor of the plain metric ladder.
- **All-universe target cycling** and **target relative bearing** — considered for HUD-04; current-space name+distance chosen for v1 (bearing may be revisited per the open tension above).
- Deeper `GameWorld` hardening from CONCERNS.md (null-slot compaction / free-list, `DistanceSq`, SOI input validation, SIMD layout asserts) — touch only what STAB-01 requires; the rest is future tech-debt work.

</deferred>

---

*Phase: 1-In-System Flight MVP*
*Context gathered: 2026-06-12*
