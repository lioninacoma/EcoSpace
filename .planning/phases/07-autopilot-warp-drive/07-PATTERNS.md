# Phase 7: Autopilot & Warp Drive — Pattern Map

**Mapped:** 2026-06-22
**Files analyzed:** 5 (2 modified, 1 new, 1 config, 1 scene)
**Analogs found:** 5 / 5

---

## File Classification

| New/Modified File | Role | Data Flow | Closest Analog | Match Quality |
|-------------------|------|-----------|----------------|---------------|
| `Scripts/Flight/FlightController.cs` | controller | request-response + state machine | itself (existing file, direct modification) | exact |
| `Scripts/Hud/WarpConfirmationScreen.cs` | component/panel | request-response | `Scripts/Hud/TargetSelectorPanel.cs` | exact (same role, same data flow) |
| `Scripts/Hud/Hud.cs` | component/HUD | request-response | itself (no changes required per RESEARCH.md) | exact |
| `project.godot` | config | — | `project.godot` [input] section (existing entries) | exact |
| `Main.tscn` | scene | — | existing CanvasLayer > TargetSelectorPanel node structure | exact |

---

## Pattern Assignments

### `Scripts/Flight/FlightController.cs` (controller, state machine modification)

**Analog:** itself — direct modification of `Scripts/Flight/FlightController.cs`

**Existing export pattern** (lines 48–189) — copy this format for new exports:
```csharp
private double _manualMaxSpeed = 1e6;
/// <summary>
/// Manual flight speed cap in m/s (D-09). All non-warp throttle clamped to this.
/// System.Math.Max(0.0, value) blocks negative and NaN inputs.
/// </summary>
[Export]
public double ManualMaxSpeed
{
    get => _manualMaxSpeed;
    set => _manualMaxSpeed = System.Math.Max(0.0, value);
}

private double _warpMaxSpeed = 2e20;
/// <summary>
/// Safety cap on computed warp speed in m/s (D-07). Technical knob only.
/// </summary>
[Export]
public double WarpMaxSpeed
{
    get => _warpMaxSpeed;
    set => _warpMaxSpeed = System.Math.Max(0.0, value);
}
```

**IsPanelOpen gate pattern** (lines 292–349) — already exists, reused by warp confirm screen:
```csharp
// In _Input (line 300):
if (IsPanelOpen) return;

// In _UnhandledInput (line 321):
if (IsPanelOpen) return;

// In _Process (line 349):
if (IsPanelOpen) return;
```

**_Process routing pattern** (lines 341–356) — replace with switch on new WarpState enum:
```csharp
// EXISTING (lines 341-356) — replace body with switch:
public override void _Process(double delta)
{
    if (_world == null) return;
    if (delta <= 0.0) return;
    if (IsPanelOpen && _warpState == WarpState.Manual) return;

    switch (_warpState)
    {
        case WarpState.Manual:
            HandleThrottleInput();
            UpdateAttitude(delta);
            UpdateSpeedEnvelope(delta);
            ApplyMotion(delta);
            UpdateReticlePosition();
            break;
        case WarpState.Confirming:
            break;   // IsPanelOpen=true; WarpConfirmationScreen handles all input
        case WarpState.Warping:
            UpdateLookAround(delta);   // look-around always active in warp (D-14)
            _WarpProcess(delta);
            ApplyMotion(delta);
            break;
    }
}
```

**UpdateAttitude pattern** (lines 385–417) — look-around inserts a branch before the existing rotation code:
```csharp
// EXISTING camera write (line 415-416) — becomes conditional:
if (_camera != null)
    _camera.Basis = _shipBasis;

// BECOMES (look-around override):
if (_camera != null)
    _camera.Basis = (_shipBasis * _cameraOffset).Orthonormalized();
```

**Orthonormalize pattern** (line 412) — MUST apply to _cameraOffset same as _shipBasis:
```csharp
// Existing _shipBasis orthonormalization (line 412):
_shipBasis = _shipBasis.Orthonormalized();

// New _cameraOffset must do the same every frame it is mutated:
_cameraOffset = _cameraOffset.Orthonormalized();
```

