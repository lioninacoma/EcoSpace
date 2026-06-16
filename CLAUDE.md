<!-- GSD:project-start source:PROJECT.md -->

## Project

**EcoSpace**

A first-person retro space sim set in a 1:1-scale universe. The player flies a low-poly spaceship through nested coordinate spaces — from inside a star system out to galaxy scale and across to other galaxies — rendered in an 8-bit color, dithered, CRT-styled aesthetic reminiscent of early-90s space sims (Wing Commander, Elite/Frontier). It is built on an existing high-precision universe engine (hierarchical sphere-of-influence transitions, unlimited-range `UniVec3` positions, SIMD math) that keeps relative distances small enough to render a real 1:1 cosmos without floating-point drift.

**Core Value:** The player can fly seamlessly through a massive 1:1-scale universe — from a planet's surroundings out to interstellar and intergalactic distances — with rendering and flight that stay correct and feel good across every scale.

### Constraints

- **Tech stack**: Godot 4.6.2 Mono, C# 12 / .NET 8.0 — existing engine; new work stays in this stack and the folder-aligned namespace conventions
- **Architecture**: Must build on the existing `UniVec3` / SOI / `GameWorld` model — do not replace the precision/space system; flight and rendering consume it
- **Performance**: Real-time first-person rendering; SIMD math paths and floating-origin must keep per-frame cost stable across scales
- **Rendering**: Forward Plus renderer, DirectX 12 on Windows; 8-bit/dithered look achieved via post-process shaders
- **Scope discipline**: v1 uses hand-authored test data and a minimal HUD — no procedural generation, no cockpit art, no economy/combat yet

<!-- GSD:project-end -->

<!-- GSD:stack-start source:codebase/STACK.md -->

## Technology Stack

## Languages

- C# 12 - Game logic, math utilities, renderers, universe simulation
- GDScript - UI scripting (FPS display)
- GLSL (GDShader) - Post-processing shaders for dithering and CRT effects
- XML - Project configuration and solution files

## Runtime

