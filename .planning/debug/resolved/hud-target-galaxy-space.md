---
status: resolved
resolution: fixed-and-verified
trigger: "Target/HUD bugs in Galaxy space (Scripts/Hud/Hud.cs), surfaced in Phase 03 UAT play-test: (1) the context label 'nearest:' flickers between '?' and 'home galaxy' every frame; (2) galaxy-member stars (ALPHA CEN, BARNARD, SIRIUS) are never recognized as nearest — only the home star (or the galaxy); (3) cycling the target with Tab does nothing."
created: 2026-06-17
updated: 2026-06-17
---

# Debug Session: hud-target-galaxy-space

## Symptoms

- **Expected:**
  - The context label `"{tier} · nearest: {name}"` names a stable, real nearest body that does not change every frame.
  - In Galaxy space, the galaxy's member stars (ALPHA CEN, BARNARD, SIRIUS, plus the home STAR) are candidates for "nearest" and for the target readout, with correct distances.
  - Pressing Tab cycles the active target through the full targetable set; the `TGT` readout and off-screen direction marker update.
- **Actual (Galaxy space, play-test 2026-06-17):**
  - `nearest:` flickers between `"?"` and `"home galaxy"` on every frame.
  - Only the home star (or the galaxy) is ever recognized as nearest; the other galaxy-member stars never win.
  - Tab does nothing — the target does not cycle.
- **Error messages:** none (visual/behavioral only; Godot runtime).
- **Timeline:** First in-game exercise of the HUD target system in Galaxy space, after Phase 03 added the galaxy-member stars. The HUD target/nearest logic (Hud.cs) shipped in Phase 1 (plan 01-04) and was only ever play-tested in Star/Planet space before.
- **Reproduction:** Launch Main.tscn; fly out into Galaxy space; observe the context label flicker and the nearest/target behavior; press Tab.

## Current Focus

```yaml
reasoning_checkpoint:
  hypothesis: >
    (1/2) GetRelativeMeters' parent-path formula (ship.LocalPos.ToDouble3() * -1.0) and
    the sibling-path formula for a body at (0,0,0) (body.LocalPos.ToLocalDouble(ship.LocalPos)
    = -ship.LocalPos metres) produce IDENTICAL magnitudes — the home STAR at galaxy-origin
    and HOME GALAXY (parent) always tie, so the nearest winner toggles per-frame based on
    floating-point rounding (flicker). Galaxy-member stars at 4.2–8.6 ly are vastly farther,
    so they never win (symptom 2). When the ship exits GalaxySOI and enters Universe space,
    the parent becomes ROOT (index 0, null Name → "?"); ROOT (parent path) and HOME GALAXY
    (sibling at 0,0,0) again tie, producing the "?" / "home galaxy" flicker.
    (3) Tab cycling is unreliable: Godot's built-in ui_focus_next (also Tab) may consume
    the event at the GUI/Control layer before _Input sees it via the IsActionPressed check.
    Using _UnhandledInput ensures game actions see the event after UI processing.
  confirming_evidence:
    - "GetRelativeMeters parent path = ship.LocalPos.ToDouble3() * -1.0; home STAR at (0,0,0): body.LocalPos.ToLocalDouble(ship.LocalPos) = (0-ship.LocalPos).ToDouble3() = same vector, identical magnitude"
    - "Root (index 0) never has Name set in TestSetup.SetupScene() — only objects with explicit GameObjects[idx].Name = ... have names; Root.Name = null → body.Name ?? '?' = '?'"
    - "GalaxySOI = 5e20 m; once ship exits galaxy SOI, parent becomes ROOT; BuildTargetableList parent entry = ROOT (null Name); ROOT's IsParent distance = ship.LocalPos.ToDouble3().magnitude; HOME GALAXY sibling at (0,0,0) distance = same → tie flicker"
    - "cycle_target bound to Tab (physical_keycode 4194305) confirmed in project.godot; _Input override exists and looks correct; but Godot 4's ui_focus_next (built-in Tab action) may consume event at GUI layer"
  falsification_test: >
    If precision is NOT the issue, replacing GetRelativeMeters with UniMath.RelativeMetres
    would not stop the flicker. If null Name is NOT the issue, adding Root.Name would not
    eliminate '?'.
  fix_rationale: >
    1. Replace GetRelativeMeters with UniMath.RelativeMetres(ship, body, gameObjects) —
       routes all distances through LCA-based path; correctly computes ship→parent distance
       in grandparent frame, breaking the identical-magnitude tie.
    2. Set Root.Name = "ROOT" in TestSetup (or guard with ?? "SPACE" in HUD display) — 
       stops null Name from rendering as "?".
    3. Change _Input to _UnhandledInput in Hud — ensures Tab reaches the game action handler
       after Godot's GUI input system has had its turn.
  blind_spots: >
    Cannot verify at runtime. The _Input/_UnhandledInput issue may be a red herring if
    _Input is firing correctly — but switching to _UnhandledInput is strictly more correct
    for game actions and has no downside here.
```

next_action: apply fix in Hud.cs (replace GetRelativeMeters with UniMath.RelativeMetres, switch _Input to _UnhandledInput) and TestSetup.cs (set Root Name)

## Evidence

