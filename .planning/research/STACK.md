# Stack Research

**Domain:** Retro first-person space sim game layer (flight, rendering, skybox, HUD) on top of an existing Godot 4.6 universe engine
**Researched:** 2026-06-12
**Confidence:** HIGH for core Godot APIs; MEDIUM for CompositorEffect C# (known bugs); HIGH for shader patterns

---

## Context: What Already Exists (Do Not Re-Implement)

The following are built and working. This document covers only what is needed on top of them.

| Existing Component | File | Notes |
|-------------------|------|-------|
| Multi-scale universe simulation | `Scripts/Universe/GameWorld.cs` | SOI transitions, reparenting |
| UniVec3 unlimited-range position | `Scripts/Universe/Math/UniVec3.cs` | Long3 + Double3 + scale |
| SIMD Double3 math (AVX2) | `Scripts/Universe/Math/Double3.cs` | Scalar fallback |
| Dithering post-process shader | `Shaders/dithering.gdshader` | `canvas_item` on ColorRect |
| CRT scanline shader (wired but unused) | `Shaders/crt.gdshader` | `canvas_item` on ColorRect |
| UniRenderer shader controller | `Scripts/Universe/UniRenderer.cs` | Exports inspector params |

---

## Recommended Stack

### Core Game-Layer Technologies

| Technology | Version | Purpose | Why Recommended |
|------------|---------|---------|-----------------|
| Godot `Node3D` (plain) | 4.6.2 | Ship root node | Floating-origin pattern requires the world to move, not the ship. `CharacterBody3D` and `RigidBody3D` both fight this — they expect to move through the world. A bare `Node3D` at origin with manual `Transform3D` manipulation gives full control. |
| Godot `Camera3D` | 4.6.2 | First-person view | Direct child of the ship `Node3D`. Set `Fov = 75`, `Near = 0.05`, `Far = 4000`. Call `MakeCurrent()` on ready. Since the ship stays at origin, the camera is always at (0,0,0) in Godot world space — exactly what floating-origin requires. |
| `Input` singleton | 4.6.2 | Ship flight controls | `Input.GetAxis()` and `Input.GetVector()` for keyboard/gamepad. `InputEventMouseMotion` via `_UnhandledInput()` for mouse look. Call `Input.SetUseAccumulatedInput(false)` in `_Ready()` for responsive mouse. |
| `Basis` / `Transform3D` | 4.6.2 | Ship orientation math | Use `Node3D.RotateObjectLocal(axis, angle)` for pitch/yaw/roll relative to ship's own frame. Call `Transform3D.Orthonormalized()` every frame to prevent float drift accumulation. Never use `rotation` (Euler) for cumulative flight rotation — use basis directly. |
| `SphereMesh` | 4.6.2 | Planet and star sphere bodies | Built-in primitive: `new SphereMesh { Radius = r, RadialSegments = 32, Rings = 16 }` assigned to a `MeshInstance3D`. 32×16 is sufficient for retro low-poly look; default 64×32 is unnecessarily dense. |
| `StandardMaterial3D` | 4.6.2 | Planet surface material | Use `ShadingMode = ShadingModeEnum.PerPixel` with `AlbedoColor` set per biome. The dithering post-process handles the retro look — planets do not need complex materials. |
| `StandardMaterial3D` (unshaded) | 4.6.2 | Star sphere material | Set `ShadingMode = ShadingModeEnum.Unshaded` and set `EmissionEnabled = true` with `EmissionEnergyMultiplier = 8f+`. Unshaded means the mesh ignores all scene lighting and glows at full intensity. Do NOT use `OmniLight3D` for star visual glow — use `EmissionEnabled` on the mesh material. |
| `OmniLight3D` | 4.6.2 | Star light cast on nearby objects | Place at star position (translated in floating-origin frame). Set `OmniRange` to cover system scale. Set `LightEnergy` and `OmniAttenuation = 1.0` (not 2.0) to avoid extreme falloff over large in-system distances. One `OmniLight3D` per star in the current SOI. |
| `WorldEnvironment` + `Sky` | 4.6.2 | Dynamic starfield / galaxy skybox | `WorldEnvironment` node holds an `Environment` resource with a `Sky` resource. Assign a `ShaderMaterial` with `shader_type sky;` to the `Sky`. Update star/galaxy uniforms from C# on SOI transitions via `ShaderMaterial.SetShaderParameter()`. |
| `CanvasLayer` + `Label` | 4.6.2 | Minimal retro HUD | `CanvasLayer` (layer = 128, above all) holds `Label` nodes for speed, scale, target. Use `DynamicFont` with a bitmap/pixel font (load via `FontFile`). Update from C# in `_Process`. |

