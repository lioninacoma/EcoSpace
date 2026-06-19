# Phase 5: Rendering Overhaul - Discussion Log

> **Audit trail only.** Do not use as input to planning, research, or execution agents.
> Decisions are captured in CONTEXT.md — this log preserves the alternatives considered.

**Date:** 2026-06-19
**Phase:** 5-rendering-overhaul
**Areas discussed:** Rewrite scope, Handoff guarantee, Acceptance bar, **Scope reframing (user-driven)**, Sub-phase structure, Debt relationship, CRT scope

---

## Rewrite scope

| Option | Description | Selected |
|--------|-------------|----------|
| Consolidate, preserve math | Architecturally a rewrite (one unified classify→describe→draw pipeline) but UniMath precision, floating-origin loop, StarRendering, TierClassifier preserved/extended, not re-derived | ✓ |
| Full clean-slate rewrite | New subsystem from scratch; delete WorldRenderer + SkyboxRenderer; re-implement precision math in the new structure | |

**User's choice:** Consolidate, preserve math
**Notes:** The real problem is structural (scattered per-ObjectType branches, two parallel handoff paths), not mathematical. Precision math is the hard-won part and stays.

---

## Handoff guarantee (pop-free, criterion #2)

| Option | Description | Selected |
|--------|-------------|----------|
| One descriptor, shared by all drawers | Compute dir/size/brightness/color ONCE per body per frame; point/disc/mesh drawers consume the same numbers; crossing = same descriptor, different drawer → pop-free by construction | ✓ |
| Keep two caches, share the math | Keep separate sky/mesh caches but force both to call shared projection/appearance fns | |

**User's choice:** One descriptor, shared by all drawers
**Notes:** Collapses the two existing caches (`_skyDirs`, `_lastRenderPositions`) into one source. This descriptor becomes the feed for the post-process pass after the scope reframing below.

---

## Acceptance bar

| Option | Description | Selected |
|--------|-------------|----------|
| Strict parity + explicit allowlist | Pixel-equivalent vs reference screenshots, with a short allowlist of permitted deltas | |
| Strict parity, no deltas | Pixel-equivalent everywhere, zero permitted change | |
| No-regression, improvement OK | Look may improve anywhere as long as nothing regresses; judged subjectively per tier | ✓ |

**User's choice:** No-regression, improvement OK
**Notes:** User wants freedom to improve the look during the overhaul. Verification = subjective per-tier play-test (Planet/Star/Galaxy/Universe), not screenshot-matching.

---

## Scope reframing (user-driven — superseded the original gray-area flow)

The user rejected the "wanted polish" question and supplied the real phase vision instead:

> A post-processing effect for all visible stars and galaxies in EVERY space — glow/halo for nearby stars, point light for distant stars, blended seamlessly. Galaxies fade in/out by distance: approaching a galaxy fades out its placeholder so stars become its representation (like real life); leaving fades the placeholder back in. Only from far does the galaxy placeholder render. Skybox rendering is replaced by post-processing. Create a branch for easy revert. Create sub-phases — don't implement too much in one execution. Take time, do it correctly.

This redefined the phase from "consolidate the existing skybox + mesh renderers" to "replace the Sky-shader skybox with a unified depth-aware post-process luminous-body renderer with continuous distance crossfades." Captured as the new Phase Boundary + D-01..D-10 in CONTEXT.md. Supersedes ROADMAP criterion #5.

---

## Sub-phase structure

| Option | Description | Selected |
|--------|-------------|----------|
| Sequential plans in Phase 5, play-test gate each | One phase; plan-phase produces 3-4 small sequential plans, each executed + play-tested before the next | ✓ |
| Split into separate ROADMAP phases | Restructure roadmap into literal separate phases, each its own discuss/plan/execute cycle | |
| One phase, let planner slice | Keep one phase, let plan-phase decide the wave breakdown | |

**User's choice:** Sequential plans in Phase 5, play-test gate each
**Notes:** Agreed slicing: (1) branch + descriptor/projection foundation (no visual change); (2) post-process star glow + points, replace sky star points; (3) galaxy distance crossfade, remove skybox; (4) dither composition + cleanup. "Don't implement too much in one execution — it breaks things."

---

## Debt relationship (P1 / P2)

| Option | Description | Selected |
|--------|-------------|----------|
| Fold in and close here | Galaxy crossfade closes P2; post-process point lights + floor close P1; mark resolved-by-Phase-5, verified at play-test gates; supersedes criterion #5 | ✓ |
| Build mechanism here, close debts later | This phase builds the mechanism; P1/P2 stay open as separate follow-up phases | |

