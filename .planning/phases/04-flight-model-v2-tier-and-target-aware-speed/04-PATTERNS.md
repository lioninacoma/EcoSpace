# Phase 4: Flight Model v2 — Tier & Target-Aware Speed - Pattern Map

**Mapped:** 2026-06-17
**Files analyzed:** 2 (modified); 1 (read-only reference)
**Analogs found:** 5 / 5 (all change sites have in-file analogs — no new files)

---

## File Classification

| Modified File | Role | Data Flow | Closest Analog | Match Quality |
|---------------|------|-----------|----------------|---------------|
| `Scripts/Flight/FlightController.cs` | controller | request-response (per-frame sim update) | Self — `UpdateSpeedEnvelope` (lines 380–449), `_Ready` (lines 213–249), exports (lines 45–162) | exact — same file, same method |
| `Scripts/Hud/Hud.cs` | HUD / overlay | event-driven (per-frame read + `_Draw`) | Self — `UpdateDirectionMarker` (lines 226–286), `BuildTargetableList` (lines 333–354), `_Ready` (lines 82–122) | exact — same file, same class |
| `Scripts/Render/WorldRenderer.cs` | renderer | batch (SyncBodies per frame) | Read-only reference — `GetRenderPosition` (lines 327–332) | read-only gate |

---

## Pattern Assignments

### Change Site 1: `FlightController.UpdateSpeedEnvelope` — Speed Envelope Reshape

**Role:** controller · request-response  
**Analog:** `Scripts/Flight/FlightController.cs` lines 380–449 (the full current method)

**Current method — copy pattern from here** (lines 380–449):

```csharp
// FlightController.cs lines 380–449
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
        // At root — no bodies; skip envelope update, keep previous _easedSpeed
        CurrentSpeed = _easedSpeed;
        return;
    }

    double nearest = double.MaxValue;

    // ── ALWAYS include the parent body itself (Bug 3 fix) ──────────────
    if (parent.RadiusMeters > 0.0)
    {
        double distToParentCentre = ship.LocalPos.Magnitude();
        double distToParentSurface = System.Math.Max(0.0, distToParentCentre - parent.RadiusMeters);
        nearest = System.Math.Min(nearest, distToParentSurface);
    }

    // ── Scan same-space siblings ────────────────────────────────────────
    int[] siblings = [.. parent.ChildIndices];

    foreach (int idx in siblings)
    {
        if (idx == shipIndex) continue;

        var body = (uint)idx < (uint)gameObjects.Count ? gameObjects[idx] : null;
        if (body == null || body.RadiusMeters <= 0.0) continue;

        // Both ship and sibling share the same parent frame — LocalPos values
        // are in the same coordinate space, so UniVec3.Distance is safe here.
        double centreDist = UniVec3.Distance(ship.LocalPos, body.LocalPos);
        double surfaceDist = System.Math.Max(0.0, centreDist - body.RadiusMeters);
        nearest = System.Math.Min(nearest, surfaceDist);
    }

    // If still no bodies found, open space: allow max speed.
    if (nearest == double.MaxValue)
        nearest = _maxSpeed / System.Math.Max(_speedPerMeter, 1.0);

    // ← THIS LINE CHANGES: clamp upper bound changes from _maxSpeed to tierCeiling (D-42)
    double targetMax = Mathf.Clamp(nearest * _speedPerMeter, _minSpeed, _maxSpeed);

    // ← THESE TWO LERPS MUST ALWAYS RUN — never guard-return above them (Bug 4 / Pitfall 2)
    _contextMax = Mathf.Lerp(_contextMax, targetMax, Mathf.Clamp(_speedEasing * delta, 0.0, 1.0));

    double targetSpeed = _throttle01 * _contextMax;
    _easedSpeed = Mathf.Lerp(_easedSpeed, targetSpeed, Mathf.Clamp(_speedEasing * delta, 0.0, 1.0));

    CurrentSpeed = _easedSpeed;
}
```

