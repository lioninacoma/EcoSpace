# Phase 4: Flight Model v2 — Tier & Target-Aware Speed - Research

**Researched:** 2026-06-17
**Domain:** Godot 4.6 C# arcade flight envelope; HUD target coupling; 2D-over-3D world-pinned UI
**Confidence:** HIGH — all findings are derived from direct source-file reading of the existing codebase

---

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions

- **D-40:** Max speed per tier is `parent.SOIMeters × k` (one tunable `[Export]`). NOT a hand-authored table.
- **D-41:** Ceiling transitions ease via the existing `_contextMax` lerp (D-07) — no speed snap on SOI crossing.
- **D-42:** With no target: speed bounded by tier ceiling + symmetric proximity damp near nearest body. "Receding" returns to tier ceiling, NEVER to global `MaxSpeed`.
- **D-43:** With target: `v → min(tierCeiling, distToTarget × k')`, eased. Tier ceiling still caps top speed.
- **D-44:** "Thrust handled by current target; if no target, bounded by current space" is realized by D-42 + D-43 together.
- **D-45:** Targeting reach stays current-tier (current SOI parent + its children). D-12 is NOT overridden. True cross-SOI targeting stays in backlog 999.1.
- **D-46:** Active target gets a world-pinned outline/circle with minimum on-screen radius. Drawn ONLY when target body is in the rendered set (current space). Fallback to existing off-screen edge-marker when target is not rendered.
- **D-47:** Single auto-scaling envelope — NO separate FTL mode. `MaxSpeed`/`MinSpeed`/throttle `[-1,1]`/`full_stop` and the `_easedSpeed` lerp all stay. This phase reshapes how `targetMax`/`_contextMax` are derived.

### Claude's Discretion

- Exact value of `k` / `k'` and the proximity-damp curve shape — tune by feel in-game.
- Whether the tier "characteristic distance" is exactly `parent.SOIMeters` or a related per-Space quantity — researcher to pick the cleanest source already on `UniObject`.
- Where the target-circle is drawn (Hud Control vs a WorldRenderer overlay) — implementation detail.

### Deferred Ideas (OUT OF SCOPE)

- True cross-SOI / cross-space targeting (selecting a body in a different SOI). Stays in backlog 999.1.
- Full 999.1 nav-HUD — hierarchy tree selector across the whole universe.
- `galaxy-visibility-in-universe-space.md` (P2) — its own phase.
</user_constraints>

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|------------------|
| FLT-02 (refine) | Player can control forward throttle and slow or stop the ship | Existing throttle `[-1,1]` machinery stays; only `UpdateSpeedEnvelope`'s `targetMax` derivation changes |
| FLT-03 (refine) | Ship speed auto-scales to SOI surroundings — crawling near bodies, accelerating enormously in empty space — with no manual mode switching | Per-tier ceiling derived from `parent.SOIMeters × k`; proximity damp preserves the "near bodies = slow" contract |
| HUD-04 (minimal 999.1 slice) | World-pinned target outline with minimum on-screen radius drawn when target body is in rendered set; fallback to existing off-screen edge-marker otherwise | `Camera3D.UnprojectPosition` pattern already in `UpdateDirectionMarker`; drawn in Hud `_Draw` or as a child `Control` |
</phase_requirements>

---

## Summary

Phase 4 replaces the single-value `_maxSpeed` clamp in `FlightController.UpdateSpeedEnvelope` with a two-layer model: a per-tier ceiling derived from the ship's current SOI radius, and an optional target-distance ease-out when a target is active. The existing proximity-distance scan, the `_contextMax` lerp (D-07), and the `_easedSpeed` lerp (Bug 4 fix) are all preserved and reshaped — this is a surgical edit of `UpdateSpeedEnvelope`, not a rewrite.

The root cause of both Phase-03 UAT failures (in-system over-speed + galaxy-SOI-exit dead zone) is a single global `_maxSpeed = 2e20` applied at every scale. When receding from the nearest body the envelope has no per-tier ceiling to return to — it reaches for the global intergalactic value. The fix is that `targetMax` is computed from `parent.SOIMeters × k`, so the ceiling is always contextually appropriate and there is never a global intergalactic value to "snap to" inside a star system.

The second deliverable — a world-pinned target circle — extends the existing `Hud` class. The camera-projection pattern used by `UpdateDirectionMarker` (`Camera3D.UnprojectPosition`) is directly reused. The circle is gated on the target body being in `WorldRenderer`'s `_lastRenderPositions` dictionary (the RND-07 baseline already maintained there) so it only draws when the body is a visible mesh.

**Primary recommendation:** Reshape `UpdateSpeedEnvelope` in-place to compute `targetMax = Mathf.Clamp(parent.SOIMeters * k, MinSpeed, tierFloor)` where `k` and `tierFloor` are tuning exports. Add a `CurrentTargetIndex` read-only property to `Hud` (or expose `_targetIndex` as a public getter) so `FlightController` can read the active target object index. Implement the target circle as an additional draw pass inside `Hud._Draw` (or a sibling `Control` node), gated on `WorldRenderer.GetRenderPosition`.

---

## Architectural Responsibility Map

