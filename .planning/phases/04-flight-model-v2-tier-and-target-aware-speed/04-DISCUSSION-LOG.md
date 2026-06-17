# Phase 4: Flight Model v2 — tier & target-aware speed - Discussion Log

> **Audit trail only.** Do not use as input to planning, research, or execution agents.
> Decisions are captured in CONTEXT.md — this log preserves the alternatives considered.

**Date:** 2026-06-17
**Phase:** 4-flight-model-v2-tier-and-target-aware-speed
**Areas discussed:** Tier ceiling, Target speed coupling, No-target behavior, Targeting reach & marker, Scope/D-12

---

## Tier ceiling — how the per-tier max speed is defined

| Option | Description | Selected |
|--------|-------------|----------|
| Derived from tier scale | Compute each tier's max from its characteristic distance (parent SOI radius) × one factor k; auto-scales Planet→Universe | ✓ |
| Explicit per-tier table | Four hand-authored [Export] constants, one per Space | |
| You decide | Defer to planning | |

**User's choice:** Derived from tier scale (D-40).
**Notes:** Less hand-tuning, stays 1:1-consistent; single tunable factor k.

---

## Target speed coupling — how much a set target governs speed

| Option | Description | Selected |
|--------|-------------|----------|
| Ease-out within tier ceiling | Target distance shapes deceleration; tier ceiling still caps top speed | ✓ |
| Target distance sets the ceiling | Far target raises ceiling (cross-tier autopilot feel) | |
| You decide | Defer to planning | |

**User's choice:** Ease-out within tier ceiling (D-43). Predictable; target never exceeds tier speed.

---

## No-target behavior — proximity slow-down

| Option | Description | Selected |
|--------|-------------|----------|
| Tier ceiling + gentle approach damp | Bounded by tier ceiling + symmetric proximity slow-down near nearest body | ✓ |
| Pure tier ceiling only | Flat tier ceiling, no proximity term | |
| You decide | Defer to planning | |

**User's choice:** Tier ceiling + gentle proximity damp (D-42).
**Notes:** Symmetric (no direction gate); "receding" returns to TIER ceiling, not the global max — this is the structural reason the reverted 260617-j6b recede-exempt bug cannot recur.

---

## Targeting reach & on-screen marker

| Option | Description | Selected |
|--------|-------------|----------|
| Current tier + circle when in-frame | Reach = current SOI parent + children; world-pinned circle when rendered, else edge marker | ✓ |
| Full hierarchy reach | Target any body anywhere (near full 999.1) | |
| You decide | Defer to planning | |

**User's choice:** Current tier + circle when in-frame (D-45, D-46).

---

## Scope / D-12 confirmation

| Option | Description | Selected |
|--------|-------------|----------|
| Re-target per tier (keep D-12) | Navigate between systems by re-targeting at each tier; no true cross-SOI targeting; soften ROADMAP criterion #5 | ✓ |
| True cross-SOI targeting now | Target across SOIs from afar; larger phase, pulls in more of 999.1 | |

**User's choice:** Re-target per tier — D-12 preserved, true cross-space targeting stays in 999.1.

## Claude's Discretion
- Exact `k`/`k'` values and proximity-damp curve shape (play-test tuning knobs).
- Whether tier characteristic distance is exactly `parent.SOIMeters` or a related quantity.
- Where the target circle is drawn (Hud Control vs WorldRenderer overlay) and how FlightController reads the active target without the read-only HUD mutating sim state.

## Deferred Ideas
- True cross-SOI / cross-space targeting → backlog 999.1.
- Full 999.1 nav-HUD (hierarchy tree selector; tracking name/distance label).
- `galaxy-visibility-in-universe-space.md` (P2) — separate phase.
