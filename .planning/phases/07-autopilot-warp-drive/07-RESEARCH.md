# Phase 7: Autopilot & Warp Drive — Research

**Researched:** 2026-06-22
**Domain:** Godot 4.6 C#, FlightController state machine, Basis interpolation, Control/UI panels, InputMap
**Confidence:** MEDIUM (codebase verified by direct read; Godot API claims ASSUMED from training/WebSearch)

---

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions

- **D-01:** Autopilot reuses `Hud.ActiveTargetIndex` as warp destination. No separate warp-destination UI.
- **D-02:** J key (`warp_engage` InputMap action) opens warp confirmation screen. No-op if `ActiveTargetIndex < 0`.
- **D-03:** On warp engage, ship auto-orients toward `UniMath.RelativeMetres(ship, target, objs)` normalized, lerped into `_shipBasis` each frame.
- **D-04:** Warp confirmation screen sets `FlightController.IsPanelOpen = true` while open.
- **D-05:** Travel time is in-game configurable (slider or numeric input, minutes). Default: 2 minutes.
- **D-06:** Warp speed computed per-frame as `warpSpeed = UniMath.Distance(ship, target, objs) / selectedTravelTime`. Natural deceleration as distance closes.
- **D-07:** `WarpMaxSpeed` editor export (default 2e20 m/s) caps computed warp speed. Technical safety cap only.
- **D-08:** Warp auto-disengages when `UniMath.Distance(ship, target, objs) < target.SOIMeters`. Speed eases down to `ManualMaxSpeed` via `_speedEasing`.
- **D-09:** `ManualMaxSpeed` = 1e6 m/s (1,000 km/s). Editor export. All manual throttle clamped to this.
- **D-10:** Phase-4 tier ceiling (D-40) and proximity damp (D-42) continue to apply for autopilot speed envelope. Manual flight ignores them — manual speed = `min(throttle01 × contextMax, ManualMaxSpeed)`.
- **D-11:** Left Alt hold activates look-around in both normal flight and warp. New `look_around` InputMap action bound to `Key.Alt`.
- **D-12:** While look-around active: mouse motion drives `_cameraOffset`, NOT `_shipBasis`. Ship holds heading; throttle works; steering accumulation suspended.
- **D-13:** On Left Alt release, `_cameraOffset` lerps back to identity over ~0.3 s. During warp, eases back to face warp direction.
- **D-14:** During warp, look-around always available. Ship stays on autopilot rails.
- **D-15:** Screen shows: target name, estimated distance, travel time input (slider + numeric minutes), computed warp speed, Enter to engage / Esc or J to cancel.
- **D-16:** Screen is a new Godot `Control` node (similar to `TargetSelectorPanel`) parented to CanvasLayer in Main.tscn. Read-only consumer of world state. D-53 from Phase 6 preserved.
- **D-17:** Selected travel time stored as runtime field. Defaults to 120 s (2 minutes) each session.
- **D-18:** During warp: ship on rails, throttle input ignored, steering cursor accumulation suspended. Only active input: look-around.
- **D-19:** On warp disengage (SOI arrival or player cancel), speed eases to `ManualMaxSpeed` via `_speedEasing` lerp.

### Claude's Discretion

- Warp confirmation screen layout and exact slider UX (position, width, step size) — retro-consistent style matching Phase-6 `TargetSelectorPanel` (phosphor green, monospace font).
- `look_around` InputMap action added to `project.godot` alongside `warp_engage`.
- Warp speed display: m/s in scientific notation (consistent with HUD speed display conventions).

### Deferred Ideas (OUT OF SCOPE)

- Body collision avoidance (dedicated future phase).
- Travel cost mechanic (future phase reads `selectedTravelTime`).
- Warp Visual FX (Phase 999.4 backlog — depends on Phase 7 warp state machine).

</user_constraints>

---

## Summary

Phase 7 adds two tightly coupled capabilities to the existing FlightController/Hud stack: a manual speed cap (ManualMaxSpeed = 1e6 m/s) that limits all non-warp thrust, and a warp-drive state machine that takes the ship on autopilot rails to the Phase-6 ActiveTargetIndex destination.

The most structurally significant change is splitting FlightController's single flight loop into two paths: the **manual path** (existing `UpdateSpeedEnvelope` + `UpdateAttitude` capped at ManualMaxSpeed) and the **warp path** (per-frame UniMath.Distance-based speed, auto-orient toward target, SOI transitions handled automatically by existing GameWorld.TranslatePos). The existing IsPanelOpen gate, _speedEasing lerp, and SOI transition machinery (GameWorld.TrySpaceTransition) all carry over without modification.

The look-around camera adds a `_cameraOffset` Basis field to FlightController. When Left Alt is held, mouse motion accumulates into `_cameraOffset` instead of `_shipBasis`. On release, `_cameraOffset` lerps back to identity using `Basis.Slerp`. The WarpConfirmationScreen follows the TargetSelectorPanel pattern exactly: a Control node on the CanvasLayer that sets IsPanelOpen=true, builds a VBoxContainer with Labels and an HSlider, handles Enter/Esc/J in `_UnhandledInput`.

**Primary recommendation:** Implement as three waves — (1) ManualMaxSpeed cap + warp state machine in FlightController + InputMap additions, (2) WarpConfirmationScreen UI node + Main.tscn wiring, (3) look-around camera decoupling. This order keeps each wave independently testable.

---

