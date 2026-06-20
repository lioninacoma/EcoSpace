# Phase 6: Targeting & Navigation HUD - Context

**Gathered:** 2026-06-20
**Status:** Ready for planning

<domain>
## Phase Boundary

Deliver the extended targeting/navigation HUD deferred from plan 01-04 — promoted from
backlog 999.1 with **999.2 folded in**. Three coupled pieces, all on top of the existing
read-only `Scripts/Hud/Hud.cs`:

1. A **cross-space object selector** — pick **any** body in the universe hierarchy
   (Galaxy / Star / Planet across spaces), overriding the current current-tier-only
   targeting (D-12 / D-45).
2. A **3D sphere-outline target marker** — a true sphere encapsulating the selected body,
   drawn as a **silhouette outline only** (projection-matched, distorts like the body),
   computed directly from `UniObject` data so it works **cross-space with no mesh** (folds
   in 999.2; supersedes the 2D `DrawArc` circle from 04-02).
3. A **name + distance tracking label pinned to the marker** that follows the body on
   screen, augmenting (not replacing) the fixed-corner TGT readout + off-screen edge marker.

**Not in scope:** autopilot / "warp drive" traversal and distance-ranked auto-selection
(backlog 999.3); any change to the flight speed model itself (Phase 4 owns it).
</domain>

<decisions>
## Implementation Decisions

### Target marker (folds in 999.2)
- **D-50:** The marker is a **3D sphere-outline silhouette** (a sphere encapsulating the
  target body, only its outline drawn), NOT the flat 2D `DrawArc` circle. It distorts under
  perspective exactly like the body (hugs the egg-shaped off-axis projection), replacing
  `Hud.UpdateTargetCircle`/`_Draw` (04-02, D-46 2D path).
- **D-51 (key decision, user 2026-06-20):** Marker position + size are computed **directly
  from `UniObject`** — direction/distance via `UniMath` (LCA path), size via the **same
  observer-unit render-factor math `WorldRenderer` uses to place/size a mesh**
  (`metres / ship.LocalPos.Scale * factor` — the Phase-5 canonical conversion). The marker
  does **NOT** require the body to be in the live rendered mesh set. This **removes the old
  D-46 render-set gate** (`WorldRenderer.GetRenderPosition` returning false → no circle) and
  the dependency on the `galaxy-visibility-in-universe-space` render debt: a cross-space
  target (incl. a galaxy in Universe space with no mesh) still gets a correctly-placed,
  correctly-sized marker showing where it is.
- **D-52:** **Minimum on-screen size floor.** Below a minimum apparent size the sphere
  outline stops shrinking and holds a floor radius — it stays a small round outline reticle
  at any distance (preserves the D-46 findability guarantee for sub-pixel-distant targets).
- **D-53:** Marker stays phosphor-green and **read-only** of sim state (Hud must never
  mutate `GameObjects`/`_targetIndex` as a side effect — existing Hud anti-pattern rule).

### Selector
- **D-54:** Selection UI is a **compact side list panel** grouped by tier
  (Galaxy / Star / Planet), listing targetable bodies with name + distance; the selected
  body is marked. (Not a full-screen tree overlay — the lighter side panel was chosen.)
- **D-55:** Selection reach is **cross-space — any object in the hierarchy** (overrides
  D-12 / D-45 current-tier-only). Galaxies are selectable (their marker is computed via
  D-51 even with no mesh). `Hud.BuildTargetableList` (parent + same-frame siblings only) is
  replaced/extended into a full-hierarchy candidate set.
- **D-56:** The panel **replaces the Tab quick-cycle** — all selection goes through the
  panel; the single-key current-tier Tab cycle is retired. (Frees the `cycle_target`/Tab
  binding; planner to decide the panel open/close key + cursor handling vs the T-key
  mouse-mode toggle from 01-03.)

### Tracking label
- **D-57:** A **name + distance** label is **pinned to the marker** and tracks the body on
  screen; when the body is off-screen it rides the existing off-screen **edge marker**
  (`Hud.UpdateDirectionMarker`). The **fixed-corner TGT readout is kept** as an
  always-visible fallback (augment, not replace).