| Capability | Primary Tier | Secondary Tier | Rationale |
|------------|-------------|----------------|-----------|
| Per-tier speed ceiling derivation | Flight / Controller | — | `FlightController.UpdateSpeedEnvelope` already owns `_contextMax`; it reads `parent.SOIMeters` from the sim layer |
| Proximity damp (symmetric) | Flight / Controller | — | Already computed inside `UpdateSpeedEnvelope`; reshaping is a local change |
| Target-aware ease-out | Flight / Controller | HUD (read source) | Controller owns the envelope; HUD exposes the active target index as a read-only property |
| Target selection / cycling | HUD | — | `Hud._targetIndex`, `BuildTargetableList`, `_Input(cycle_target)` already own this; no change needed |
| World-pinned target circle | HUD | WorldRenderer (gating) | Hud owns all 2D overlay drawing; WorldRenderer's `GetRenderPosition` provides the "is this body a mesh?" gate |
| Target object data lookup | Sim (GameObjects) | — | `TestSetup.GameObjects[index]` is the authoritative object store; all tiers read it |

---

## Standard Stack

No new external packages. This phase is a pure in-codebase change to three existing files.

### Core (already present)

| Component | File | Purpose in Phase 4 |
|-----------|------|---------------------|
| `FlightController` | `Scripts/Flight/FlightController.cs` | Primary edit target — `UpdateSpeedEnvelope` reshaped |
| `Hud` | `Scripts/Hud/Hud.cs` | Add read-only target accessor; add target-circle draw |
| `WorldRenderer` | `Scripts/Render/WorldRenderer.cs` | Read `GetRenderPosition` to gate circle draw; no edit |
| `UniObject` | `Scripts/UniObject.cs` | `SOIMeters`, `RadiusMeters`, `Space` — read only |
| `UniMath` | `Scripts/Math/UniMath.cs` | `RelativeMetres`, `Distance` — read only |
| `Camera3D` | Godot built-in | `UnprojectPosition` for 2D screen projection |

### No External Packages

**Installation:** No `dotnet add package` commands required. No NuGet changes.

## Package Legitimacy Audit

No external packages are added in this phase. This section is not applicable.

---

## Architecture Patterns

### System Architecture Diagram

```
[Player Input: W/S throttle, Tab cycle_target]
        │
        ▼
[FlightController._Process]
  ├─ HandleThrottleInput → _throttle01 [-1,1]
  ├─ UpdateSpeedEnvelope (RESHAPED)
  │     ├─ Read: ship.ParentIndex → parent
  │     ├─ Tier ceiling: parent.SOIMeters × k   [D-40]
  │     ├─ Proximity damp: nearest surface dist  [D-42 symmetric]
  │     ├─ Target ease-out (if target set):      [D-43]
  │     │     └─ Read Hud.ActiveTargetIndex → look up target object
  │     │         → UniMath.Distance(ship, target) × k'
  │     │         → min(tierCeiling, targetDist × k')
  │     ├─ targetMax = min(tierCeiling, dampedMax, targetEaseMax)
  │     ├─ _contextMax lerp toward targetMax     [D-41/D-07]
  │     └─ _easedSpeed lerp toward throttle×_contextMax [Bug4]
  └─ ApplyMotion → TranslatePos
        │
        ▼
    [UniVec3 position updated]

[Hud._Process]
  ├─ UpdateSpeedLabel ← FlightController.CurrentSpeed
  ├─ UpdateContextLabel ← UniMath.Distance
  ├─ UpdateTargetReadout ← UniMath.RelativeMetres
  │     └─ UpdateDirectionMarker (off-screen edge, existing)
  └─ _Draw [NEW]
        ├─ Gate: WorldRenderer.GetRenderPosition(targetIdx, out pos)?
        │   YES → UnprojectPosition(pos) → draw circle at screen pos
        │           enforce min-radius floor
        │   NO  → skip (edge marker fallback already active)
        └─ PhosphorGreen color, no fill

[WorldRenderer._Process → SyncBodies]
  ├─ Renders current-space bodies as MeshInstance3D
  └─ Persists _lastRenderPositions (RND-07 baseline)
       └─ GetRenderPosition(idx) → read by Hud._Draw [gate]
```

### Recommended Project Structure

No new files are required. All edits go to existing files:

```
Scripts/
├── Flight/
│   └── FlightController.cs     ← PRIMARY EDIT: UpdateSpeedEnvelope, new [Export] k/k'/DampRadius
├── Hud/
│   └── Hud.cs                  ← Add: ActiveTargetIndex property, _Draw override, _worldRenderer ref
└── Render/
    └── WorldRenderer.cs        ← READ ONLY (GetRenderPosition already public)
```

### Pattern 1: Per-Tier Ceiling from SOI Radius (D-40)

**What:** Derive `tierCeiling` from `parent.SOIMeters × k` instead of a hard-coded table.
**When to use:** At the top of `UpdateSpeedEnvelope`, before the proximity scan.

```csharp
// Source: direct analysis of FlightController.cs + UniObject.cs field inventory
// parent.SOIMeters is set for ALL Space levels in TestSetup:
//   Planet space: PlanetSOI = 1.0e9 m
//   Star space:   StarSOI   = 1.5e15 m
//   Galaxy space: GalaxySOI = 5e20 m
//   Universe (Root): double.MaxValue
// Guard double.MaxValue to avoid Infinity in downstream multiply.
double tierCeiling = (parent.SOIMeters < double.MaxValue / 2.0)
    ? Mathf.Clamp(parent.SOIMeters * k, _minSpeed, _maxSpeed)
    : _maxSpeed;   // root/open-universe: use authored MaxSpeed
```

**Key insight for Root space:** When the ship is in Universe space, `parent` is the Root object (index 0), which has `SOIMeters = double.MaxValue`. Guard against this explicitly and fall back to `_maxSpeed` (the intergalactic tuning knob).

**[ASSUMED]** — The Root SOI is confirmed `double.MaxValue` from TestSetup (`AddGameObject(-1, ..., double.MaxValue)`). This guard is structural.

### Pattern 2: Symmetric Proximity Damp — Reshaping the Existing Scan (D-42)