## Architectural Responsibility Map

| Capability | Primary Tier | Secondary Tier | Rationale |
|------------|-------------|----------------|-----------|
| Warp state machine (Manual/Confirming/Warping) | FlightController (Node) | — | Flight logic lives in FlightController; warp is a flight mode, not HUD state |
| Travel time input (slider) | WarpConfirmationScreen (Control) | FlightController (reads value) | UI owns user input; controller owns the physics value |
| Warp speed computation (distance/time) | FlightController._WarpProcess | — | Per-frame speed derived from UniMath.Distance — belongs in the flight tick |
| SOI boundary traversal during warp | GameWorld.TranslatePos (existing) | — | Already automatic: TranslatePos calls TrySpaceTransition; warp just calls it |
| Look-around camera offset | FlightController.UpdateAttitude | — | Camera ownership is already in UpdateAttitude; offset is additive to existing |
| Warp auto-orient toward target | FlightController._WarpProcess | — | Ship heading mutation belongs in FlightController, not HUD |
| InputMap action registration | project.godot [input] section | — | Permanent actions registered at project level, not runtime |
| Warp confirmation panel UI | WarpConfirmationScreen (Control) | — | Follows TargetSelectorPanel pattern; read-only consumer |

---

## Standard Stack

### Core (no new packages — Godot 4.6 Mono existing stack only)

| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| Godot 4.6 C# | 4.6.2 | Game loop, Node lifecycle, Input, Control/UI | Existing engine; all Phase 7 work is within this stack |
| System.Math | .NET 8.0 | Double arithmetic (warp speed clamp, IsFinite guard) | Already used in FlightController |

**No new packages required.** Phase 7 is pure engine-code and scene-wiring work — no NuGet packages, no asset library additions.

### Package Legitimacy Audit

> No external packages are being installed in this phase. Audit not applicable.

---

## Architecture Patterns

### System Architecture Diagram

```
[Player Input]
    │
    ├─ Left Alt held ──────────────────────────→ _cameraOffset accumulates mouse delta
    │                                             (NOT _shipBasis)
    │
    ├─ J key (warp_engage) ────────────────────→ WarpConfirmationScreen.OpenPanel()
    │                                             → FlightController.IsPanelOpen = true
    │
    └─ Normal mouse/WASD ──────────────────────→ HandleThrottleInput / UpdateAttitude
                                                  (when state == Manual)

[FlightController._Process]
    │
    ├─ state == Manual ────────────────────────→ HandleThrottleInput
    │                                           → UpdateAttitude (_shipBasis only)
    │                                           → UpdateSpeedEnvelope (capped ManualMaxSpeed)
    │                                           → ApplyMotion
    │
    ├─ state == Confirming ────────────────────→ IsPanelOpen=true blocks all flight
    │                                           (panel handles Enter/Esc/J)
    │
    └─ state == Warping ───────────────────────→ _WarpProcess:
                                                    dist = UniMath.Distance(ship, target, objs)
                                                    speed = Clamp(dist / travelTime, 0, WarpMaxSpeed)
                                                    auto-orient: _shipBasis Slerp toward target dir
                                                    ApplyMotion (using warp speed)
                                                    check SOI arrival → disengage

[GameWorld.TranslatePos] ← called by ApplyMotion every frame (manual AND warp)
    │
    └─ TrySpaceTransition() — automatic SOI boundary handling (EXISTING, no change)
```

### Recommended Project Structure

```
Scripts/
├── Flight/
│   └── FlightController.cs   ← ADD: WarpState enum, ManualMaxSpeed export,
│                                      WarpMaxSpeed export, _cameraOffset field,
│                                      _warpState field, _selectedTravelTime field,
│                                      _WarpProcess(), look-around in UpdateAttitude
└── Hud/
    └── WarpConfirmationScreen.cs   ← NEW: follows TargetSelectorPanel pattern

project.godot [input] section ← ADD: warp_engage (J), look_around (Left Alt)
Main.tscn ← ADD: WarpConfirmationScreen node under CanvasLayer
```

### Pattern 1: Warp State Machine (Enum-Based)

**What:** Three-state enum in FlightController routing `_Process` behavior.
**When to use:** 3 states, no reuse across scenes — enum + switch is the right weight.

```csharp
// Source: established Godot C# pattern; verified in TargetSelectorPanel/FlightController code [ASSUMED]
private enum WarpState { Manual, Confirming, Warping }
private WarpState _warpState = WarpState.Manual;

public override void _Process(double delta)
{
    if (_world == null || delta <= 0.0) return;
    if (IsPanelOpen && _warpState == WarpState.Manual) return;  // panel gate (existing)

    switch (_warpState)
    {
        case WarpState.Manual:
            HandleThrottleInput();
            UpdateAttitude(delta);
            UpdateSpeedEnvelope(delta);   // now also clamps to ManualMaxSpeed
            ApplyMotion(delta);
            UpdateReticlePosition();
            break;

        case WarpState.Confirming:
            // IsPanelOpen=true; WarpConfirmationScreen handles input
            break;

        case WarpState.Warping:
            UpdateLookAround(delta);   // look-around always active in warp (D-14)
            _WarpProcess(delta);
            ApplyMotion(delta);        // uses _easedSpeed set by _WarpProcess
            break;
    }
}
```

### Pattern 2: ManualMaxSpeed Clamp in UpdateSpeedEnvelope