### Supporting Technologies

| Technology | Version | Purpose | When to Use |
|------------|---------|---------|-------------|
| `ShaderMaterial` | 4.6.2 | Runtime uniform updates to sky shader | Every SOI transition: call `skyMaterial.SetShaderParameter("star_positions", ...)` to push new data. Also used by existing `UniRenderer` for dithering params. |
| `ColorRect` + `CanvasLayer` | 4.6.2 | Full-screen post-processing (existing pattern) | Continue using this for dithering and CRT shaders. Already wired via `UniRenderer`. **Stick with this pattern** — do not migrate to `CompositorEffect` for v1 (see "What NOT to Use"). |
| `InputMap` (project settings) | 4.6.2 | Named input actions | Define `ship_pitch_up`, `ship_pitch_down`, `ship_yaw_left`, `ship_yaw_right`, `ship_roll_left`, `ship_roll_right`, `ship_throttle_up`, `ship_throttle_down` in Project Settings > Input Map. Use `Input.GetAxis("ship_pitch_down", "ship_pitch_up")` to read. |
| `Jolt Physics` | 4.6 default | Collision detection only | Only needed if collision shapes are added (asteroids, station hulls). The ship itself does not use physics — it's a `Node3D` moved manually. Jolt is already the default in this project. |
| `MeshInstance3D` | 4.6.2 | Container for sphere/ship meshes | Standard node for 3D mesh rendering. Create via `new MeshInstance3D()` and `AddChild()` from C# in `_Ready()`, or place in scene. |
| `DirectionalLight3D` | 4.6.2 | Ambient fill light for deep space | A faint `DirectionalLight3D` with `LightEnergy = 0.1` prevents completely black surfaces in starfield far from any star. Only one needed per scene. |

### Shader Stack

| Shader | `shader_type` | Purpose | Notes |
|--------|--------------|---------|-------|
| `dithering.gdshader` | `canvas_item` | Luminance threshold dither (existing) | Already working. Apply 8-bit palette by extending to map luminance buckets to palette colors rather than binary black/white. |
| `crt.gdshader` | `canvas_item` | Scanline CRT overlay (existing, unused) | Wire into the existing `CanvasLayer` stack. Stack CRT on top of dithering by making it a second `ColorRect` on a higher-numbered `CanvasLayer`. |
| `sky.gdshader` (new) | `sky` | Dynamic procedural starfield + galaxy sprites | Declare `shader_type sky;`. Use `EYEDIR` for ray direction, `TIME` for twinkling, custom `uniform vec3 star_positions[]` arrays for nearby stars. Update uniforms from C# on SOI transition. Enable `use_half_res_pass` render mode for the star calculation subpass to halve GPU cost. |
| Planet surface shader (optional, new) | `spatial` | Per-planet color + dithering in local space | Optional v2 enhancement. For v1, use `StandardMaterial3D` with a flat `AlbedoColor` — the screen-space dithering post-process will handle the retro shading automatically. |

### Development Tools

| Tool | Purpose | Notes |
|------|---------|-------|
| Godot Editor 4.6.2 (Mono build) | Scene authoring, shader live-editing | Use "Remote" scene tree inspector to debug node positions at runtime |
| Visual Studio 2022 or Rider | C# IDE | `.sln` already present. Rider has better Godot 4 C# integration than VS Code for stepping through engine callbacks. |
| Godot's built-in profiler | Performance analysis | Enable via `Debug > Monitor`. Watch `3D render calls` and `shader compilations` to catch hot spots in floating-origin translation loop. |
| RenderDoc | GPU frame capture | For diagnosing shader pipeline issues (dithering order, sky blend). Use `Debug > RenderDoc` in Godot editor. |

---

## Architecture Patterns for the Game Layer

### Floating-Origin Ship Controller

