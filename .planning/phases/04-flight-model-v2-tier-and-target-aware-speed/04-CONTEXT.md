# Phase 4: Flight Model v2 — tier & target-aware speed - Context

**Gathered:** 2026-06-17
**Status:** Ready for planning

<domain>
## Phase Boundary

Replace the single global-`MaxSpeed` speed envelope in `Scripts/Flight/FlightController.cs`
(`UpdateSpeedEnvelope`) with a **context-correct, tier- and target-aware** model so flight
feels good and stays usable at every scale — slow/precise in-system, FTL-equivalent
intergalactic — within ONE auto-scaling envelope (no separate FTL mode). Adds a world-pinned
target outline (circle) so the active target stays findable.

This fixes both Phase-03-UAT flight failures at their shared root (one global MaxSpeed for
all tiers): the in-system over-speed (flying past the home star) and the thrust-zero dead
zone at a galaxy's SOI exit.

**Not** a new targeting paradigm: targeting stays scoped to the current SOI tier (D-12 holds);
inter-system travel works by re-targeting at each tier as you ascend/descend.
</domain>

<decisions>
## Implementation Decisions

### Per-tier speed ceiling
- **D-40:** The max speed for each scale tier is **derived from that tier's characteristic
  distance** (the ship's parent SOI radius for the current `UniObject.Space`) × one tunable
  factor `k` — NOT a hand-authored per-tier constant table. So Planet/Star/Galaxy/Universe
  ceilings scale automatically and stay 1:1-consistent. `k` is a single `[Export]` tuning knob.
- **D-41:** Ceiling transitions across SOI boundaries are eased (keep the existing
  `_contextMax` lerp, D-07) so there is no speed snap when the tier changes.

### No-target behavior (default)
- **D-42:** With no target, speed is bounded by the **tier ceiling**, plus a **symmetric
  proximity damp** near the nearest body (slows you as you get close so you don't blow past
  planets/stars). The damp is symmetric (no direction gate) — the key fix vs the reverted
  260617-j6b: "receding" simply returns you to the **tier ceiling** (a sane in-system speed),
  never to the global intergalactic MaxSpeed. This is why the recede-exempt bug cannot recur.

### Target-aware speed (when a target is set)
- **D-43:** Target distance shapes **ease-out onto the target** (decelerate as you approach),
  but the **tier ceiling still caps top speed** — a target never makes you faster than the
  current tier allows. Model: `v → min(tierCeiling, distToTarget × k')`, eased.
- **D-44:** "Thrust handled by current target; if no target, bounded by current space" — the
  user's model — is realized by D-42 + D-43 together.

### Targeting reach & on-screen marker
- **D-45:** Targeting reach stays **current-tier** (current SOI parent + its children) — D-12
  is NOT overridden in this phase. In Galaxy space this already includes the galaxy's member
  stars (now correctly rendered as meshes, Phase 03), so inter-system navigation works by
  re-targeting per tier. True cross-SOI targeting (target a body in a different SOI than you're
  in) stays in backlog 999.1.
- **D-46:** The active target gets a **world-pinned outline/circle** with a **minimum on-screen
  radius** (so a distant target is never a sub-pixel speck), drawn **only when the target body
  is in the rendered set** (current space). When the target is not rendered, fall back to the
  existing off-screen **edge-marker + distance** (Hud `UpdateDirectionMarker`). No full
  hierarchy-tree nav-HUD (that's 999.1).

### Constraints carried forward (locked)
- **D-47:** Single auto-scaling envelope — NO separate FTL mode (preserves D-35/D-36 intent).
  `MaxSpeed`/`MinSpeed`/throttle `[-1,1]`/`full_stop` and the `_easedSpeed` lerp (D-03/D-07,
  "Bug 4" fix) all stay; this phase reshapes how `targetMax`/`_contextMax` are derived.

### Claude's Discretion
- Exact value of `k` / `k'` and the proximity-damp curve shape — tune by feel in-game
  (play-test knobs, like GALAXY_DISC_SCALE was).
- Whether the tier "characteristic distance" is exactly `parent.SOIMeters` or a related
  per-Space quantity — planner/researcher to pick the cleanest source already on `UniObject`.
- Where the target-circle is drawn (Hud Control vs a WorldRenderer overlay) — implementation detail.

### Folded Todos
- **`flight-speed-model-tier-and-target-aware.md`** (P1) — the phase source. Single global
  MaxSpeed across all tiers; needs per-tier ceiling + target ease-out; absorbs cross-SOI
  targeting intent (now scoped down per D-45) + the world-pinned target circle. Supersedes
  `thrust-zero-at-galaxy-soi-exit.md` (the SOI-exit dead zone is fixed here via D-42's
  return-to-tier-ceiling, not a global recede-exemption).
</decisions>

<canonical_refs>
## Canonical References

**Downstream agents MUST read these before planning or implementing.**

### Phase scope & decisions
- `.planning/ROADMAP.md` §"Phase 4: Flight Model v2" — goal, 6 success criteria, out-of-scope.
- `.planning/todos/pending/flight-speed-model-tier-and-target-aware.md` — folded source todo (problem + folded-in scope).
- `.planning/todos/pending/thrust-zero-at-galaxy-soi-exit.md` — superseded; the dead-zone symptom this phase must also close.

### Position / scale conventions (critical)
- `CLAUDE.md` §"Position Math (UniVec3 / UniMath)" — MUST follow; use `UniMath` LCA helpers for any cross-frame distance, never raw absolute-from-root `ToDouble3()` accumulation.

### Code the model lives in / consumes
- `Scripts/Flight/FlightController.cs` — `UpdateSpeedEnvelope` (the envelope to replace), `ApplyMotion`, `_contextMax`/`_easedSpeed`/`_throttle01`, `MaxSpeed`/`MinSpeed`/`SpeedPerMeter`/`SpeedEasing` exports.
- `Scripts/UniObject.cs` — `Space` enum (Planet/Star/Galaxy/Universe), `SOIMeters`, `ParentIndex`, `RadiusMeters` (tier characteristic distance source).
- `Scripts/Hud/Hud.cs` — `BuildTargetableList`, `_targetIndex`, `UpdateTargetReadout`, `UpdateDirectionMarker` (edge marker), `_Input` cycle_target (now Tab). Target-circle builds on this.
- `Scripts/Render/WorldRenderer.cs` — renders only current-space bodies (the constraint that bounds where the target circle can draw).
- `Scripts/Math/UniMath.cs` — `Distance`/`RelativeMetres` (sanctioned distance math).

### Prior decisions referenced
- D-03, D-06, D-07, D-08 (flight feel / distance→speed / easing), D-35, D-36 (MaxSpeed tuning / no-overshoot), D-12 (current-SOI targeting — preserved), backlog 999.1 (full nav-HUD — deferred).
</canonical_refs>

<code_context>
## Existing Code Insights

### Reusable Assets
- `FlightController.UpdateSpeedEnvelope` already computes the nearest-body surface distance
  (parent + siblings) and eases `_contextMax`/`_easedSpeed` — the per-tier ceiling + proximity
  damp can be built by reshaping how `targetMax` is derived, reusing the existing scan + lerps.
- `Hud` already has the targetable-list + active-target + off-screen edge-marker machinery
  (just fixed this session) — the target-circle extends it; the "target distance" needed for
  D-43 is already computed via `UniMath.RelativeMetres`.
- `UniObject.SOIMeters` / `Space` give the tier characteristic distance for D-40 with no new state.

### Established Patterns
- Per-frame eased envelope (D-07) — no snap on transitions; both lerps must keep running on
  every path (the reverted fix's one correct property).
- Play-test tuning knobs as `[Export]` floats (e.g. GALAXY_DISC_SCALE) — `k`/`k'`/damp follow this.
- `UniMath` LCA distance math (CLAUDE.md) — all cross-frame distances route through it.

### Integration Points
- Speed model: `FlightController.UpdateSpeedEnvelope` (reads ship/parent/Space; writes `_contextMax`→`CurrentSpeed`).
- Target coupling: `FlightController` needs the current target (today the target lives in `Hud`).
  Planner must decide how the controller reads the active target (e.g. Hud exposes it, or the
  target/selection moves to a shared owner) WITHOUT the HUD mutating sim state (Hud is read-only).
- Target circle: `Hud` + camera projection (like `UpdateDirectionMarker`), gated on the body
  being in `WorldRenderer`'s current-space rendered set.
</code_context>

<specifics>
## Specific Ideas

- User's framing (verbatim intent): "thrust that is handled by current target. If no target is
  set, thrust would be bounded by current space."
- The reverted 260617-j6b direction-aware clamp is the **anti-pattern to avoid**: exempting the
  proximity clamp to the GLOBAL max made in-system unusable. The fix here is that the ceiling is
  per-tier, so there's no global value to jump to.
- A circle around the target "would be helpful" (user, 2026-06-17) — D-46.
</specifics>

<deferred>
## Deferred Ideas

- **True cross-SOI / cross-space targeting** — selecting a body in a different SOI than the ship
  occupies (e.g. ALPHA CEN from the home planet). Stays in backlog **999.1** (full nav-HUD:
  hierarchy tree selector). D-45 keeps Phase 4 to current-tier targeting.
- **Full 999.1 nav-HUD** — hierarchy tree selector across the whole universe; name/distance label
  pinned to & tracking the outline. Only the minimal "reliable target + circle" slice is in Phase 4.
- **`galaxy-visibility-in-universe-space.md`** (P2) — galaxies vanish in Universe space; its own
  phase/discussion. Not part of the flight model, but pairs with intergalactic travel testing.

### Reviewed Todos (not folded)
- `hud-cycle-target-not-working.md` — already RESOLVED this session (Tab keybinding).
- `hud-target-nearest-galaxy-space.md` — flicker RESOLVED; cross-SOI-nearest half folded via D-45.
- `sibling-star-distances-1e4-too-close.md` — already RESOLVED (quick 260617-lip).
</deferred>

---

*Phase: 4-flight-model-v2-tier-and-target-aware-speed*
*Context gathered: 2026-06-17*