**ManualMaxSpeed clamp in UpdateSpeedEnvelope** — insert after existing targetMax computation (line 531):
```csharp
// AFTER existing line 531: targetMax = System.Math.Min(targetMax, targetEaseMax);
// ADD ManualMaxSpeed clamp for manual path only (D-09/D-10):
if (_warpState == WarpState.Manual)
    targetMax = System.Math.Min(targetMax, _manualMaxSpeed);

// Existing easing lerps run unchanged (lines 539-550):
_contextMax = Mathf.Lerp(_contextMax, targetMax, Mathf.Clamp(_speedEasing * delta, 0.0, 1.0));
double targetSpeed = _throttle01 * _contextMax;
_easedSpeed = Mathf.Lerp(_easedSpeed, targetSpeed, Mathf.Clamp(_speedEasing * delta, 0.0, 1.0));
CurrentSpeed = _easedSpeed;
```

**ApplyMotion pattern** (lines 553–585) — unchanged; warp sets _easedSpeed directly before calling it:
```csharp
// Existing forward vector and TranslatePos call (lines 576-584):
Vector3 forward = -_shipBasis.Z;
var motionDelta = new Double3(
    forward.X * CurrentSpeed * delta,
    forward.Y * CurrentSpeed * delta,
    forward.Z * CurrentSpeed * delta);
_world.TranslatePos(_world.ShipIndex, motionDelta);
```

**_hud reference resolution pattern** (lines 267–270) — reuse for _flight refs in WarpConfirmationScreen:
```csharp
// Existing Hud FindChild resolve (FlightController._Ready, lines 267-270):
_hud = GetTree().Root.FindChild("Hud", true, false) as Hud.Hud;
```

**UniMath.Distance usage pattern** (line 529 — existing target ease-out):
```csharp
// EXISTING cross-frame distance (line 529) — use same API for warp distance (D-06):
double distToTarget = UniMath.Distance(ship, gameObjects[tgtIdx], gameObjects);
// (Warp: replace tgtIdx with ActiveTargetIndex, var renamed to dist)
```

**Index bounds guard pattern** (line 440/526) — use on every GameObjects lookup:
```csharp
// Pattern: (uint) cast checks both >= 0 and < Count in one comparison
var ship = (uint)shipIndex < (uint)gameObjects.Count ? gameObjects[shipIndex] : null;
if (ship == null) return;
```

**_UnhandledInput action pattern** (lines 316–338) — for warp_engage handler:
```csharp
// Existing T-key handler uses raw PhysicalKeycode check.
// New warp_engage MUST use IsActionPressed (InputMap action) per RESEARCH.md anti-pattern rule:
if (@event.IsActionPressed("warp_engage"))
{
    // only open if active target exists (D-02)
    if (_hud?.ActiveTargetIndex >= 0)
    {
        // signal WarpConfirmationScreen or set _warpState = WarpState.Confirming
    }
    GetViewport().SetInputAsHandled();
    return;
}
```

---

### `Scripts/Hud/WarpConfirmationScreen.cs` (NEW file — component, request-response)

**Analog:** `Scripts/Hud/TargetSelectorPanel.cs` — copy structure exactly.

**File header + namespace** (TargetSelectorPanel.cs lines 1–5):
```csharp
using Godot;
using System.Collections.Generic;

namespace Hud
{
    public partial class WarpConfirmationScreen : Control
    {
```

**Export declarations** (TargetSelectorPanel.cs lines 43–55) — copy all three NodePath exports + PhosphorGreen:
```csharp
[Export] public NodePath WorldPath  { get; set; }
[Export] public NodePath HudPath    { get; set; }
[Export] public NodePath FlightPath { get; set; }
[Export] public Color PhosphorGreen { get; set; } = new Color(0.1f, 1.0f, 0.3f);
```

**Private references** (TargetSelectorPanel.cs lines 58–65):
```csharp
private TestSetup              _world;
private Hud                    _hud;
private Flight.FlightController _flight;
private VBoxContainer          _vbox;
```