The ship stays at `Vector3.Zero` in Godot's world space. Every frame, all scene `Node3D` objects (planets, stars, lights) are translated by `-shipMovement` via `GameWorld.TranslatePos()` which already exists. The new `ShipController` script translates this to Godot node positions.

```csharp
// In ShipController._Process(delta):
// 1. Read input
float pitch = Input.GetAxis("ship_pitch_down", "ship_pitch_up") * PitchSpeed * delta;
float yaw   = Input.GetAxis("ship_yaw_right",  "ship_yaw_left")  * YawSpeed  * delta;
float roll  = Input.GetAxis("ship_roll_left",  "ship_roll_right") * RollSpeed * delta;

// 2. Apply rotations around ship's LOCAL axes
RotateObjectLocal(Vector3.Right,   pitch);
RotateObjectLocal(Vector3.Up,      yaw);
RotateObjectLocal(Vector3.Forward, roll);

// 3. Prevent float drift
Transform = Transform.Orthonormalized();

// 4. Compute forward movement in ship-local frame
var forward = -Transform.Basis.Z;  // Godot: -Z is forward
float speed  = ComputeContextSpeed(); // SOI-distance-based scaling
var movement = (Double3)(forward * speed * delta);

// 5. Move the WORLD, not the ship (floating-origin)
_world.TranslatePos(_shipIndex, movement);
// GameWorld already updates UniVec3 positions and Godot node positions
```

**Why not `CharacterBody3D`:** `MoveAndCollide()` / `MoveAndSlide()` move the body's own Transform3D and call the physics server. For floating-origin, the body must stay at origin — those APIs work against that. `Node3D` + manual `Transform` manipulation is the correct primitive.

### Context-Auto-Scaling Speed

```csharp
// Distance from player to nearest SOI boundary (in meters, from UniVec3)
// Returned by GameWorld using existing SOI radius data
float distanceToSOIEdge = _world.GetDistanceToSOIBoundary(_shipIndex);
float t = Mathf.Clamp01(distanceToSOIEdge / SOI_TRANSITION_ZONE_METERS);
float speed = Mathf.Lerp(MinSpeedMs, MaxSpeedMs, Mathf.Pow(t, 2.0f));
```

`MinSpeedMs` (e.g. 100 m/s near planet) to `MaxSpeedMs` (e.g. 1e10 m/s in deep space). Uses the existing `UniVec3` magnitude for distance, so no new math is needed. The squaring of `t` gives a non-linear ramp that feels more natural.

### Mouse Look

```csharp
public override void _Ready() {
    Input.SetUseAccumulatedInput(false);
    Input.MouseMode = Input.MouseModeEnum.Captured;
}

public override void _UnhandledInput(InputEvent @event) {
    if (@event is InputEventMouseMotion motion) {
        var rel = motion.XformedBy(GetTree().Root.GetFinalTransform()).Relative;
        _yawAccum   += -rel.X * MouseSensitivity;
        _pitchAccum += -rel.Y * MouseSensitivity;
    }
}

public override void _Process(double delta) {
    // Apply accumulated mouse rotation (do NOT multiply by delta — mouse events are already frame-independent)
    RotateObjectLocal(Vector3.Up,   _yawAccum);
    RotateObjectLocal(Vector3.Right, _pitchAccum);
    Transform = Transform.Orthonormalized();
    _yawAccum = _pitchAccum = 0f;
}
```

**Critical:** `XformedBy(GetTree().Root.GetFinalTransform())` corrects for viewport stretching so sensitivity is consistent across window sizes.

### Sky Shader Pattern

The sky shader gets star/galaxy data as uniforms updated from C# on each SOI transition:

```csharp
// In GameWorld or a new SkyController, called on SOI transition:
var skyMat = (ShaderMaterial)_worldEnvironment.Environment.Sky.SkyMaterial;
skyMat.SetShaderParameter("near_star_count", nearStars.Count);
skyMat.SetShaderParameter("near_star_directions", directionArray);
skyMat.SetShaderParameter("near_star_colors", colorArray);
```

