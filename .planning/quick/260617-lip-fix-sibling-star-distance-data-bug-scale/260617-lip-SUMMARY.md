---
status: complete
phase: quick-260617-lip
plan: "01"
subsystem: TestData
completed_date: 2026-06-17
tasks_completed: 3
tasks_total: 3
files_changed: 1
---

# Quick Task 260617-lip — Fix sibling-star-distance data bug

**One-liner:** Scaled all within-galaxy star positions ×1e4 to true metres so member
stars sit at real ly distances and their SOIs no longer overlap the home star's.

## Task Outcomes

### Task 1: Scale within-galaxy star positions ×1e4 — COMPLETE
**Commit:** (fix committed on main; see git log `fix(260617-lip): ...`)

`Scripts/TestSetup.cs`:
- Home-galaxy siblings: `Sibling1_GalX` 3.97e12→3.97e16 (4.2 ly), `Sibling2_GalX`
  5.63e12→5.63e16 (5.96 ly), `Sibling3_GalX/GalZ` -6.0e12/5.6e12→-6.0e16/5.6e16 (8.68 ly).
- DEST galaxy: DEST SIB 1 (-3.2e12)→(-3.2e16); DEST SIB 2 (4.5e12,0,3.1e12)→(4.5e16,0,3.1e16).
- Elliptical cluster: CLUSTER STAR 1 (1.5e12)→(1.5e16); STAR 2 (-2.1e12,0,1.2e12)→e16; STAR 3 (0.8e12,0,-2.5e12)→e16.
- Updated the stale class-level + sibling "Galaxy units" doc-comments to say metres.
- Untouched: planet (AU), galaxy (e22), StarSOI/GalaxySOI, radii, luminosities, colors.

**Verification:** build 0/0; tests 30/30. Each member star is now ~26× the StarSOI radius
(1.5e15 m) from its primary — no SOI overlap (by construction).

### Task 2 (checkpoint:human-verify, blocking) — PASSED (with one caveat)
User play-test 2026-06-17:
- ✅ Sibling sky-points still visible at correct relative brightness (StarRendering
  magnitude curve + D-19 floor hold at true ly distance — ~1e8× dimmer in flux).
- ✅ No Phase 2/3 visual regression.
- ⚠️ Fly-out SOI-transition behavior NOT testable — the current flight controller is too
  slow to exit the home star's SOI usably. Deferred to the P1 flight model
  (flight-speed-model-tier-and-target-aware). No-overlap holds mathematically regardless.

### Task 3: Resolve the tech-debt todo — COMPLETE
`.planning/todos/pending/sibling-star-distances-1e4-too-close.md` → status: resolved.

## Notes

- Same units-vs-metres bug class as the galaxy-position fix (debug session
  galaxy-sky-disc-antipode, commit 6f5f728), which corrected the galaxies but missed the stars.
- Requires no further code; the fly-out re-test will fall out naturally once the P1 flight
  model lands.