**What:** The existing nearest-surface-distance scan already finds `nearest` (surface distance from ship). The only change is that `targetMax` is clamped to `tierCeiling` rather than `_maxSpeed`.

**Current code (to reshape):**

```csharp
// CURRENT — line 434 in FlightController.cs
double targetMax = Mathf.Clamp(nearest * _speedPerMeter, _minSpeed, _maxSpeed);
```

**New code:**

```csharp
// NEW — tier ceiling computed above; proximity damp still uses nearest surface dist
double dampedMax = nearest * _speedPerMeter;          // same formula as before
double targetMax = Mathf.Clamp(dampedMax, _minSpeed, tierCeiling);   // cap changed
```

**Why this fixes the recede bug:** When the ship recedes from the galaxy boundary, `nearest` grows large, `dampedMax` grows proportionally, but it is now clamped to `tierCeiling` (= `galaxySOI × k`, an appropriately large but not intergalactic value). It NEVER reaches the global `_maxSpeed = 2e20` while still at Galaxy scale. The dead zone at the SOI exit disappears because `tierCeiling` for Galaxy space (~`5e20 × k`) is already a large value, not constrained by the nearest-surface falloff.

**[ASSUMED]** — Mathematical reasoning from the existing code; confirmed by direct source reading.

### Pattern 3: Target-Aware Ease-Out (D-43)

**What:** When a target is set, compute an additional `targetEaseMax = distToTarget × k'` and take the minimum with the tier ceiling.

**Integration point — reading the active target:**

The active target index lives in `Hud._targetIndex` (private). The cleanest pattern given the existing ownership is to **expose a read-only property on Hud**:

```csharp
// In Hud.cs — add this property (read-only, no mutation)
/// <summary>Index into GameObjects of the active target, or -1 if none.</summary>
public int ActiveTargetIndex
{
    get
    {
        if (_world == null) return -1;
        var objs = _world.GameObjects;
        int shipIdx = _world.ShipIndex;
        if ((uint)shipIdx >= (uint)(objs?.Count ?? 0)) return -1;
        var ship = objs[shipIdx];
        if (ship == null) return -1;
        var targets = BuildTargetableList(ship.ParentIndex, shipIdx, objs);
        if (targets.Count == 0) return -1;
        int clamped = Mathf.Clamp(_targetIndex, 0, targets.Count - 1);
        return targets[clamped].Index;
    }
}
```

**Alternative (simpler):** expose `_targetIndex` as `public int TargetListIndex => _targetIndex;` and let `FlightController` reconstruct the target from the targetable list. This is slightly more coupled but avoids the repeated `BuildTargetableList` call.

**Recommended:** expose `ActiveTargetIndex` as above — it returns the actual `GameObjects` index directly, and the `BuildTargetableList` call is cheap (scans ≤ ~10 children).

**In FlightController — resolving the Hud reference:**

`FlightController` already resolves `_world` via `WorldPath`. It can also resolve `_hud` via a new `[Export] public NodePath HudPath { get; set; }` or by `FindChild("Hud", ...)` in `_Ready`, mirroring how `_camera` is already found.

```csharp
// In FlightController._Ready, alongside camera resolution:
_hud = GetTree().Root.FindChild("Hud", true, false) as Hud.Hud;
```

**Target-aware envelope:**

```csharp
// Inside UpdateSpeedEnvelope, after proximity targetMax is computed:
int tgtIdx = _hud?.ActiveTargetIndex ?? -1;
if (tgtIdx >= 0 && (uint)tgtIdx < (uint)gameObjects.Count && gameObjects[tgtIdx] != null)
{
    var tgtObj = gameObjects[tgtIdx];
    double distToTarget = UniMath.Distance(ship, tgtObj, gameObjects);
    double targetEaseMax = distToTarget * _speedPerTarget;   // k' export
    targetMax = System.Math.Min(targetMax, Mathf.Clamp(targetEaseMax, _minSpeed, tierCeiling));
}
```

**CRITICAL:** Use `UniMath.Distance(ship, tgtObj, gameObjects)` — NOT `ship.LocalPos.Magnitude()` or any raw `ToDouble3()` accumulation. `UniMath.Distance` uses the LCA path (CLAUDE.md §"Position Math"). [ASSUMED from CLAUDE.md convention — verified against UniMath.cs source]

### Pattern 4: World-Pinned Target Circle (D-46) — Hud._Draw

**What:** A 2D circle drawn in screen space, pinned to the 3D world position of the active target, with a minimum on-screen radius so a distant target is never sub-pixel.

**Where to draw — `Hud._Draw` override:**

`Hud` is a `Control` (partial class), so it can override `_Draw()` and call `DrawCircle` / `DrawArc`. This keeps all HUD drawing in one node. Set `QueueRedraw()` at the end of `_Process` to trigger a redraw each frame.

**Godot 4.6 C# `_Draw` pattern for a circle:**

```csharp
// Source: Godot 4.x Control._Draw override — standard pattern [ASSUMED: Godot 4.x docs]
public override void _Draw()
{
    if (!_showTargetCircle) return;
    // DrawArc(center, radius, startAngle, endAngle, pointCount, color, width)
    DrawArc(_targetCirclePos, _targetCircleRadius, 0f, Mathf.Tau, 32, PhosphorGreen, 1.5f);
}
```

**Projecting the 3D render position to screen (`Camera3D.UnprojectPosition`):**