In `sky.gdshader`:
```glsl
shader_type sky;
render_mode use_half_res_pass;

uniform int near_star_count = 0;
uniform vec3 near_star_directions[16]; // max 16 nearby stars
uniform vec3 near_star_colors[16];

void sky() {
    vec3 col = vec3(0.0);
    // procedural starfield base (noise-based point stars)
    // ... render star field from EYEDIR ...
    // overlay nearby named stars
    for (int i = 0; i < near_star_count; i++) {
        float d = dot(EYEDIR, near_star_directions[i]);
        col += near_star_colors[i] * smoothstep(0.9998, 1.0, d);
    }
    COLOR = col;
}
```

**Why `shader_type sky` not a sphere mesh:** A sphere mesh skybox requires geometry and has seams/UV issues. Godot's built-in `shader_type sky` renders directly onto the sky background pass with no geometry — it is the correct pattern for a dynamic procedural sky in Godot 4.

### Star Rendering

Stars in the current SOI (the local star) are rendered as a `MeshInstance3D` with a `SphereMesh` and an **unshaded emissive** `StandardMaterial3D`:

```csharp
var mat = new StandardMaterial3D {
    ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
    EmissionEnabled = true,
    Emission = new Color(1.0f, 0.95f, 0.8f),         // warm white
    EmissionEnergyMultiplier = 12f,
    AlbedoColor = new Color(1.0f, 0.95f, 0.8f)
};
```

An `OmniLight3D` is placed at the same position as the star mesh to cast light on planets and the ship. Keep `OmniAttenuation = 0.5f` (less than physically correct 2.0) so light reaches across the system without extreme falloff.

**Do not** set `ShadowEnabled = true` on the OmniLight3D for v1 — shadow maps for omni lights are expensive (6 cubemap faces) and the retro dithered look does not require accurate shadows.

---

## What NOT to Use

| Avoid | Why | Use Instead |
|-------|-----|-------------|
| `CharacterBody3D` as ship root | `MoveAndCollide`/`MoveAndSlide` moves the body's own transform. Floating-origin requires the ship to stay at world zero; these APIs fight that invariant. | Bare `Node3D` at origin with manual `Transform` manipulation |
| `RigidBody3D` for ship | Physics forces accumulate in the physics server. The floating-origin model bypasses the physics server entirely for ship movement. | `Node3D` + manual velocity integration |
| `CompositorEffect` in C# for v1 | As of Godot 4.4/4.5, C# `CompositorEffect` has a confirmed MSAA bug (Issue #95286, #105637) where creating uniform sets with the color buffer fails. GDScript version works; C# does not reliably. The existing `ColorRect` + `canvas_item` approach is stable, already wired, and sufficient for dithering + CRT. | Existing `ColorRect` / `canvas_item` shader pipeline via `UniRenderer` |
| `PanoramaSkyMaterial` (static texture) | A static panorama cannot be updated on SOI transitions without re-uploading the entire texture — expensive and not real-time. | `ShaderMaterial` with `shader_type sky` — updated via `SetShaderParameter()` per transition |
| `ProceduralSkyMaterial` | Designed for planetary atmospheres (Rayleigh/Mie scattering). Produces a sky-blue dome, which is wrong for space. Customization requires converting to `ShaderMaterial` anyway. | `ShaderMaterial` with `shader_type sky` directly |
| `DirectX 12` depth-stencil via `CompositorEffect` | Accessing depth buffers from C# compositor effects has additional MSAA complications. Depth-based effects (depth of field, SSAO) are not needed for v1 retro look. | Skip depth-buffer effects in v1 |
| Euler-angle rotation (`Node3D.Rotation`) | Accumulating Euler rotations for free-space flight causes gimbal lock. The documentation explicitly warns against using the `rotation` property for games with cumulative rotations. | `RotateObjectLocal()` + `Transform.Orthonormalized()` every frame |
| `OmniLight3D` for star visual glow | Light nodes cast into the scene; they do not make the source mesh appear bright. At large distances, `OmniLight3D` contributes nothing visually to the star sphere itself. | `StandardMaterial3D` with `Unshaded` + `EmissionEnabled` for the visual glow; a separate `OmniLight3D` only for illuminating nearby planets |
| `ShadowEnabled = true` on `OmniLight3D` | Omni shadow maps require rendering 6 cubemap faces per light per frame. In a retro dithered aesthetic, shadows are not visible through the post-process anyway. | No shadows in v1; if needed later, use `DirectionalLight3D` from star direction instead |
| Per-pixel planet shaders (v1) | Complex planet surface shaders (atmosphere, terrain) are premature. The dithering post-process handles shading. | Flat `AlbedoColor` on `StandardMaterial3D` per planet; the screen-space dither provides the retro look |
| Multiplying mouse motion delta by `Time.GetTicksMsec()` | Mouse `InputEventMouseMotion.Relative` is already frame-independent — it is the physical motion since the last event. Multiplying by delta introduces frame-rate dependency. | Apply mouse delta directly to rotation accumulator; reset accumulator each `_Process` frame |