**What changes (surgical edits only):**
- Before the nearest scan: add `double tierCeiling` derivation from `parent.SOIMeters * k` (D-40).
- Line 434 (`double targetMax = Mathf.Clamp(nearest * _speedPerMeter, _minSpeed, _maxSpeed)`): change `_maxSpeed` → `tierCeiling` (D-42).
- After `targetMax` is set: insert target-aware ease-out block using `_hud?.ActiveTargetIndex` (D-43).
- The two `Mathf.Lerp` calls (lines 437, 446) stay verbatim and ALWAYS run.

**Conventions to replicate:**
- `(uint)idx < (uint)gameObjects.Count` index-safety pattern (CLAUDE.md §Error Handling).
- `UniVec3.Distance(ship.LocalPos, body.LocalPos)` for same-frame sibling scan — NOT `UniMath.Distance` (siblings share parent frame; see comment at line 424).
- `UniMath.Distance(ship, target, gameObjects)` for cross-frame target distance — NOT raw `ToDouble3()` (CLAUDE.md §Position Math).
- `System.Math.Max(0.0, ...)` — use `System.Math` not `Mathf` for doubles; `Mathf` for floats (existing convention throughout file).

---

### Change Site 2: New `[Export]` Tuning Knobs in `FlightController`

**Role:** controller · config  
**Analog:** `Scripts/Flight/FlightController.cs` lines 113–162 (existing `SpeedPerMeter`, `MinSpeed`, `MaxSpeed`, `SpeedEasing` exports)

**Existing export pattern to copy** (lines 113–162):

```csharp
// FlightController.cs lines 113–162 — exact export + backing field pattern

private double _speedPerMeter = 0.5;
/// <summary>
/// Context-max speed multiplier per metre of nearest-surface distance (D-06).
/// contextMax = clamp(nearest * SpeedPerMeter, MinSpeed, MaxSpeed).
/// </summary>
[Export]
public double SpeedPerMeter
{
    get => _speedPerMeter;
    set => _speedPerMeter = System.Math.Max(0.0, value);
}

private double _minSpeed = 10.0;
/// <summary>Minimum context-max speed in m/s (never stops the throttle from doing something).</summary>
[Export]
public double MinSpeed
{
    get => _minSpeed;
    set => _minSpeed = System.Math.Max(0.0, value);
}

private double _maxSpeed = 2e20;
/// <summary>
/// Maximum context-max speed in m/s. No SpeedOfLight cap — the distance→speed curve
/// (D-06/07/08) decelerates naturally near bodies via RadiusMeters (D-36).
/// System.Math.Max(0.0, value) blocks negative and NaN inputs (T-03-04 mitigation).
/// </summary>
[Export]
public double MaxSpeed
{
    get => _maxSpeed;
    set => _maxSpeed = System.Math.Max(0.0, value);  // no SpeedOfLight cap (Plan 03-02)
}

private double _speedEasing = 1.0;
/// <summary>
/// Easing rate for the contextMax lerp (D-07). Higher = faster transition.
/// Absorbs SOI-boundary discontinuities so there is no visible speed snap (Pitfall 9).
/// </summary>
[Export]
public double SpeedEasing
{
    get => _speedEasing;
    set => _speedEasing = System.Math.Max(0.0, value);
}
```

**New exports to add (same pattern):**
- `_tierSpeedFactor` / `TierSpeedFactor` — `double`, default `1e-5`. Setter: `System.Math.Max(0.0, value)`.
- `_speedPerTarget` / `SpeedPerTarget` — `double`, default `0.1`. Setter: `System.Math.Max(0.0, value)`.

**Conventions to replicate:**
- Private underscore-prefixed backing field with default literal.
- `[Export]` on the public property (not the field).
- `System.Math.Max(0.0, value)` in setter — blocks negative/NaN (T-03-04 pattern).
- `/// <summary>` XML doc on the property explaining the formula and a concrete example value.
- Place in the `// ── Exports (tuning knobs) ──` block (line 45 section).

