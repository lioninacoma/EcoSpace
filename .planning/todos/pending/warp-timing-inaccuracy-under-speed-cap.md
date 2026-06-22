---
type: tech-debt
status: pending
priority: P3
created: 2026-06-22
area: flight/warp
origin: phase-07 play-test (approved with known inaccuracy)
tags: [tech-debt, warp, timing, speed-cap, flight]
---

# Warp timing inaccuracy when WarpMaxSpeed cap is active

## Problem

`EngageWarp` computes `T_int = T_sel / ln(d0 / SOI)` to calibrate the
exponential decay `dist(t) = d0·exp(-t/T_int)` so the ship arrives in
exactly `T_sel` seconds. This derivation assumes speed = dist / T_int at
all times.

However, `_WarpProcess` caps speed at `_warpMaxSpeed`:

```csharp
double warpSpeed = Math.Min(dist / _warpInternalTimeSec, _warpMaxSpeed);
```

When the initial `dist / T_int > _warpMaxSpeed` (i.e. at the start of a
very long warp where the computed initial speed exceeds the cap), the ship
travels at constant `_warpMaxSpeed` for some duration before transitioning
to the exponential decay phase. The formula's assumption breaks and the
actual arrival time differs from `T_sel`.

For in-system warps (short distance) or intergalactic warps with a generous
`T_sel`, the cap is usually not hit and timing is accurate. For very short
`T_sel` on long warps the discrepancy may be noticeable.

## Accepted for now

Phase-07 play-test approved with this known inaccuracy. Timing is "good
enough" for the current travel distances and the default 120s slider value.

## Fix approach (when prioritized)

At `EngageWarp`, compute the uncapped initial speed `v0 = d0 / T_int`. If
`v0 > WarpMaxSpeed`, split travel into two phases:
1. Constant-speed phase at `WarpMaxSpeed` until speed would drop below the
   cap (i.e. until dist = `WarpMaxSpeed * T_int`).
2. Exponential decay phase from that point.

Derive a combined time formula that accounts for the constant-speed prefix
and adjust `T_int` so the total is still `T_sel`.
