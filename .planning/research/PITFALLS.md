# Pitfalls Research

**Domain:** Large-scale 1:1 space sim — floating origin, multi-scale SOI rendering, arcade flight, Godot 4.6 C#
**Researched:** 2026-06-12
**Confidence:** HIGH (core precision/rendering pitfalls well-documented across multiple independent sources; flight feel pitfalls derived from design literature and known codebase issues)

---

## Critical Pitfalls

### Pitfall 1: Single-Precision Truncation When Syncing UniVec3 to Node3D.Position

**What goes wrong:**
Godot's `Node3D.Position` and `Transform3D` are single-precision (32-bit float). When you compute a camera-relative render position from `UniVec3` in C# (double) and assign it to a `Node3D`, the cast silently truncates to ~7 significant digits. At 10 million Godot units from the origin, positional precision degrades to ~1 meter per step, causing visible mesh jitter even when the underlying `UniVec3` is exact. This is distinct from the floating-origin architecture — it happens even if floating origin is implemented, because the *result* of subtracting the camera position is still cast through a float boundary.

**Why it happens:**
Developers assume "I'm doing the math in double, so the result is safe." The truncation is invisible in code — `(Vector3)(double3result)` compiles cleanly — but the GPU receives 32-bit values. The codebase already has this cast risk: `UniVec3 → Double3 → (Vector3)` for rendering.

**How to avoid:**
Keep the player ship pinned at or very near Godot's world origin (0,0,0) by translating all other objects around it. The floating-origin layer should be implemented first, before any body meshes are placed. The camera-relative offset passed to each `Node3D` should then represent a small number (within a few thousand Godot units), well within float precision. Never set a `Node3D.Position` from a raw `UniVec3` world coordinate; always subtract the camera/player `UniVec3` position first.

**Warning signs:**
- Meshes jitter or shimmer when the player moves more than a few hundred meters from origin
- Jitter scales with distance — worse at the edge of a star system than near a planet
- The C# simulation layer shows correct positions in `PrintPositions()` but the scene visually drifts

**Phase to address:**
Floating-origin / render-sync layer — the very first rendering phase. Must be solved before any body meshes are placed.

---

### Pitfall 2: Depth Buffer Z-Fighting Between Near and Far Objects at Astronomical Scale

**What goes wrong:**
Godot's depth buffer (even with Godot 4.3+ reverse-Z) cannot handle a near/far clip ratio greater than approximately 1:10,000 without z-fighting artifacts. At a 1:1-scale star system, a scene that renders a cockpit (0.1 m away) and a distant planet (10^9 m away) has a ratio of 10^10 — ten billion. This causes flickering polygon edges and depth-sort failures on any surface that overlaps in depth, including the planet sphere against background stars.

**Why it happens:**
Reverse-Z in Godot 4.3 improves precision distribution but cannot expand total available depth range beyond what a 32-bit float depth buffer can encode (~7 digits of precision). Extreme near/far ratios redistribute precision but don't eliminate the hard cap.

**How to avoid:**
Split the scene into two or more camera/depth-buffer passes: a "near" camera for the cockpit/ship geometry (near = 0.05, far = 10,000) and a "far" camera for celestial bodies (near = 1,000, far = depends on scale). Render the far pass first, clear depth, then render the near pass on top. In this project, since the player is always at origin, celestial bodies are rendered in their own pass with a matched near/far window that covers only the relevant objects in the current SOI. Stars and skybox objects use direction-only rendering with no depth write.

**Warning signs:**
- Flickering edges on planet meshes or on the ship model when a planet is visible
- Ground (if any) visually "above" the sky in some frames
- Setting a large `far` on `Camera3D` and noticing z-fighting immediately

**Phase to address:**
Multi-pass depth rendering — planet/body rendering phase, before ship and HUD are finalized.

---

### Pitfall 3: One-Frame Position Pop on SOI Transition Without Render-Frame Synchronization

