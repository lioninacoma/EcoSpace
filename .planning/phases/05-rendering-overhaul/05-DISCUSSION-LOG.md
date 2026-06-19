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
