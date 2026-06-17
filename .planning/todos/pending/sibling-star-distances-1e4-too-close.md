---
type: tech-debt
status: pending
priority: P2
created: 2026-06-17
area: data
origin: phase-03 UAT play-test (found while debugging hud-target-galaxy-space)
tags: [tech-debt, test-data, soi, units, render, skybox]
related: [hud-target-nearest-galaxy-space, galaxy-visibility-in-universe-space]
---

# Sibling/cluster/dest stars authored ~1e4× too close (SOIs overlap)

## What

All non-primary stars in the test universe are placed ~10,000× too close, so they sit
deep inside each other's (and the home star's) SOIs. Same units-vs-metres bug class as
the galaxy-position fix (commit 6f5f728) — that fix corrected the galaxies but MISSED the
member stars.

`AddGameObject(parent, Double3 pos, soiMeters)` takes **metres**, but the sibling-star
positions were authored as "Galaxy units" (1 unit = 1e4 m). Example (TestSetup.cs):

```
// Sibling1: Alpha Cen A-like (4.2 ly ≈ 3.97e16 m → 3.97e12 Galaxy units)
private const double Sibling1_GalX = 3.97e12;   // ← passed straight to AddGameObject as METRES
```

So ALPHA CEN lands at 3.97e12 m = **26.5 AU**, not 3.97e16 m = 4.2 ly.

| Star | Stored (m) | Actual | Intended |
|------|-----------|--------|----------|
| ALPHA CEN | 3.97e12 | 26.5 AU | 4.2 ly (3.97e16) |
| BARNARD | 5.63e12 | 37.6 AU | 5.96 ly (5.63e16) |
| SIRIUS | (-6.0e12, 5.6e12) | 54.9 AU | 8.6 ly (8.21e16) |
| DEST SIB 1/2, CLUSTER STAR 1/2/3 | e12 | tens of AU | ly-scale |

With `StarSOI = 1.5e15 m` (10,000 AU), every sibling is at ~0.3% of the SOI radius — the
star SOIs overlap almost completely. The distance is too small, not the SOI too big.

## Why it matters

- **Degenerate hierarchy** — member stars are geometrically inside the home star's SOI but
  parented to the galaxy. Breaks the 1:1-scale intent and any "fly to another star" test.
- **SOI transitions** — overlapping SOIs are ill-defined.
- **Phase 2 skybox (D-23)** — these siblings were the sky-point test data; Phase 2 was
  validated with them at tens of AU, not ly. Fixing moves them ~1e8× dimmer in flux (the
  StarRendering inverse-square magnitude model must still float them above the D-19 floor)
  — needs in-game re-verification.

## What "done" looks like

- Sibling/cluster/dest-sib star positions are in true metres at their intended ly distances
  (ALPHA CEN 4.2 ly, BARNARD 5.96 ly, SIRIUS 8.6 ly, etc.) — scale the offending constants
  ×1e4 (or re-author in metres).
- No star SOI overlaps another; member stars sit outside the home star's SOI.
- Phase 2/3 skybox still shows the sibling sky-points at correct relative brightness (re-verify
  in-game); galaxy/skybox visuals not regressed.
- Build 0/0, tests 30/30.

## Notes

- Requires in-game Godot verification (visual; not catchable by build/tests).
- Same root as the galaxy fix in debug session `galaxy-sky-disc-antipode` (6f5f728).
