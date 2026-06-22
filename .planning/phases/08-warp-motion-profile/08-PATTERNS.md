# Phase 8: Warp Motion Profile - Pattern Map

**Mapped:** 2026-06-22
**Files analyzed:** 4 (3 modified existing + 1 net-new helper at Claude's discretion)
**Analogs found:** 4 / 4

> All four "files" already exist or have a strong in-repo analog. Phase 8 is a **rewrite of existing warp logic**, not greenfield — most patterns are *in the file being edited*. The only net-new artifact (a closed-form trapezoid profile helper) has two clean existing analogs for "where pure logic lives" (`TierClassifier.cs` static class; `LuminousBodyDescriptor` struct).

---

## File Classification

| File | New/Modified | Role | Data Flow | Closest Analog | Match Quality |
|------|--------------|------|-----------|----------------|---------------|
| `Scripts/Flight/FlightController.cs` | modified (primary) | controller | event-driven (per-frame `_Process`) + transform | *itself* — existing `_WarpProcess` / `EngageWarp` / `UpdateSpeedEnvelope` | exact (self) |
| `Scripts/Math/UniMath.cs` | modified (WR-01 only) | utility (static math) | transform (pure function) | *itself* — `NormalizedDirection` threshold + comment fix | exact (self) |
| `Scripts/Hud/WarpConfirmationScreen.cs` | modified (D-12) | component (HUD panel) | request-response (read-only display refresh) | *itself* — `RefreshDisplay` speed line | exact (self) |
| **Trapezoid profile helper** (e.g. `Scripts/Flight/WarpMotionProfile.cs`) | **new (Claude's discretion — D-02/Area 1)** | utility / data type | transform (closed-form `v(t)`) | `Scripts/TierClassifier.cs` (pure static) **or** `Scripts/Render/LuminousBodyDescriptor.cs` (immutable struct) | role-match |

**Discretion note (CONTEXT D-60):** The profile may also be inlined into `FlightController` as private fields + a `WarpVelocityAt(double t)` helper method. Both placements are below. The planner picks one — the standalone struct is recommended (testable, pure, mirrors `TierClassifier`'s "dependency-free for unit testing" precedent).

---

## Pattern Assignments

### `Scripts/Flight/FlightController.cs` (controller, event-driven + transform) — PRIMARY

**Analog:** itself. The plumbing (`CurrentSpeed`/`_easedSpeed` → `ApplyMotion`, the `WarpState` machine, bounds-safe lookups, NaN guards) is reused; only the speed *source* changes.

#### 1. `[Export]` tuning-knob pattern (add accel-fraction `f`; neutralize `WarpMaxSpeed`)

Every export uses the **backing-field + clamping setter** idiom. Copy this exact shape for the new `f` knob (lines 191–229 are the cluster to model on):

```csharp
private double _warpMaxSpeed = 2e20;
/// <summary>...</summary>
[Export]
public double WarpMaxSpeed
{
    get => _warpMaxSpeed;
    set => _warpMaxSpeed = System.Math.Max(0.0, value);  // blocks negative + NaN (T-07-05)
}
```

New `f` knob (D-02, default 1/3) follows the same pattern but clamps to the open interval to keep cruise = `(1−2f)·T_sel` ≥ 0:

```csharp
private double _warpAccelFraction = 1.0 / 3.0;
[Export]
public double WarpAccelFraction
{
    get => _warpAccelFraction;
    set => _warpAccelFraction = System.Math.Clamp(value, 0.0, 0.5);  // f ∈ [0, 0.5]
}
```

`Mathf.Clamp` is also used for fractional knobs (`DeadzoneFraction` line 83, `ThrottleStep` line 110) — either clamp helper is idiomatic here. **D-05/D-15:** `WarpMaxSpeed` (lines 204–216) is dropped — remove the export and every reference (`_WarpProcess` line 913, `WarpConfirmationScreen` line 232).

#### 2. Where elapsed-warp-time + profile state should live (D-10)

Mirror the existing private-warp-state field block (lines 283–295). The Phase-7 `_warpInternalTimeSec` (lines 290–295) is **replaced** by the new profile params. Add an elapsed-time accumulator next to `_selectedTravelTimeSec`:

```csharp
/// <summary>Travel time in seconds selected by the player (D-17). Stored per-session.</summary>
private double _selectedTravelTimeSec = 120.0;

// REMOVE: _warpInternalTimeSec (lines 290-295) — exponential calibration is gone (D-01).
// ADD: closed-form trapezoid params solved once at EngageWarp + an elapsed-time accumulator.
private double _warpElapsedSec;     // accumulates += delta each warp frame (D-10)
private double _warpCruiseSpeed;    // v_c, derived per-warp (D-03) — also feeds D-12 display
// + profile distance D, accel/cruise/decel durations, launch/terminal speeds (or a struct, below)
```

#### 3. Existing per-frame warp drive pattern (`_WarpProcess`, lines 888–954) — REWRITE the speed line, KEEP the scaffold

The method's skeleton is the template. **Keep verbatim:** bounds-safe ship lookup (894–895), bounds-safe target lookup (898–899), the `DisengageWarp()`-on-missing safe abort (902), the LCA-safe `dist` (906), the `dist < target.SOIMeters` terminal disengage (909), the `double.IsFinite` guard (916), the entire auto-orient slerp block (918–948), and the final `_easedSpeed`/`CurrentSpeed` write (950–953).

**Replace only** lines 911–913 (the exponential speed) with elapsed-time accumulation + closed-form `v(t)`:

```csharp
// EXISTING (lines 911-913) — REMOVE:
double warpSpeed = System.Math.Min(dist / _warpInternalTimeSec, _warpMaxSpeed);

// REPLACEMENT (D-10): drive by elapsed warp time, not remaining distance.
_warpElapsedSec += delta;
double warpSpeed = WarpVelocityAt(_warpElapsedSec);   // closed-form trapezoid (D-01/D-04)
```

The NaN guard at line 916 (`if (!double.IsFinite(warpSpeed)) { DisengageWarp(); return; }`) is **retained unchanged** (D-06 → safe disengage on non-finite). The `dist` value is still needed for the line-909 SOI terminal disengage (D-10 keeps it as the floating-origin overshoot safety net).

#### 4. Bounds-safe lookup pattern for the WR-03 valid-target gate (D-14)

`EngageWarp` (lines 834–839) already computes the `hasRefs` flag with the canonical `(uint)idx < (uint)count` double-bounds check. WR-03 (D-14) hardens this into an **early safe-abort** instead of the silent fallback at lines 853–857 (which currently sets a bogus `_warpInternalTimeSec` and warps anyway). Copy the `hasRefs` shape and abort before mutating state / entering `Warping`:

```csharp
// EXISTING gate to reuse (lines 837-839):
bool hasRefs = gameObjects != null
    && (uint)shipIdx < (uint)gameObjects.Count
    && tgtIdx >= 0 && (uint)tgtIdx < (uint)gameObjects.Count;

// WR-03 (D-14): no valid resolved target → no-op, stay Manual. Do NOT enter Warping.
if (!hasRefs || gameObjects[shipIdx] == null || gameObjects[tgtIdx] == null)
    return;   // safe abort — _warpState stays Manual
```

This is the **same defensive shape** `_WarpProcess` uses at line 902 (`if (ship == null || target == null) { DisengageWarp(); return; }`) and `UpdateSpeedEnvelope` at 671 (`tgtIdx >= 0 && (uint)tgtIdx < (uint)gameObjects.Count && gameObjects[tgtIdx] != null`).

#### 5. `EngageWarp` rewrite (lines 827–862) — replace calibration with closed-form solve

Keep the `T_sel ≥ 1.0` clamp (line 829, D-06 / T-07-01) and the `_cursor = Vector2.Zero` + `_warpState = WarpState.Warping` tail (lines 860–861). Replace the `_warpInternalTimeSec` ln-ratio block (831–857) with: WR-03 gate → capture `d0 = UniMath.Distance(shipObj, tgtObj, gameObjects)` (the call already exists at line 846) → `D = d0 − tgtObj.SOIMeters` (D-08) → store `T_sel`, reset `_warpElapsedSec = 0`, solve `v_c = D / (T_sel · (1 − f))` (D-03) and the accel/cruise/decel durations.

#### 6. WR-04 fix — `UpdateSpeedEnvelope` open-space sentinel (line 653, D-15)

```csharp
// EXISTING (line 652-653) — the bug: at SpeedPerMeter=0.5, nearest = tierCeiling/1.0,
// then line 660 targetMax = nearest * 0.5 halves the open-space target.
if (nearest == double.MaxValue)
    nearest = tierCeiling / System.Math.Max(_speedPerMeter, 1.0);
```

The fix makes open-space `targetMax` resolve to `tierCeiling` (not `tierCeiling × 0.5`) — e.g. set the sentinel so `nearest * _speedPerMeter` (line 660) lands on `tierCeiling`: `nearest = tierCeiling / _speedPerMeter` (guarding `_speedPerMeter > 0`). This is a **manual-flight feel change** (CONTEXT D-15 warns ~2× open-space top speed; expect `SpeedPerMeter`/tier-factor re-tune). Touch **only** this branch; do not alter the proximity-damp math at lines 655–677.

---

### Trapezoid profile helper (NEW — utility/transform) — *recommended standalone struct*

**Analogs:** `Scripts/TierClassifier.cs` (pure static class, "dependency-free for unit testing", global namespace, no `using Godot`) and `Scripts/Render/LuminousBodyDescriptor.cs` (immutable `struct` data type with XML-doc'd fields).

**Imports/namespace pattern** — follow `TierClassifier.cs` (no namespace wrapper, global namespace, pure C#) since this is closer to a math primitive than a render type, OR put it under `namespace Flight` if it stays coupled to the controller. Per CLAUDE.md §Module Design: Math files at `Scripts/Math/` use the global namespace; consumer-layer files use folder-aligned namespaces (`Flight`). A pure profile struct fits either `Scripts/Math/` (global ns) or `Scripts/Flight/` (`namespace Flight`).

**Struct/header doc pattern** (copy `LuminousBodyDescriptor.cs` lines 1–14 shape):

```csharp
/// <summary>
/// Closed-form symmetric smoothstep trapezoidal velocity profile (D-01/D-04).
/// T_sel is the EXACT input; v_c is derived: v_c = D / (T_sel·(1−f)) (D-03).
/// Accel = decel = f·T_sel; cruise = (1−2f)·T_sel. Ramps use s(u)=3u²−2u³.
/// Pure — no Godot dependency, no mutation; one Solve() + one Velocity(t) call (D-10).
/// </summary>
public readonly struct WarpMotionProfile { /* D, T_sel, f, v_c, durations; Velocity(t) */ }
```

**Numeric guards** (Claude's discretion D-61): use the existing project idioms — `System.Math.Max(EPSILON, …)` for divisor safety (cf. `EngageWarp` line 847 `System.Math.Max(tgtObj.SOIMeters, 1.0)`, `UniVec3.cs` `EPSILON = 1e-11` div-by-zero guard) and `double.IsFinite` checked by the *caller* (the existing line-916 guard already covers the `v(t)` output). Smoothstep: Godot's `Mathf.SmoothStep(0, 1, u)` is available, or hand-roll `3u²−2u³`.

**Alternative (inline in FlightController):** add `_warpCruiseSpeed`, `_warpProfileDist`, durations as private fields (mirror lines 283–295) and a private `double WarpVelocityAt(double t)` helper (mirror the private-method style of `_WarpProcess`). Use this if the profile never needs independent testing.

---

### `Scripts/Hud/WarpConfirmationScreen.cs` (component, request-response) — D-12

**Analog:** itself + the `TargetSelectorPanel` pattern it already mirrors.

**Speed-display line to change** (`RefreshDisplay`, lines 230–234):

```csharp
// EXISTING (lines 230-234) — REMOVE the WarpMaxSpeed cap (D-05) and the dist/time decay:
double warpSpeed = System.Math.Min(dist / _selectedTravelTimeSec, _flight?.WarpMaxSpeed ?? 2e20);
if (_speedLabel != null)
    _speedLabel.Text = $"WARP  {Hud.FormatSpeed(warpSpeed)}";

// REPLACEMENT (D-12): show PEAK cruise speed v_c = D / (T_sel·(1−f)),  D = dist − target.SOIMeters.
// No cap, no time clamp (slider MinValue=5s already prevents div-by-zero).
double profileDist = System.Math.Max(0.0, dist - target.SOIMeters);
double f  = _flight?.WarpAccelFraction ?? (1.0 / 3.0);
double vc = profileDist / (_selectedTravelTimeSec * (1.0 - f));
if (_speedLabel != null)
    _speedLabel.Text = $"WARP  {Hud.FormatSpeed(vc)}";
```

**Reuse:** the existing read-only contract (D-53, no sim mutation), `UniMath.Distance(ship, target, gameObjects)` at line 222, `Hud.FormatSpeed` (the adaptive m/s → km/s → AU/s → ly/s ladder, `Hud.cs` lines 464–471), and the bounds-safe ship/target lookups at lines 209–215. Keep the phosphor-green `AddRow` styling (D-16). The slider min/floor stays at 5 s (line 102) — **no time clamp added** since the cap is dropped (D-12).

---

### `Scripts/Math/UniMath.cs` (utility, transform) — WR-01 only (D-13)

**Analog:** itself. Single targeted fix in `NormalizedDirection` (lines 225–234).

```csharp
// EXISTING (lines 231-233) — comment says "sub-unit ... fall back to Offset in metres"
// but `u` here is rel.ToDouble3Units() = INTEGER UNITS, not metres. The 1e-3 threshold
// is evaluated in UNIT SPACE, not metres (WR-01 / D-13). Fix threshold + stale comment.
Double3 u = rel.ToDouble3Units();
double mag = u.Magnitude();
return mag < 1e-3 ? Double3.Zero : new Double3(u.X / mag, u.Y / mag, u.Z / mag);
```

The coincidence test must reflect that `mag` is in **integer units** (where the smallest non-zero value is 1.0, not a metric epsilon). Correct the threshold to the intended unit-space coincidence cutoff and rewrite the misleading "in metres" comment. **Read-only contract** of `UniMath` (header lines 21–23) is preserved — no mutation, signature unchanged so all callers (`_WarpProcess` line 921, `LuminousDescriptorBuilder`) are unaffected.

---

## Shared Patterns

### Bounds-safe object lookup
**Source:** `FlightController.cs` lines 585, 589, 895, 899; `WarpConfirmationScreen.cs` lines 209, 214; `UniMath.cs` lines 58, 68
**Apply to:** WR-03 gate (D-14), every `GameObjects[idx]` access in the rewrite
```csharp
var obj = (uint)idx < (uint)gameObjects.Count ? gameObjects[idx] : null;
if (obj == null) { /* safe abort: DisengageWarp() / return / continue */ }
```
The `(uint)` cast checks `>= 0` and `< Count` in one comparison (CLAUDE.md §Error Handling).

### LCA-safe cross-frame distance/direction (CLAUDE.md §Position Math — MANDATORY)
**Source:** `UniMath.Distance` / `UniMath.NormalizedDirection` (`UniMath.cs` lines 207–208, 225–234)
**Apply to:** all warp distance (`d0`, per-frame `dist`) and auto-orient direction
- `UniMath.Distance(ship, target, objs)` — used at `EngageWarp:846`, `_WarpProcess:906`, `WarpConfirmationScreen:222`.
- `UniMath.NormalizedDirection(ship, target, objs)` — used at `_WarpProcess:921`.
- **NEVER** `UniVec3.Distance` / raw `ToDouble3()` across frames. (Same-frame sibling scan in `UpdateSpeedEnvelope:645` correctly uses `UniVec3.Distance` because ship+siblings share a parent frame — do not "fix" that.)

### `[Export]` backing-field + clamping setter
**Source:** `FlightController.cs` lines 113–229 (every export); `System.Math.Max(0.0, value)` blocks negative+NaN (T-07-05)
**Apply to:** new `WarpAccelFraction` knob (D-02); removal target = `WarpMaxSpeed` (D-05)

### NaN/Infinity guard before writing speed/position
**Source:** `FlightController.cs` line 719 (`ApplyMotion`), line 916 (`_WarpProcess`)
**Apply to:** the closed-form `v(t)` output (D-06) — `if (!double.IsFinite(warpSpeed)) { DisengageWarp(); return; }` stays as-is; the line-719 `ApplyMotion` guard is the last-resort catch.

### Adaptive unit formatting for HUD speed/distance
**Source:** `Hud.cs` `FormatSpeed` (lines 464–471), `FormatDistance` (lines 478–485)
**Apply to:** the D-12 `v_c` display — reuse `Hud.FormatSpeed(vc)` (already imported via `_hud`), no new formatter needed.

### Pure, dependency-free, testable helper placement
**Source:** `TierClassifier.cs` (lines 1–3 — "Pure C# — no Godot dependency ... kept dependency-free for unit testing") and `LuminousBodyDescriptor.cs` (immutable struct)
**Apply to:** the new trapezoid profile struct/class — keep it Godot-free so it can be unit-tested like `TierClassifier` (cf. `HudFormatTests.cs` precedent for in-repo tests).

---

## No Analog Found

None. Every Phase-8 change is a rewrite of existing warp logic or has a direct in-repo structural analog. The closed-form trapezoid *math* is novel, but its **placement and shape** map cleanly onto `TierClassifier` (pure static) / `LuminousBodyDescriptor` (struct), so the planner has a concrete template.

---

## Metadata

**Analog search scope:** `Scripts/Flight/`, `Scripts/Math/`, `Scripts/Hud/`, `Scripts/` root, `Scripts/Render/` (helper-placement precedents)
**Files scanned:** 22 (full `Scripts/**/*.cs` enumeration); 6 read in depth (FlightController, UniMath, WarpConfirmationScreen, TierClassifier, LuminousBodyDescriptor, Hud format methods)
**Pattern extraction date:** 2026-06-22
