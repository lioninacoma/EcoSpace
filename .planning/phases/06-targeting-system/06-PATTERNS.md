# Phase 6: Targeting & Navigation HUD - Pattern Map

**Mapped:** 2026-06-20
**Files analyzed:** 4 (2 modified, 1 new C# class, 1 new shader)
**Analogs found:** 4 / 4

---

## File Classification

| New/Modified File | Role | Data Flow | Closest Analog | Match Quality |
|-------------------|------|-----------|----------------|---------------|
| `Scripts/Hud/Hud.cs` (modified) | HUD controller / Control node | request-response (read-only sim state) | `Scripts/Hud/Hud.cs` itself | self — extend existing |
| `Scripts/Hud/TargetSelectorPanel.cs` (new) | UI panel / Control | event-driven (input → selection state) | `Scripts/Hud/Hud.cs` (Control, read-only, phosphor-green, [Export] knobs) | role-match |
| `Scripts/Render/TargetMarkerRenderer.cs` (new) | renderer / Node3D | request-response (UniObject → render-space mesh + label) | `Scripts/Render/WorldRenderer.cs` (observer-unit math, floating-origin, MeshInstance3D lifecycle) | exact |
| `Shaders/target_outline.gdshader` (new) | shader | transform (vertex rim/outline pass) | `Shaders/body_lit.gdshader` (unshaded spatial, render_mode flags, uniform pattern) | role-match |

---

## Pattern Assignments

### `Scripts/Hud/Hud.cs` — modifications (controller, request-response)

**Self-analog.** The file is being extended, not replaced. Key patterns within it to preserve:

**Header doc + read-only contract** (`Hud.cs` lines 1–27):
```csharp
/// Read-only consumer of GameWorld state — MUST NOT mutate sim state.
public partial class Hud : Control
```
All new methods (BuildFullHierarchyTargetList, UpdateTrackingLabel, UpdateSphereMarker) must carry the same read-only guarantee in their doc comments.

**[Export] tuning knobs pattern** (`Hud.cs` lines 350–355):
```csharp
private const float MIN_CIRCLE_RADIUS    = 6f;
private const float MAX_CIRCLE_RADIUS    = 200f;
private const float CIRCLE_BODY_PADDING  = 1.15f;
```
New constants for the 3D marker follow the same UPPER_SNAKE_CASE naming and should be `[Export]` properties or private consts at the same location in the file:
```csharp
// D-52 min-size floor — keeps the marker visible as a reticle at any distance.
[Export] public float MinMarkerRadius  { get; set; } = 6f;
[Export] public float MaxMarkerRadius  { get; set; } = 200f;
[Export] public float MarkerPadding    { get; set; } = 1.15f;
```

**ActiveTargetIndex read-only property contract** (`Hud.cs` lines 98–113):
```csharp
/// Read-only — never mutates _targetIndex or any sim state (Hud is a read-only
/// consumer; HUD mutating sim state is an anti-pattern).
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
The cross-space version must keep the same property signature — `FlightController` reads `ActiveTargetIndex` for the D-43 ease-out (canonical reference: `Scripts/Flight/FlightController.cs`). The only change is `BuildTargetableList` is replaced/extended; the property itself is unchanged.

**Analytic on-screen radius formula** (`Hud.cs` lines 418–433 — the math to port to the 3D marker):
```csharp
float depth = -(float)camLocal.Z;
if (depth > 1e-4f)
{
    float fovRad       = Mathf.DegToRad(_camera.Fov);
    float tanHalfFov   = Mathf.Tan(fovRad * 0.5f);
    float refExtent    = _camera.KeepAspect == Camera3D.KeepAspectEnum.Height
        ? vpSize.Y * 0.5f
        : vpSize.X * 0.5f;
    bodyPixelRadius = refExtent * (renderRadius / depth) / tanHalfFov * CIRCLE_BODY_PADDING;
}
```
For D-51 the marker, `renderRadius` is computed from UniObject directly:
```csharp
// D-51: compute render-space radius from UniObject — same formula WorldRenderer.RenderBodyAt uses.
double radiusMeters = targetObj.RadiusMeters > 0.0 ? targetObj.RadiusMeters : _defaultBodyRadius;
float  renderRadius = (float)((radiusMeters / ship.LocalPos.Scale) * factor);
```
where `factor` = `WorldRenderer.RenderFactorFor(ship.CurrentSpace)` (accessed via public accessor or mirrored in Hud).

**_Draw / DrawArc pattern to REPLACE** (`Hud.cs` lines 447–459):
The 2D `DrawArc` path (`_showTargetCircle`, `_targetCirclePos`, `_targetCircleRadius`, `_Draw()`) is superseded by the 3D sphere-outline mesh (D-50). Remove or comment out `_showTargetCircle` state, `UpdateTargetCircle()`, and the `_Draw()` override. The `QueueRedraw()` call in `_Process` is also removed unless another 2D draw is retained.

**Input: cycle_target / _Input — to RETIRE** (`Hud.cs` lines 461–486):
The entire `_Input` block handling `cycle_target` (Tab) is retired per D-56. Selection moves to `TargetSelectorPanel`.

**BuildTargetableList — to REPLACE** (`Hud.cs` lines 501–531):
Replace the parent+same-space siblings scan with a full-hierarchy walk. New method should:
1. Walk all GameObjects in `_world.GameObjects`.
2. Skip Ship, null, Root-space, and Galaxy-type bodies if desired (or make Galaxy selectable per D-55).
3. Return entries ordered by Space tier (Galaxy first, then Star, then Planet) to match the panel mock.
4. Preserve the `TargetEntry` struct — it only needs `int Index`.

**FormatDistance — reuse verbatim** (`Hud.cs` lines 589–596):
Used unchanged for the panel's distance column and the tracking label:
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

**Off-screen direction marker — keep verbatim** (`Hud.cs` lines 276–344):
`UpdateDirectionMarker(Double3 relD)` is kept as-is. The tracking label rides this when off-screen (D-57).

---

### `Scripts/Hud/TargetSelectorPanel.cs` (new — UI panel, event-driven)

**Analog:** `Scripts/Hud/Hud.cs` — same namespace, same read-only contract, same Control base, same Godot lifecycle.

**Namespace + imports pattern** (`Hud.cs` lines 1–4):
```csharp
using Godot;

namespace Hud
{
    public partial class TargetSelectorPanel : Control
    {
```
No `using System.Collections.Generic` alias needed — use the fully-qualified form `System.Collections.Generic.List<>` or add the using, consistent with Hud.cs.

**[Export] NodePath resolution pattern** (`Hud.cs` lines 39–43, 117–135):
```csharp
[Export] public NodePath WorldPath { get; set; }

public override void _Ready()
{
    if (WorldPath != null && !WorldPath.IsEmpty)
        _world = GetNode<TestSetup>(WorldPath);
    else
        _world = GetTree().Root.FindChild("Main", true, false) as TestSetup;
    // ...
    MouseFilter = MouseFilterEnum.Ignore;  // or Pass when panel is open
}
```
When the panel is open it should set `MouseFilter = MouseFilterEnum.Stop` to capture clicks; when hidden, `MouseFilter = MouseFilterEnum.Ignore` (the anti-pattern in the header).

**PhosphorGreen color [Export] pattern** (`Hud.cs` lines 49–50):
```csharp
[Export] public Color PhosphorGreen { get; set; } = new Color(0.1f, 1.0f, 0.3f);
```
The panel's label and border use the same export so the scene can override it without code changes.

**Read-only sim access pattern** (`Hud.cs` lines 167–183):
```csharp
public override void _Process(double delta)
{
    if (_world == null) return;
    var gameObjects = _world.GameObjects;
    int shipIndex   = _world.ShipIndex;
    if ((uint)shipIndex >= (uint)(gameObjects?.Count ?? 0)) return;
    var ship = gameObjects[shipIndex];
    if (ship == null) return;
    // ... read-only queries only
}
```

**Selection state ownership:** `_targetIndex` stays in `Hud` (the class that exposes `ActiveTargetIndex` to FlightController). `TargetSelectorPanel` calls back into `Hud` to set `_targetIndex`, OR Hud exposes a `SetTargetIndex(int)` method the panel calls. Either way `_targetIndex` is never accessed directly from the panel — the Hud read-only contract is about sim state, not internal HUD index ownership.

**Tier grouping pattern** — copy the `SpaceTierName` helper from Hud.cs for the tier header labels:
```csharp
// Hud.cs lines 533–541
private static string SpaceTierName(UniObject.Space space) => space switch
{
    UniObject.Space.Universe => "UNIVERSE SPACE",
    UniObject.Space.Galaxy   => "GALAXY SPACE",
    UniObject.Space.Star     => "STAR SPACE",
    UniObject.Space.Planet   => "PLANET SPACE",
    _                        => "SPACE"
};
```
Shortened equivalents for the panel header: `"GALAXY"`, `"STAR"`, `"PLANET"` per the user mock.

**Uint bounds check pattern** (`Hud.cs` lines 213, 473):
```csharp
if ((uint)entry.Index >= (uint)gameObjects.Count) continue;
```
Apply to every GameObjects access in the panel.

---

### `Scripts/Render/TargetMarkerRenderer.cs` (new — renderer, request-response)

**Analog:** `Scripts/Render/WorldRenderer.cs` — same namespace, same Node3D base, same floating-origin + observer-unit math, same MeshInstance3D lifecycle.

**Namespace + imports pattern** (`WorldRenderer.cs` lines 1–4):
```csharp
using Godot;
using System.Collections.Generic;

namespace Render
{
    public partial class TargetMarkerRenderer : Node3D
    {
```

**[Export] NodePath resolution pattern** (`WorldRenderer.cs` lines 176–184):
```csharp
[Export] public NodePath WorldPath { get; set; }

public override void _Ready()
{
    if (WorldPath != null && !WorldPath.IsEmpty)
        _world = GetNode<TestSetup>(WorldPath);
    else
        _world = GetParent<TestSetup>() ?? GetTree().Root.FindChild("Main", true, false) as TestSetup;
}
```

**Observer-unit render factor dispatch** (`WorldRenderer.cs` lines 142–149 — canonical pattern for D-51):
```csharp
private float RenderFactorFor(UniObject.Space space) => space switch
{
    UniObject.Space.Planet   => PlanetRenderFactor,
    UniObject.Space.Star     => StarRenderFactor,
    UniObject.Space.Galaxy   => GalaxyRenderFactor,
    UniObject.Space.Universe => UniverseRenderFactor,
    _                        => StarRenderFactor,
};
```
`TargetMarkerRenderer` needs the same dispatch. Copy as-is; values can be `[Export]` knobs or constants referencing `WorldRenderer.StarRenderFactor` for the shared factor.

**Radius-from-UniObject formula** (`WorldRenderer.cs` lines 454–456 — the D-51 size math):
```csharp
// Per-frame radius: true metres → observer units (÷ ship.LocalPos.Scale) → render units (× factor).
double rawRadiusMeters = body.RadiusMeters > 0.0 ? body.RadiusMeters : DefaultBodyRadius;
float r = (float)((rawRadiusMeters / ship.LocalPos.Scale) * factor);
mesh.Scale = new Vector3(r, r, r);
```
Apply identically for the marker mesh. When `rawRadiusMeters` produces a sub-pixel `r`, enforce the D-52 min-size floor AFTER projecting `r` to pixels (use the analytic formula from Hud.cs lines 418–433, then clamp).

**Position from UniObject — sibling vs parent path** (`WorldRenderer.cs` lines 458–479):
```csharp
Double3 relUnits;
if (isParent)
{
    // Parent sits at the origin of the ship's frame.
    relUnits = ship.LocalPos.ToDouble3Units() * -1.0;
}
else
{
    // Sibling: direct delta in the same parent frame.
    relUnits = body.LocalPos.ToLocalDoubleUnits(ship.LocalPos);
}
renderPos = new Vector3(
    (float)(relUnits.X * factor),
    (float)(relUnits.Y * factor),
    (float)(relUnits.Z * factor));
mesh.Position = renderPos;
```
For a **cross-space target** (D-51 / D-55) neither path applies — the target is not a same-frame sibling. Use the **UniMath LCA path** instead:
```csharp
// D-51 cross-space position: UniMath.RelativeMetres → observer units → render units.
Double3 relMetres  = UniMath.RelativeMetres(ship, targetObj, gameObjects);
double  obsFactor  = factor / ship.LocalPos.Scale;   // same scale chain as WorldRenderer.ComputeStarRenderPosFromHierarchy (lines 401–406)
renderPos = new Vector3(
    (float)(relMetres.X * obsFactor),
    (float)(relMetres.Y * obsFactor),
    (float)(relMetres.Z * obsFactor));
```
Exact mirror of `WorldRenderer.ComputeStarRenderPosFromHierarchy` lines 401–406.

**Cross-space render position from hierarchy** (`WorldRenderer.cs` lines 378–407):
```csharp
Double3 starRelToShip = UniMath.RelativeMetres(ship, star, gameObjects);
if (starRelToShip.X == 0.0 && starRelToShip.Y == 0.0 && starRelToShip.Z == 0.0)
    return Vector3.Up * 1e7f;
double obsFactor = factor / ship.LocalPos.Scale;
return new Vector3(
    (float)(starRelToShip.X * obsFactor),
    (float)(starRelToShip.Y * obsFactor),
    (float)(starRelToShip.Z * obsFactor));
```
This is the canonical cross-space `metres → observer-units → render-units` chain. Copy as the body of the marker position calculation.

**Lazy MeshInstance3D creation + unit-sphere + Scale-per-frame** (`WorldRenderer.cs` lines 520–592):
```csharp
private MeshInstance3D GetOrCreateMesh(int bodyIdx, UniObject body)
{
    if (_meshes.TryGetValue(bodyIdx, out var existing))
        return existing;

    var sphereMesh = new SphereMesh { Radius = 1f, Height = 2f };
    // ...
    var meshInstance = new MeshInstance3D { Mesh = sphereMesh, Visible = false };
    AddChild(meshInstance);
    _meshes[bodyIdx] = meshInstance;
    return meshInstance;
}
```
The marker has at most ONE active target at a time, so `_meshes` can be a single `MeshInstance3D` field (not a Dictionary). The unit-sphere + Scale-per-frame idiom is still the right pattern.

**Shader assignment pattern** (`WorldRenderer.cs` lines 536–545, 562–580):
```csharp
// In _Ready:
_bodyLitShader = GD.Load<Shader>("res://Shaders/body_lit.gdshader");

// In GetOrCreateMesh:
var mat = new ShaderMaterial { Shader = _bodyLitShader };
mat.SetShaderParameter("albedo", baseColor);
meshInstance = new MeshInstance3D { Mesh = sphereMesh, MaterialOverride = mat, Visible = false };
```
For the outline marker, load `res://Shaders/target_outline.gdshader` in `_Ready` and set it as `MaterialOverride`. The only uniform to push per frame is the phosphor-green color (or bake it in the shader as a constant).

**Read-only contract doc** (`WorldRenderer.cs` line 34):
```csharp
/// Read-only consumer of GameWorld state — MUST NOT mutate UniVec3 or call TranslatePos.
```
Copy verbatim to `TargetMarkerRenderer` class header.

---

### `Shaders/target_outline.gdshader` (new — unshaded spatial, rim/outline)

**Analog:** `Shaders/body_lit.gdshader` — same `shader_type spatial`, same `render_mode unshaded`, same uniform declaration style.

**Header doc convention** (`body_lit.gdshader` lines 1–28):
```glsl
// target_outline.gdshader
// Unshaded spatial shader that draws a sphere silhouette outline (rim) for the
// target marker (D-50/D-51). render_mode unshaded so Godot adds no engine lighting.
// Uniform: outline_color (phosphor-green, set from TargetMarkerRenderer each frame).
// Technique: vertex shell (scale-up by shell_thickness in vertex shader, cull_front
// to draw only the back-face shell) OR a fragment rim test (dot(VIEW, NORMAL) ≈ 0).
```

**Shader type + render mode** (`body_lit.gdshader` line 30):
```glsl
shader_type spatial;
render_mode unshaded, cull_back, depth_draw_opaque, diffuse_lambert;
```
For an outline-only marker use one of:
- **Rim/fresnel approach** (fragment): `render_mode unshaded, cull_back;` — discard fragments where `abs(dot(NORMAL_view, VIEW)) > rim_threshold`, leaving only the silhouette ring visible.
- **Shell approach** (two passes): `render_mode unshaded, cull_front;` on the outline pass — scales the sphere slightly outward in the vertex shader and renders only back faces, producing an outline halo around the body. A second `cull_back` pass draws the transparent interior if needed.

Either approach is valid per the context decisions (Claude's Discretion). The rim/fresnel approach in a single pass is simpler and consistent with the retro aesthetic.

**Uniform declaration style** (`body_lit.gdshader` lines 33–47):
```glsl
// Phosphor-green outline color. Set from TargetMarkerRenderer._outlineColor each frame.
uniform vec4 outline_color : source_color = vec4(0.1, 1.0, 0.3, 1.0);

// Rim threshold: dot(VIEW, NORMAL) < rim_threshold draws the outline ring.
// 0.2 = thin ring; 0.4 = thicker band. Play-test [Export] knob.
uniform float rim_width : hint_range(0.0, 1.0) = 0.25;
```

**Fragment pattern for rim outline**:
```glsl
void fragment()
{
    float rim = abs(dot(normalize(VIEW), normalize(NORMAL)));
    if (rim > RIM_WIDTH) discard;   // interior: discard; edge ring: draw
    ALBEDO = outline_color.rgb;
    ALPHA  = outline_color.a;
}
```

---

## Shared Patterns

### Observer-Unit Render Conversion (D-51 — critical)
**Source:** `Scripts/Render/WorldRenderer.cs` `ComputeStarRenderPosFromHierarchy` (lines 395–406) and `RenderBodyAt` (lines 454–456)
**Apply to:** `TargetMarkerRenderer` position + radius computations, `Hud` sphere-marker sizing

Position (cross-space, via UniMath):
```csharp
Double3 relMetres = UniMath.RelativeMetres(ship, targetObj, gameObjects);
double  obsFactor = factor / ship.LocalPos.Scale;
var renderPos = new Vector3(
    (float)(relMetres.X * obsFactor),
    (float)(relMetres.Y * obsFactor),
    (float)(relMetres.Z * obsFactor));
```

Radius:
```csharp
double rawRadiusMeters = body.RadiusMeters > 0.0 ? body.RadiusMeters : DefaultBodyRadius;
float r = (float)((rawRadiusMeters / ship.LocalPos.Scale) * factor);
```

### UniMath LCA Position Math
**Source:** `Scripts/Math/UniMath.cs` `RelativeMetres` (line 197–198), `Distance` (line 207–208)
**Apply to:** All cross-space distance/direction calculations in TargetMarkerRenderer and TargetSelectorPanel (distance column), and Hud's new BuildFullHierarchyTargetList
```csharp
Double3 relMetres = UniMath.RelativeMetres(ship, body, gameObjects);
double  dist      = UniMath.Distance(ship, body, gameObjects);
```
Never use `body.LocalPos.ToDouble3()` or cross-scale `UniVec3` subtraction for cross-space bodies.

### Uint Bounds-Check Guard
**Source:** `Scripts/Hud/Hud.cs` lines 105, 213, 473; `Scripts/Render/WorldRenderer.cs` lines 215, 219
**Apply to:** Every `GameObjects[idx]` access in all new/modified files
```csharp
if ((uint)idx >= (uint)gameObjects.Count) continue;
var body = gameObjects[idx];
if (body == null) continue;
```

### Phosphor-Green Color
**Source:** `Scripts/Hud/Hud.cs` line 49–50
**Apply to:** TargetSelectorPanel labels, TargetMarkerRenderer outline shader uniform, tracking label
```csharp
[Export] public Color PhosphorGreen { get; set; } = new Color(0.1f, 1.0f, 0.3f);
```
Push to the shader each frame: `_outlineMaterial.SetShaderParameter("outline_color", PhosphorGreen)`.

### Read-Only Consumer Contract
**Source:** `Scripts/Hud/Hud.cs` line 8–9; `Scripts/Render/WorldRenderer.cs` line 34
**Apply to:** All new HUD and renderer files
```csharp
/// Read-only consumer of GameWorld state — MUST NOT mutate sim state.
```

### Godot Node Resolution (with fallback)
**Source:** `Scripts/Hud/Hud.cs` lines 119–135 (WorldPath → fallback FindChild pattern)
**Apply to:** TargetSelectorPanel._Ready(), TargetMarkerRenderer._Ready()
```csharp
if (WorldPath != null && !WorldPath.IsEmpty)
    _world = GetNode<TestSetup>(WorldPath);
else
    _world = GetTree().Root.FindChild("Main", true, false) as TestSetup;
```

### Camera Projection (behind-camera guard + UnprojectPosition)
**Source:** `Scripts/Hud/Hud.cs` lines 293–300 and 392–401
**Apply to:** TargetMarkerRenderer (determine on-screen vs off-screen for tracking label), Hud.UpdateSphereMarker
```csharp
Vector3 cameraLocal = _camera.GlobalTransform.AffineInverse() * (globalPos - _camera.GlobalPosition);
bool isBehindCamera = cameraLocal.Z > 0;
Vector2 screenPos   = _camera.UnprojectPosition(globalPos);
bool isOffScreen = isBehindCamera
    || screenPos.X < 0 || screenPos.X > vpSize.X
    || screenPos.Y < 0 || screenPos.Y > vpSize.Y;
```

---

## No Analog Found

No files are without a close codebase match. All four files have direct analogs.

However, the following aspects have no codebase precedent and must use the CONTEXT.md decisions as the primary reference:

| Aspect | Gap | Reference |
|--------|-----|-----------|
| `TargetSelectorPanel` panel layout (VBoxContainer / ItemList vs manual DrawString) | No Godot Control panel exists in project yet | User mock in 06-CONTEXT.md §Specific Ideas; Godot 4 `VBoxContainer`/`Label` API |
| `target_outline.gdshader` rim/fresnel or shell technique | No outline shader in project (body_lit draws filled bodies) | Claude's Discretion (06-CONTEXT.md); GLSL `dot(VIEW, NORMAL)` rim idiom |
| Panel open/close key + mouse-mode reconciliation with T-key | No precedent in project (FlightController owns mouse mode) | 06-CONTEXT.md Claude's Discretion + FlightController T-key binding |

---

## Metadata

**Analog search scope:** `Scripts/Hud/`, `Scripts/Render/`, `Scripts/Math/`, `Scripts/Flight/`, `Shaders/`
**Files scanned:** 19 C# files + 4 shaders
**Pattern extraction date:** 2026-06-20