**What goes wrong:**
When the SOI transition logic reparents the player and recalculates position from one coordinate space to another, the Godot scene nodes holding the visual representations of bodies are not necessarily repositioned in the same frame. If `GameWorld.TrySpaceTransition()` fires mid-frame and the visual sync runs in `_Process()` on the same or a different order, there is a one-frame window where the simulation's new position is written but Godot nodes still show the old positions, causing a visible position jump or "pop."

**Why it happens:**
The existing codebase separates simulation (`GameWorld`) from rendering. `TestSetup._Process()` drives both in sequence, but with the new game layer added on top, the render-sync and simulation-tick may be out of order, or the transition occurs inside the simulation loop before all visual nodes are updated.

**How to avoid:**
Implement a strict "simulate then render" order in a single `_Process()` call. The simulation tick (including all SOI transitions) must complete before any `Node3D.Position` writes happen. The render-sync step reads the final settled positions and writes them all to Godot nodes in one pass. Transition events should be queued as events, not applied mid-iteration.

**Warning signs:**
- A brief flash of objects in wrong positions when crossing an SOI boundary
- `PrintPositions()` shows the correct position but the visual object is one frame behind
- SOI transition fires while iterating `GameObjects`, causing a transition to read stale sibling positions

**Phase to address:**
Flight and simulation loop architecture — establish the tick-then-sync contract before implementing flight and camera code.

---

### Pitfall 4: Skybox Star/Galaxy Direction Vector Precision Loss on Scale Transition

