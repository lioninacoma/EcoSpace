# Phase 2: Dynamic Skybox - Context

**Gathered:** 2026-06-14
**Status:** Ready for planning

<domain>
## Phase Boundary

Build a **stable spherical skybox** that projects **only the next scale tier out** as distant light points, on top of the existing floating-origin mesh renderer (`WorldRenderer`) and dithering post-process. Concretely, Phase 2 delivers (RND-05, RND-07):

1. **Skybox projection** — real next-tier-out bodies (other star systems' stars while in-system) rendered as world-fixed light points on a spherical sky that never drifts with camera rotation.
2. **Magnitude-ranked appearance** — points sized/brightened by real luminosity × true-distance attenuation, carrying each body's authored color, with a minimum-brightness floor so no real body is ever invisible.
3. **Re-tier logic + per-frame update** — the framework that decides which tier is meshes vs skybox per the ship's space, updated each frame and on SOI/scale transitions; correctness unit-tested.
4. **Handoff machinery (RND-07 baseline)** — the mechanism for an instant, exact-match point↔mesh swap (matched screen position, color, brightness) so a future promotion/demotion shows no pop.
5. **Test-data extension** — add 2-3 sibling star systems under the Galaxy (real interstellar distances, varied colors/luminosities) so the in-system skybox has real content to project and rank.

**Phase 2 vs Phase 3 split (IMPORTANT):** Phase 2 builds and *proves* the skybox machinery and re-tier **logic** in-system. The **visible** Star↔Galaxy re-tier and the actual point→mesh **promotion/demotion** are demonstrated in **Phase 3** (when galaxy-tier mesh rendering + cross-galaxy travel exist). This is consistent with the existing REQUIREMENTS.md traceability that already splits RND-05/RND-07 across Phase 2 (in-system) and Phase 3 (in-galaxy).

**Out of scope (later phases):** visible Star↔Galaxy re-tier demo, in-galaxy stars-as-meshes, point→mesh promotion demo, other-galaxy point data, cross-galaxy travel (all Phase 3, TRV-02); procedural generation, cockpit art, economy, combat, CRT overlay, boost/audio (v2).

</domain>

<decisions>
## Implementation Decisions

### Point Appearance & Brightness (RND-05)
- **D-17: Real-magnitude brightness/size model.** Each point's apparent brightness and size derive from the body's real luminosity attenuated by true distance — points are magnitude-ranked (Elite/Frontier realism), not uniform dots.
- **D-18: Points carry the body's authored color.** A point uses the body's `BaseColor` (consistent with Phase 1 D-13 per-body hues); required for the RND-07 color match on handoff.
- **D-19: Minimum-brightness floor.** Apparent brightness is clamped to a floor so every real next-tier body always shows as at least one lit dithered pixel — the sky is never deceptively empty. Magnitude still ranks everything above the floor. (Exact floor/curve = planner tuning.)
- **D-20: Points feed the shared bloom.** Skybox points bloom through the same `WorldEnvironment` glow the star mesh uses (Main.tscn `glow_enabled`), unifying the look and aiding the RND-07 brightness match across the handoff.

### Mesh↔Skybox Handoff (RND-07)
- **D-21: Instant exact-match swap.** On a tier transition, the point and the new mesh occupy the same screen position with pre-matched color & brightness so the swap is imperceptible. **No crossfade/alpha blend** — cross-dissolves fight the 8-bit dither palette. (Phase 2 builds the machinery; the visible swap is exercised in Phase 3.)
- **D-22: Promotion/demotion triggered by scale-boundary crossings only.** A body swaps point↔mesh strictly on tier/SOI scale crossings (e.g. Star↔Galaxy), never by proximity/screen-size. A sibling star stays a skybox point until the ship actually enters its tier — matches the locked tiered model.

### Demonstration Scope & Test Data
- **D-23: Add 2-3 sibling star systems under the Galaxy.** Authored in `TestSetup` at realistic interstellar distances with varied colors/luminosities, so the in-system skybox has real, magnitude-ranked content and the min-floor is genuinely exercised. Other-galaxy point data is deferred to Phase 3.
- **D-24: Phase 2 = in-system build + logic; visible re-tier/promotion = Phase 3.** Phase 2 proves: world-fixed projection, no camera drift, dither integration, per-frame update, correct re-tier **logic** (unit-tested), and the handoff machinery. The *visible* Star↔Galaxy re-tier and point→mesh promotion are demonstrated in Phase 3. **→ ROADMAP follow-up:** Phase 2 success criteria 1 ("skybox visibly changes when crossing a scale boundary") and 4 (visible promotion handoff) must be reworded to reflect this in-system-build vs Phase-3-visible-demo split before/at planning (use `/gsd-phase` to edit).

### Backdrop Richness
- **D-25: Real next-tier bodies only — no decorative starfield.** The sky shows exactly the real next-tier objects (sparse now, fills in as authored universe grows). Honors the honest-1:1 ethos and RND-05's "only the next tier out"; every point is a real, eventually-flyable body that can promote to a mesh.

### Luminosity Data Model
- **D-26: Add an explicit `Luminosity` (absolute-magnitude-style) attribute to `UniObject`,** authored per body in `TestSetup` — alongside existing `RadiusMeters` / `BaseColor`. Drives the D-17 magnitude model with physically-meaningful relative brightness, decoupled from radius. (Field name/units = implementation detail.)

### Dither Integration (RND-07 constraint)
- **D-27: Skybox points pass through the SAME dithering post-process as the meshes.** Points are quantized by the existing `dithering.gdshader` exactly like meshes so a point and the mesh it becomes share one palette — essential for the RND-07 no-pop color/brightness match. Locked as a constraint.

### Carried Forward (locked upstream — do not re-litigate)
- **World-fixed, never camera-drifting** (RND-05 criterion 2). The sky is fixed to the absolute/world orientation, not the ship or parent body.
- **True 1:1 radii (Phase 1 D-15)** — distant bodies are sub-pixel as meshes; the skybox is what makes them visible as points.
- **Unit-space render model (RND-06)** + per-space `1e-8` render factors + `1e6` camera far plane — the skybox lives *outside* this finite-render volume (infinite-distance/direction-only projection).
- **Shader-driven, space-independent body shading** (Phase 1 `body_lit.gdshader`) — the handoff baseline builds on shading that doesn't change character across spaces.

### Claude's Discretion
- The sky **technique** (Godot `Sky` resource + sky `ShaderMaterial` with direction encoding vs. an inverted far-sphere mesh vs. another approach) and the **half-resolution sky pass** — flagged in STATE.md as "moderately novel"; left to phase research/planner.
- Exact min-brightness floor value and the luminosity→apparent-brightness curve.
- Exact sibling-star coordinates, colors, and luminosity values (lock: "varied, real-ish, true interstellar distances").
- `Luminosity` field name, units, and default; how re-tier logic is unit-tested.

</decisions>

<canonical_refs>
## Canonical References

**Downstream agents MUST read these before planning or implementing.**

### Phase requirements & scope
- `.planning/ROADMAP.md` §"Phase 2: Dynamic Skybox" — goal, success criteria (note D-24: criteria 1 & 4 need a reword for the Phase 2/3 split), requirements RND-05 + RND-07.
- `.planning/REQUIREMENTS.md` §Rendering (RND-05, RND-07) + the phase-spanning note on RND-02/04/05/07 — full requirement text and the explicit Phase 2 (in-system) vs Phase 3 (in-galaxy) delivery split.
- `.planning/PROJECT.md` §Context "Skybox model (tiered)" + §Key Decisions (RND-06 unit-space render, 8-bit palette) — the tiered mesh/skybox model and render-scale rationale.

### Engine / render code to build on (consume, do not replace)
- `Scripts/Render/WorldRenderer.cs` — floating-origin mesh sync; `SyncBodies()`, `RenderFactorFor()`, per-space `*RenderFactor` exports, `CameraFarPlane`, `IsStarBody()` (name-based). The skybox is the complement of what this renders; the handoff (RND-07) couples to its per-frame body render positions.
- `Scripts/TestSetup.cs` — authored `Root→Galaxy→Star→PlanetA/B→Ship` hierarchy with `BaseColor`/`RadiusMeters` per body; **single star, single galaxy today** — extend here with sibling star systems (D-23) and the `Luminosity` attribute (D-26).
- `Scripts/UniObject.cs` — `Space` enum + `ChildSpace`/`ParentSpace` (the source of "current tier" and "next tier out"); add the `Luminosity` field here (D-26).
- `Scripts/GameWorld.cs` — SOI transition entry points; re-tier logic keys off space/parent changes that happen here.
- `Scripts/Render/PostProcessRenderer.cs` + `Shaders/dithering.gdshader` — the dither post-process the skybox MUST pass through (D-27).
- `Shaders/body_lit.gdshader` — space-independent body shading; reference for matching point↔mesh brightness/color at handoff (RND-07).
- `Main.tscn` — `Camera3D` (far=1e6) + `Environment` (`background_mode=1` flat black today; glow enabled). The skybox changes the background to a real sky; `WorldRenderer` mounted as `RenderBridge`.

### Codebase maps
- `.planning/codebase/ARCHITECTURE.md`, `.planning/codebase/STRUCTURE.md`, `.planning/codebase/CONVENTIONS.md` — namespace/layer conventions (`Render` namespace) and where the skybox renderer should live.
- `.planning/phases/01-in-system-flight-mvp/01-CONTEXT.md` — Phase 1 decisions the skybox builds on (D-13 hues, D-14 emissive stars, D-15 1:1 radii, RND-06 reframe).

</canonical_refs>

<code_context>
## Existing Code Insights

### Reusable Assets
- `WorldRenderer.SyncBodies()` per-frame loop + `renderPositions` map — the skybox can hook the same per-frame pass; body render positions are already computed here for the handoff.
- `RenderFactorFor()` / per-space factors + `IsStarBody()` — existing per-space scaling and star identification to reuse for tier classification.
- `dithering.gdshader` + `PostProcessRenderer` — the 8-bit dither already runs full-screen; route the sky through it (D-27) for one unified palette.
- `WorldEnvironment` glow (Main.tscn) — existing bloom path for D-20.
- `TestSetup.SetupScene()` — the authored-data entry point to extend with sibling stars + luminosity (D-23, D-26).

### Established Patterns
- Read-only renderer consuming `GameWorld` state — `WorldRenderer` MUST NOT mutate `UniVec3`/call `TranslatePos`; the skybox follows the same read-only consumer rule.
- Per-frame node reposition (never spawn/free per frame); lazy `GetOrCreate` for mesh instances — mirror for any sky point primitives.
- Integer-index object model with `(uint)i < (uint)Count` bounds checks; `Space` enum drives tier classification.

### Integration Points
- **Tier classifier** → for the ship's current space, partition `GameObjects` into current-tier (meshes, `WorldRenderer`) vs next-tier-out (skybox points) vs beyond (neither).
- **Skybox projection** → world-fixed direction per next-tier body (absolute orientation), brightness from `Luminosity` × distance with floor (D-17/19), color from `BaseColor` (D-18), through dither (D-27) + glow (D-20).
- **Handoff** → at a tier crossing, align a body's point and mesh in screen position/color/brightness for an instant swap (D-21); driven by the same SOI transition events `GameWorld` emits (D-22).

</code_context>

<specifics>
## Specific Ideas

- **Reference feel:** Elite/Frontier for the vast star-strewn cosmos; the magnitude-ranked, honest-1:1 sky is the point (D-17, D-25).
- **Known novelty (flagged for research, not a blocker):** sky-shader direction encoding + half-resolution sky pass (per STATE.md "Blockers/Concerns") — the recommended shallow phase research target before planning.
- **Sparse-sky acceptance:** with only 2-3 sibling stars and no decorative backdrop (D-25), the Phase 2 sky is intentionally sparse; the min-floor (D-19) guarantees they're all visible. Richness grows with Phase 3 data.

</specifics>

<deferred>
## Deferred Ideas

- **Visible Star↔Galaxy re-tier + point→mesh promotion demo** — Phase 3 (galaxy-tier mesh rendering + cross-galaxy travel). Phase 2 builds the logic/machinery only (D-24).
- **Other-galaxy point data** (in-system skybox showing galaxies, in-galaxy skybox = only galaxies) — Phase 3 authored data at galaxy/universe scale.
- **In-galaxy stars-as-meshes** (galaxy-tier RND-02/04) — Phase 3.
- **Decorative/ambient background starfield** — rejected for v1 (D-25); non-diegetic and against the honest-1:1 model.
- **Derive luminosity from radius+color / authored-brightness-only models** — considered and rejected in favor of an explicit `Luminosity` attribute (D-26).
- **Crossfade/blended handoff** — considered and rejected (D-21) in favor of instant exact-match (cleaner under dither).

None — discussion stayed within phase scope.

</deferred>

---

*Phase: 2-Dynamic Skybox*
*Context gathered: 2026-06-14*