**What:** After the existing `targetMax` computation, apply a hard cap for manual mode.
**When to use:** Manual path only. Autopilot path (warp) bypasses this cap.

```csharp
// Source: FlightController.cs UpdateSpeedEnvelope (VERIFIED: direct file read)
// ADD after the existing targetEaseMax block, before the easing lerps:
if (_warpState == WarpState.Manual)
{
    // D-09: clamp manual contextMax to ManualMaxSpeed regardless of tier/target.
    // D-10: tier ceiling (D-40) still governs _contextMax before this clamp.
    targetMax = System.Math.Min(targetMax, _manualMaxSpeed);
}

// The existing easing lerps run unchanged on every path (D-07 invariant preserved).
_contextMax = Mathf.Lerp(_contextMax, targetMax, Mathf.Clamp(_speedEasing * delta, 0.0, 1.0));
```

### Pattern 3: Warp Per-Frame Process

**What:** Computes distance-based warp speed, auto-orients ship, checks SOI arrival.
**When to use:** Called only when `_warpState == WarpState.Warping`.

```csharp
// Source: [ASSUMED] — derived from D-06/D-07/D-08 decisions + UniMath.Distance signature
private void _WarpProcess(double delta)
{
    var gameObjects = _world?.GameObjects;
    if (gameObjects == null) return;

    int shipIdx = _world.ShipIndex;
    var ship = (uint)shipIdx < (uint)gameObjects.Count ? gameObjects[shipIdx] : null;
    int tgtIdx = _hud?.ActiveTargetIndex ?? -1;
    var target = (tgtIdx >= 0 && (uint)tgtIdx < (uint)gameObjects.Count) ? gameObjects[tgtIdx] : null;
    if (ship == null || target == null) { DisengageWarp(); return; }

    // D-06: warp speed = remaining distance / selected travel time
    double dist = UniMath.Distance(ship, target, gameObjects);

    // D-08: auto-disengage on SOI arrival
    if (dist < target.SOIMeters) { DisengageWarp(); return; }

    double warpSpeed = System.Math.Min(dist / _selectedTravelTime, _warpMaxSpeed);

    // D-03: auto-orient toward target — smooth Basis Slerp each frame
    Double3 relMetres = UniMath.RelativeMetres(ship, target, gameObjects);
    if (relMetres.Magnitude() > 1e-3)
    {
        var relVec = new Vector3((float)relMetres.X, (float)relMetres.Y, (float)relMetres.Z);
        Vector3 forward = -relVec.Normalized();   // Godot -Z is forward
        // Build target basis from forward direction
        var targetBasis = new Basis(Quaternion.FromEuler(Vector3.Zero));
        // Align -Z to forward: use LookingAt pattern or manual basis construction
        // Slerp _shipBasis toward the target orientation
        var targetQuat = Quaternion.FromEuler(Vector3.Zero);  // placeholder — see Pitfall 4
        // NOTE: see "Pattern 4: Warp Auto-Orient" below for the correct implementation
    }

    // Write warpSpeed into _easedSpeed directly (bypasses throttle, D-18)
    _easedSpeed = warpSpeed;
    CurrentSpeed = _easedSpeed;
}
```

### Pattern 4: Warp Auto-Orient (Basis Slerp)

**What:** Smooth ship orientation toward warp target direction each frame.
**When to use:** During `_WarpProcess` (D-03).

```csharp
// Source: Godot docs Basis.Slerp / Quaternion.Slerp [ASSUMED: WebSearch]
// The cleanest approach for forward-vector alignment in Godot C#:

// 1. Compute the desired forward direction in world space (already computed as relVec.Normalized()).
//    In Godot, -_shipBasis.Z is ship forward. We want that to point at the target.
Vector3 targetForward = (-relVec).Normalized();   // direction FROM ship TO target = warp forward
// 2. Compute an up vector (use _shipBasis.Y to keep local roll stable during warp)
Vector3 currentUp = _shipBasis.Y;
// 3. Build the target Basis using LookingAt — available on Transform3D, not Basis directly
//    Pattern: build a Transform3D with ship as origin, use LookAt to get target orientation
//    Then extract Basis. In Godot 4 C#: Transform3D.LookingAt(target, up).Basis
//    But we have no Node3D position, only the basis. The reliable pattern is:
//    Quaternion approach:
var currentQuat = new Quaternion(_shipBasis).Normalized();
// Build the desired basis: -Z axis = targetForward, Y axis = currentUp (orthogonalized)
Vector3 right = currentUp.Cross(targetForward).Normalized();
if (right.LengthSquared() < 1e-6f) right = _shipBasis.X;   // degenerate guard
Vector3 up    = targetForward.Cross(right).Normalized();
var desiredBasis = new Basis(right, up, -targetForward).Orthonormalized();
var desiredQuat  = new Quaternion(desiredBasis).Normalized();

float slerpWeight = Mathf.Clamp((float)(_warpOrientRate * delta), 0f, 1f);
var lerpedQuat = currentQuat.Slerp(desiredQuat, slerpWeight);
_shipBasis = new Basis(lerpedQuat).Orthonormalized();
```

### Pattern 5: Look-Around Camera Decoupling

**What:** When `look_around` is held, accumulate mouse delta into `_cameraOffset` instead of `_shipBasis`. Apply combined result to `_camera.Basis`.
**When to use:** In `UpdateAttitude` when `Input.IsActionPressed("look_around")`.