The render position stored in `WorldRenderer._lastRenderPositions[targetIdx]` is in **render space** (WorldRenderer coordinate frame, which is `Node3D`-based, floating origin at ship). To convert to a global scene position for `Camera3D.UnprojectPosition`, add it to `WorldRenderer.GlobalPosition` (which is always at/near origin since it's anchored to ship):

```csharp
// The render pos is in WorldRenderer's local space (same as GlobalPosition when
// WorldRenderer.Position = Vector3.Zero, which it is under floating origin).
// Camera3D.UnprojectPosition expects a GLOBAL position.
Vector3 globalRenderPos = _worldRenderer.GlobalPosition + renderPos;
Vector2 screenPos = _camera.UnprojectPosition(globalRenderPos);
```

**Behind-camera guard (mirror of UpdateDirectionMarker):**

```csharp
Vector3 cameraLocal = _camera.GlobalTransform.AffineInverse() * (globalRenderPos - _camera.GlobalPosition);
bool isBehind = cameraLocal.Z > 0;
if (isBehind) { _showTargetCircle = false; return; }  // edge marker fallback
```

**Minimum on-screen radius:**

```csharp
// Compute projected radius: angular size of body at current distance
// For a simple always-readable circle, enforce a minimum floor regardless of distance.
// The body's physical radius in render units can serve as the projected-radius input
// when available; when zero/tiny, use the minimum floor.
float MIN_CIRCLE_RADIUS = 20f;   // pixels — play-test knob
float MAX_CIRCLE_RADIUS = 200f;  // cap so it doesn't fill the screen close-up

// Angular size: renderRadius / screenDistFromOrigin * someViewportFactor
// Simplest approach: fixed minimum with a distance-based cap.
_targetCircleRadius = Mathf.Clamp(computedRadius, MIN_CIRCLE_RADIUS, MAX_CIRCLE_RADIUS);
```

**GetRenderPosition gate — "is the body rendered?":**

The `WorldRenderer.GetRenderPosition(targetIdx, out Vector3 pos)` method (already public, already documented) is the exact gate needed:

```csharp
if (_worldRenderer == null || !_worldRenderer.GetRenderPosition(tgtIdx, out Vector3 renderPos))
{
    _showTargetCircle = false;   // target not in rendered set → edge marker handles it
    return;
}
// body is a mesh → proceed with circle draw
```

**Resolving `_worldRenderer` reference in Hud:**

Add `[Export] public NodePath WorldRendererPath { get; set; }` (or `FindChild("WorldRenderer")`), following the same pattern as `FlightPath`.

**QueueRedraw pattern:**

At the end of `Hud._Process`, call `QueueRedraw()` to trigger `_Draw` on the next paint cycle:

```csharp
// At end of _Process, after UpdateTargetReadout:
QueueRedraw();
```

### Anti-Patterns to Avoid

- **Anti-pattern (from reverted 260617-j6b):** Exempting the proximity damp when the ship is receding. This jumps to the global `_maxSpeed`, making in-system travel unusable. The fix is to remove the global ceiling, not to special-case the damp.
- **Anti-pattern: absolute-from-root metres for cross-frame distance.** Do NOT use `ship.LocalPos.ToDouble3().Magnitude()` for distance-to-target when the target is in a different space frame. Always use `UniMath.Distance(ship, target, gameObjects)`. (CLAUDE.md §"Position Math" — DO NOT hand-roll `Units × Scale + Offset` accumulation.)
- **Anti-pattern: hard-coded per-tier speed table.** D-40 explicitly forbids a `switch(ship.CurrentSpace)` table. The ceiling must be derived from `parent.SOIMeters × k`.
- **Anti-pattern: HUD mutating sim state.** The `ActiveTargetIndex` property on `Hud` MUST be read-only — it reads `_targetIndex` and `_world.GameObjects` but never modifies them. `Hud` is a read-only consumer.
- **Anti-pattern: Sharing ShaderMaterial instances.** Already avoided in WorldRenderer — irrelevant to this phase but noted for completeness.
- **Anti-pattern: Re-projecting from raw LocalPos in Hud._Draw.** The circle must use the render-space position from `WorldRenderer.GetRenderPosition`, not recompute from `LocalPos`. The render position already accounts for floating-origin and scale factor; computing independently would produce a discrepancy.

---

## Open Questions (Answered by Research)

### Q1: Is `parent.SOIMeters` the cleanest source for D-40?

**Answer: YES. `parent.SOIMeters` is populated for every Space level and is already read in `UpdateSpeedEnvelope` (via `parent = gameObjects[parentIdx]`).**

Confirmed values from `TestSetup.cs`:
- Planet space: `PlanetSOI = 1.0e9 m` — ship orbiting a planet
- Star space: `StarSOI = 1.5e15 m` — ship inside a star system
- Galaxy space: `GalaxySOI = 5e20 m` — ship inside a galaxy
- Universe / Root: `double.MaxValue` — no containing body; guard needed

`RadiusMeters` on the parent is also populated but is smaller than `SOIMeters` (e.g. star radius 6.96e8 m vs SOI 1.5e15 m). `SOIMeters` is the better characteristic distance because it represents the extent of the gravitational sphere, matching the "how far can you go in this tier" intuition. `RadiusMeters` is the physical body size — useful for the proximity damp that already uses it, but not for the tier ceiling.

**Conclusion:** Use `parent.SOIMeters` for D-40. `RadiusMeters` stays in the proximity scan as-is. [VERIFIED: TestSetup.cs direct read — all SOI values confirmed set]

### Q2: Symmetric proximity damp and the recede-bug root cause

**Answer: The existing nearest-surface scan IS the symmetric proximity damp. No direction gate is needed. The only change is capping `targetMax` with `tierCeiling` instead of `_maxSpeed`.**

The scan already covers parent + all siblings symmetrically (lines 398–427 of FlightController.cs). The proximity damp fires equally approaching AND receding because it only looks at current distance, not velocity direction. The 260617-j6b bug was adding a direction gate; the correct fix is removing the global ceiling so there's nothing objectionable to gate.

Existing code that stays verbatim:
- Parent surface distance: `distToParentSurface = ship.LocalPos.Magnitude() - parent.RadiusMeters` (line 407)
- Sibling scan: `UniVec3.Distance(ship.LocalPos, body.LocalPos) - body.RadiusMeters` (lines 424-426)
- Both lerps (lines 437, 446) — MUST continue running on EVERY path (the one correct property of the reverted fix)

**Conclusion:** Minimum code change — only line 434's clamp changes from `_maxSpeed` to `tierCeiling`. [VERIFIED: FlightController.cs direct read]

### Q3: Target coupling pattern — how should `FlightController` read the active target?

**Answer: Expose `public int ActiveTargetIndex { get; }` on `Hud`. FlightController holds a `_hud` reference (resolved in `_Ready` via `FindChild`).**

Ownership analysis:
- `_targetIndex` is `private int` in `Hud` (line 79 of Hud.cs)
- `BuildTargetableList` is `private` — it will need to become internal or the property must call it
- `FlightController` already gets `_world` the same way Hud gets `_flight` — via `FindChild`. Same pattern works for `_hud` in `FlightController`.
- Hud's read-only contract (MUST NOT mutate sim state) is preserved — the property only reads, never writes.
- The `TargetEntry` struct only has `int Index` (line 72 of Hud.cs) — the property can just return `targets[clamped].Index` directly.
- `BuildTargetableList` must be changed from `private` to `private` (unchanged) but called inside the property getter — OR make `BuildTargetableList` `internal` to allow the getter to call it. Since both are in the `Hud` namespace the getter can call it directly.

This approach requires zero new files and zero mutations. The `ActiveTargetIndex` property is a thin read-only accessor. [VERIFIED: Hud.cs lines 72-80, 333-354 direct read]

### Q4: World-pinned target circle — `Hud._Draw` vs WorldRenderer overlay

**Answer: Draw in `Hud._Draw` (Control node). Gate on `WorldRenderer.GetRenderPosition`. Resolve WorldRenderer ref in Hud._Ready.**

Why `Hud._Draw` (not WorldRenderer overlay):
- HUD is already a `Control` with `_Draw` support — Godot's standard 2D drawing path for UI
- `WorldRenderer` is a `Node3D`, not a `Control` — it would need a `CanvasLayer` child or a separate `Control` overlay, adding complexity
- All other HUD elements (speed, context, target label, edge marker) are in `Hud` — the circle naturally belongs there
- `Hud` already has `_camera` reference (line 55 of Hud.cs) needed for `UnprojectPosition`

Gate mechanism:
- `WorldRenderer.GetRenderPosition(int bodyIdx, out Vector3 pos)` is already `public` (lines 327-332 of WorldRenderer.cs)
- It returns `true` only when the body was in the render set during the most-recent `SyncBodies` call
- This is exactly the "is the body a visible mesh this frame?" gate D-46 requires
- `Hud` needs a `_worldRenderer` reference — add via `[Export] NodePath WorldRendererPath` or `FindChild("WorldRenderer")`

`QueueRedraw()` is the standard Godot 4.x pattern to trigger `_Draw` per frame in a `Control`. [ASSUMED: Godot 4.x docs — standard Control._Draw pattern; no Context7 query performed as nyquist_validation is false and no external MCP providers are configured]

---

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Cross-frame distance to target | Raw `ToDouble3()` from LocalPos across spaces | `UniMath.Distance(ship, target, gameObjects)` | LCA path prevents catastrophic cancellation at universe scale (CLAUDE.md §"Position Math") |
| 2D screen position of 3D point | Manual viewport math | `Camera3D.UnprojectPosition` | Accounts for FOV, camera transform, viewport scale — already used in `UpdateDirectionMarker` |
| "Is body currently rendered as mesh?" | Secondary render tracking | `WorldRenderer.GetRenderPosition(idx, out pos)` | RND-07 baseline already maintained; authoritative answer each frame |
| Per-tier speed lookup table | `switch(ship.CurrentSpace) { case Star: return 1e15; ... }` | `parent.SOIMeters × k` | D-40 explicitly forbids the table; SOI radius is already present and auto-scales |

---

## Common Pitfalls

### Pitfall 1: Root SOI is double.MaxValue → Infinity when multiplied

**What goes wrong:** `parent.SOIMeters * k` overflows to `Infinity` when the ship is in Universe space (parent = Root, `SOIMeters = double.MaxValue`).
**Why it happens:** `AddGameObject(-1, ..., double.MaxValue)` in TestSetup.cs line 141 — Root has `SOIMeters = double.MaxValue` as an intentional sentinel.
**How to avoid:** Guard before the multiply: `if (parent.SOIMeters < double.MaxValue / 2.0)` then use formula; else fall back to `_maxSpeed`.
**Warning signs:** HUD shows "Infinity ly/s"; `CurrentSpeed` becomes `NaN` after `ApplyMotion` guard catches it.

### Pitfall 2: Both lerps must run on every code path (Bug 4)

**What goes wrong:** An early `return` inside `UpdateSpeedEnvelope` that skips the `_contextMax` or `_easedSpeed` lerp causes a speed snap when the skipped condition resolves.
**Why it happens:** The reverted 260617-j6b fix had this — one code path skipped the easing lerps. The comments in FlightController.cs (line 437, "Ease contextMax ... D-07, Pitfall 9") document why.
**How to avoid:** Structure the new code so BOTH lerps (lines 437 and 446) always execute regardless of whether a target is set. Compute `targetMax` by the two-or-three-stage logic above, then fall through to the existing lerps unconditionally.
**Warning signs:** Hitting/releasing a target causes a visible speed snap.

### Pitfall 3: UniVec3.Distance for ship-to-sibling inside UpdateSpeedEnvelope

**What goes wrong:** The existing `UniVec3.Distance(ship.LocalPos, body.LocalPos)` on line 424 works for siblings because they share the same parent frame. If you accidentally use `UniMath.Distance(ship, body, objs)` here it is correct but slower — and vice versa.
**Why it matters:** The existing scan uses `UniVec3.Distance` specifically because ship + siblings are in the SAME parent frame (documented in the comment at line 412). The target-aware ease-out uses `UniMath.Distance` because the target may (in a future phase) be in a DIFFERENT frame. Keep these separate.
**How to avoid:** Comment each call site explaining which path it uses and why.

### Pitfall 4: `_Draw` not triggered every frame without `QueueRedraw()`

**What goes wrong:** Target circle position freezes, shows at wrong location, or never appears.
**Why it happens:** Godot `Control._Draw` is NOT called every frame by default — only when `QueueRedraw()` is called (or the node is dirtied). Unlike `_Process`, it is an on-demand draw event.
**How to avoid:** Call `QueueRedraw()` at the end of `Hud._Process` every frame when the circle is active.

### Pitfall 5: `UnprojectPosition` returns wrong position if called with non-global coordinates

**What goes wrong:** Circle appears at the wrong screen location, offset from the actual target body.
**Why it happens:** `Camera3D.UnprojectPosition` takes a **global** `Vector3`. The render position from `GetRenderPosition` is in `WorldRenderer`'s local space (a `Node3D`). If `WorldRenderer` is a child of a node that is NOT at global origin, `localPos` ≠ `globalPos`.
**How to avoid:** Convert to global: `_worldRenderer.GlobalPosition + renderPos`. In practice, WorldRenderer is a child of Main which is at origin, so `GlobalPosition` is `Vector3.Zero` — but using `GlobalPosition` explicitly is safe for future refactors.

### Pitfall 6: Target circle draws when behind-camera, producing a mirrored screen artifact

**What goes wrong:** The circle appears at the edge of the screen on the opposite side when the target is behind the ship.
**Why it happens:** `UnprojectPosition` wraps behind-camera positions to the opposite side of the viewport (screen-space flip).
**How to avoid:** Apply the same camera-local Z-test used in `UpdateDirectionMarker` (Hud.cs lines 240-244): `cameraLocal.Z > 0` → behind camera → skip circle draw (edge marker fallback). [VERIFIED: Hud.cs lines 240-255 direct read]

---

## Code Examples

### Full reshaped `UpdateSpeedEnvelope` — minimal-diff structure

```csharp
// Source: analysis of FlightController.cs + context decisions D-40/D-42/D-43
private void UpdateSpeedEnvelope(double delta)
{
    var gameObjects = _world?.GameObjects;
    if (gameObjects == null) return;

    int shipIndex = _world.ShipIndex;
    var ship = (uint)shipIndex < (uint)gameObjects.Count ? gameObjects[shipIndex] : null;
    if (ship == null) return;

    int parentIdx = ship.ParentIndex;
    var parent = (uint)parentIdx < (uint)gameObjects.Count ? gameObjects[parentIdx] : null;
    if (parent == null)
    {
        CurrentSpeed = _easedSpeed;
        return;
    }

    // ── Tier ceiling (D-40) ──────────────────────────────────────────────────
    // Derived from the ship's current SOI radius × tuning factor k.
    // Guard double.MaxValue (Root SOI) to avoid Infinity.
    double tierCeiling = (parent.SOIMeters < double.MaxValue / 2.0)
        ? Mathf.Clamp(parent.SOIMeters * _tierSpeedFactor, _minSpeed, _maxSpeed)
        : _maxSpeed;

    // ── Nearest-surface distance scan (unchanged — Bug 3 fix preserved) ──────
    double nearest = double.MaxValue;
    if (parent.RadiusMeters > 0.0)
    {
        double distToParentCentre  = ship.LocalPos.Magnitude();
        double distToParentSurface = System.Math.Max(0.0, distToParentCentre - parent.RadiusMeters);
        nearest = System.Math.Min(nearest, distToParentSurface);
    }
    int[] siblings = [.. parent.ChildIndices];
    foreach (int idx in siblings)
    {
        if (idx == shipIndex) continue;
        var body = (uint)idx < (uint)gameObjects.Count ? gameObjects[idx] : null;
        if (body == null || body.RadiusMeters <= 0.0) continue;
        double centreDist  = UniVec3.Distance(ship.LocalPos, body.LocalPos);
        double surfaceDist = System.Math.Max(0.0, centreDist - body.RadiusMeters);
        nearest = System.Math.Min(nearest, surfaceDist);
    }
    if (nearest == double.MaxValue)
        nearest = tierCeiling / System.Math.Max(_speedPerMeter, 1.0);

    // ── Proximity damp + tier ceiling (D-42) ─────────────────────────────────
    // ONLY change from current code: clamp to tierCeiling instead of _maxSpeed.
    double targetMax = Mathf.Clamp(nearest * _speedPerMeter, _minSpeed, tierCeiling);

    // ── Target-aware ease-out (D-43, only when target is set) ────────────────
    int tgtIdx = _hud?.ActiveTargetIndex ?? -1;
    if (tgtIdx >= 0 && (uint)tgtIdx < (uint)gameObjects.Count && gameObjects[tgtIdx] != null)
    {
        // MUST use UniMath.Distance — LCA path required (CLAUDE.md §Position Math)
        double distToTarget = UniMath.Distance(ship, gameObjects[tgtIdx], gameObjects);
        double targetEaseMax = Mathf.Clamp(distToTarget * _speedPerTarget, _minSpeed, tierCeiling);
        targetMax = System.Math.Min(targetMax, targetEaseMax);
    }

    // ── Easing lerps — ALWAYS run (D-07 / D-41 / Bug 4 fix) ─────────────────
    _contextMax  = Mathf.Lerp(_contextMax,  targetMax,              Mathf.Clamp(_speedEasing * delta, 0.0, 1.0));
    _easedSpeed  = Mathf.Lerp(_easedSpeed,  _throttle01 * _contextMax, Mathf.Clamp(_speedEasing * delta, 0.0, 1.0));
    CurrentSpeed = _easedSpeed;
}
```

### New exports to add to FlightController

```csharp
// Source: analysis of D-40/D-43/D-46 tuning requirements
private double _tierSpeedFactor = 1e-5;
/// <summary>
/// Per-tier speed ceiling factor (D-40): tierCeiling = parent.SOIMeters × k.
/// Default 1e-5 → StarSOI 1.5e15 m × 1e-5 = 1.5e10 m/s (≈50 AU/s in-system).
/// Play-test tuning knob — raise for faster in-system flight, lower for more precision.
/// </summary>
[Export]
public double TierSpeedFactor
{
    get => _tierSpeedFactor;
    set => _tierSpeedFactor = System.Math.Max(0.0, value);
}

private double _speedPerTarget = 0.1;
/// <summary>
/// Target ease-out factor (D-43): targetEaseMax = distToTarget × k'.
/// Default 0.1 → at 1 AU distance (1.5e11 m) → 1.5e10 m/s max. Tune by feel.
/// </summary>
[Export]
public double SpeedPerTarget
{
    get => _speedPerTarget;
    set => _speedPerTarget = System.Math.Max(0.0, value);
}

// Reference to Hud for reading active target
private Hud.Hud _hud;
// (resolved in _Ready alongside _camera resolution)
```

### Hud._Draw target circle implementation sketch

```csharp
// Source: Godot 4.x Control._Draw pattern + analysis of UpdateDirectionMarker
// Fields added to Hud:
private Render.WorldRenderer _worldRenderer;
private bool  _showTargetCircle;
private Vector2 _targetCirclePos;
private float   _targetCircleRadius;

// In _Ready: resolve _worldRenderer (mirroring _camera resolution)
_worldRenderer = GetTree().Root.FindChild("WorldRenderer", true, false) as Render.WorldRenderer;

// At end of _Process: compute circle state, then queue redraw
private void UpdateTargetCircle(UniObject ship, System.Collections.Generic.List<UniObject> gameObjects)
{
    _showTargetCircle = false;
    if (_worldRenderer == null || _camera == null) return;

    // Resolve active target
    var targets = BuildTargetableList(ship.ParentIndex, _world.ShipIndex, gameObjects);
    if (targets.Count == 0) return;
    int clamped = Mathf.Clamp(_targetIndex, 0, targets.Count - 1);
    int tgtIdx = targets[clamped].Index;

    // Gate: is body in WorldRenderer's rendered set?
    if (!_worldRenderer.GetRenderPosition(tgtIdx, out Vector3 renderPos)) return;

    // Behind-camera check (mirror of UpdateDirectionMarker)
    Vector3 globalPos = _worldRenderer.GlobalPosition + renderPos;
    Vector3 camLocal  = _camera.GlobalTransform.AffineInverse() * (globalPos - _camera.GlobalPosition);
    if (camLocal.Z > 0) return;   // behind camera → edge marker fallback

    // Project to screen
    var viewport = GetViewport();
    if (viewport == null) return;
    Vector2 vpSize = viewport.GetVisibleRect().Size;
    Vector2 screenPos = _camera.UnprojectPosition(globalPos);
    if (screenPos.X < 0 || screenPos.X > vpSize.X || screenPos.Y < 0 || screenPos.Y > vpSize.Y)
        return;   // off-screen → edge marker fallback

    // Compute on-screen radius — minimum floor for findability (D-46)
    // Simple approach: body angular size in render units / viewport scale
    // For now, use a fixed minimum; improve with physical radius if needed
    float MIN_R = 20f;
    float MAX_R = 200f;
    _targetCirclePos    = screenPos;
    _targetCircleRadius = Mathf.Clamp(MIN_R, MIN_R, MAX_R);   // minimum floor (tuning knob)
    _showTargetCircle   = true;
}

public override void _Draw()
{
    if (!_showTargetCircle) return;
    DrawArc(_targetCirclePos, _targetCircleRadius, 0f, Mathf.Tau, 32, PhosphorGreen, 1.5f);
}
```

---

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| Single `_maxSpeed = 2e20` for all tiers | Per-tier `parent.SOIMeters × k` ceiling | Phase 4 | In-system travel usable; intergalactic unchanged |
| Direction-gate recede-exempt (260617-j6b, reverted) | Symmetric damp, tier ceiling caps the return value | Phase 4 (revert of j6b) | Recede no longer jumps to intergalactic speed |
| No target-coupled envelope | Target distance modulates ease-out (D-43) | Phase 4 | Ship decelerates onto chosen target |
| Off-screen edge marker only | Edge marker + world-pinned circle (D-46) | Phase 4 | Target always findable whether on- or off-screen |

**Deprecated/outdated in this phase:**
- The single `_maxSpeed` as the envelope ceiling (it becomes the ROOT fallback only)
- `SpeedPerMeter` as the only tuning knob for `targetMax` (joined by `TierSpeedFactor` and `SpeedPerTarget`)

---

## Assumptions Log

| # | Claim | Section | Risk if Wrong |
|---|-------|---------|---------------|
| A1 | `QueueRedraw()` is the correct Godot 4.6 C# pattern to trigger `Control._Draw` every frame | Pattern 4 / Pitfall 4 | Circle never redraws → frozen or invisible. Mitigation: verify in Godot docs or test immediately |
| A2 | `DrawArc(center, radius, startAngle, endAngle, pointCount, color, width)` is the correct `Control._Draw` API signature in Godot 4.6 C# | Code Examples | Build error. Mitigation: confirm signature in Godot 4.6.2 API; alternative is `DrawCircle` for filled or `DrawPolyline` for an arc |
| A3 | `WorldRenderer.GlobalPosition` is at or near `Vector3.Zero` in all frames (floating origin keeps ship at origin) | Pattern 4 | Circle position offset by WorldRenderer's actual global position. Mitigation: use `_worldRenderer.GlobalPosition + renderPos` explicitly (already in example code) — safe regardless of global pos |
| A4 | `BuildTargetableList` accessibility from the `ActiveTargetIndex` getter — it is `private` today | Pattern 3 | Property getter cannot call `BuildTargetableList`. Mitigation: make `BuildTargetableList` `private` (unchanged) and the getter calls it from within the same class — works fine. No access change needed. |

**If this table is empty:** All claims were verified from direct source-file reads. The four assumptions above are Godot API behavior items not verifiable by source-code reading alone. They are low-risk and the mitigation (build + test) is immediate.

---

## Environment Availability

Step 2.6: SKIPPED — this phase is a pure in-codebase change. No external tools, CLIs, databases, or runtimes beyond the existing Godot 4.6.2 + .NET 8.0 project setup are required. The project already builds and runs (Phase 03 plans completed).

---

## Security Domain

`security_enforcement: true` in config.json. ASVS Level 1 applies.

### Applicable ASVS Categories

| ASVS Category | Applies | Standard Control |
|---------------|---------|-----------------|
| V2 Authentication | No | — |
| V3 Session Management | No | — |
| V4 Access Control | No | — |
| V5 Input Validation | Yes | `System.Math.Max(0.0, value)` setters on all new exports; `double.IsFinite` guard already in `ApplyMotion` |
| V6 Cryptography | No | — |

### Known Threat Patterns

| Pattern | STRIDE | Standard Mitigation |
|---------|--------|---------------------|
| Non-finite speed from `parent.SOIMeters × k` (Root SOI = double.MaxValue) | Tampering | Guard `parent.SOIMeters < double.MaxValue / 2.0` before multiply; existing `double.IsFinite` in `ApplyMotion` is last-resort catch |
| Negative or NaN export values for `TierSpeedFactor` / `SpeedPerTarget` | Tampering | `System.Math.Max(0.0, value)` in property setters (same pattern as existing `MaxSpeed`, `MinSpeed`, `SpeedPerMeter`) |
| `ActiveTargetIndex` returning stale index after SOI transition | Elevation of privilege | The getter rebuilds `BuildTargetableList` fresh each call (same logic as `_Input(cycle_target)`); `_targetIndex` is clamped inside `UpdateTargetReadout` |

---

## Sources

### Primary (HIGH confidence — direct source-file reading)

All findings in this research derive from reading the actual codebase. No external web searches were performed (all search providers disabled in config.json).

- `Scripts/Flight/FlightController.cs` — Full read. `UpdateSpeedEnvelope` (lines 365-449), exports (lines 45-162), `_contextMax`/`_easedSpeed` fields (lines 179-186), `ApplyMotion` (lines 459-483).
- `Scripts/UniObject.cs` — Full read. `SOIMeters` (line 75), `RadiusMeters` (line 91), `Space` enum (lines 19-27), `Scale()` static (lines 24-33).
- `Scripts/Hud/Hud.cs` — Full read. `_targetIndex` (line 79), `BuildTargetableList` (lines 333-354), `UpdateDirectionMarker` (lines 226-286), `_camera` (line 55).
- `Scripts/Render/WorldRenderer.cs` — Full read. `GetRenderPosition` (lines 327-332), `_lastRenderPositions` (line 149), `SyncBodies` (lines 200-305).
- `Scripts/Math/UniMath.cs` — Full read. `Distance` (lines 207-209), `RelativeMetres` (lines 197-198), `FindLca` (lines 52-77).
- `Scripts/TestSetup.cs` — Full read. SOI values (lines 53-56), Root setup (line 141), galaxy/star/planet chain (lines 146-260).
- `.planning/phases/04-flight-model-v2-tier-and-target-aware-speed/04-CONTEXT.md` — Decisions D-40 through D-47.
- `CLAUDE.md` — §"Position Math (UniVec3 / UniMath)" — enforced across all distance calls.

### Tertiary (LOW confidence — training knowledge, not verified against external docs)

- Godot 4.x `Control._Draw` / `QueueRedraw()` / `DrawArc` API — [ASSUMED]; verified by expected build + test in Wave 0 of planning.

---

## Metadata

**Confidence breakdown:**
- Speed envelope reshape: HIGH — derived entirely from existing code + locked decisions
- Target coupling pattern: HIGH — derived from Hud.cs + FlightController.cs source
- Target circle draw: HIGH for approach; MEDIUM for exact `DrawArc` API signature (Godot API not re-verified against docs)
- Tier characteristic distance (D-40): HIGH — `parent.SOIMeters` confirmed populated for all spaces in TestSetup.cs

**Research date:** 2026-06-17
**Valid until:** 2026-07-17 (30 days; codebase is stable between phases)