### Claude's Discretion
- Exact sphere-outline rendering technique — per-target sphere mesh with an unlit
  rim/fresnel outline shader (rim where view·normal ≈ 0) vs a screen-space SDF/silhouette
  pass. Either is acceptable as long as it is projection-matched, phosphor-green, read-only,
  and honors the D-52 min-size floor.
- The min-size floor value and the marker padding multiplier (play-test `[Export]` knobs,
  like the existing `MIN_CIRCLE_RADIUS` / `CIRCLE_BODY_PADDING`).
- Panel open/close key, scroll/keyboard-vs-mouse navigation, and whether the cursor is
  freed while the panel is open (reconcile with the 01-03 T-key Confined/Captured mouse
  mode). Whether the panel groups strictly by `Space` tier or by hierarchy parentage.
- Where the active target/selection is owned now that Tab is gone and selection is
  cross-space — keep it in `Hud` (read-only, exposed via `ActiveTargetIndex` which
  `FlightController` already reads for D-43 ease-out) or move to a shared owner. Must keep
  Hud read-only of sim state.

### Folded Todos
None folded from the todo backlog. (The matched render debts belong to their own rendering
phase; the matched HUD todos are already resolved — see Reviewed Todos below.)
</decisions>

<canonical_refs>
## Canonical References

**Downstream agents MUST read these before planning or implementing.**

### Phase scope & decisions
- `.planning/ROADMAP.md` §"Phase 6: Targeting & Navigation HUD" — goal (3 pieces), the
  D-51 compute-from-UniObject key decision, 999.2 fold-in, 999.3 overlap (out of scope).

### Prior targeting / HUD decisions (extended or superseded here)
- `.planning/phases/04-flight-model-v2-tier-and-target-aware-speed/04-CONTEXT.md` —
  D-45 (current-tier targeting, now overridden by D-55), D-46 (world-pinned target circle +
  min on-screen radius + render-set gate, now superseded by D-50/D-51/D-52), D-43
  (FlightController target ease-out consuming `Hud.ActiveTargetIndex`).

### Position / scale conventions (critical)
- `CLAUDE.md` §"Position Math (UniVec3 / UniMath)" — MUST follow; all cross-frame
  direction/distance via `UniMath` LCA helpers, never raw absolute-from-root accumulation.
- `.planning/phases/05-rendering-overhaul/05-03-SUMMARY.md` §"Key Lesson: Observer-Unit
  Render Conversion" — the `metres / ship.LocalPos.Scale * factor` rule the marker size math
  (D-51) MUST use; `WorldRenderer.StarRenderFactor` / `RenderFactorFor` are the factor source.

### Code the feature lives in / consumes
- `Scripts/Hud/Hud.cs` — `BuildTargetableList`, `_targetIndex`, `ActiveTargetIndex`,
  `UpdateTargetReadout`, `UpdateTargetCircle` + `_Draw` (2D circle to be replaced),
  `UpdateDirectionMarker` (edge marker, kept), `_Input` cycle_target (Tab, to be retired).
- `Scripts/Render/WorldRenderer.cs` — `GetRenderPosition`/`GetRenderRadius` (no longer the
  gate, but the mesh-placement/size math to mirror), `RenderFactorFor`, floating-origin sync.
- `Scripts/UniObject.cs` — `Space`, `ParentIndex`, `ChildIndices`, `RadiusMeters`,
  `SOIMeters`, `Name`, `ObjectType` (the data the marker + hierarchy panel are built from).