**User's choice:** Fold in and close here
**Notes:** The distance-fade model is the mechanism those debts were waiting for, so they close here rather than in separate later phases.

---

## CRT scope

| Option | Description | Selected |
|--------|-------------|----------|
| Dither only, defer CRT | Plan 4 composes the luminous pass into the existing 8-bit dither (HDR before quantize); CRT scanlines deferred | ✓ |
| Add CRT too | Reintroduce crt.gdshader (RND-01) into the post-process stack as part of this overhaul | |

**User's choice:** Dither only, defer CRT
**Notes:** Keeps the phase focused on the luminous-body rendering vision. RND-01 CRT deferred to its own later task.

---

## Claude's Discretion

- Exact post-process technique for feeding N projected positions to a full-screen shader (uniform arrays vs point/ID buffer vs additive accumulation) — flag for research; must be depth-aware.
- Depth-buffer access in Godot 4.6 Forward+ and ordering relative to the dither pass.
- Glow/halo kernel; distance→LOD-weight curves for star mesh↔point and galaxy disc↔stars; always-visible brightness floor.
- Galaxy placeholder representation in post (reuse procedural disc vs simpler sprite) + crossfade thresholds.
- Descriptor/projection structure for unit-testability (extend TierClassifier tests).

## Deferred Ideas

- CRT scanline effect (RND-01) — own later task.
- `galaxy-disc-tilt-foreshortening` — verify post-rework, not actively reworked.
- Procedural generation, cockpit art, economy, combat — milestone out-of-scope, unchanged.

---

# Revision Session — 2026-06-19 (after 05-02 play-test)

**Trigger:** 05-02 play-test rejected. The post-process spatial quad rendered distant
stars/galaxies IN FRONT of planet meshes (post-process cannot occlude behind opaque
geometry); home sun didn't render up close; a galaxy popped in. User proposed: sky shader
for distant stars+galaxies, post-process for glow/halo. This reverses the original
"replace the skybox entirely" frame (D-07/D-09, discussion-log line 53).

## Architecture split (sky shader vs post-process)

| Option | Description | Selected |
|--------|-------------|----------|
| Sky shader (distant) + post-process (glow/halo) | Keep `skybox.gdshader` for distant stars+galaxies (renders behind geometry); narrow `luminous_pass` to near-star glow/halo; both fed by the Plan-1 descriptor | ✓ |
| Keep unified post-process, fix depth gate | Make the depth-texture occlusion gate work in the single post-process pass | |
| Full replan from scratch | Discard Plan 1 too | |

**User's choice:** Sky shader + post-process split. **Notes:** post-process structurally
can't occlude behind meshes; sky shader is the right tool for the distant celestial sphere.
Plan-1 descriptor survives and feeds both layers. → reverses D-07, revises D-03/D-05/D-08/D-09.

## Star handoff (far → near)

| Option | Description | Selected |
|--------|-------------|----------|
| 3-stage continuous blend (point → glow → mesh) | Sky point far; glow/halo grows mid; sphere mesh near; LodWeight-driven | ✓ |
| 2-stage (point ↔ mesh+glow) | | |
| Sky shader does points + glow | | |

**User's choice:** 3-stage continuous blend. → new D-11.

## Near-star findability (missing-sun bug)

| Option | Description | Selected |
|--------|-------------|----------|
| Sphere mesh + glow, fix now | Parent sun + in-SOI siblings render as WorldRenderer sphere meshes; missing-sun is an in-scope regression | ✓ |
| Mesh + glow, defer the fix | | |
| Glow sprite only (no sphere) | | |

**User's choice:** Sphere mesh + glow, fix now. → new D-12.

## Galaxy rendering + crossfade

| Option | Description | Selected |
|--------|-------------|----------|
| Sky shader, reuse procedural disc math | Far disc ↔ resolved-stars crossfade; fix the "pops out of nowhere" bug | ✓ |
| Sky shader, simpler sprite disc | | |
| Galaxies in post-process | | |

**User's choice:** Sky shader, reuse disc math. → new D-13.

## Plan 2–4 re-slicing

| Option | Description | Selected |
|--------|-------------|----------|
| Findability-first | (2) sky-shader refeed + near-sun fix; (3) glow/halo + 3-stage handoff; (4) galaxy crossfade + dither composition + cleanup | ✓ |
| Galaxy-focused middle | | |
| Let the planner decide | | |

**User's choice:** Findability-first. → revised D-08. Plan 1 (05-01) stays complete.
