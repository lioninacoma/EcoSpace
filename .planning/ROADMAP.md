# Roadmap: EcoSpace

## Overview

Three phases build the playable game layer on top of EcoSpace's existing multi-scale universe engine. Phase 1 delivers the first sequenced acceptance goal — in-system flight with dithered body rendering and a minimal HUD — as a single large vertical slice that also fixes the known pre-flight crash. Phase 2 adds the dynamic skybox, the only subsystem flagged for shallow phase research. Phase 3 delivers the second sequenced goal: cross-galaxy travel with full SOI transitions at intergalactic scale.

## Phases

**Phase Numbering:**

- Integer phases (1, 2, 3): Planned milestone work
- Decimal phases (2.1, 2.2): Urgent insertions (marked with INSERTED)

Decimal phases appear between their surrounding integers in numeric order.

- [x] **Phase 1: In-System Flight MVP** - Fix pre-flight crash, floating-origin rendering, arcade flight, HUD — player can fly a star system and approach dithered bodies (COMPLETE 2026-06-14)
- [x] **Phase 2: Dynamic Skybox** - Shader-type sky updated on scale-tier transitions; only the next tier out (other systems' stars, then only galaxies) is projected onto a stable spherical skybox, with a visually continuous skybox↔mesh handoff (completed 2026-06-15)
- [x] **Phase 3: Cross-Galaxy Travel** - Extend hand-authored data to galaxy/universe scale; full SOI chain validated at intergalactic distances (UAT paused 1/7 — gated on Phase 4 flight model) (completed 2026-06-17)
- [x] **Phase 4: Flight Model v2 — tier & target-aware speed** - Per-tier speed ceiling + target-distance ease-out replacing the single global MaxSpeed; world-pinned target outline (minimal 999.1 slice); fixes in-system over-speed and the galaxy-SOI-exit dead zone within one envelope (COMPLETE 2026-06-17)
- [x] **Phase 5: Rendering Overhaul** - Foundational full rewrite that unifies world rendering, post-process (8-bit/dither/CRT), and body-lighting into one coherent multi-tier rendering layer; replaces the ad-hoc skybox-loop + per-tier WorldRenderer routing and gives the tracked render debts (galaxy-space star findability, Universe-space galaxy visibility) a robust base to be solved on — individually, in later phases, not all at once (completed 2026-06-20)

## Phase Details

### Phase 1: In-System Flight MVP

**Goal**: Player can fly around a single star system, approach dithered planets and stars, and read their speed and context on a minimal HUD
**Mode:** mvp
**Depends on**: Nothing (first phase); consumes existing engine (GameWorld, UniVec3, SOI, dithering shader)
**Requirements**: STAB-01, WORLD-01, RND-01, RND-02, RND-03, RND-04, RND-06, FLT-01, FLT-02, FLT-03, HUD-01, HUD-02, HUD-03, HUD-04, TRV-01
**Success Criteria** (what must be TRUE):

  1. Player can pitch, yaw, and roll the ship with mouse input and arcade auto-stabilization; the ship does not drift or spin uncontrolled
  2. Ship speed auto-scales to its SOI context — noticeably slower near bodies, faster in open space — with no manual mode switch
  3. Planets and stars appear as dithered sphere meshes with an 8-bit color palette; bodies grow visibly as the player approaches
  4. A minimal HUD shows current speed (with scale-adaptive units), a context/location label, a crosshair, and a cycle-able target readout
  5. Player can cross SOI boundaries (enter and exit planet/star SOI) at high speed without a crash or hierarchy corruption

**Plans**: 4 plans
Plans:

- [x] 01-01-PLAN.md — Walking Skeleton: STAB-01 iterative transition + floating-origin render + thrust-driven ship + live speed HUD
- [x] 01-02-PLAN.md — Body rendering: per-body hues + 1:1 radii + emissive star/OmniLight + color-palette dither
- [x] 01-03-PLAN.md — Flight feel: virtual-joystick steering + roll + persistent throttle [-1,1] (reverse) + distance-scaled speed + crosshair/reticle (APPROVED)
- [x] 01-04-PLAN.md — Minimal HUD: adaptive speed units + context label + target cycle + off-screen marker + phosphor-green (TRV-01) (APPROVED)

**UI hint**: yes

### Phase 2: Dynamic Skybox

**Goal**: A stable spherical skybox represents only the next scale tier out as distant light points — other systems' stars (and galaxies) while in-system, and *only* other galaxies while in Galaxy space — updates when the player crosses a scale boundary, never drifts with camera rotation, and hands off seamlessly to/from meshes. Render scope is the current scale **tier**, not the immediate SOI parent: in Galaxy space the current galaxy's stars/suns become meshes
**Mode:** mvp
**Depends on**: Phase 1
**Requirements**: RND-05, RND-07
**Success Criteria** (what must be TRUE):

  1. The re-tier logic correctly classifies which bodies are skybox points vs current-tier meshes for the ship's space, recomputed each frame and on SOI/scale transitions — verified by unit tests; in-system, the sibling-star points are projected at world-fixed directions. (The *visible* Star↔Galaxy re-tier is demonstrated in Phase 3 — D-24.)
  2. The skybox does not rotate or drift as the player rotates the ship — it remains fixed to world space
  3. Only the next tier out is on the skybox; bodies of the current tier are meshes (in Galaxy space the galaxy's stars are meshes, not skybox points), and bodies beyond the next tier are neither
  4. The skybox↔mesh handoff machinery (RND-07 baseline) is built and proven in-system: a skybox point and its corresponding mesh can be aligned to the same screen position with matched color and brightness for an instant, pop-free swap (no crossfade — D-21). (The *visible* promotion/demotion on a scale boundary is demonstrated in Phase 3 — D-24.)

**Plans**: TBD

### Phase 3: Cross-Galaxy Travel

**Goal**: Player can fly from one galaxy to another, with SOI transitions and the skybox updating correctly across the full Root → Universe → Galaxy hierarchy
**Mode:** mvp
**Depends on**: Phase 2
**Requirements**: TRV-02; galaxy-tier portions of RND-02, RND-04, RND-05, RND-07 (in Galaxy space the current galaxy's stars/suns become emissive meshes; the skybox carries only other galaxies; the Star↔Galaxy skybox↔mesh handoff stays seamless)
**Success Criteria** (what must be TRUE):

  1. The hand-authored test universe contains at least two distinct galaxies the player can navigate between
  2. Player can fly from inside one galaxy's SOI to intergalactic space and into a second galaxy's SOI with no crash, pop, or loading screen
  3. The skybox updates correctly at each scale transition (galaxy-to-universe and universe-to-galaxy) during cross-galaxy flight
  4. Ship speed at intergalactic scale reaches FTL-equivalent magnitudes via auto-scaling — the journey is completable in a reasonable play session
  5. In Galaxy space the current galaxy's stars/suns render as emissive sphere meshes (promoted from skybox light-points) and the skybox there carries only other galaxies; the promotion/demotion handoff across the Star→Galaxy boundary is visually continuous (RND-07) — no perceptible pop in position, brightness, or color

**Plans**: 3 plans (3 waves)Plans:
**Wave 1**

- [x] 03-01-PLAN.md — Galaxy foundation + headline visual: UniObject.Type/galaxy fields + 3 authored galaxies (home spiral + mirror + elliptical cluster) at true 1:1 distances + procedural galaxy sky shader + SkyboxRenderer Type partition (wave 1)

**Wave 2** *(blocked on Wave 1 completion)*

- [x] 03-02-PLAN.md — Intergalactic flight: remove SpeedOfLight cap, raise MaxSpeed to FTL magnitude (D-35), NaN-safe motion guard — fly home→destination galaxy SOI with natural ease-out (wave 2)
- [x] 03-03-PLAN.md — Galaxy-tier meshes + visible handoff: WorldRenderer routes by ObjectType (emissive Galaxy-space star meshes, skip galaxies), Star↔Galaxy point↔mesh swap visually continuous (RND-02/04/05/07 galaxy tier) (wave 2)

### Phase 4: Flight Model v2 — tier & target-aware speed

**Goal**: Replace the single global-`MaxSpeed` envelope with a context-correct flight model so flight feels good and stays usable across every scale — slow and precise in-system, FTL-equivalent intergalactic — within one auto-scaling envelope, with no separate FTL mode.
**Depends on**: Phase 3
**Origin**: Phase 03 UAT — `.planning/todos/pending/flight-speed-model-tier-and-target-aware.md` (supersedes the rejected `thrust-zero-at-galaxy-soi-exit` quick fix; absorbs the deferred cross-SOI half of the HUD target work).
**Requirements**: refines FLT-02/FLT-03 (context-auto-scaling speed); pulls a minimal slice of backlog 999.1 (cross-SOI target selection overriding D-12; world-pinned target outline).
**Success Criteria** (what must be TRUE):

  1. In-system travel is usable: the player can approach and stop near a planet/star without overshooting; speeds feel proportional to the current scale tier (no jump to intergalactic speed inside a star system).
  2. Intergalactic travel still reaches FTL-equivalent speed and eases onto the destination galaxy — and flying OUT of a galaxy's SOI ramps speed back up smoothly (no thrust-zero dead zone at the boundary).
  3. The speed envelope is tier-aware (per `UniObject.Space`: Planet/Star/Galaxy/Universe) with smooth easing across SOI transitions in both directions — no speed pop.
  4. When a target is set, thrust/ease-out is governed by distance to that target ("thrust handled by current target"); with no target, speed is bounded by the current space tier.
  5. The player can select a target in another SOI (cross-space targeting, overriding D-12), and a world-pinned target outline/circle with a minimum on-screen radius marks the active target so it stays findable.
  6. No separate FTL mode is introduced; the model is a single auto-scaling envelope (D-35/D-36 spirit preserved).

**Out of scope**: the full 999.1 nav-HUD (hierarchy tree selector across the whole universe); procedural content; cockpit art. (Only the minimal "reliable current target + outline" slice of 999.1 is pulled in.)

**Verification**: requires in-game Godot play-test (the speed/feel and SOI behavior are not unit-testable). Unblocks the deferred Phase 03 UAT items (fly-out SOI behavior, intergalactic transit timing).

**Plans**: 2 plans (2 waves)
Plans:

**Wave 1**

- [x] 04-01-PLAN.md — Tier- & target-aware speed envelope: per-tier ceiling (parent.SOIMeters x k), symmetric proximity damp capped at tier ceiling, target-distance ease-out; + read-only Hud.ActiveTargetIndex accessor (wave 1)

**Wave 2** *(blocked on Wave 1 — shares Hud.cs)*

- [x] 04-02-PLAN.md — World-pinned target circle: Hud._Draw outline gated on WorldRenderer.GetRenderPosition with minimum on-screen radius + edge-marker fallback; full-phase play-test checkpoint (wave 2)

### Phase 5: Rendering Overhaul

**Goal**: Replace the current ad-hoc, per-tier-special-cased rendering with one coherent rendering layer that draws every body correctly and robustly across all scale tiers (Planet → Star → Galaxy → Universe), with a single source of truth for classification, appearance, and tier handoffs — so that the outstanding render debts can be solved cleanly on top of it instead of fought one engine quirk at a time.
**Mode:** TBD (set in discuss/plan-phase — likely multi-wave given the scope)
**Depends on**: Phase 4
**Origin**: Phase 05 (old "findability bundle") was abandoned after the StarPointRenderer manual clip-space billboard dead-end (HANDOFF.json, 2026-06-18). User direction (2026-06-19): do a foundational rendering rewrite first; keep the individual render problems as standalone debts, fixed later one at a time — not all bundled in one phase.
**Scope (full rewrite)**: unify the three rendering concerns into one layer —

  1. **World rendering** — `WorldRenderer` (floating-origin mesh sync) + `SkyboxRenderer` (next-tier-out points/discs) + the per-tier `ObjectType` routing, replaced by a single tier-driven render path with one classification + appearance source of truth (continuing the `StarRendering` / `TierClassifier` direction).
  2. **Post-process** — the 8-bit / dither / CRT stack (`PostProcessRenderer`, `dithering.gdshader`).
  3. **Body-lighting** — the unified `body_lit.gdshader` Lambert/terminator model.

**Requirements**: refines RND-02/RND-04/RND-05/RND-07 (tier-member mesh/point rendering + continuous point↔mesh handoff) and RND-01/RND-03/RND-06 (8-bit dithered look); does not by itself close the findability/visibility debts.
**Success Criteria** (what must be TRUE):

  1. There is ONE rendering path that classifies and draws every body by its scale tier each frame, with no per-`ObjectType` special-case branches scattered across renderers — appearance derives from a single source of truth (physical size + luminosity), reused by point, mesh, and disc representations.
  2. The skybox↔mesh (point↔mesh) handoff (RND-07) is structurally guaranteed by shared math, not coincidental per-renderer alignment — no pop in position, brightness, or color across any tier boundary (Star↔Galaxy, Galaxy↔Universe).
  3. The 8-bit/dither/CRT post-process and the unified body-lighting model are preserved (or improved) and integrated into the new layer — the established look does not regress.
  4. No fragile manual clip-space billboard technique is reintroduced (the abandoned StarPointRenderer anti-pattern); robustness is verified by in-game play-test across Planet/Star/Galaxy/Universe space with a clean build and green TierClassifier tests.
  5. The new layer exposes the seams needed to later add a findability floor (minimum on-screen size/brightness) for distant tier bodies, WITHOUT itself implementing the fix — the tracked debts remain explicitly out of scope.

**Out of scope** (tracked as standalone debts, to be promoted to their own later phases — *not* solved here):

  - `galaxy-space-star-meshes-invisible` (P1) — minimum on-screen size/brightness floor for galaxy-space star meshes.
  - `galaxy-visibility-in-universe-space` (P2, design fork) — how galaxies render in Universe space; revisits D-28.
  - `galaxy-disc-tilt-foreshortening` (polish) — largely addressed by the kept c98f56c tilt; verify post-rewrite.

**Verification**: in-game Godot play-test (GDShader/visual; not fully unit-testable) across all four spaces, plus the existing TierClassifier unit suite.

**Plans**: 4/4 plans complete
Plans:

**Wave 1**

- [x] 05-01-PLAN.md — Branch + descriptor/projection foundation: LuminousBodyDescriptor + LuminousLod + LuminousDescriptorBuilder (single classify→project→appearance loop migrated from SkyboxRenderer) + unit tests, alongside the still-running skybox (no visual change) (wave 1) — COMPLETE 2026-06-19 (play-test APPROVED)

**Wave 2** *(blocked on Wave 1 + play-test)*

- [x] 05-02-PLAN.md — Sky-shader refeed + near-star findability: re-enable SkyboxRenderer (reset process_mode), drive skybox.gdshader from LuminousDescriptorBuilder.Descriptors[] (skybox KEPT, D-07 reversed); MinVisibleBrightness floor for distant stars (closes P1); NearStarEmissionFloor fix for the missing sun (D-12); galaxy_disc_weights uniform groundwork (D-13) (wave 2) — COMPLETE 2026-06-19 (play-test APPROVED)

**Wave 3** *(blocked on Wave 2 + play-test)*

- [x] 05-03-PLAN.md — Post-process glow/halo + 3-stage handoff + galaxy crossfade: narrow luminous_pass.gdshader to near-star glow/halo (relax depth gate via is_near=LodWeight, halo wraps the mesh, D-11); remove the galaxy loop (galaxies stay in the sky shader, D-13); extend LuminousLod.GalaxyDiscWeight fade band past the SOI boundary to fix the galaxy pop-in (D-13) (wave 3)

**Wave 4** *(blocked on Wave 3 + play-test)*

- [x] 05-04-PLAN.md — HDR dither composition + cleanup: verify/lock the near-star glow pass composing in HDR before the 8-bit dither (D-05); remove the dead WorldRenderer._lastRenderPositions cache while keeping the Hud GetRenderPosition/GetRenderRadius accessors working (D-46); final per-tier parity/improvement play-test (CRT stays out per D-06) (wave 4)

## Backlog

### Phase 999.1: Targeting & navigation HUD — hierarchy tree selector + world-pinned target outline (BACKLOG)

**Goal:** A target/selection system beyond the Phase-1 minimal HUD, deferred from plan 01-04:

  1. A folder/tree-structure menu to select **any** object in the universe hierarchy (across spaces) — overrides locked decision **D-12** (current-parent-space targeting only).
  2. A **world-pinned target outline** drawn around the selected body that holds a **minimum on-screen radius**, so a distant star/planet is always visible even as a sub-pixel speck.
  3. A **name + distance label pinned to the outline** that tracks the object on screen (moves with the body), augmenting the fixed-corner target readout + off-screen edge marker shipped in 01-04.

  Cross-space constraint to resolve in planning: `WorldRenderer` only renders bodies in the ship's current space, so a cross-space target can show direction + distance (edge marker) but the 3D outline can only be drawn once that body enters the rendered set (i.e. once the ship is in its space).
**Requirements:** TBD (extends HUD-04 + findability; revisits D-12)
**Plans:** 2/2 plans complete

Plans:

- [ ] TBD (promote with /gsd-review-backlog when ready)

### Phase 999.2: Shader-rendered target-sphere outline (BACKLOG)

**Origin:** Phase 04 play-test (2026-06-17). The 04-02 target circle is a flat 2D
`DrawArc` sized analytically from the body's projected depth. It matches the body's
size when centred, but a perspective camera projects a sphere to an ELLIPSE off-axis,
so at wide FOV the body looks egg-shaped near the screen edge while the round circle
does not match (accepted as a known cosmetic limitation for v1 — correct rectilinear
renderer behaviour, not a defect).

**Goal:** Replace the 2D circle with a target marker that is a true 3D **sphere
encapsulating the target body**, rendered by a **shader that draws ONLY the silhouette
outline** of that sphere. Because the outline is computed in the same projection as the
body mesh, it distorts identically — hugging the egg-shaped body at the screen edge.

**Notes for planning:**

  - Likely a per-target sphere mesh (radius = body render radius × padding) with an
    unlit outline/fresnel shader (rim where view·normal ≈ 0), or a screen-space
    signed-distance pass. Must stay phosphor-green and read-only of sim state.

  - Replaces `Hud._Draw`/`UpdateTargetCircle` (04-02) — the 2D path is the fallback /
    can be retired once the shader path ships.

  - Keep the minimum-on-screen-size findability guarantee (D-46) for distant specks.

**Requirements:** TBD (supersedes the 2D D-46 circle with a projection-matched outline)
**Plans:** TBD

### Phase 999.3: Distance-based cross-space target traversal + autopilot ("warp drive") (BACKLOG)

**Origin:** Phase 04 play-test (2026-06-17). Folds/extends the deferred cross-SOI
half of 999.1 and supersedes the current-tier targeting constraint (D-12 / D-45).

**Goal:** Two coupled capabilities:

  1. **Distance-based, space-independent target selection** — select ANY target within
     a given distance regardless of which SOI/space it (or the ship) occupies. Refactors
     `Hud.BuildTargetableList` (currently parent + same-frame siblings only) into a
     distance-ranked, cross-space candidate set using `UniMath.Distance` (LCA path).
     Overrides D-12/D-45 (current-tier-only targeting).

  2. **Autopilot traversal ("warp drive")** — selecting a target starts an automatic
     route that eases IN and OUT to arrive at the target in a reasonable, bounded time,
     traversing SOI boundaries automatically (planet ↔ star ↔ galaxy ↔ intergalactic).

**Speed model split (locked intent from user, 2026-06-17):**

  - **Free roaming** (manual flight) is bounded to **km/s** — slow, precise, hands-on.
  - The **warp drive is autopilot-only**: the large auto-scaling speeds (up to FTL-equivalent)
    live ONLY in the autopilot path, not manual thrust. Intergalactic transit completes in
    **minutes**.

  - This re-shapes the Phase-04 envelope: the per-tier ceiling (D-40) becomes the AUTOPILOT
    ceiling; manual `MaxSpeed` is clamped to km/s scale. Planning must reconcile D-42/D-43
    (manual ease-out) with the new manual km/s cap.

**Requirements:** TBD (supersedes D-12/D-45; reshapes D-40/D-42/D-43/D-47 manual-vs-autopilot split)
**Plans:** TBD

### Phase 999.4: Warp-drive visual effects (BACKLOG)

**Origin:** Phase 04 play-test (2026-06-17). Pairs with 999.3.

**Goal:** Visual FX for the autopilot "warp drive" traversal — the look/feel of engaging
warp, sustained intergalactic transit, and arrival. (e.g. starfield streaking / tunnel,
speed-line dithered post-process consistent with the 8-bit CRT aesthetic, engage/disengage
transitions). Read-only consumer of the autopilot state from 999.3.

**Depends on:** 999.3 (autopilot/warp drive must exist first).
**Requirements:** TBD (visual only; no sim-state mutation)
**Plans:** TBD

## Progress

**Execution Order:**
Phases execute in numeric order: 1 → 2 → 3 → 4 → 5

| Phase | Plans Complete | Status | Completed |
|-------|----------------|--------|-----------|
| 1. In-System Flight MVP | 4/4 (TRV-01 milestone) | ✓ Complete | 2026-06-14 |
| 2. Dynamic Skybox | 3/3 | Complete   | 2026-06-15 |
| 3. Cross-Galaxy Travel | 3/3 | UAT partial (3/7 pass) — gated on the render debts (post-overhaul) | — |
| 4. Flight Model v2 — tier & target-aware speed | 2/2 | Complete   | 2026-06-17 |
| 5. Rendering Overhaul | 4/4 | Complete   | 2026-06-20 |

Plans:

- [x] 04-01-PLAN.md — Tier- & target-aware speed envelope + Hud.ActiveTargetIndex (wave 1)
- [x] 04-02-PLAN.md — World-pinned target circle + full-phase play-test (wave 2)

> **Phase 5 detail** lives in the `## Phase Details` section above (Rendering Overhaul).
> The old "Outer-tier body findability & galaxy visibility" Phase 5 was removed
> (2026-06-19) — see ### Roadmap Evolution in STATE.md. Its three render problems are
> tracked as standalone debts in `.planning/todos/pending/` and will be promoted to their
> own later phases, individually, after the Rendering Overhaul lands.