- `Scripts/Math/UniMath.cs` — `RelativeMetres` / `Distance` / `RelativePosition` (sanctioned
  cross-frame math for marker placement and the panel's distance column).

### Related (do NOT re-implement here)
- `.planning/todos/pending/galaxy-visibility-in-universe-space.md` — the render debt the
  marker NO LONGER depends on (D-51). The marker shows where a galaxy is; making the galaxy
  itself *visible* in Universe space remains that debt's job.
</canonical_refs>

<code_context>
## Existing Code Insights

### Reusable Assets
- `Hud.UpdateTargetCircle` already computes the analytic on-screen radius from body depth +
  FOV (`pixelRadius = refExtent · (r/d) / tan(fovY/2)`) — the math to size the sphere-outline
  marker is already here; D-51 changes the *source* of `r` and the body position to be
  computed from `UniObject` rather than read from `_meshes`/`GetRenderPosition`.
- `Hud.UpdateDirectionMarker` (off-screen edge marker) is kept verbatim as the off-screen
  fallback the tracking label rides.
- `Hud.ActiveTargetIndex` is already the read-only handle `FlightController` consumes for the
  D-43 target ease-out — keep this contract when selection goes cross-space.
- `FormatDistance` (adaptive m/km/AU/ly) is ready for the panel's distance column and the
  tracking label.

### Established Patterns
- Hud is a strictly **read-only consumer** of `GameWorld`/`UniObject` state (class header
  contract). All new selection/marker code must preserve this.
- Observer-unit render conversion (`metres / ship.LocalPos.Scale * factor`) — Phase 5
  canonical rule; the marker size/position math must use it (D-51) to match the meshes.
- Play-test `[Export]` tuning knobs (e.g. `MIN_CIRCLE_RADIUS`, `CIRCLE_BODY_PADDING`).

### Integration Points
- Selection ownership: today the active target lives in `Hud` (`_targetIndex`) and
  `FlightController` reads `ActiveTargetIndex`. Cross-space selection + retiring Tab changes
  how a target is chosen; the read-only Hud→FlightController handle should be preserved.
- Marker render: a per-target sphere-outline (3D mesh + outline shader, or screen-space
  pass) positioned at the UniObject-computed render location — connects to the camera/
  WorldRenderer floating-origin frame, NOT to the body's own mesh (which may not exist).
- FlightController D-43 ease-out will now potentially ease onto a **cross-space** target;
  the tier ceiling (D-40) still caps speed, so this is selection-only (no autopilot). Planner
  should confirm the ease-out behaves sanely with a far cross-space target distance.
</code_context>

<specifics>
## Specific Ideas

- User framing (verbatim, 2026-06-20): "You do not need a mesh for rendering the circle, you
  have everything you need in UniObject. Calculate the position and size like the mesh would
  be calculated. Then render the circle. In fact add 999.2 to this phase and render the
  outline of a sphere mesh as target marker."
- Side list panel mock the user selected:
  ```
  TARGETS ────────
  GALAXY
    HOME GALAXY 12.4ly
  STAR
    SOL         1.2AU
    ALPHA CEN   4.2ly
  PLANET
    EARTH ◀     0.3AU
    MARS        0.9AU
  ```
- The galaxy-targetability gap from Phase 5 UAT (Test 8 — galaxies not selectable in
  Universe space) is resolved by D-51 + D-55: galaxies become selectable and get a marker
  computed from UniObject, independent of the galaxy-visibility render debt.
</specifics>

<deferred>
## Deferred Ideas

- **Autopilot / "warp drive" traversal** and **distance-ranked auto-selection** — backlog
  **999.3** (distance-based cross-space traversal + autopilot). Phase 6 is manual selection +
  marker + label only; selecting a far target does NOT auto-travel.
- **Warp-drive visual FX** — backlog **999.4** (depends on 999.3).
- **Making galaxies actually visible in Universe space** — render debt
  `galaxy-visibility-in-universe-space` (P2). Phase 6 marks *where* a galaxy is; rendering the
  galaxy body itself there stays that debt's job.

### Reviewed Todos (not folded)
- `galaxy-space-star-meshes-invisible.md` / `galaxy-visibility-in-universe-space.md` —
  rendering debts, not targeting UI; belong to their own rendering phase. The marker no longer
  depends on them (D-51).
- `hud-cycle-target-not-working.md` / `hud-target-nearest-galaxy-space.md` — already RESOLVED
  in prior sessions; the Tab cycle they fixed is being retired here anyway (D-56).
</deferred>

---

*Phase: 6-targeting-system*
*Context gathered: 2026-06-20*