**Also add** a `private Hud.Hud _hud;` field in the `// ── Private references ──` block (line 188), initialised in `_Ready` (see Change Site 3 below).

---

### Change Site 3: `_hud` Node Reference Resolution in `FlightController._Ready`

**Role:** controller · config  
**Analog:** `Scripts/Flight/FlightController.cs` lines 213–228 (`_camera` and `_steeringReticle` resolution in `_Ready`)

**Exact `_camera` resolution pattern to mirror** (lines 221–228):

```csharp
// FlightController.cs lines 221–228

// Resolve camera
if (CameraPath != null && !CameraPath.IsEmpty)
    _camera = GetNode<Camera3D>(CameraPath);
else
    _camera = GetTree().Root.FindChild("Camera3D", true, false) as Camera3D;

// Resolve optional reticle node (added in Task 3)
_steeringReticle = GetTree().Root.FindChild("SteeringReticle", true, false) as Control;
```

**New `_hud` resolution to add (same pattern):**

```csharp
// Add after _steeringReticle resolution, same style:
_hud = GetTree().Root.FindChild("Hud", true, false) as Hud.Hud;
```

**Conventions to replicate:**
- `FindChild(name, recursive: true, owned: false)` — two-argument booleans always explicit.
- `as TypeName` null-safe cast (no hard crash if node absent — `_hud` stays null and callers use `?.`).
- No `[Export] NodePath` needed for `_hud` unless the scene structure becomes ambiguous (match how `_steeringReticle` is resolved — no export path, just `FindChild`).

---

### Change Site 4: `Hud.ActiveTargetIndex` Read-Only Property

**Role:** HUD · request-response  
**Analog:** `Scripts/Hud/Hud.cs` lines 64–78 (`_targetIndex` field), lines 333–354 (`BuildTargetableList`), lines 295–317 (`_Input` cycle_target using `BuildTargetableList`)

**`_targetIndex` field and `BuildTargetableList` — existing pattern** (lines 64–78, 333–354):

```csharp
// Hud.cs lines 64–78
private readonly struct TargetEntry
{
    public readonly int Index;
    public TargetEntry(int index) { Index = index; }
}

/// <summary>Current index into the targetable body list (parent + siblings).</summary>
private int _targetIndex = 0;
```

```csharp
// Hud.cs lines 333–354
private System.Collections.Generic.List<TargetEntry> BuildTargetableList(
    int parentIdx, int shipIndex,
    System.Collections.Generic.List<UniObject> gameObjects)
{
    var result = new System.Collections.Generic.List<TargetEntry>();
    if ((uint)parentIdx >= (uint)(gameObjects?.Count ?? 0)) return result;
    var parent = gameObjects[parentIdx];
    if (parent == null) return result;

    // Parent body is always targetable (it is visible as the SOI origin).
    result.Add(new TargetEntry(parentIdx));

    // Siblings: other children of parent, excluding the ship itself.
    foreach (int idx in parent.ChildIndices)
    {
        if (idx == shipIndex) continue;
        if ((uint)idx >= (uint)gameObjects.Count) continue;
        if (gameObjects[idx] == null) continue;
        result.Add(new TargetEntry(idx));
    }
    return result;
}
```

**New `ActiveTargetIndex` property to add (same class — `_Ready` access pattern):**

```csharp
// Add in the // ── Read-only accessors ── section (new section after _targetIndex field)
/// <summary>
/// Index into GameObjects of the active target, or -1 if none.
/// Read-only — never mutates _targetIndex or any sim state.
/// </summary>
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

**Conventions to replicate:**
- `(uint)idx >= (uint)(objs?.Count ?? 0)` index-safety pattern throughout.
- `BuildTargetableList` remains `private` — the property calls it from within the same class (no visibility change needed; A4 assumption confirmed safe).
- Return sentinel `-1` for "no target" — matches `AddGameObject()` returns `-1` on failure convention (CLAUDE.md §Function Design).
- `/// <summary>` XML doc stating the read-only contract explicitly.
- Place property in the `// ── Target cycling state ──` section alongside `_targetIndex`.