---

## Alternatives Considered

| Recommended | Alternative | When to Use Alternative |
|-------------|-------------|-------------------------|
| `Node3D` (plain) as ship root | `CharacterBody3D` | Only if you add collision detection against asteroids/stations in a later milestone — then wrap `CharacterBody3D` behavior but keep floating-origin translation in `_PhysicsProcess` |
| `shader_type sky` (ShaderMaterial) | `MeshInstance3D` sphere with sky texture | If you need parallax or explicit cubemap — unlikely for this project's procedural approach |
| `ColorRect` + `canvas_item` shaders | `CompositorEffect` (C#) | Consider `CompositorEffect` (GDScript) in a later milestone if screen-space effects needing depth or velocity buffers are required (motion blur, SSAO). Check that the MSAA C# bug is fixed before using in C#. |
| `OmniLight3D` per star (one at a time) | `DirectionalLight3D` | For a single-star system, a directional light from the star direction is cheaper than an omni. Use directional if the player never gets close enough to the star for falloff to matter. Switch to omni only when building multi-star or fly-near-star mechanics. |
| Flat `AlbedoColor` planets (v1) | Full spatial shader per planet | Upgrade to per-planet spatial shaders when adding biome/atmosphere rendering in a later milestone |

---

## Version Compatibility Notes

| Component | Godot Version | Notes |
|-----------|--------------|-------|
| `CompositorEffect` (GDScript) | 4.3+ | Introduced in 4.3. Experimental. GDScript stable; C# has MSAA bugs through at least 4.5. |
| `CompositorEffect` (C#) | 4.4+ (buggy) | C# port requires 4.4+. Avoid for v1 — stick with ColorRect. |
| `shader_type sky` | 4.0+ | Fully stable. `use_half_res_pass` and `use_quarter_res_pass` render modes available since 4.0. |
| `SphereMesh` C# | 4.0+ | `new SphereMesh()` — constructor available in C# bindings. Set `.Radius`, `.RadialSegments`, `.Rings` directly. |
| `Input.SetUseAccumulatedInput(false)` | 4.0+ | Available since 4.0. Required for responsive mouse look. |
| `RotateObjectLocal()` | 4.0+ | Stable. The equivalent of Godot 3's `rotate_object_local()`. |
| `Transform3D.Orthonormalized()` | 4.0+ | Stable. Returns corrected transform; does not modify in place — must assign back: `Transform = Transform.Orthonormalized()`. |
| `ShaderMaterial.SetShaderParameter()` | 4.0+ | C# API stable. Parameter name is case-sensitive and matches the GLSL uniform name exactly (not the inspector-capitalized name). |
| `Jolt Physics` as default | 4.6 | Jolt became the default 3D physics engine in 4.6. Already configured in this project. No action needed unless you switch to collision-based ship movement. |
| `ReflectionProbe` octahedral maps | 4.6 | Changed from cubemap to octahedral in 4.6 — reduces GPU memory. Relevant if reflection probes are added later. |
| SSR full/half-resolution mode | 4.6 | SSR was rewritten in 4.6. New `use_full_precision` flag. Irrelevant for v1 (no SSR needed in retro look). |

---

## Installation / Setup

No new packages need to be installed. The recommended stack is entirely within Godot 4.6.2's built-in APIs and the existing `.NET 8.0` C# project. All new work follows the existing `Universe` namespace conventions.

**New files to create (suggested locations):**

```
Scripts/Universe/
├── ShipController.cs      # Node3D ship root: input, rotation, context-speed, floating-origin translation
├── SkyController.cs       # Manages WorldEnvironment + sky ShaderMaterial uniform updates on SOI transitions
├── BodyRenderer.cs        # Factory: creates MeshInstance3D + SphereMesh + material for planets/stars
└── HudController.cs       # CanvasLayer + Label nodes for speed/scale/target display

Shaders/
└── sky.gdshader           # shader_type sky; dynamic starfield + galaxy background

Scenes/
└── GameScene.tscn         # Replaces/extends Main.tscn: adds Camera3D, WorldEnvironment, OmniLight3D
```

**Scene node structure for game layer:**

```
Node3D "Ship" (ShipController.cs) — at origin, stays at (0,0,0)
├── Camera3D (fov=75, near=0.05, far=4000, current=true)
├── MeshInstance3D "ShipMesh" (low-poly ship, optional v1)
└── CollisionShape3D (optional, only if Jolt collision needed)

WorldEnvironment (Environment → Sky → ShaderMaterial: sky.gdshader)
DirectionalLight3D "AmbientFill" (energy=0.05, no shadows)

Node3D "StarRoot" (planet/star instances added here by BodyRenderer.cs)
├── MeshInstance3D "Star" (SphereMesh, StandardMaterial3D unshaded emissive)
├── OmniLight3D "StarLight" (at star position, attenuation=0.5)
├── MeshInstance3D "PlanetA" (SphereMesh, StandardMaterial3D flat color)
└── MeshInstance3D "PlanetB" ...

CanvasLayer (layer=10) "PostProcess"
├── ColorRect (dithering.gdshader) — UniRenderer
└── ColorRect (crt.gdshader) — wire this in

CanvasLayer (layer=128) "HUD" — HudController.cs
├── Label "SpeedLabel"
└── Label "ScaleLabel"
```

---

## Sources

- [Godot 4.6 Release Notes](https://godotengine.org/releases/4.6/) — Jolt default, SSR rewrite, reflection probe changes (HIGH — official)
- [Godot Docs: Sky Shaders](https://docs.godotengine.org/en/stable/tutorials/shaders/shader_reference/sky_shader.html) — EYEDIR, POSITION, render modes, half-res pass (HIGH — official)
- [Godot Docs: CompositorEffect](https://docs.godotengine.org/en/stable/classes/class_compositoreffect.html) — effect callback types, experimental status (HIGH — official)
- [Godot Docs: Custom Post-Processing](https://docs.godotengine.org/en/stable/tutorials/shaders/custom_postprocessing.html) — ColorRect/CanvasLayer approach, screen texture (HIGH — official)
- [Godot Docs: Using 3D Transforms](https://docs.godotengine.org/en/stable/tutorials/3d/using_transforms.html) — RotateObjectLocal, orthonormalize, Euler pitfall (HIGH — official)
- [Godot Issue #95286](https://github.com/godotengine/godot/issues/95286) — C# CompositorEffect engine freeze on re-open (HIGH — confirmed bug)
- [Godot Issue #105637](https://github.com/godotengine/godot/issues/105637) — C# MSAA missing texture storage bit in CompositorEffect (HIGH — confirmed bug)
- [Arcade Spaceship — Godot 4 Recipes](https://kidscancode.org/godot_recipes/4.x/3d/spaceship/index.html) — CharacterBody3D flight pattern reference (MEDIUM — community tutorial, verified against official APIs)
- [Mouse Input Best Practices — Yo Soy Freeman](https://yosoyfreeman.github.io/article/godot/tutorial/achieving-better-mouse-input-in-godot-4-the-perfect-camera-controller/) — SetUseAccumulatedInput, XformedBy, no-delta rule (MEDIUM — community, cross-referenced with official docs)
- [C# CompositorEffect port](https://github.com/Rokojori/godot-compositor-effect-c-sharp) — requires Godot 4.4+, MSAA bug documented (MEDIUM — community)
- [Godot Docs: SphereMesh](https://docs.godotengine.org/en/stable/classes/class_spheremesh.html) — properties, defaults (HIGH — official)
- [Godot Docs: OmniLight3D](https://docs.godotengine.org/en/stable/classes/class_omnilight3d.html) — attenuation, range behavior (HIGH — official)
- [Godot Docs: ShaderMaterial](https://docs.godotengine.org/en/stable/classes/class_shadermaterial.html) — SetShaderParameter case-sensitivity, per-instance uniforms (HIGH — official)

---

*Stack research for: EcoSpace retro space sim game layer*
*Researched: 2026-06-12*