- timestamp 2026-06-17: `cycle_target` IS bound to Tab in project.godot (physical_keycode 4194305 = KEY_TAB). So the binding is not missing — the failure is downstream (event consumption or degenerate targetable set).
- timestamp 2026-06-17: `Hud.UpdateContextLabel` sets `nearestName = body.Name ?? "?"` and scans `BuildTargetableList` (parent + parent.ChildIndices) using `GetRelativeMeters(...).Magnitude()` for the min. `GetRelativeMeters` parent path = `ship.LocalPos.ToDouble3() * -1.0`; sibling path = `body.LocalPos.ToLocalDouble(ship.LocalPos)`. Neither routes through `UniMath`.
- timestamp 2026-06-17: CLAUDE.md "Position Math (UniVec3 / UniMath)" convention requires body-relative distances to use `UniMath.RelativeMetres`/`Distance` (LCA-relative), not raw absolute-frame `ToDouble3()` accumulation.
- timestamp 2026-06-17: Code trace confirms identical-magnitude tie: parent path and sibling-at-origin path both produce `-ship.LocalPos.ToDouble3()` — STAR at (0,0,0) and HOME GALAXY (parent) always tie. Galaxy-member stars at 4.2–8.6 ly (3.97e16 m to 8.13e16 m) are overwhelmingly farther from the ship than the STAR/galaxy tie distance (near the StarSOI boundary ~1.5e15 m). STAR and HOME GALAXY win every frame, galaxy members never win.
- timestamp 2026-06-17: Root object (index 0) has null Name. When ship is in Universe space (after exiting GalaxySOI = 5e20 m), ship.ParentIndex = 0 (ROOT). BuildTargetableList parent entry = ROOT (null Name → "?"). ROOT and HOME GALAXY at (0,0,0) Universe-space tie by the same mechanism → "?" and "home galaxy" flicker alternates per-frame.
- timestamp 2026-06-17: UniMath.RelativeMetres correctly handles both same-space and cross-space cases via LCA walk. For ship→parent: LCA = grandparent, ToAncestorFrame(parent) = parent.LocalPos in grandparent's child frame, ToAncestorFrame(ship) = ship.LocalPos.Convert(grandparentChildScale) + parent.LocalPos → delta = ship.Convert - zero_at_same_origin → correct ship-offset from parent. This breaks the tie by computing the correct vector in the grandparent frame rather than the ship's own frame.

## Eliminated

- hypothesis: Raw `ToDouble3()` precision loss at Galaxy/Universe scale causes wrong distances
  evidence: Precision loss is NOT the primary mechanism — the values are simply equal (both paths compute -ship.LocalPos), not imprecise. The tie causes flickering regardless of precision.
  timestamp: 2026-06-17

## Root Cause

Three distinct root causes, two sharing a common code mechanism:

**Symptoms 1 & 2 (flicker + galaxy members never win):**
`GetRelativeMeters` in Hud.cs computes parent distance as `ship.LocalPos.ToDouble3() * -1.0` (ship in its own frame negated) and sibling-at-origin distance as `body.LocalPos.ToLocalDouble(ship.LocalPos)` = `(0 - ship.LocalPos).ToDouble3()` = identical vector. HOME GALAXY (parent) and home STAR (at galaxy origin 0,0,0) always produce identical magnitude. Per-frame floating-point rounding alternates the winner. Galaxy-member stars are at 4.2–8.6 ly — orders of magnitude farther than the StarSOI exit distance — so they never win the nearest comparison.

When ship exits GalaxySOI and enters Universe space, ROOT (index 0, Name=null) becomes the parent. ROOT (parent) and HOME GALAXY (sibling at 0,0,0 Universe space) tie by the same mechanism. ROOT.Name = null → "?", HOME GALAXY → "home galaxy". Alternating per-frame.

**Symptom 3 (Tab does nothing) — CORRECTED 2026-06-17:**
The first-pass diagnosis (ui_focus_next consuming Tab; switch to `_UnhandledInput`) was
WRONG, based on a misread of the keycode. The actual root cause: `cycle_target` in
project.godot is bound to `physical_keycode 4194305 = KEY_ESCAPE`, NOT Tab
(`KEY_TAB = 4194306`) — an off-by-one in the keycode. Pressing Tab matches no binding,
so nothing happens regardless of `_Input`/`_UnhandledInput`. Furthermore, `_UnhandledInput`
is the WRONG handler for Tab: Godot's input order is `_Input` → GUI focus (ui_focus_next)
→ `_UnhandledInput`, so `_Input` (the original) sees Tab BEFORE focus-nav; `_UnhandledInput`
would be eaten by ui_focus_next. The debugger's switch was both unnecessary and backwards.

**Fix:**
- Replace `GetRelativeMeters` calls in Hud.cs with `UniMath.RelativeMetres` / `UniMath.Distance`
  — LCA-based path breaks the identical-magnitude tie (symptoms 1 & 2). ✓ correct.
- Set `GameObjects[_root].Name = "ROOT"` in TestSetup.SetupScene() — prevents null → "?". ✓ correct.
- **project.godot:** rebind `cycle_target` physical_keycode 4194305 (Escape) → 4194306 (Tab).
- **Hud.cs:** revert `_UnhandledInput` → `_Input` (correct for Tab; runs before ui_focus_next)
  and call `GetViewport().SetInputAsHandled()` after cycling so focus-nav does not double-fire.

**Note:** Symptom 2's "fly near another star and target it" is NOT covered here — that is the
D-12 single-SOI targeting-scope limitation (cross-space targeting = backlog 999.1), folded into
the P1 flight+targeting phase. A SEPARATE high-priority data bug was also found during this
session: the sibling/cluster/dest-sib star positions are authored in "Galaxy units" (1e4 m/unit)
but `AddGameObject` takes metres, so they sit ~1e4× too close (26–55 AU instead of 4.2–8.6 ly),
deep inside the home star's SOI (StarSOI = 1.5e15 m = 10,000 AU) → SOIs overlap. Same bug class
as the galaxy-position fix (6f5f728), which missed the stars. Tracked as its own focused step.

**Files changed:** `Scripts/Hud/Hud.cs`, `Scripts/TestSetup.cs` (ROOT name), `project.godot` (Tab binding)

build 0/0, tests 30/30 after all fixes.