```csharp
// Source: [ASSUMED] — derived from existing UpdateAttitude pattern in FlightController.cs
// New field:
private Basis _cameraOffset = Basis.Identity;
private float _lookEaseRate = 1f / 0.3f;   // ease back in ~0.3 s (D-13)

private void UpdateAttitude(double delta)
{
    bool isLookAround = Input.IsActionPressed("look_around");

    if (!isLookAround)
    {
        // Ease _cameraOffset back to Identity (D-13)
        // Basis does not have a direct Slerp-to-identity; use Quaternion:
        var offsetQuat = new Quaternion(_cameraOffset).Normalized();
        var identQuat  = Quaternion.Identity;
        float t = Mathf.Clamp(_lookEaseRate * (float)delta, 0f, 1f);
        _cameraOffset = new Basis(offsetQuat.Slerp(identQuat, t)).Orthonormalized();

        // Normal steering accumulates into _shipBasis (existing path, D-02)
        // ... [existing rotation code] ...
    }
    else
    {
        // Look-around: mouse delta accumulates into _cameraOffset only.
        // _shipBasis is NOT updated (ship holds heading, D-12).
        // Steering cursor does not accumulate (suspend steer input).
        var pitchBasis = new Basis(Vector3.Right, pitch);
        var yawBasis   = new Basis(Vector3.Up,    yaw);
        _cameraOffset = (_cameraOffset * pitchBasis * yawBasis).Orthonormalized();
        // NOTE: _cursor is not reset here — it stays accumulated for when look-around releases
    }

    // Apply combined orientation to camera
    if (_camera != null)
        _camera.Basis = (_shipBasis * _cameraOffset).Orthonormalized();
}
```

### Pattern 6: WarpConfirmationScreen (follows TargetSelectorPanel)

**What:** A Control node parented under CanvasLayer. Sets IsPanelOpen, shows travel-time slider, engages warp on Enter.
**When to use:** Opened by J key when ActiveTargetIndex >= 0.

```csharp
// Source: TargetSelectorPanel.cs [VERIFIED: direct file read] — structural template
public partial class WarpConfirmationScreen : Control
{
    // NodePath exports same pattern as TargetSelectorPanel
    [Export] public NodePath WorldPath { get; set; }
    [Export] public NodePath HudPath { get; set; }
    [Export] public NodePath FlightPath { get; set; }
    [Export] public Color PhosphorGreen { get; set; } = new Color(0.1f, 1.0f, 0.3f);

    private TestSetup _world;
    private Hud.Hud _hud;
    private Flight.FlightController _flight;
    private VBoxContainer _vbox;
    private Label _targetLabel, _distLabel, _speedLabel;
    private HSlider _timeSlider;  // [ASSUMED: Godot HSlider API]
    private Label _timeLabel;

    // Runtime state — not persisted (D-17)
    private double _selectedTravelTimeSec = 120.0;   // default 2 min

    public override void _Ready()
    {
        // ... resolve _world, _hud, _flight same as TargetSelectorPanel ...
        _vbox = new VBoxContainer();
        AddChild(_vbox);
        Visible = false;
        MouseFilter = MouseFilterEnum.Ignore;
    }

    private void OpenPanel()
    {
        Visible = true;
        MouseFilter = MouseFilterEnum.Stop;
        Input.MouseMode = Input.MouseModeEnum.Visible;
        if (_flight != null) _flight.IsPanelOpen = true;
        RefreshDisplay();
    }

    private void ClosePanel()
    {
        Visible = false;
        MouseFilter = MouseFilterEnum.Ignore;
        Input.MouseMode = Input.MouseModeEnum.Captured;
        if (_flight != null) _flight.IsPanelOpen = false;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsActionPressed("warp_engage"))
        {
            if (Visible)
            {
                ClosePanel();
                // IsPanelOpen is now false; FlightController reads _selectedTravelTimeSec and begins warp
            }
            else
            {
                // Only open if there is an active target (D-02)
                if (_hud?.ActiveTargetIndex >= 0)
                    OpenPanel();
            }
            GetViewport().SetInputAsHandled();
            return;
        }

        if (!Visible) return;

        if (@event.IsActionPressed("ui_cancel"))  // Esc
        {
            ClosePanel();
            GetViewport().SetInputAsHandled();
        }
        else if (@event.IsActionPressed("ui_accept"))  // Enter
        {
            ClosePanel();
            // FlightController transitions to Warping state
        }
    }
}
```

### Anti-Patterns to Avoid

- **Calling UniVec3.Distance or raw ToDouble3() for warp distance:** Distance must route through `UniMath.Distance(ship, target, objs)` — the LCA path. At intergalactic scale, direct cross-frame subtraction produces catastrophic cancellation (CLAUDE.md §Position Math).
- **Computing warpSpeed with remainingTime instead of currentFrameDistance:** Use `dist / selectedTravelTime` where `dist` is recomputed each frame. Never pre-compute a fixed speed at engage time — the speed must track the shrinking distance to produce the deceleration curve (D-06).
- **Setting _shipBasis from look-around mouse delta directly:** Mouse delta during look-around must go into `_cameraOffset`, not `_shipBasis`. If _shipBasis is mutated, the ship changes heading instead of just the view (D-12).
- **Not resetting _cursor when entering warp:** During warp, steering input is suspended (D-18). If `_cursor` is not zeroed on warp entry, the accumulated delta causes the ship to rotate immediately on warp disengage.
- **Hard-stopping speed on warp disengage:** Speed must ease to `ManualMaxSpeed` via `_speedEasing` lerp (D-19). A hard stop causes a jarring snap visible on the HUD and in motion.
- **Using Input.IsKeyPressed(Key.Alt) instead of InputMap "look_around":** Always use the InputMap action to respect rebinding and handle platform differences.