**_Ready node resolution** (TargetSelectorPanel.cs lines 107–134) — copy verbatim, change class name and add HSlider setup:
```csharp
public override void _Ready()
{
    if (WorldPath != null && !WorldPath.IsEmpty)
        _world = GetNode<TestSetup>(WorldPath);
    else
        _world = GetTree().Root.FindChild("Main", true, false) as TestSetup;

    if (HudPath != null && !HudPath.IsEmpty)
        _hud = GetNode<Hud>(HudPath);
    else
        _hud = GetTree().Root.FindChild("Hud", true, false) as Hud;

    if (FlightPath != null && !FlightPath.IsEmpty)
        _flight = GetNode<Flight.FlightController>(FlightPath);
    else
        _flight = GetTree().Root.FindChild("FlightController", true, false) as Flight.FlightController;

    _vbox = new VBoxContainer();
    AddChild(_vbox);

    Visible = false;
    MouseFilter = MouseFilterEnum.Ignore;
}
```

**OpenPanel / ClosePanel** (TargetSelectorPanel.cs lines 200–232) — copy exactly, add EngageWarp call on close-with-engage:
```csharp
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
```

**_UnhandledInput toggle pattern** (TargetSelectorPanel.cs lines 136–191) — adapt for warp_engage / ui_cancel / ui_accept:
```csharp
public override void _UnhandledInput(InputEvent @event)
{
    if (@event.IsActionPressed("warp_engage"))
    {
        if (Visible)
            ClosePanel();
        else if (_hud?.ActiveTargetIndex >= 0)
            OpenPanel();
        GetViewport().SetInputAsHandled();
        return;
    }

    if (!Visible) return;

    if (@event.IsActionPressed("ui_cancel"))
    {
        ClosePanel();
        GetViewport().SetInputAsHandled();
    }
    else if (@event.IsActionPressed("ui_accept"))
    {
        _flight?.EngageWarp(_selectedTravelTimeSec);
        ClosePanel();
        GetViewport().SetInputAsHandled();
    }
}
```

**AddRow label helper** (TargetSelectorPanel.cs lines 658–673) — copy verbatim for consistent phosphor-green styling:
```csharp
private Label AddRow(string text, bool highlight)
{
    var label = new Label();
    label.Text = text;
    label.Modulate = highlight
        ? new Color(
            Mathf.Min(1f, PhosphorGreen.R + 0.2f),
            Mathf.Min(1f, PhosphorGreen.G + 0.1f),
            Mathf.Min(1f, PhosphorGreen.B + 0.2f),
            PhosphorGreen.A)
        : PhosphorGreen;
    label.MouseFilter = MouseFilterEnum.Ignore;
    _vbox.AddChild(label);
    return label;
}
```

**Distance display** (TargetSelectorPanel.cs line 579 + Hud.cs lines 477–485) — reuse static formatters:
```csharp
// Distance display on warp confirmation screen:
double dist = UniMath.Distance(ship, target, gameObjects);
string distStr = Hud.FormatDistance(dist);

// Warp speed display — use FormatSpeed (D-15, RESEARCH open question 3 recommendation):
double warpSpeed = System.Math.Min(dist / _selectedTravelTimeSec, _flight?.WarpMaxSpeed ?? 2e20);
string speedStr = Hud.FormatSpeed(warpSpeed);
```

**HSlider pattern** (RESEARCH.md Code Examples section):
```csharp
// Runtime field — not persisted (D-17):
private double _selectedTravelTimeSec = 120.0;   // 2-minute default

// In _Ready or BuildUI:
var slider = new HSlider();
slider.MinValue = 1.0;    // 1 minute minimum
slider.MaxValue = 60.0;   // 60 minutes maximum
slider.Step     = 0.5;
slider.Value    = 2.0;    // default (D-17: 2 minutes shown pre-filled)
slider.ValueChanged += (double value) =>
{
    _selectedTravelTimeSec = value * 60.0;   // slider in minutes, stored in seconds
    RefreshDisplay();   // update computed warp speed label
};
_vbox.AddChild(slider);
```