---

### Change Site 5: Target Circle in `Hud._Draw`

**Role:** HUD / overlay · event-driven  
**Analog A:** `Scripts/Hud/Hud.cs` lines 226–286 (`UpdateDirectionMarker` — camera-projection pattern)  
**Analog B:** `Scripts/Render/WorldRenderer.cs` lines 327–332 (`GetRenderPosition` — render-set gate)

**Analog A — Camera projection pattern** (lines 226–286):

```csharp
// Hud.cs lines 226–286
private void UpdateDirectionMarker(Double3 relD)
{
    if (_dirMarker == null || _camera == null) return;

    // Convert ship-relative metres to a Godot Vector3 for projection.
    var relVec = new Vector3((float)relD.X, (float)relD.Y, (float)relD.Z);

    var viewport = GetViewport();
    if (viewport == null) { HideMarker(); return; }
    Vector2 vpSize = viewport.GetVisibleRect().Size;

    // Camera frustum check — transform to camera-local space
    Vector3 cameraLocal = _camera.GlobalTransform.AffineInverse() * relVec;

    // If behind camera (cameraLocal.Z > 0 in Godot's -Z-forward convention)
    bool isBehindCamera = cameraLocal.Z > 0;
    Vector2 screenPos = _camera.UnprojectPosition(_camera.GlobalPosition + relVec);

    bool isOffScreen = isBehindCamera ||
                       screenPos.X < 0 || screenPos.X > vpSize.X ||
                       screenPos.Y < 0 || screenPos.Y > vpSize.Y;

    if (!isOffScreen)
    {
        HideMarker();
        return;
    }
    // ... edge-pin logic follows ...
}
```

**Key projection calls to reuse in `_Draw`:**
- Behind-camera guard: `_camera.GlobalTransform.AffineInverse() * relVec`, then `cameraLocal.Z > 0` (line 240–244).
- `_camera.UnprojectPosition(_camera.GlobalPosition + relVec)` — passes a **global** position (line 245).
- `viewport.GetVisibleRect().Size` for bounds check (line 236).

**Analog B — WorldRenderer.GetRenderPosition gate** (lines 327–332):

```csharp
// WorldRenderer.cs lines 327–332
public bool GetRenderPosition(int bodyIdx, out Vector3 pos)
{
    if (_lastRenderPositions.TryGetValue(bodyIdx, out pos)) return true;
    pos = default;
    return false;
}
```

The returned `pos` is in WorldRenderer's local render space. Convert to global: `_worldRenderer.GlobalPosition + pos` before passing to `_camera.UnprojectPosition`.

**New code to add to Hud:**

Three new private fields in `Hud` (after the `_dirMarker` field):
```csharp
// After existing private reference fields:
private Render.WorldRenderer _worldRenderer;
private bool    _showTargetCircle;
private Vector2 _targetCirclePos;
private float   _targetCircleRadius;
```

`_worldRenderer` resolved in `_Ready`, mirroring the `_camera` resolution pattern (lines 97–100):
```csharp
// Hud._Ready — after _camera resolution (lines 97–100):
if (CameraPath != null && !CameraPath.IsEmpty)
    _camera = GetNode<Camera3D>(CameraPath);
else
    _camera = GetTree().Root.FindChild("Camera3D", true, false) as Camera3D;

// NEW — same FindChild pattern:
_worldRenderer = GetTree().Root.FindChild("WorldRenderer", true, false) as Render.WorldRenderer;
```

`_Draw` override — place after `HideMarker()`:
```csharp
public override void _Draw()
{
    if (!_showTargetCircle) return;
    DrawArc(_targetCirclePos, _targetCircleRadius, 0f, Mathf.Tau, 32, PhosphorGreen, 1.5f);
}
```

