# Project Research Summary

**Project:** EcoSpace
**Domain:** Retro first-person space sim game layer on a multi-scale universe engine (Godot 4.6 C#)
**Researched:** 2026-06-12
**Confidence:** HIGH

## Executive Summary

EcoSpace is a brownfield project: the hardest technical problems — 1:1-scale multi-level coordinate math (`UniVec3`), sphere-of-influence (SOI) transitions, SIMD double-precision arithmetic, and a dithering post-process pipeline — are already built and tested. The v1 work is building the *playable game layer* (flight controller, body rendering, dynamic skybox, minimal HUD) on top of that foundation without disrupting any of it. Research confirms this is the correct sequencing. The entire recommended stack is Godot 4.6.2 built-in APIs plus the existing codebase — no external dependencies are needed.

The recommended architecture is a clean four-component bridge between the simulation layer (`GameWorld`) and Godot's scene graph: `FlightController` (input → rotation + speed), `WorldTranslator` (moves the world around a stationary player), `RenderSync` (converts `UniVec3` positions to `Node3D` positions each frame), and `SkyboxController` (rebuilds the sky shader on SOI transitions). The floating-origin pattern — player ship pinned at `Vector3.Zero`, everything else translated around it — is non-negotiable and must be established first. Every other subsystem depends on it being correct.

The primary risks are not algorithmic: the precision and coordinate systems are solved. The risks are integration-ordering errors (building rendering before floating-origin is locked), Godot-version-specific bugs (C# `CompositorEffect` MSAA bug, Godot 4.6 glow-before-tonemapping change), and a known existing bug in `TrySpaceTransition()` that will corrupt state under high-speed multi-boundary crossings. All three are avoidable with a disciplined build order and a pre-flight bugfix pass.

## Key Findings

### Recommended Stack

The full game layer is implemented in Godot 4.6.2's built-in C# APIs. No new packages are required. The ship is a bare `Node3D` (not `CharacterBody3D`) — floating-origin requires the ship to stay at world zero, and `MoveAndCollide`/`MoveAndSlide` fight that invariant. The dynamic skybox uses `shader_type sky` with a `ShaderMaterial`, updated via `SetShaderParameter()` on SOI transitions — not a `PanoramaSkyMaterial` (can't be updated in real-time) and not `ProceduralSkyMaterial` (wrong for space). The existing `ColorRect` / `canvas_item` post-processing pipeline (dithering, CRT) must be kept as-is — avoid `CompositorEffect` in C# due to a confirmed MSAA bug through at least Godot 4.5.

**Core technologies:**
- `Node3D` (plain) as ship root — stays at `Vector3.Zero`; floating-origin requires manual `Transform` control
- `Camera3D` child of ship — inherits rotation, always at world origin
- `SphereMesh` + `StandardMaterial3D` — planet/star bodies; low segment count for retro low-poly look; stars use Unshaded + EmissionEnabled
- `ShaderMaterial` (`shader_type sky`) — dynamic skybox updated via `SetShaderParameter()` on SOI transitions
- `RotateObjectLocal()` + `Transform.Orthonormalized()` — ship rotation; never use Euler angles for free-space flight
- `OmniLight3D` (no shadows) co-located with star meshes — casts light on planets, separate from the emissive star visual
- Existing `ColorRect` / `CanvasLayer` pipeline — dithering + CRT; do not migrate to `CompositorEffect`

### Expected Features

**Must have (table stakes):**
- Forward throttle with speed number readout (unit must scale with context speed)
- Pitch/yaw on primary axes (mouse X/Y), roll as secondary — genre standard since X-Wing/Wing Commander
- Boost / afterburner (short burst, audio + visual feedback)
- Crosshair / reticle at screen center
- Target name + distance label (cycle-able)
- Context/location label (current space level or body name)
- Stars as bright presences; planets as distinct spheres that grow on approach
- Stable starfield skybox that does not drift with camera rotation

**Should have (EcoSpace differentiators):**
- Context-auto-scaling speed — the defining mechanic; no other space sim does this automatically (all competitors use manual speed-mode switches)
- Seamless SOI transitions with no loading screen or cut
- Dynamic skybox that reflects actual scale context on SOI transitions (every reference title uses a static skybox)
- Dithered 8-bit indexed-color rendering of all bodies

**Defer (v2+):**
- Galaxy map / route planner; combat / weapons; trading / economy; full cockpit art; procedural generation; atmospheric landing

### Architecture Approach

The game layer is a strict four-component bridge. `WorldTranslator` is the sole writer of world-shift translations; `RenderSync` is the sole writer of body `Node3D.Position` values. All gameplay distance/position logic reads from `GameWorld.GameObjects[idx].LocalPos` (UniVec3) — Godot positions are rendering outputs only.

**Major components:**
1. `FlightController` — reads input, rotates PlayerShip transform, produces forward-delta; reads maxSpeed from SpeedScaler
2. `WorldTranslator` — calls `GameWorld.TranslatePos()`, shifts all active Node3D positions by `-delta`; raises `OnSOITransition` event
3. `SpeedScaler` — reads parent SOI radius, maps to maxSpeed via power curve (`base * (soiRadius/refSoi)^exp`)
4. `RenderSync` — each frame converts sibling UniVec3 positions to Vector3 and writes to pooled Node3D.Position
5. `BodyPool` — pooled Node3D + MeshInstance3D nodes; activates/deactivates on SOI transition
6. `SkyboxController` — on SOI transition only, recomputes out-of-SOI body directions and pushes to sky ShaderMaterial

### Critical Pitfalls

1. **Float truncation jitter** — always subtract the player's UniVec3 position before casting to Vector3; floating-origin must be implemented first, before any body meshes are placed
2. **Null-slot cascade at high speed** — existing recursive `TrySpaceTransition()` corrupts state when crossing multiple SOI boundaries per frame; convert to iterative and add null guards before enabling any speed-scaling (Phase 0 bugfix)
3. **One-frame SOI position pop** — strict simulate-then-render order in `_Process()`; on transition frames, let RenderSync rebuild positions from scratch rather than applying a Godot-space shift
4. **HDR glow breaking 8-bit palette (Godot 4.6 specific)** — glow now runs before tonemapping (PR #110671); high emissive values produce washed-out halos; keep environment glow low or implement palette-aware glow in the dithering shader, which must remain the final pass
5. **Depth-buffer z-fighting at 1:1 scale** — a single camera pass cannot cover a near/far ratio of ~10^10; a two-pass (far bodies → clear depth → near ship) setup is needed if cockpit/ship geometry and distant planets share a frame
6. **Context-speed curve discontinuities** — use a smooth power curve, not a step function; expose all curve parameters as `[Export]` fields for in-editor tuning

## Implications for Roadmap

Research suggests this phase decomposition (note: project granularity is **coarse**, so the roadmapper may merge some of these into broader phases):

- **Phase 0 — Pre-Flight Bugfix:** Fix `TrySpaceTransition()` recursion + null-slot cascade in `GameWorld` before any new code. Crash-level risk under speed-scaling. (CONCERNS.md documents the issue.)
- **Phase 1 — Render Sync Foundation:** Floating-origin + `BodyPool`/`RenderSync`; bodies rendered as dithered spheres, validated using the existing `TestSetup` simulation (no player input yet). Proves the UniVec3→Node3D precision boundary in isolation.
- **Phase 2 — Player Flight (In-System):** `FlightController` + `WorldTranslator` + `SpeedScaler`. Player flies a single star system, approaches bodies, speed auto-scales. **First sequenced goal.**
- **Phase 3 — Minimal HUD:** Speed/context/target labels + crosshair. Runs alongside Phase 2; speed readout aids flight tuning.
- **Phase 4 — Dynamic Skybox:** `shader_type sky` + `SkyboxController`, event-driven on SOI transitions. Requires stable transitions from Phase 2. **Flag for shallow phase research** (sky shader direction encoding, half-res pass).
- **Phase 5 — Cross-Galaxy Travel:** Extend hand-authored data to galaxy/universe scale; validate full SOI chain; SpeedScaler delivers FTL-equivalent velocity. **Second sequenced goal.**
- **Phase 6 — Polish:** Wire up the existing unused `crt.gdshader`, audio, validate palette-limited star glow.

### Research Flags

- **Needs phase research:** Phase 4 (skybox) — sky shader direction encoding and half-res pass are moderately novel.
- **Standard patterns (research-phase optional):** Phases 0, 1, 2, 3, 5, 6 use documented Godot APIs and known codebase fixes.

## Confidence Assessment

| Area | Confidence | Notes |
|------|------------|-------|
| Stack | HIGH | All core APIs are official Godot 4.6.2 docs; C# CompositorEffect bug confirmed via GitHub issues |
| Features | HIGH | Genre conventions 30+ years established; retro references extensively documented |
| Architecture | HIGH | Component boundaries derived from existing codebase analysis + official large-world docs |
| Pitfalls | HIGH | Float truncation, null-slot, and glow issues are confirmed documented bugs with specific issue numbers |

**Overall confidence:** HIGH

### Gaps to Address

- **SpeedScaler curve values:** Exponent and base speed are design unknowns requiring in-editor playtest tuning. Cannot be determined by research.
- **Depth-buffer split setup:** Exact Camera3D near/far values and Godot 4.6 viewport config for the two-pass setup need an implementation spike at Phase 2 if ship geometry and planets appear in the same frame.
- **8-bit palette definition:** Specific palette colors are a design decision not yet made; resolve by Phase 1.
- **`TrySpaceTransition` rewrite scope:** CONCERNS.md identifies the issue; full scope (including whether UniObject structures need changes) assessed during Phase 0 planning.

## Sources

### Primary (HIGH confidence)
- Godot 4.6 Official Docs — sky shaders, CompositorEffect, custom post-processing, transforms, SphereMesh, OmniLight3D, ShaderMaterial
- Godot GitHub Issues #95286, #105637 — C# CompositorEffect MSAA confirmed bugs
- Godot GitHub PR #110671 — glow-before-tonemapping change in 4.6
- Godot GitHub Issues #44988, #58516 — Z-fighting and float precision
- EcoSpace codebase audit: `GameWorld.cs`, `UniVec3.cs`, `UniRenderer.cs`, `TestSetup.cs`, CONCERNS.md (2026-06-12)

### Secondary (MEDIUM confidence)
- Frozen Fractal blog: floating origin implementation reference
- KidsCanCode Godot 4 arcade spaceship recipe
- Yosoygames mouse input best practices (SetUseAccumulatedInput, XformedBy pattern)
- Pioneer Space Sim, Elite Dangerous, Wing Commander / Privateer manuals — HUD and flight conventions

### Tertiary (LOW confidence)
- SpeedScaler curve shape (exponent ~0.8–1.0) — design reasoning, requires in-editor validation

---
*Research completed: 2026-06-12*
*Ready for roadmap: yes*