---

### `Scripts/Hud/Hud.cs` (no code changes required)

Per RESEARCH.md Integration Points: "Hud.cs changes — None required."
`ActiveTargetIndex`, `IsPanelOpen`, `FormatSpeed`, `FormatDistance`, `GetTargetCandidates` all already exist and are consumed as-is.

If a cosmetic "WARP" prefix on the speed label is desired (RESEARCH.md open question 4), the planner may add it as a Claude's Discretion item. The data plumbing requires no change.

**FormatSpeed** (Hud.cs lines 464–471) — consumed by WarpConfirmationScreen:
```csharp
public static string FormatSpeed(double metersPerSecond)
{
    double v = System.Math.Abs(metersPerSecond);
    if (v < 1_000.0) return $"{metersPerSecond:0.#} m/s";
    if (v < AU)      return $"{metersPerSecond / 1_000.0:0.#} km/s";
    if (v < LY)      return $"{metersPerSecond / AU:0.###} AU/s";
    return $"{metersPerSecond / LY:0.###} ly/s";
}
```

**FormatDistance** (Hud.cs lines 478–485) — consumed by WarpConfirmationScreen:
```csharp
public static string FormatDistance(double meters)
{
    double v = System.Math.Abs(meters);
    if (v < 1_000.0) return $"{meters:0.#} m";
    if (v < AU)      return $"{meters / 1_000.0:0.#} km";
    if (v < LY)      return $"{meters / AU:0.###} AU";
    return $"{meters / LY:0.###} ly";
}
```

---

### `project.godot` (config — add two InputMap actions)

**Analog:** existing [input] entries (lines 27–70). Copy the exact serialization format.

**Pattern for a simple key action** (project.godot line 66–70, toggle_target_panel / Tab):
```ini
toggle_target_panel={
"deadzone": 0.5,
"events": [Object(InputEventKey,"resource_local_to_scene":false,"resource_name":"","device":-1,"window_id":0,"alt_pressed":false,"shift_pressed":false,"ctrl_pressed":false,"meta_pressed":false,"pressed":false,"keycode":0,"physical_keycode":4194306,"key_label":0,"unicode":0,"location":0,"echo":false,"script":null)
]
}
```

**New entries to add** (append after existing [input] block):
```ini
warp_engage={
"deadzone": 0.5,
"events": [Object(InputEventKey,"resource_local_to_scene":false,"resource_name":"","device":-1,"window_id":0,"alt_pressed":false,"shift_pressed":false,"ctrl_pressed":false,"meta_pressed":false,"pressed":false,"keycode":0,"physical_keycode":74,"key_label":0,"unicode":106,"location":0,"echo":false,"script":null)
]
}
look_around={
"deadzone": 0.5,
"events": [Object(InputEventKey,"resource_local_to_scene":false,"resource_name":"","device":-1,"window_id":0,"alt_pressed":false,"shift_pressed":false,"ctrl_pressed":false,"meta_pressed":false,"pressed":false,"keycode":0,"physical_keycode":4194326,"key_label":0,"unicode":0,"location":0,"echo":false,"script":null)
]
}
```

Notes on key codes:
- J key: `physical_keycode=74`, `unicode=106` (verified from existing pattern — W=87/119, S=83/115, Q=81/113, E=69/101, X=88/120)
- Left Alt: `physical_keycode=4194326` (ASSUMED from RESEARCH.md A2 — verify in Godot editor before committing; unicode=0 matches the Tab entry which also has unicode=0)

---

### `Main.tscn` (scene — add WarpConfirmationScreen node under CanvasLayer)

**Analog:** existing TargetSelectorPanel node under CanvasLayer. Add WarpConfirmationScreen as a sibling.