**What goes wrong:**
The dynamic skybox stores distant objects (stars, galaxies) as direction vectors projected onto a sphere. When an SOI scale transition changes the reference frame (e.g., player exits a star's SOI into galaxy space), the direction from the player to each background body must be recomputed. If this recomputation is done using the `Double3`/`UniVec3` math but the result is stored in a GPU buffer as `float`, angular precision is lost for objects at extreme distance. A star 10,000 light-years away stored as a `float` direction vector has only ~7 digits of angular precision — which translates to roughly 0.1-degree angular error, visible as stars "snapping" position on transition.

**Why it happens:**
Developers compute the correct double-precision direction and then write it to a shader uniform or GPU buffer as `Vector3` (float). The transition between `double` and `float` happens silently at the C# → Godot bridge. Because direction vectors are unit vectors, the error is not magnitude error but angular error — and the human eye notices angular offset in star positions.

**How to avoid:**
Normalize direction vectors before writing to GPU uniforms and treat the skybox as direction-only (never position-based). Re-project all skybox objects from scratch on each SOI transition using the new parent-space `UniVec3` differences, not accumulated transforms. Test sky rendering by placing a known object (e.g., the sun) at a real stellar distance and verifying it occupies the same sky pixel before and after a scale transition.

**Warning signs:**
- Stars visibly shift position during or immediately after SOI transitions
- The skybox "resets" suddenly rather than smoothly maintaining apparent star positions
- Objects near the horizon of the SOI boundary show the largest angular errors

**Phase to address:**
Skybox / scale-transition rendering — second major rendering phase after in-system flight is stable.

---

### Pitfall 5: HDR Glow/Emission Breaking the 8-Bit Dithered Palette Pipeline

**What goes wrong:**
The 8-bit dithered aesthetic depends on color quantization happening as the final step in the post-process pipeline. Godot 4.6 changed glow/bloom to run *before* tonemapping (Screen blend mode, PR #110671). Emissive star meshes designed to bleed into surrounding pixels via glow now do so in HDR linear space before the dithering quantization, which means the glow bleeds into the palette-reduced image differently than expected — typically producing washed-out, unconstrained halos rather than the chunky, palette-limited look of retro games.

**Why it happens:**
The existing `UniRenderer` applies the dithering shader via a `ColorRect` post-process, but it runs *after* Godot's built-in glow pass. If emissive energy values are high (as needed for visible star glow at scale), the glow halo can exceed the 8-bit palette range before dithering quantizes it, resulting in HDR-style bloom that clashes with the lo-fi aesthetic.

**How to avoid:**
Use the environment's `glow_intensity` and `glow_strength` at low values that contain the glow within the intended palette range, or disable Godot's built-in glow and implement a palette-aware custom glow pass in the dithering shader itself. For star emission, test with actual 8-bit palette values rather than unbounded HDR emission. Check that `UniRenderer`'s dithering pass runs as the last layer (CanvasLayer 1025+, above all UI).

**Warning signs:**
- Stars show white/washed halos that ignore the 8-bit palette
- Bright pixels bleed into neighbors across many pixels rather than 1-2 dithered pixels
- Glow looks fundamentally different between Godot 4.5 and 4.6 (version was updated)

**Phase to address:**
Star rendering and emission — directly when implementing emissive star meshes. Verify with a palette chart that emissive halos land within the 8-color dither range.

---

### Pitfall 6: Per-Frame Node3D Position Writes Are Expensive at Scale

**What goes wrong:**
If the floating-origin render-sync updates the `Transform3D` of every visible body node every frame, performance degrades proportionally. Benchmarks show updating 10,000 `Node3D.Position` values takes ~6ms on mid-range hardware — consuming nearly half a 16ms frame budget. In a star system with many objects (moons, debris, stations), this becomes a bottleneck before any game logic runs.

**Why it happens:**
Developers set up a simple loop: `foreach object → node.Position = relativePos`. This works in prototypes but the per-call cost of Godot's C# interop for property writes accumulates quickly.

**How to avoid:**
Only sync `Node3D.Position` for objects that are visible in the current frame (frustum cull before syncing). Objects outside the current SOI are not rendered as geometry and should not have their positions written at all. Batch all position writes together at the end of `_Process()` after all simulation is settled. For the skybox and background objects, use a single dedicated shader that takes an array of direction uniforms rather than many individual nodes.

**Warning signs:**
- Frame time grows linearly as more bodies are added to the scene
- Profiler shows significant time in `Node3D.set_position` calls
- FPS is stable when only the ship exists, but drops when stars/planets are added

**Phase to address:**
Render-sync / floating-origin architecture — build the culled, batched sync pattern from the beginning rather than retrofitting it.

---

### Pitfall 7: Auto-Stabilize and Banking Feel Becoming Mushy at Low Speeds

**What goes wrong:**
Arcade flight auto-stabilize (returning rotation rate to zero when no input) feels correct at cruise speed but becomes lifeless and unresponsive at very low speeds (crawling near a planet) or during context-speed scaling transitions. If the stabilizer is implemented as a fixed damping factor, it damps rotation equally at all speeds, making the ship feel "locked on rails" at low speeds and potentially over-steering at high speeds.

**Why it happens:**
A single PD controller or lerp-to-zero for angular velocity does not know about the current speed context. The context-auto-scaling speed mechanic will vary speed by many orders of magnitude (centimeters/s near a moon surface vs. millions of km/s in deep space), and a fixed stabilizer gain covers at most 2-3 orders of magnitude comfortably.

**How to avoid:**
Scale the auto-stabilizer gain as a function of current speed context — slow at low speed (let the player micro-adjust), strong at high speed (prevents drift from building over long travel). Implement banking angle as a pure cosmetic visual roll derived from yaw rate, not as a physics influence. Clamp banking angle with a smooth lag function so it doesn't over-rotate and then snap back. Test at every speed decade: at 1 m/s, 100 m/s, 1 km/s, 1000 km/s, 1e6 km/s.

**Warning signs:**
- Ship feels locked or unresponsive when crawling near a planet surface
- Ship oscillates (overshoots then corrects) during banking at high speed
- Test: reduce throttle to zero and tap yaw — if the ship oscillates 2+ times before settling, damping is too high

**Phase to address:**
Arcade flight model — core of the first gameplay phase. Should be designed with speed-parameterized gains from day one.

---

### Pitfall 8: Context-Auto-Scaling Speed Feels Wrong at Transition Points

**What goes wrong:**
Context-auto-scaling speed (the mechanic that makes the ship accelerate enormously in empty space vs. crawl near bodies) will feel disorienting if the speed curve has visible discontinuities or if the transition from "planetary speed" to "interstellar speed" happens abruptly. Players will perceive sudden acceleration or deceleration that doesn't match their input.

**Why it happens:**
If the speed scale function is implemented as a step function (`if distance > threshold: use cosmic speed`) or uses a sharp sigmoid, the transition is immediate and feels like a warp drive triggering without player intent. The player's visual reference (nearby geometry) disappears at exactly the moment speed changes, compounding the disorientation.

**How to avoid:**
Use a continuous, smooth speed scale function: `speed = base_speed * f(distance_from_nearest_body)` where `f` is a gentle exponential or power curve. The transition should be imperceptible as a discrete event. Add a subtle HUD speed readout so the player can observe the scaling numerically. Test by flying directly away from a planet and measuring whether the player can predict when objects will shrink.

**Warning signs:**
- Players report "warp drive feel" without pressing a dedicated warp button
- Speed jumps from near-zero to enormous over a single frame when crossing a distance threshold
- HUD speed display changes by more than 10x per second during normal travel

**Phase to address:**
Arcade flight model — must be designed before the first playable flight test.

---

### Pitfall 9: SOI Null Slot Cascade During Rapid Multi-Boundary Crossing

**What goes wrong:**
The existing codebase has a known bug: rapid SOI boundary crossings (CONCERNS.md: "Double-Reparenting in TryEnterChildSOI") can produce incorrect local positions after rescaling. When the player ship is moving fast enough to cross multiple SOI boundaries in one frame (which is mandatory behavior once speed-scaling is in), the recursive `TrySpaceTransition()` can fire multiple times per frame, and if any transition leaves null slots or accesses a stale index (CONCERNS.md: "Null Slot Compaction in GameWorld"), the transition chain corrupts the hierarchy.

**Why it happens:**
The recursion in `TrySpaceTransition()` was designed for single-transition-per-frame scenarios. The player ship at high speed may skip through multiple SOI boundaries faster than the loop can track. Combined with null slot fragmentation, a transition mid-loop may read a sibling at a null index.

**How to avoid:**
Convert `TrySpaceTransition()` from recursive to iterative (already noted in CONCERNS.md as the fix). Add null guards on all `GameObjects[index]` dereferences before adding speed-scaling. Test by setting a very high velocity (faster than multiple SOI diameters per frame) and verifying the transition chain settles correctly. Fix the null slot issue (free-list or IsAlive flag) before enabling speed-scaling.

**Warning signs:**
- NullReferenceException in `TryEnterChildSOI` or `TryExitParentSOI` at high speeds
- Ship teleports to wrong position after crossing two SOI boundaries in quick succession
- `PrintState()` shows duplicate parent indices or missing child links after high-speed travel

**Phase to address:**
Pre-flight bugfix pass — must fix null-slot and recursive transition issues before any speed-scaling is implemented.

---

### Pitfall 10: Godot 4.6 Rendering Regression and Forward+ Crash

**What goes wrong:**
Godot 4.6 shipped with a documented rendering regression (Issue #115599): sky shaders, VoxelGI, and SDFGI lighting are broken in some upgrade paths from 4.5. More severely, Issue #115636 documents crashes when opening Forward+ projects. This project uses Forward+ (required for DirectX 12, as stated in PROJECT.md constraints). A mid-milestone engine patch or hot-fix that silently corrupts the existing `dithering.gdshader` or `crt.gdshader` uniforms is a real risk.

**Why it happens:**
Godot 4.6 changed shader uniform handling for glow and the tonemapping pipeline. The existing `UniRenderer` hardcodes shader paths with no fallback (CONCERNS.md: "Hardcoded Shader Path in UniRenderer"). If the shader API changes between patch versions, the silent failure leaves the renderer in an undefined state.

**How to avoid:**
Pin the Godot SDK version in `.csproj` to `4.6.2` exactly (already done). Add a startup validation in `UniRenderer._Ready()` that verifies the shader loaded correctly and logs a clear error if not. Before starting each new phase of work, verify the baseline scene still renders as expected in the pinned Godot 4.6.2 editor. Keep a screenshot of the expected dithering output for visual comparison.

**Warning signs:**
- Dithering shader renders as a fully black or white screen after an engine update
- Forward+ mode crashes on project open (use Compatibility mode to open, then verify settings)
- Glow intensity looks radically different without any project changes

**Phase to address:**
Project hygiene — validate renderer on every significant milestone boundary.

---

## Technical Debt Patterns

| Shortcut | Immediate Benefit | Long-term Cost | When Acceptable |
|----------|-------------------|----------------|-----------------|
| Set `Node3D.Position` from raw `UniVec3` world coordinate | Fast to code | All objects jitter at >1 km from origin | Never — always subtract camera position first |
| Single camera pass for cockpit + planets | Simple setup | Z-fighting between near cockpit and distant planets | Never at 1:1 scale |
| Fixed auto-stabilizer gain for all speeds | Simple controller | Feels mushy at low speed or oscillatory at high speed | Never — must be speed-parameterized |
| Skip null checks after `GameObjects[index]` during transitions | Saves code lines | Crash when high-speed crossing leaves null slot | Never |
| Use Godot built-in glow for star emission without tuning | Instant star halo | Blows out 8-bit palette, destroys retro aesthetic | Only in first day's prototype, never in playtest |
| Step function for context-speed scaling | Easy to implement | Disorienting speed jump for player | Never — use continuous curve |
| Recursive `TrySpaceTransition()` for high-speed flight | Works at low speed | Stack overflow / stale state at multi-boundary crossing | Acceptable only below 1 SOI/frame speed |
| Hardcoded shader path in UniRenderer | Works now | Silent crash if shader moved | Fix before first playtest |

---

## Integration Gotchas

| Integration | Common Mistake | Correct Approach |
|-------------|----------------|------------------|
| `UniVec3` → `Node3D.Position` | Cast `Double3` result directly to `Vector3` | Always subtract player `UniVec3` first; result should be small floats |
| `GameWorld` SOI transitions + Godot `_Process` | Call transition and render-sync in arbitrary order | Simulate all transitions first; write all `Node3D` positions in a single batch at end of frame |
| Godot glow + custom dithering shader | Rely on environment glow for star halos | Reduce or disable built-in glow; implement palette-aware glow in dithering shader |
| Skybox direction on SOI scale change | Re-use accumulated transforms | Recompute direction vectors from scratch using `UniVec3` differences after each transition |
| Godot 4.6 shader API | Assume 4.5 glow behavior | Verify tonemapping order: glow now runs before tonemapping in 4.6 |
| CanvasLayer post-processing + HUD | Place HUD at layer 0 and dithering at layer 1 | Set dithering layer to 1025 so it renders above all dynamic UI popups |
| `TrySpaceTransition()` recursion + speed scaling | Enable speed scaling before fixing recursion | Fix null-slot and iterative transition first; then enable high-speed crossing |

---

## Performance Traps

| Trap | Symptoms | Prevention | When It Breaks |
|------|----------|------------|----------------|
| Per-frame `Node3D.Position` write for all objects | Frame time grows with body count; 10k objects ≈ 6ms/frame overhead | Frustum-cull before syncing; only write positions for objects in current SOI and camera frustum | When more than ~200 bodies are in the scene simultaneously |
| Full sibling-list iteration in `TryEnterChildSOI` | CPU spike proportional to number of sibling bodies | Add distance pre-filter; only check siblings within N × SOI radius before full check | When a star has >10 planets or a galaxy has >20 stars |
| Computing `Magnitude()` (Sqrt) per SOI check | Wasted sqrt in every check for every body | Use `DistanceSq()` (already noted in CONCERNS.md) | Any scene with >5 bodies per SOI level |
| Rebuilding skybox every frame | GPU uniform writes every frame for all background bodies | Only rebuild skybox when SOI transition occurs; mark dirty flag | Continuous skybox rebuilding at 60 fps for hundreds of background stars |
| Single-pass depth rendering for cockpit + planets | Z-fighting begins at near/far ratio > 10,000:1 | Multi-pass depth: far pass for planets, clear depth, near pass for ship | As soon as a planet appears in the same frame as the ship |

---

## UX Pitfalls

| Pitfall | User Impact | Better Approach |
|---------|-------------|-----------------|
| Speed scaling with no HUD indicator | Player has no idea why the ship is "faster" — feels like a bug | Always show current speed and nearest body on HUD, even minimally |
| Banking that overshoots and snaps back | Nauseating, looks broken | Cosmetic bank with smooth lag (not physics-driven); cap at ±30 degrees |
| Auto-stabilize that fights the player | Inputs feel sluggish, unresponsive | Player input always wins; stabilizer torque never exceeds input torque |
| SOI transition with no visual feedback | Player thinks game crashed when stars reposition | A brief flash, color shift, or HUD label ("Entering star system") marks the transition |
| Crawling speed near bodies with no reference point | Player cannot tell they are moving | Keep a body visible in the frame when crawling; speed relative to it is perceptible |

---

## "Looks Done But Isn't" Checklist

- [ ] **Floating origin:** Looks stable near origin — verify at 10,000+ Godot units from origin; ship and planet should not jitter
- [ ] **Depth precision:** Planets render cleanly at range — verify no z-fighting when planet disk is >50% of screen
- [ ] **SOI transition:** Transition fires without error — verify that crossing at 10x, 100x, 1000x SOI-diameter speed all settle correctly without null crash
- [ ] **Skybox reposition:** Stars appear stationary during travel — verify a known bright star stays in same screen pixel before and after an SOI scale transition
- [ ] **Speed scaling:** Context speed changes — verify no discrete jump; player cannot tell exactly when the speed threshold crosses
- [ ] **Dithering + glow:** Emissive star looks retro — verify emissive halos are palette-limited, not HDR washed-out
- [ ] **Banking feel:** Ship banks when turning — verify bank recovers smoothly in < 0.5 seconds after releasing input, no oscillation
- [ ] **HUD overlay:** Post-process covers 3D world — verify HUD elements are above the dithering ColorRect layer (not dithered themselves)
- [ ] **High-speed multi-boundary crossing:** Single fast transit through 3 SOI levels — verify no NullReferenceException and correct final position

---

## Recovery Strategies

| Pitfall | Recovery Cost | Recovery Steps |
|---------|---------------|----------------|
| Float truncation jitter discovered after many bodies built | HIGH | Introduce floating-origin layer; all body nodes must be repositioned relative to camera rather than world; regression-test all body placements |
| Z-fighting discovered after full ship model imported | MEDIUM | Add second camera pass (far scene); clear depth between passes; existing ship model is unaffected |
| SOI null crash discovered at high speed | MEDIUM | Fix iterative transition and null-slot guards; requires careful regression test of all transitions |
| Skybox star pop discovered after skybox implemented | LOW | Recompute direction vectors from UniVec3 differences on transition; no structural change needed |
| Glow destroys palette look discovered in playtesting | LOW-MEDIUM | Reduce environment glow; add palette clamp to dithering shader; emissive values may need retuning |
| Speed curve feels wrong discovered in playtesting | LOW | Tune the curve parameters; no structural change; needs iteration time |
| Godot 4.6 rendering regression discovered mid-phase | MEDIUM | Revert to last working state; report bug upstream; workaround may require shader patches |

---

## Pitfall-to-Phase Mapping

| Pitfall | Prevention Phase | Verification |
|---------|------------------|--------------|
| Float truncation jitter (Pitfall 1) | Phase 1: Floating-origin render-sync | Place body at 50,000 Godot units; confirm zero jitter on camera movement |
| Depth buffer z-fighting (Pitfall 2) | Phase 2: Planet mesh rendering | Render planet at full SOI approach distance; confirm no flickering edges |
| One-frame SOI position pop (Pitfall 3) | Phase 1: Simulation loop architecture | Record 10 SOI crossings at 60fps; confirm no single-frame position discontinuity |
| Skybox direction precision (Pitfall 4) | Phase 3: Skybox / scale-transition rendering | Mark sun position in sky; cross into galaxy space; confirm sun is in same screen pixel |
| HDR glow breaking 8-bit palette (Pitfall 5) | Phase 2: Star rendering | Screenshot star at close range; verify halos are within 8-bit palette colors |
| Per-frame position write cost (Pitfall 6) | Phase 1: Render-sync architecture | Profile with 50 bodies; ensure position-sync cost < 1ms per frame |
| Auto-stabilizer feel (Pitfall 7) | Phase 1: Arcade flight model | Test at 5 speed decades; stabilizer should feel responsive at each |
| Context-speed curve feel (Pitfall 8) | Phase 1: Arcade flight model | Blind playtest: player cannot identify speed-scaling moment during normal travel |
| Null-slot cascade at high speed (Pitfall 9) | Phase 0: Pre-flight bugfix | Run stress test: ship at 100x SOI-crossing speed; zero exceptions |
| Godot 4.6 rendering regression (Pitfall 10) | Every phase boundary | Baseline screenshot comparison at start of each new phase |

---

## Sources

- Godot Engine: [Emulating Double Precision on the GPU to Render Large Worlds](https://godotengine.org/article/emulating-double-precision-gpu-render-large-worlds/)
- Godot GitHub Issue #58516: [Camera jitter even with float=64 build](https://github.com/godotengine/godot/issues/58516)
- Godot Engine: [Introducing Reverse Z](https://godotengine.org/article/introducing-reverse-z/)
- Godot GitHub Issue #44988: [Z-fighting with high far plane value](https://github.com/godotengine/godot/issues/44988)
- Godot GitHub Issue #115599: [Major rendering regression in Godot 4.6](https://github.com/godotengine/godot/issues/115599)
- Frozen Fractal blog: [Around The World Part 14 — Floating the Origin](https://frozenfractal.com/blog/2024/4/11/around-the-world-14-floating-the-origin/)
- Game Developer: [A Real-Time Procedural Universe Part 3 — Matters of Scale](https://www.gamedeveloper.com/programming/a-real-time-procedural-universe-part-three-matters-of-scale)
- Godot GitHub PR #110671: [Blend glow before tonemapping, change default to screen](https://github.com/godotengine/godot/pull/110671)
- Godot Forum: [Setting global_position is slow — benchmark references](https://forum.godotengine.org/t/setting-global-position-is-slow-what-are-some-benchmark-perfomance-references/118288)
- GDQuest: [Godot 4.6 — What changes for you](https://www.gdquest.com/library/godot_4_6_workflow_changes/)
- Yosoygames: [New way of handling camera relative rendering](https://www.yosoygames.com.ar/wp/2013/10/new-way-of-handling-camera-relative-rendering/)
- EcoSpace codebase: `.planning/codebase/CONCERNS.md` and `.planning/codebase/ARCHITECTURE.md` (2026-06-12 audit)

---
*Pitfalls research for: 1:1-scale space sim on Godot 4.6 C# — brownfield game layer over UniVec3/SOI engine*
*Researched: 2026-06-12*
