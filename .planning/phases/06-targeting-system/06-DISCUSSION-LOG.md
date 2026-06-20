# Phase 6: Targeting & Navigation HUD - Discussion Log

> **Audit trail only.** Do not use as input to planning, research, or execution agents.
> Decisions are captured in CONTEXT.md — this log preserves the alternatives considered.

**Date:** 2026-06-20
**Phase:** 6-targeting-system
**Areas discussed:** Marker rendering approach (user-directed scope change), Selector UX, Tab vs tree, Min-size marker behavior, Tracking label

---

## Marker rendering approach + scope (user-directed)

Before the structured questions, the user redirected the marker approach and expanded scope:

> "You do not need a mesh for rendering the circle, you have everything you need in UniObject.
> Calculate the position and size like the mesh would be calculated. Then render the circle.
> In fact add 999.2 to this phase and render the outline of a sphere mesh as target marker."

**Outcome:**
- Marker computed from `UniObject` (UniMath direction/distance + WorldRenderer render-factor
  math), NOT gated on a live mesh → works cross-space incl. galaxies (D-51). Removes the old
  D-46 render-set gate and the galaxy-visibility render-debt dependency.
- Backlog **999.2** (shader-rendered 3D sphere-outline target marker) **folded into Phase 6**
  (D-50). ROADMAP updated accordingly.

---

## Selector UX

| Option | Description | Selected |
|--------|-------------|----------|
| Full-screen tree overlay | Nested expandable tree (Universe→Galaxy→Star→Planet), key opens, arrows/mouse navigate | |
| Compact side list panel | Slide-in side panel, targets grouped by tier, scroll/click to pick | ✓ |
| Decide in planning | Capture requirement, let planning pick the Control form | |

**User's choice:** Compact side list panel (grouped by Galaxy/Star/Planet tier).
**Notes:** Lighter, less modal than a full-screen tree.

---

## Tab vs tree

| Option | Description | Selected |
|--------|-------------|----------|
| Keep both (Tab + tree) | Tab quick-cycles nearby; panel is the full cross-space picker | |
| Tree replaces Tab | Remove the Tab current-tier cycle; all selection via the panel | ✓ |
| Decide in planning | Capture intent; planning decides | |

**User's choice:** Tree (panel) replaces Tab — single selection path.
**Notes:** Frees the `cycle_target`/Tab binding; panel open/close key + cursor handling left to planning (reconcile with 01-03 T-key mouse mode).

---

## Min-size marker behavior

| Option | Description | Selected |
|--------|-------------|----------|
| Floor the sphere outline size | Below a min apparent size, hold a floor radius — stays a round outline reticle at any distance | ✓ |
| Swap to a bracket reticle | Below floor, swap to a fixed-size corner-bracket/crosshair reticle | |
| Decide in planning | Capture requirement; planning chooses the form | |

**User's choice:** Floor the sphere outline size (preserves D-46 findability as a round reticle).

---

## Tracking label

| Option | Description | Selected |
|--------|-------------|----------|
| Name + distance, keep corner too | Pinned label follows the body; rides edge marker off-screen; fixed-corner TGT readout kept | ✓ |
| Name + distance, replace corner | Pinned label becomes primary; remove the corner readout | |
| Name + tier + distance | Richer pinned label; keep corner as fallback | |

**User's choice:** Name + distance pinned to the marker, keep the fixed-corner TGT readout as fallback.

---

## Claude's Discretion

- Sphere-outline rendering technique (per-target sphere mesh + rim/fresnel outline shader vs
  screen-space SDF/silhouette pass).
- Min-size floor value + marker padding multiplier (play-test `[Export]` knobs).
- Panel open/close key, navigation input, cursor freeing vs the T-key mouse mode, grouping
  by `Space` tier vs hierarchy parentage.
- Where the active target/selection is owned (keep in read-only Hud vs shared owner), keeping
  the Hud→FlightController `ActiveTargetIndex` read-only contract.

## Deferred Ideas

- Autopilot / "warp drive" traversal + distance-ranked auto-selection — backlog 999.3.
- Warp-drive visual FX — backlog 999.4.
- Making galaxies actually visible in Universe space — render debt
  `galaxy-visibility-in-universe-space` (P2); Phase 6 only marks where they are.