---

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| SOI boundary traversal during warp | Custom SOI crossing logic | `GameWorld.TranslatePos` (existing) | Already calls `TrySpaceTransition` — warp just moves the ship forward each frame; transitions happen automatically |
| Smooth basis interpolation | Linear Basis component lerp | `Quaternion.Slerp` → `new Basis(q)` | Linear lerp on Basis columns produces non-orthogonal results; Quaternion Slerp is the correct spherical interpolation |
| UI layout in code | Manual rect positioning | `VBoxContainer` + `Label` + `HSlider` | TargetSelectorPanel already proves VBoxContainer pattern works; consistent with existing Hud code style |
| Cross-frame warp distance | `UniVec3.Distance` or absolute-from-root metres | `UniMath.Distance(ship, target, objs)` | LCA path only; at ~2.4e22 m intergalactic distance, float cancellation destroys precision otherwise |

**Key insight:** The existing GameWorld/FlightController/UniMath stack already handles everything the warp engine needs: TranslatePos moves the ship (and triggers SOI transitions), UniMath.Distance measures the remaining gap, Hud.ActiveTargetIndex gives the destination. Warp is an overlay on the existing motion system, not a replacement.

---

## Common Pitfalls

### Pitfall 1: `selectedTravelTime` set once at engage time, not tracked as remaining time

**What goes wrong:** If `_selectedTravelTime` is decremented each frame (as a countdown), the formula `dist / remainingTime` blows up as `remainingTime → 0` even though the ship is still far from the target. Speed goes to infinity.

**Why it happens:** Confusing "user-selected travel time" with "time elapsed since engage."

