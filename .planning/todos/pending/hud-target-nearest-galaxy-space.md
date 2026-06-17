---
type: tech-debt
status: partially-resolved
priority: P3
created: 2026-06-17
resolved: 2026-06-17
area: hud
origin: phase-03 UAT play-test
tags: [tech-debt, hud, target, nearest, galaxy-space, precision]
related: [hud-cycle-target-not-working, flight-speed-model-tier-and-target-aware]
debug-session: .planning/debug/hud-target-galaxy-space.md
---

> **PARTIALLY RESOLVED 2026-06-17** (debug session `hud-target-galaxy-space`, user-verified).
> - **Flicker — FIXED.** Root cause was not precision but an identical-vector tie:
>   `Hud.GetRelativeMeters` computed the parent-body distance and a sibling-at-origin
>   distance as the SAME vector, so the parent (HOME GALAXY / ROOT) and a body at (0,0,0)
>   (home STAR / HOME GALAXY) always tied and the nearest-winner flipped per frame. Replaced
>   all ship-relative distance math with `UniMath.Distance` / `UniMath.RelativeMetres` (LCA
>   path, per CLAUDE.md Position Math); removed `GetRelativeMeters` + the `IsParent` flag.
>   Also set `GameObjects[_root].Name = "ROOT"` (TestSetup) so Universe-space nearest shows
>   "ROOT", not "?". Verified stable in-game. Build 0/0, tests 30/30.
> - **"Galaxy-member stars not recognized as nearest" — DEFERRED (by design).** This is the
>   D-12 single-SOI targeting scope: while in the home star's SOI, the other stars (children
>   of the galaxy) are not in scope. Targeting bodies across SOI levels is the cross-space
>   targeting feature → folded into [[flight-speed-model-tier-and-target-aware]] (D-12
>   override / backlog 999.1). A separate data bug (sibling stars 1e4× too close, SOIs
>   overlapping) is also tracked as its own focused step.

# "nearest" label flickers + galaxy-member stars not recognized as nearest

## What

Two defects in the HUD context/target logic (`Scripts/Hud/Hud.cs`), seen in Galaxy space:

1. **Flickering nearest label** — context label switches between "?" and "home galaxy"
   every frame. → **FIXED** (UniMath LCA distances + ROOT name).
2. **Galaxy stars not nearest** — galaxy-member stars never recognized as nearest. →
   **DEFERRED**: this is the D-12 single-SOI scope; cross-space targeting is owned by
   [[flight-speed-model-tier-and-target-aware]]. (Compounded by the separate
   sibling-star-distance data bug — stars authored 1e4× too close.)
