---
type: tech-debt
status: pending
priority: P3
created: 2026-06-17
area: hud
origin: phase-03 UAT play-test
tags: [tech-debt, hud, target, nearest, galaxy-space, precision]
related: [hud-cycle-target-not-working, flight-speed-model-tier-and-target-aware]
---

# "nearest" label flickers + galaxy-member stars not recognized as nearest

## What

Two related defects in the HUD context/target logic (`Scripts/Hud/Hud.cs`), seen in
Galaxy space:

1. **Flickering nearest label** — the context label `"{tier} · nearest: {name}"`
   switches between `"?"` and `"home galaxy"` every frame.
2. **Galaxy stars not nearest** — in Galaxy space the newly authored galaxy-member
   stars (e.g. ALPHA CEN, BARNARD, SIRIUS) are never recognized as nearest; only the
   home star (or the galaxy itself) ever shows.

## Why (suspected)

- The flicker between `"?"` and a name implies the min-distance scan
  (`UpdateContextLabel`) is alternating between two bodies whose computed distances
  jitter frame-to-frame, and one of them has a null `Name` (rendered as `"?"` via
  `body.Name ?? "?"`). Likely a precision problem in `GetRelativeMeters`: the parent
  path uses `ship.LocalPos.ToDouble3()` which collapses `Long3.Units` (see CLAUDE.md
  "Position Math" caveat) — at Galaxy scale this can produce unstable/!nearest values.
  The body-relative distances should go through `UniMath` (LCA-relative) rather than
  raw `ToDouble3()` / `ToLocalDouble`.
- "Galaxy stars not nearest" suggests `BuildTargetableList` (parent + `parent
  .ChildIndices`) isn't returning the galaxy's member stars in Galaxy space, or their
  distances are computed in the wrong frame so they never win the min. Verify the
  Phase-3 galaxy-member stars are actually children of the galaxy node and have
  non-null `Name`.

## What "done" looks like

- The nearest label is stable (no per-frame flicker) and names a real body.
- In Galaxy space, the galaxy's member stars are correctly considered for "nearest"
  and targeting, with correct distances.
- Body-relative distance math routes through `UniMath` (LCA-relative), per the CLAUDE.md
  Position Math convention — no raw absolute-from-frame `ToDouble3()` at Galaxy/Universe
  scale.

## Notes

- Prerequisite for the target-aware flight model
  ([[flight-speed-model-tier-and-target-aware]]).
- Pairs with [[hud-cycle-target-not-working]] (same target subsystem).
- Good candidate for `/gsd-debug` (precision + hierarchy investigation).