**How to avoid:** `_selectedTravelTime` is a constant per-warp-session (the user's setting, stored at engage). Do NOT decrement it. The natural deceleration comes from `dist` shrinking, not `time` shrinking. The formula is: `warpSpeed = dist / _selectedTravelTime`.

**Warning signs:** HUD shows speed increasing as ship approaches target (should always decrease).

### Pitfall 2: `_cameraOffset` not orthonormalized, accumulates skew

**What goes wrong:** After many frames of look-around input, `_cameraOffset` drifts from orthonormal, causing camera visual corruption (shear/scale artifacts).

**Why it happens:** Floating-point accumulation in Basis multiply — same issue as `_shipBasis` without Orthonormalized().

**How to avoid:** Call `.Orthonormalized()` on `_cameraOffset` every frame it is modified, exactly as the existing code does for `_shipBasis`.

### Pitfall 3: IsPanelOpen gate blocks warp orientation update during warp

**What goes wrong:** In D-04, the warp confirmation screen sets `IsPanelOpen = true`. The existing `_Process` guard `if (IsPanelOpen) return;` then blocks the warp orientation update every frame of confirmation.

**Why it happens:** The current `IsPanelOpen` guard was added for the TargetSelectorPanel case where all flight must freeze. Warp confirmation also needs this (correct). But during `Warping` state, IsPanelOpen should be false — the panel has closed. Ensure `ClosePanel()` sets `IsPanelOpen = false` before `FlightController` transitions to `WarpState.Warping`.

**How to avoid:** State machine transition is: panel opens (IsPanelOpen=true, WarpState=Confirming) → player presses Enter → panel closes (IsPanelOpen=false) → FlightController observes panel close + pending warp engage → sets WarpState=Warping. These happen in sequence in the same frame.

**Warning signs:** Ship enters warp state but does not move (speed stays 0); `_WarpProcess` never runs.

### Pitfall 4: Degenerate forward vector when ship is already aligned with target

**What goes wrong:** `relVec.Normalized()` returns a near-zero vector (NaN) when the ship is already nearly pointing at the target. The Quaternion.Slerp with a NaN quaternion corrupts `_shipBasis`.

**Why it happens:** When `relMetres.Magnitude() ≈ 0` OR when the ship is perfectly aligned, the cross product for the new basis axes is near-zero.

**How to avoid:** Guard with `if (relMetres.Magnitude() > 1e-3)` before computing the orient rotation (already noted in Pattern 4 above). Also guard the `right = currentUp.Cross(targetForward)` cross product — if its LengthSquared is < 1e-6, the ship is already aligned; skip the orientation update.

**Warning signs:** `_shipBasis` contains NaN after a few frames; ship freezes or teleports.

### Pitfall 5: Minimum warp distance — ship stalls just outside SOI radius

**What goes wrong:** With `warpSpeed = dist / _selectedTravelTime` and auto-disengage at `dist < target.SOIMeters`, if `target.SOIMeters` is very small (e.g. a small asteroid), the computed `warpSpeed` at disengage threshold becomes tiny but non-zero. The ship may oscillate around the SOI boundary if `_speedEasing` lerps too slowly.

**Why it happens:** The ease-in from 0 speed on warp engage, combined with the natural deceleration curve, can produce a very slow approach speed that takes many seconds to traverse the last few SOI radii.

**How to avoid:** The disengage condition is `dist < target.SOIMeters`, which triggers early enough. After disengage, `_speedEasing` lerp continues to apply motion at the eased speed — this is fine because `ManualMaxSpeed` is the lerp target and `_speedEasing` will eventually converge. No oscillation should occur because once the ship enters the SOI, warp stays disengaged (warp cannot re-engage automatically). Document the minimum `_selectedTravelTime` in the editor tooltip: very short times (< 10 s) for very close targets produce absurdly high warpSpeed values, hence the `WarpMaxSpeed` cap (D-07).

### Pitfall 6: `roll` input during look-around rotates ship instead of view

**What goes wrong:** If `roll_left`/`roll_right` keys are not suspended during look-around, they still rotate `_shipBasis` while the player expects to be looking around.

**Why it happens:** The look-around decision only reroutes mouse deltas; Q/E roll input still reaches `UpdateAttitude` and mutates `_shipBasis`.

**How to avoid:** During look-around, skip the roll computation (set `roll = 0`), or only skip _shipBasis mutation. The yaw/pitch steer components are already suspended (they go to `_cameraOffset`); roll must be suspended too for consistent feel.

---

## Code Examples

### How SOI Traversal Already Works (no new code needed)

```csharp
// Source: GameWorld.cs TranslatePos [VERIFIED: direct file read]
// During warp, FlightController.ApplyMotion calls:
_world.TranslatePos(_world.ShipIndex, motionDelta);

// Which calls (inside GameWorld):
private void TranslatePos(UniObject obj, Double3 delta)
{
    obj.LocalPos += delta;
    TrySpaceTransition(obj);   // ← automatic SOI boundary handling every frame
}
// TrySpaceTransition runs exit (upward) + entry (downward) in a loop, bounded by MaxIterations=32.
// Warp does not need to trigger transitions manually — they happen automatically.
```

### Existing IsPanelOpen Gate (reused for warp confirm screen)

```csharp
// Source: FlightController.cs lines 292-349, 342-349 [VERIFIED: direct file read]
// In _Input:
if (IsPanelOpen) return;   // suppresses mouse steering
// In _UnhandledInput:
if (IsPanelOpen) return;   // suppresses T-key toggle
// In _Process:
if (IsPanelOpen) return;   // suppresses all flight processing

// WarpConfirmationScreen.OpenPanel() sets _flight.IsPanelOpen = true
// WarpConfirmationScreen.ClosePanel() sets _flight.IsPanelOpen = false
// Same pattern as TargetSelectorPanel.OpenPanel()/ClosePanel() [VERIFIED: TargetSelectorPanel.cs]
```

### TargetSelectorPanel Panel Lifecycle (template for WarpConfirmationScreen)

```csharp
// Source: TargetSelectorPanel.cs [VERIFIED: direct file read]
private void OpenPanel()
{
    _treeLevel = 0; _expandedGalaxyIdx = -1; _expandedStarIdx = -1; _highlightRow = 0;
    SyncHighlightToActiveTarget();
    Visible = true;
    MouseFilter = MouseFilterEnum.Stop;
    Input.MouseMode = Input.MouseModeEnum.Visible;
    if (_flight != null) _flight.IsPanelOpen = true;
    RefreshList();
}

private void ClosePanel()
{
    Visible = false;
    MouseFilter = MouseFilterEnum.Ignore;
    Input.MouseMode = Input.MouseModeEnum.Captured;
    if (_flight != null) _flight.IsPanelOpen = false;
}
```

### UniMath.Distance for Cross-Frame Warp Distance

```csharp
// Source: UniMath.cs [VERIFIED: direct file read]
// The ONE safe way to measure remaining warp distance:
double dist = UniMath.Distance(ship, target, gameObjects);
// Internally: RelativeMetres → RelativePosition (LCA path) → ToDouble3() once on small delta
// Safe at intergalactic scale (~2.4e22 m) because it cancels in integer Units space.
```

### HSlider for Travel Time (warp confirmation screen)

```csharp
// Source: [ASSUMED: WebSearch Godot 4 HSlider docs]
var slider = new HSlider
{
    MinValue = 1.0,   // 1 minute minimum
    MaxValue = 60.0,  // 60 minutes maximum
    Step = 0.5,
    Value = 2.0       // 2-minute default (D-17)
};
slider.ValueChanged += (double value) =>
{
    _selectedTravelTimeSec = value * 60.0;   // slider in minutes, stored in seconds
    UpdateSpeedLabel();   // refresh computed warp speed display
};
_vbox.AddChild(slider);
```

---

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| Single global MaxSpeed (2e20 m/s) for manual+warp | ManualMaxSpeed (1e6 m/s) for manual; WarpMaxSpeed (2e20) for warp-only | Phase 7 | Manual flight is slow and precise; warp is the only FTL path |
| Tab key target cycle (retired in Phase 6) | TargetSelectorPanel tree selector (Phase 6, D-56) | Phase 6 | Phase 7 reuses this; no targeting UI change in Phase 7 |
| Phase-4 tier ceiling applies to all flight | Tier ceiling + proximity damp applies to autopilot only; manual ignores them | Phase 7 (D-10) | Simplifies the manual speed path |

**Deprecated in this phase:**
- The old `MaxSpeed` usage for manual flight: `MaxSpeed` is repurposed as `WarpMaxSpeed` (the autopilot safety cap). A new `ManualMaxSpeed` export replaces it for the manual path.

---

## Existing InputMap Actions (confirmed by project.godot read)

Current actions in project.godot [VERIFIED: direct file read]:
- `thrust_forward` → W (physical key 87)
- `thrust_back` → S (physical key 83)
- `roll_left` → Q (physical key 81)
- `roll_right` → E (physical key 69)
- `throttle_up` → W + ScrollUp
- `throttle_down` → S + ScrollDown
- `full_stop` → X (physical key 88)
- `toggle_target_panel` → Tab (physical key 4194306)
- `toggle_mouse_capture` → T (physical key 84)

**Phase 7 must add to project.godot:**
- `warp_engage` → J (physical key 74)
- `look_around` → Left Alt (physical key 4194326) [ASSUMED: Left Alt physical key code — verify in Godot editor]

**Conflict check:** J is not in use. Left Alt is not in use. No existing action uses these keys [VERIFIED: full project.godot [input] section read].

---

## Integration Points Enumerated

### FlightController.cs changes

1. **New exports:** `ManualMaxSpeed` (default 1e6), `WarpMaxSpeed` (default 2e20), `WarpOrientRate` (tuning knob for Slerp speed)
2. **New fields:** `_warpState`, `_cameraOffset`, `_selectedTravelTimeSec`
3. **`UpdateSpeedEnvelope` changes:** Add `ManualMaxSpeed` clamp after `targetEaseMax` block; only applies when `_warpState == WarpState.Manual`
4. **`UpdateAttitude` changes:** Add look-around branch; accumulate into `_cameraOffset` when `look_around` held; ease back to identity on release; write `_shipBasis * _cameraOffset` to camera
5. **New method `_WarpProcess`:** Distance/time speed, Slerp orient, SOI arrival check, disengage
6. **`_UnhandledInput` changes:** Add `warp_engage` handler — open WarpConfirmationScreen when `ActiveTargetIndex >= 0`
7. **`_Process` changes:** Switch on `_warpState`; warp path skips HandleThrottleInput/UpdateSpeedEnvelope and calls `_WarpProcess` instead
8. **Public API addition:** `public void EngageWarp(double travelTimeSec)` — called by WarpConfirmationScreen on Enter; sets `_selectedTravelTimeSec`, transitions to `WarpState.Warping`
9. **Public API addition:** `public void DisengageWarp()` — transitions to `WarpState.Manual`, begins speed ease-down

### Hud.cs changes

None required. `ActiveTargetIndex` already exposes the warp destination. `IsPanelOpen` is already a public setter on FlightController. The HUD speed display already calls `_flight.CurrentSpeed` which will show warp speed correctly.

The HUD context label may want to display "WARP" or similar when `_warpState == WarpState.Warping`, but this is cosmetic — the planner can address as a Claude's Discretion item.

### WarpConfirmationScreen.cs (new file)

Pattern: `Scripts/Hud/WarpConfirmationScreen.cs`, namespace `Hud`, mirrors `TargetSelectorPanel` structure. Reads `_hud.ActiveTargetIndex`, `UniMath.Distance` for the distance display, calls `_flight.EngageWarp(travelTimeSec)` on Enter.

### Main.tscn changes

1. Add `warp_engage` and `look_around` to project.godot [input] section
2. Add `WarpConfirmationScreen` node under `CanvasLayer` (sibling of `TargetSelectorPanel`)
3. Wire NodePath exports: `WorldPath`, `HudPath`, `FlightPath`

---

## Security Domain

> `security_enforcement` is enabled (absent = enabled per config inspection).

### Applicable ASVS Categories

| ASVS Category | Applies | Standard Control |
|---------------|---------|-----------------|
| V2 Authentication | No | Not applicable — no user accounts |
| V3 Session Management | No | Not applicable — no web sessions |
| V4 Access Control | No | Not applicable — single-player game |
| V5 Input Validation | Yes | Guard all FlightController inputs: `double.IsFinite(warpSpeed)` before applying; `Mathf.Clamp` on Slerp weight [ASSUMED] |
| V6 Cryptography | No | Not applicable |

### Known Threat Patterns for Game Input Processing

| Pattern | STRIDE | Standard Mitigation |
|---------|--------|---------------------|
| NaN/Infinity speed from distance=0 or time=0 | Tampering | `if (dist < 1.0 || _selectedTravelTimeSec <= 0) { DisengageWarp(); return; }` guard in `_WarpProcess` |
| Degenerate Basis from near-zero cross product in orient | Tampering | `if (cross.LengthSquared() < 1e-6f)` skip orient update guard |
| `_cameraOffset` skew accumulation | — | `.Orthonormalized()` every frame on mutation (existing FlightController pattern) |

---

## Environment Availability

> Phase 7 is pure code/scene changes within the existing Godot 4.6 project. No external tools, CLIs, databases, or services required beyond the existing Godot editor + .NET 8.0 SDK.

All confirmed available from prior phases (Godot 4.6.2, .NET 8.0 SDK, D3D12 GPU). No new dependencies.

---

## Assumptions Log

| # | Claim | Section | Risk if Wrong |
|---|-------|---------|---------------|
| A1 | `Basis.Slerp(Basis to, float weight)` exists as a direct method in Godot 4.6 C# | Code Examples (Pattern 4) | Fallback: use `Quaternion.Slerp` → `new Basis(q)` which is confirmed from Godot docs |
| A2 | Left Alt physical key code in Godot 4.6 is 4194326 | InputMap section | Verify in Godot Editor before adding to project.godot; wrong value silently produces no key binding |
| A3 | `HSlider` constructor accepts property initializer syntax in C# Godot bindings | Pattern 6 | Fallback: use separate property assignment lines after construction |
| A4 | `Range.ValueChanged` signal does NOT fire during `_Ready` initialization when `Value` is set in the initializer | Code Examples (HSlider) | Workaround: use a `_ready` flag guard in the callback |
| A5 | Tier ceiling (D-40) + proximity damp (D-42) continue to apply to the autopilot path unchanged | Architecture, Pattern 2 | Verified against 07-CONTEXT.md D-10: "the Phase-4 tier ceiling (D-40) and proximity damp (D-42) continue to apply for autopilot speed envelope" — no risk |
| A6 | `FlightController.IsPanelOpen` can safely remain false during warp (warp state machine handles input suppression directly) | Pattern 1 | If IsPanelOpen guard is broader than expected, warp _Process may be prematurely blocked — verify on play-test |

**If this table is empty:** Not the case — 6 assumptions logged above.

---

## Open Questions

1. **WarpConfirmationScreen communication back to FlightController**
   - What we know: The screen calls `ClosePanel()` on Enter, and FlightController needs to know warp was confirmed (not just cancelled).
   - What's unclear: Is a public `EngageWarp(travelTimeSec)` method on FlightController the right coupling, or should `WarpConfirmationScreen` write to a shared field?
   - Recommendation: `public void EngageWarp(double travelTimeSec)` on FlightController is the cleanest API — same pattern as the IsPanelOpen setter; no shared state required.

2. **Look-around cursor handling on Alt release**
   - What we know: `_cursor` accumulates mouse delta for steering. During look-around it should not accumulate.
   - What's unclear: Should `_cursor` be reset to zero on look-around entry (ship snaps attitude) or left in place (ship holds cursor-implied heading when look-around releases)?
   - Recommendation: Leave `_cursor` unchanged during look-around; the ship holds the same heading. On release, the accumulated `_cursor` continues from its pre-look-around value — no snap.

3. **Warp speed display format on confirmation screen**
   - What we know: Warp speed = distance / travelTime can be > 1e15 m/s at intergalactic range. `Hud.FormatSpeed` handles this with "ly/s" scale.
   - What's unclear: Whether to reuse `Hud.FormatSpeed` or emit raw scientific notation.
   - Recommendation: Call `Hud.FormatSpeed(warpSpeed)` — consistent with HUD conventions; no new format logic.

4. **HUD speed display during warp**
   - What we know: HUD reads `_flight.CurrentSpeed`, which Phase 7 sets to warp speed in `_WarpProcess`.
   - What's unclear: Whether to show warp speed in the same SPD label, or add a "WARP" prefix.
   - Recommendation: Cosmetic — planner's call. The data plumbing requires no change; the label text is optional polish.

---

## Sources

### Primary (VERIFIED — direct codebase read)
- `Scripts/Flight/FlightController.cs` — full speed envelope, IsPanelOpen gate, `_shipBasis`/`_camera`, UpdateAttitude, UpdateSpeedEnvelope, ApplyMotion
- `Scripts/Hud/Hud.cs` — `ActiveTargetIndex`, `SetTargetIndex`, `GetTargetCandidates`, FormatSpeed, FormatDistance
- `Scripts/Hud/TargetSelectorPanel.cs` — OpenPanel/ClosePanel pattern, VBoxContainer/Label UI construction, `_UnhandledInput` toggle, `CommitSelection` via `Hud.SetTargetIndex`
- `Scripts/Math/UniMath.cs` — `Distance`, `RelativeMetres`, `RelativePosition`, `ToAncestorFrame`, `FindLca`
- `Scripts/GameWorld.cs` — `TranslatePos` + `TrySpaceTransition` (automatic SOI handling)
- `project.godot` — full [input] section (confirmed existing actions and confirmed J/Left-Alt are free)
- `.planning/phases/07-autopilot-warp-drive/07-CONTEXT.md` — all D-01 through D-19 locked decisions

### Secondary (MEDIUM confidence — WebSearch with authoritative source URLs)
- [Godot 4 Quaternion docs](https://docs.godotengine.org/en/stable/classes/class_quaternion.html) — Slerp API
- [Godot 4 HSlider docs](https://docs.godotengine.org/en/stable/classes/class_hslider.html) — Range.ValueChanged, MinValue/MaxValue/Step/Value
- [Godot 4 InputMap docs](https://docs.godotengine.org/en/stable/classes/class_inputmap.html) — AddAction / ActionAddEvent

### Tertiary (LOW confidence — WebSearch training data)
- Godot C# state machine patterns (enum approach for ≤6 states)
- HSlider C# constructor/property initializer syntax

---

## Metadata

**Confidence breakdown:**
- Standard Stack: HIGH — no new packages; confirmed existing Godot stack
- Architecture: HIGH — derived from direct read of FlightController.cs, Hud.cs, TargetSelectorPanel.cs, GameWorld.cs
- Warp state machine pattern: HIGH — derived from existing code patterns
- Basis Slerp / Quaternion API: MEDIUM — WebSearch confirmed from official Godot docs page
- HSlider API: MEDIUM — WebSearch confirmed from official Godot docs page
- Input key codes (Left Alt): LOW — requires editor verification

**Research date:** 2026-06-22
**Valid until:** 2026-07-22 (stable engine version; Godot 4.6.2 API is stable)