The exact tscn serialization format should be copied from the existing TargetSelectorPanel node entry. The executor must:
1. Open Main.tscn in the Godot editor
2. Add a new `WarpConfirmationScreen` node (type: Control, script: `res://Scripts/Hud/WarpConfirmationScreen.cs`) as a sibling of `TargetSelectorPanel` under `CanvasLayer`
3. Wire the NodePath exports: `WorldPath`, `HudPath`, `FlightPath` — same targets as TargetSelectorPanel's exports

---

## Shared Patterns

### Bounds-Safe GameObjects Index Lookup
**Source:** `Scripts/Flight/FlightController.cs` lines 440, 526; `Scripts/Hud/TargetSelectorPanel.cs` lines 364–366
**Apply to:** all new code in FlightController._WarpProcess and WarpConfirmationScreen that accesses gameObjects[idx]
```csharp
// (uint) cast checks both >= 0 and < Count in one comparison:
var obj = (uint)idx < (uint)gameObjects.Count ? gameObjects[idx] : null;
if (obj == null) return;
```

### Cross-Frame Distance (LCA path only)
**Source:** `Scripts/Flight/FlightController.cs` line 529; `Scripts/Math/UniMath.cs` lines 207–208
**Apply to:** ALL warp distance computations in _WarpProcess; distance display in WarpConfirmationScreen
```csharp
// The ONLY safe cross-frame distance:
double dist = UniMath.Distance(ship, target, gameObjects);
// Never: UniVec3.Distance(ship.LocalPos, target.LocalPos)  — different frames
// Never: (ship.LocalPos - target.LocalPos).ToDouble3()     — catastrophic cancellation
```

### Orthonormalize Every Basis Mutation
**Source:** `Scripts/Flight/FlightController.cs` line 412
**Apply to:** `_cameraOffset` (every frame it is mutated); `_shipBasis` during warp Slerp
```csharp
_shipBasis   = _shipBasis.Orthonormalized();    // existing — every UpdateAttitude call
_cameraOffset = _cameraOffset.Orthonormalized(); // new — every look-around mutation
```

### IsFinite Guard Before Motion
**Source:** `Scripts/Flight/FlightController.cs` lines 567–568
**Apply to:** warp speed before writing to _easedSpeed in _WarpProcess
```csharp
if (!double.IsFinite(CurrentSpeed)) return;
```

### Panel Lifecycle (IsPanelOpen gate)
**Source:** `Scripts/Hud/TargetSelectorPanel.cs` lines 200–232; `Scripts/Flight/FlightController.cs` lines 245, 300, 321, 349
**Apply to:** WarpConfirmationScreen.OpenPanel / ClosePanel
```csharp
// Open:
if (_flight != null) _flight.IsPanelOpen = true;
// Close:
if (_flight != null) _flight.IsPanelOpen = false;
```

### GetViewport().SetInputAsHandled()
**Source:** `Scripts/Hud/TargetSelectorPanel.cs` lines 148, 168, 175, 180, 185, 190
**Apply to:** every handled InputEvent branch in WarpConfirmationScreen._UnhandledInput
```csharp
GetViewport().SetInputAsHandled();
return;
```

### _Ready Node Resolution (NodePath with FindChild fallback)
**Source:** `Scripts/Hud/TargetSelectorPanel.cs` lines 109–125; `Scripts/Hud/Hud.cs` lines 112–128
**Apply to:** WarpConfirmationScreen._Ready for all three NodePath exports
```csharp
if (NodePathExport != null && !NodePathExport.IsEmpty)
    _ref = GetNode<T>(NodePathExport);
else
    _ref = GetTree().Root.FindChild("NodeName", true, false) as T;
```

---

## No Analog Found

None. All files have direct analogs in the codebase.

---

## Metadata

**Analog search scope:** `Scripts/Flight/`, `Scripts/Hud/`, `Scripts/Math/`, `project.godot`
**Files read:** 6 (`FlightController.cs`, `TargetSelectorPanel.cs`, `Hud.cs`, `UniMath.cs`, `project.godot` [input] section, `07-CONTEXT.md`, `07-RESEARCH.md`)
**Pattern extraction date:** 2026-06-22