- Godot Engine 4.6.2 (Mono build with C# support)
- .NET NuGet - C# package management (implicit via Godot.NET.Sdk)
- Godot Asset Library - Shader resources
- .NET CLI via Godot.NET.Sdk/4.6.2
- Visual Studio / Godot Editor

## Frameworks

- Godot 4.6.2 - Game engine and 3D rendering framework
- Jolt Physics Engine - 3D physics simulation (`project.godot` physics/3d/physics_engine)
- Forward Plus Renderer - 3D rendering backend
- Direct3D 12 - Windows graphics API (`project.godot` rendering_device/driver.windows)
- System.Runtime.Intrinsics - AVX2/AVX-512 SIMD operations (part of .NET runtime)
- System.Runtime.Intrinsics.X86 - x86-specific SIMD intrinsics for performance

## Key Dependencies

- Godot.NET.Sdk 4.6.2 - C# binding and compilation for Godot
- System.Runtime.CompilerServices - Method inlining attributes
- System.Runtime.InteropServices - Memory layout control for SIMD
- System.Runtime.Intrinsics - AVX2/FMA vector operations
- System.Collections.Generic - Generic collections (List<T>)
- System (base types and Math functions)
- System.Linq - LINQ for enumeration

## Configuration

- No external environment configuration required
- All settings embedded in `project.godot`
- .csproj targets net8.0 (net9.0 for Android target)
- `EcoSpace.csproj` - C# project configuration
- `project.godot` - Engine settings

## Platform Requirements

- Godot 4.6.2 Mono build (Windows, Linux, macOS)
- .NET 8.0 SDK or compatible runtime
- C# IDE support (Visual Studio Code + Godot Tools extension recommended)
- AVX2 CPU support recommended (falls back to scalar math on non-AVX2 systems)
- Godot 4.6 runtime (platform-specific builds available)
- Windows: DirectX 12 capable GPU
- Android: .NET 9.0 runtime support
- Minimum display: 768x432 (configurable in project.godot)

<!-- GSD:stack-end -->

<!-- GSD:conventions-start source:CONVENTIONS.md -->

## Conventions

## Naming Patterns

- PascalCase for class files: `UniObject.cs`, `GameWorld.cs`, `Double3.cs`
- Namespace-aware organization: Math types in `Scripts/Math/` subdirectory (global namespace); Render/Flight/Hud in their own namespaces under `Scripts/Render/`, `Scripts/Flight/`, `Scripts/Hud/`
- File names match primary type declaration
- PascalCase: `UniObject`, `GameWorld`, `UniRenderer`, `TestSetup`
- Struct names also PascalCase: `Double3`, `Long3`, `UniVec3`
- Enum names PascalCase: `Type` (with members: `Orb`, `Asteroid`, `Ship`, `None`), `Space` (with members: `Root`, `Universe`, `Galaxy`, `Star`, `Planet`)
- PascalCase for public methods: `_Ready()`, `_Process()`, `TranslatePos()`, `AddGameObject()`, `ToDouble3()`
- Godot lifecycle methods use Godot convention: `_Ready()`, `_Process(double delta)`
- Static helper methods PascalCase: `Min()`, `Max()`, `Distance()`, `Dot()`, `Cross()`
- PascalCase with backing fields: Property `Dithering` backed by `_dithering`
- Export properties use `[Export]` attribute: `Dithering`, `DitheringSize`, `ResolutionScale`, `Threshold`, `White`, `Black`
- Private fields use underscore prefix: `_material`, `_dithering`, `_ditheringSize`, `_resolutionScale`, `_threshold`, `_white`, `_black`
- Local variables use camelCase: `distMeters`, `shipInStar`, `relPos`, `childSpace`, `parentScale`
- Public fields use PascalCase: `Index`, `CurrentSpace`, `ParentIndex`, `SOIMeters`, `LocalPos`, `ChildIndices` (in `UniObject`)
- Public fields in structs use PascalCase: `X`, `Y`, `Z` (in `Double3`, `Long3`, `UniVec3`), `Units`, `Offset`, `Scale` (in `UniVec3`)
- Constants use UPPER_SNAKE_CASE: `EPSILON = 1e-11`, `PlanetA_Z = 1.496e11`, `PlanetB_Z = 2.279e11`, `StarSOI`, `PlanetSOI`

## Code Style

- EditorConfig configured at repository root (`.editorconfig`): UTF-8 charset enforcement
- 4-space indentation (inferred from code samples)
- Braces on same line (Allman style for methods, inline for properties and short blocks)
- Core sim types (GameWorld, UniObject, TestSetup) and Math types (Double3, Long3, UniVec3) are in the global namespace (no namespace wrapper); Render/Flight/Hud files use their respective namespace
- Spaces around binary operators: `a + b`, `a - b`, `a * b`, `a / b`
- Operators on inline methods use compact spacing: `public static Long3 operator +(Long3 a, Long3 b) => new(...)`
- Method chaining allows trailing operators on same line
- Performance-critical code uses `[MethodImpl(MethodImplOptions.AggressiveInlining)]` throughout:
- SIMD operations marked with `[MethodImpl(MethodImplOptions.AggressiveInlining)]` for optimal code generation
- Unsafe blocks allowed (project enables `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>`)
- Used for SIMD vector loading: `fixed (double* p = &X) return Avx.LoadVector256(p);` in `Double3.cs`
- Used for performance-critical pointer operations in math structures

## Import Organization

- Namespace-based organization, no explicit path aliases (`using` directives)
- Global-namespace types (Double3, Long3, UniVec3, GameWorld, UniObject, TestSetup) need no `using` import; Render/Flight/Hud files in their own namespaces use `using Godot;` and standard library usings only

## Comments & Documentation

- Mathematical complexity requires documentation: `Double3` struct explains SIMD strategy in header comment
- Complex algorithms documented: see `UniVec3.Normalize()` comment explaining renormalization
- Architectural decisions documented: see `UniObject` hierarchy comment in `TestSetup.cs`
- Used for public types and methods: `/// <summary>..Explanation..</summary>`
- Examples:
- Sparse, used only for non-obvious SIMD tricks: `// XOR the sign bit of all 4 lanes via a -0.0 mask`
- Sector comments demarcate logical sections: `// ── Constants ────────────────────`, `// ── Constructors ─────────`, `// ── SIMD helpers ──────────`

## Error Handling

- Bounds checking using unsigned comparison trick: `if ((uint)index < (uint)GameObjects.Count)` in `GameWorld.cs`
- Division by zero protection: `if (Mathf.Abs(s) < EPSILON) s = Mathf.Sign(s) * EPSILON;` in `UniVec3.cs`
- Return early on invalid state: `if (obj.ParentIndex < 0) return false;` in `GameWorld.cs`
- Null-coalescing in cleanup: check `if (o == null) continue;` in `TestSetup.cs` before processing
- `AddGameObject()` returns `-1` on failure to add object
- `RemoveGameObject()` silently no-ops on out-of-bounds index
- Explicit null checks: `if (o == null) continue;`
- Objects stored in `List<UniObject>` can be null (see `RemoveGameObject()` which sets `GameObjects[index] = null`)

## Logging

- Diagnostic output for game state: `GD.Print($"[Ship] space={ship.CurrentSpace...}")` in `TestSetup.cs`
- Transition events logged with direction markers: `GD.Print($"[Transition ↑] Exited SOI...")` and `GD.Print($"[Transition ↓] Entered SOI...")`
- Debug output only (not performance-critical), can be controlled by debug build flags
- String interpolation used throughout for formatting: `$"value={value,-10}  index={index,2}"`

## Function Design

- Example: `public static Double3 Cross(Double3 a, Double3 b) => new(...)`
- Complex operations split into well-named helpers: `Normalize()` delegates to `NormalizeSlow()`
- Pass by value for small types (`struct`): `Double3`, `Long3`, `UniVec3`
- Pass by reference (`in`) for read-only SIMD optimization: `ToVec256(in Double3 v)` in `Double3.cs`
- Out parameters used for multiple return values: `SplitDouble(v, out long integer, out double frac)` in `Long3.cs`
- No null parameters (structs are value types, classes use assertions)
- Return new instances for immutable operations: `a + b` returns `new Double3(...)`
- Return boolean for state checks: `TrySpaceTransition()`, `TryExitParentSOI()`, `TryEnterChildSOI()`
- Multiple returns via tuple: `FromDouble3()` returns `(Long3 units, Double3 frac)` in `Long3.cs`
- Return negative sentinel for error: `AddGameObject()` returns `-1` on failure

## Module Design

- Math types use `public struct` and `public static` methods
- Core game logic uses `public partial class` (Godot C# pattern) with `Node3D` base
- No public fields except in simple data types (`UniObject` data class)
- Core sim files at `Scripts/` root in the global namespace: `GameWorld`, `UniObject`, `TestSetup`
- Math files at `Scripts/Math/` in the global namespace (no namespace wrapper): `Double3`, `Long3`, `UniVec3`
- Folder-aligned namespaces for consumer layers: `Render` (Scripts/Render/), `Flight` (Scripts/Flight/), `Hud` (Scripts/Hud/)
- `Render` namespace: `PostProcessRenderer` (ColorRect/shader host), `WorldRenderer` (Node3D/floating-origin mesh sync)
- `Flight` namespace: `FlightController` (Node/arcade flight model)
- `Hud` namespace: `Hud` (Control/speed label)
- Clear separation: math is pure and reusable, game logic depends on math
- Structs for math types (`Double3`, `Long3`, `UniVec3`): immutable value semantics, SIMD friendly
- Classes for game entities (`UniObject`, `GameWorld`): mutable reference semantics, game state

## Position Math (UniVec3 / UniMath)

- Use `UniVec3` for all position math at galaxy/universe scale. `UniVec3` is the always-present representation for large-distance calculations; treat it as the source of truth for object positions across frames.
- Subtract and compare in `UniVec3` at the SAME scale (exact integer `Units` cancellation regardless of magnitude). Convert to metres (`ToDouble3()`) ONLY on the final, already-differenced small vector — never form an absolute-from-root metres value and then subtract, as this loses low bits and risks catastrophic cancellation at Universe scale (~1e30 m).
- Use the `UniMath` helper (global namespace, `Scripts/Math/UniMath.cs`, read-only) for all cross-space distances, body-relative positions, and up/down hierarchy conversions:
  - `UniMath.RelativePosition(from, to, objs, out UniVec3 result)` — `to − from` as a `UniVec3` in the LCA frame (exact integer Units cancellation; no `ToDouble3` inside).
  - `UniMath.RelativeMetres(from, to, objs)` — the one sanctioned metres conversion; calls `RelativePosition` then `ToDouble3()` once on the small delta.
  - `UniMath.Distance(a, b, objs)` — scalar metres distance.
  - `UniMath.ToAncestorFrame(node, ancestorIdx, objs)` — `node.LocalPos` walked up to an ancestor's child-frame as `UniVec3` via per-level `Convert + LocalPos` steps.
  - `UniMath.FindLca(a, b, objs)` — lowest-common ancestor index.
  - Do NOT hand-roll `Units * Scale + Offset` accumulation across frames.
- Caveat: `UniVec3.Convert()` and cross-scale `operator-/operator+` route through `ToDouble3()`, which zeroes `Long3.Units` and collapses everything to a single double. This is safe for SOI-bounded single `LocalPos` values (each ≤ ~1e17 m), but dangerous for absolute-from-root accumulation. Cross-scale subtraction of large positions loses precision; always go through the LCA path in `UniMath` instead.

<!-- GSD:conventions-end -->

<!-- GSD:architecture-start source:ARCHITECTURE.md -->

## Architecture

## System Overview

```text

```

## Component Responsibilities

| Component | Responsibility | File |
|-----------|----------------|------|
| TestSetup | Test scene setup and object simulation loop | `Scripts/TestSetup.cs` |
| GameWorld | World management, SOI transitions, position updates | `Scripts/GameWorld.cs` |
| UniObject | Game object identity, space, parent/child hierarchy | `Scripts/UniObject.cs` |
| UniVec3 | Universal 3D position with unlimited range & precision | `Scripts/Math/UniVec3.cs` |
| Double3 | SIMD-optimized double-precision 3D vector (32 bytes, AVX2) | `Scripts/Math/Double3.cs` |
| Long3 | 64-bit integer 3D vector for unit coordinates | `Scripts/Math/Long3.cs` |
| PostProcessRenderer | Post-process dithering/CRT shader management and parameter binding | `Scripts/Render/PostProcessRenderer.cs` |
| WorldRenderer | Floating-origin world→mesh sync, per-body lighting uniforms | `Scripts/Render/WorldRenderer.cs` |
| FlightController | Arcade flight model, mouse steering, throttle, speed envelope | `Scripts/Flight/FlightController.cs` |
| Hud | Speed HUD label, read-only consumer of world state | `Scripts/Hud/Hud.cs` |

## Pattern Overview

- Multi-scale universe represented as a tree of nested coordinate spaces (Root → Universe → Galaxy → Star → Planet)
- Each space has a distinct scale factor (1e16 down to 1e-4 meters per unit) for high-precision rendering without floating-point drift
- Automatic reparenting when objects cross SOI boundaries
- SIMD-optimized math library (Double3 uses AVX2, Double3 uses Lerp/FMA when available)
- Godot 4.6 C# with .NET 8.0, unsafe blocks enabled for SIMD intrinsics

## Layers

- Purpose: Godot scene graph and rendering
- Location: `Main.tscn`
- Contains: Node3D structure, Camera3D, lighting, CanvasLayer for UI
- Depends on: TestSetup script attachment
- Used by: Game loop; draws to screen
- Purpose: World state management and physics-independent object transitions
- Location: `Scripts/GameWorld.cs`
- Contains: GameObjects list, SOI logic, position translation, reparenting
- Depends on: UniObject, UniVec3, Double3
- Used by: TestSetup (_Process loop calls TranslatePos)
- Purpose: Game object identity and hierarchy
- Location: `Scripts/UniObject.cs`
- Contains: Space enum, Type enum, scale lookup, parent/child relationships
- Depends on: Math module for Scale() static values
- Used by: GameWorld for all object state
- Purpose: Unlimited-range position representation and SIMD-accelerated operations
- Location: `Scripts/Math/`
- Contains: UniVec3 (units + offset + scale), Double3 (SIMD), Long3 (integer grid)
- Depends on: Godot.Mathf, System.Runtime.Intrinsics (SIMD)
- Used by: UniVec3 implements position arithmetic; GameWorld uses it for translations
- Purpose: Post-processing shader effects and parameter management
- Location: `Scripts/Render/PostProcessRenderer.cs`
- Contains: ShaderMaterial binding, property export, parameter updates
- Depends on: Godot shader loading (`res://Shaders/dithering.gdshader`)
- Used by: CanvasLayer/ColorRect node in Main.tscn

## Data Flow

### Primary Request Path (Object Movement & Transition)

- Position state stored as `UniVec3(Long3 units, Double3 offset, double scale)` per object
- Parent-child hierarchy tracked via `obj.ParentIndex` and `parent.ChildIndices`
- Space hierarchy immutable via `Space` enum and static conversion methods (ChildSpace, ParentSpace)

### Secondary Flow: Coordinate Conversion

### Rendering Path

## Key Abstractions

- Purpose: Store unlimited-range high-precision positions without floating-point drift
- Examples: `Scripts/Math/UniVec3.cs`
- Pattern: Composite of Units (Long3) + Offset (Double3) + Scale (double)
- Purpose: Define which game objects exist in which coordinate frame (Root → Universe → Galaxy → Star → Planet)
- Examples: `Scripts/UniObject.cs` (Space enum and static conversion methods)
- Pattern: Static lookup tables (Scale method, ChildSpace, ParentSpace) allow zero-allocation space arithmetic
- Purpose: Lightweight struct holding index, space, parent, position, SOI radius, and children
- Examples: `Scripts/UniObject.cs`
- Pattern: Array-of-structs in GameWorld.GameObjects list; pointer-free (uses integer indices)
- Purpose: Fast vector operations with automatic fallback to scalar
- Examples: `Scripts/Math/Double3.cs`
- Pattern: MethodImpl(AggressiveInlining) + Avx.IsSupported check in every operator

## Entry Points

- Location: `Main.tscn`
- Triggers: Godot engine loads scene and calls _Ready(), then _Process each frame
- Responsibilities: Hosts TestSetup script on Main Node3D
- Location: `Scripts/TestSetup.cs` (_Ready and _Process)
- Triggers: Godot framework
- Responsibilities: Initializes scene hierarchy (planets, ship, star), updates ship position each frame, detects arrival
- Location: `Scripts/GameWorld.cs` (_Ready)
- Triggers: Godot framework
- Responsibilities: Initializes GameObjects empty list for TestSetup to populate
- Location: `Scripts/Render/PostProcessRenderer.cs` (_Ready)
- Triggers: Godot framework
- Responsibilities: Loads dithering shader and binds parameters

## Architectural Constraints

- **Threading:** Single-threaded. Godot runs _Process in the main thread; no multithreading used.
- **Global state:** GameWorld.GameObjects list is instance-level (per scene), not truly global. All state is owned by TestSetup instance.
- **Circular imports:** None detected. Clear dependency graph: TestSetup → GameWorld, UniObject (all global namespace); Render/Flight/Hud namespaces consume global-namespace types directly (no using import needed); Math module has no dependency on higher layers.
- **Precision model:** Double3 uses double (64-bit IEEE 754) within each 32-byte SIMD chunk. Long3 uses long (64-bit signed integer) for unit grid. Combined, UniVec3 provides arbitrary range (9.2e18 units per axis) with double precision within each cell.
- **SOI recursion:** TrySpaceTransition recurses up to `max(depth of object, depth of deepest child) * 2` calls in worst case (one exit, one entry per level). Unbounded but stable in practice for small hierarchies.
- **Shader hot-reloading:** Not supported. Shader is loaded once in _Ready; changes require scene reload.

## Anti-Patterns

### Null Object Reference in GameObjects List

### Direct Mutable Access to ChildIndices

## Error Handling

- Index safety: `if ((uint)index < (uint)GameObjects.Count)` (see GameWorld.TranslatePos, RemoveGameObject) — cast to uint to check both >= 0 and < Count in one comparison.
- Parent existence: `if (obj.ParentIndex < 0) return false;` — negative index indicates no parent.
- SOI boundary: If object far outside parent, TryExitParentSOI returns true; no exception.
- Shader parameter safety: PostProcessRenderer checks `_material?.SetShaderParameter(...)` with null-coalescing — no crash if material not loaded yet.

## Cross-Cutting Concerns

<!-- GSD:architecture-end -->

<!-- GSD:skills-start source:skills/ -->

## Project Skills

No project skills found. Add skills to any of: `.claude/skills/`, `.agents/skills/`, `.cursor/skills/`, `.github/skills/`, or `.codex/skills/` with a `SKILL.md` index file.
<!-- GSD:skills-end -->

<!-- GSD:workflow-start source:GSD defaults -->

## GSD Workflow Enforcement

Before using Edit, Write, or other file-changing tools, start work through a GSD command so planning artifacts and execution context stay in sync.

Use these entry points:

- `/gsd-quick` for small fixes, doc updates, and ad-hoc tasks
- `/gsd-debug` for investigation and bug fixing
- `/gsd-execute-phase` for planned phase work

Do not make direct repo edits outside a GSD workflow unless the user explicitly asks to bypass it.
<!-- GSD:workflow-end -->

<!-- GSD:profile-start -->

## Developer Profile

> Profile not yet configured. Run `/gsd-profile-user` to generate your developer profile.
> This section is managed by `generate-claude-profile` -- do not edit manually.
<!-- GSD:profile-end -->