`UpdateTargetCircle` helper called from `_Process` (after `UpdateTargetReadout`):
```csharp
// Call at end of _Process (after UpdateTargetReadout), then:
QueueRedraw();  // triggers _Draw each frame — required (Pitfall 4)
```

**Conventions to replicate:**
- `_showTargetCircle` bool flag: set false at the top of `UpdateTargetCircle`, set true only after all guards pass — mirrors `HideMarker()` / `_dirMarker.Visible = false` pattern (line 253, 289–291).
- Off-screen guard (same as `isOffScreen` in `UpdateDirectionMarker`): skip circle, let edge marker handle (HideMarker is already called by `UpdateTargetReadout` → `UpdateDirectionMarker`).
- `PhosphorGreen` color reused from the existing export (line 49) — no new color constant.
- Min/max radius floor: `Mathf.Clamp(computedRadius, MIN_R, MAX_R)` with `MIN_R = 20f` as a play-test knob (`[Export]` if needed, or a `private const float`).
- `DrawArc` not `DrawCircle` — unfilled circle (outline only, matches retro aesthetic).

---

## Shared Patterns

### Index Safety (All Change Sites)

**Source:** `Scripts/Flight/FlightController.cs` lines 386–390 and `Scripts/Hud/Hud.cs` lines 130–131  
**Apply to:** Every `gameObjects[i]` access in new or modified code

```csharp
// Canonical form — two equivalent expressions used in codebase:
var ship = (uint)shipIndex < (uint)gameObjects.Count ? gameObjects[shipIndex] : null;
// OR for early return:
if ((uint)shipIndex >= (uint)(gameObjects?.Count ?? 0)) return;
```

### Distance Math (UniMath vs UniVec3)

**Source:** CLAUDE.md §"Position Math (UniVec3 / UniMath)"; verified usage in `FlightController.cs` line 424 and `Hud.cs` line 172

**Apply to:** All distance calls in modified code

| Situation | Method | Why |
|-----------|--------|-----|
| Ship to sibling (same parent frame) | `UniVec3.Distance(ship.LocalPos, body.LocalPos)` | Same frame — safe and cheaper |
| Ship to target (may be different frame) | `UniMath.Distance(ship, target, gameObjects)` | LCA path — required by CLAUDE.md |
| Ship to target (metres vector needed) | `UniMath.RelativeMetres(ship, target, gameObjects)` | Returns `Double3`; call `.Magnitude()` for scalar |

**Never:** raw `ship.LocalPos.ToDouble3().Magnitude()` for cross-frame distances.

### `[Export]` Property Pattern

**Source:** `Scripts/Flight/FlightController.cs` lines 113–162  
**Apply to:** All new tuning knobs (`TierSpeedFactor`, `SpeedPerTarget`, optional circle radius exports)

- Private `_camelCase` backing field with default literal.
- `[Export]` on the public PascalCase property (not the field).
- Setter: `System.Math.Max(0.0, value)` for doubles; `Mathf.Max(...)` for floats.
- XML `/// <summary>` doc with formula and example numeric value.

### Node Reference Resolution in `_Ready`

**Source:** `Scripts/Hud/Hud.cs` lines 82–101; `Scripts/Flight/FlightController.cs` lines 213–228  
**Apply to:** `_hud` in `FlightController._Ready`; `_worldRenderer` in `Hud._Ready`

```csharp
// Pattern: check NodePath export first, fall back to FindChild
if (SomePath != null && !SomePath.IsEmpty)
    _ref = GetNode<SomeType>(SomePath);
else
    _ref = GetTree().Root.FindChild("NodeName", true, false) as SomeType;
```

---

## No Analog Found

None — all change sites have exact in-file analogs. No new files are created.

---

## Metadata

**Analog search scope:** `Scripts/Flight/FlightController.cs`, `Scripts/Hud/Hud.cs`, `Scripts/Render/WorldRenderer.cs`  
**Files read:** 3 source files + CONTEXT.md + RESEARCH.md  
**Pattern extraction date:** 2026-06-17
