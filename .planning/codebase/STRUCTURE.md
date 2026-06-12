# Codebase Structure

**Analysis Date:** 2026-06-12

## Directory Layout

```
eco-space/
├── Scripts/                    # C# game code (.NET 8.0)
│   └── Universe/              # Core universe simulation
│       ├── GameWorld.cs       # World management, SOI logic
│       ├── UniObject.cs       # Game object model
│       ├── UniRenderer.cs     # Shader rendering layer
│       ├── TestSetup.cs       # Test scene setup and simulation loop
│       └── Math/              # High-precision math library
│           ├── UniVec3.cs     # Unlimited-range 3D position
│           ├── Double3.cs     # SIMD-optimized double-precision vector
│           └── Long3.cs       # 64-bit integer 3D vector
│
├── Shaders/                    # Godot GLSL shaders (.gdshader)
│   ├── dithering.gdshader     # Dithering post-process effect
│   ├── crt.gdshader           # CRT simulation (unused)
│   └── tilemap_shader.gdshader # Tile mapping (unused)
│
├── Textures/                   # Image assets (empty)
│
├── Main.tscn                   # Main scene: Node3D with TestSetup script
├── icon.svg                    # Project icon
├── project.godot               # Godot 4.6 project configuration
├── EcoSpace.csproj             # C# project file (.NET 8.0)
├── EcoSpace.sln                # Visual Studio solution file
└── .planning/                  # GSD planning documents (generated)
```

## Directory Purposes

**Scripts:**
- Purpose: All C# source code for game logic and math
- Contains: GameWorld simulation, object model, SIMD math, test setup, shader control
- Key files: `Universe/GameWorld.cs`, `Universe/Math/UniVec3.cs`

**Scripts/Universe:**
- Purpose: Core universe simulation code (namespace: Universe)
- Contains: Scene logic (TestSetup), world management (GameWorld), object identity (UniObject), rendering control (UniRenderer)
- Key files: See Component Responsibilities section below

**Scripts/Universe/Math:**
- Purpose: High-precision 3D math library (namespace: Universe.Math)
- Contains: UniVec3 (composite position), Double3 (SIMD-accelerated double vector), Long3 (integer grid coordinates)
- Key files: `UniVec3.cs` (600+ lines, core abstraction), `Double3.cs` (SIMD implementation)

**Shaders:**
- Purpose: Godot GLSL post-process shaders
- Contains: Dithering effect (loaded and controlled by UniRenderer), CRT simulation, tilemap shader
- Active: Only `dithering.gdshader` is referenced and used
- Inactive: `crt.gdshader`, `tilemap_shader.gdshader` not loaded by any script

**Textures:**
- Purpose: Image assets
- Status: Empty directory (no texture files committed)

## Key File Locations

**Entry Points:**
- `Main.tscn`: Godot scene entry point (uid://cu0h3b3qv1uhi); loads TestSetup.cs script
- `Scripts/Universe/TestSetup.cs`: Game loop entry; inherits from GameWorld, calls _Ready and _Process

**Configuration:**
- `project.godot`: Godot engine settings (4.6, C#, Forward Plus renderer, 768×432 viewport)
- `EcoSpace.csproj`: C# project config (.NET 8.0, unsafe blocks enabled for SIMD, assembly name "EcoSpace")
- `EcoSpace.sln`: Visual Studio solution linking .csproj

**Core Logic:**
- `Scripts/Universe/GameWorld.cs`: World management (210 lines)
  - Public API: TranslatePos, AddGameObject, RemoveGameObject
  - Private logic: TrySpaceTransition, TryExitParentSOI, TryEnterChildSOI, ChildPosToParentSpace
- `Scripts/Universe/UniObject.cs`: Object model (80 lines)
  - Space enum (5 values: Root, Universe, Galaxy, Star, Planet)
  - Type enum (4 values: Orb, Asteroid, Ship, None)
  - Static scale factors and space conversion methods
- `Scripts/Universe/Math/UniVec3.cs`: Position representation (297 lines)
  - Composite struct: Units (Long3) + Offset (Double3) + Scale (double)
  - Core methods: Normalize, Convert, ToDouble3, Operators (+, -, *, /)

**Testing:**
- `Scripts/Universe/TestSetup.cs`: Integration test (113 lines)
  - Sets up Root → Galaxy → Star → PlanetA/B → Ship hierarchy
  - Simulates ship movement from Planet A to Planet B with SOI transitions
  - Prints state and distance tracking to console

**Rendering:**
- `Scripts/Universe/UniRenderer.cs`: Shader management (108 lines)
  - Loads dithering shader from `res://Shaders/dithering.gdshader`
  - Exposes [Export] properties for inspector control: Dithering, DitheringSize, ResolutionScale, Threshold, White, Black

## Naming Conventions

**Files:**
- Classes: PascalCase (e.g., GameWorld, UniRenderer, UniVec3)
- C# extension: `.cs` for source, `.csproj` for project
- Scenes: PascalCase.tscn (e.g., Main.tscn)
- Shaders: snake_case.gdshader (e.g., dithering.gdshader)

**Directories:**
- Namespaces: PascalCase (Universe, Universe.Math)
- Directory names: PascalCase (Scripts, Shaders, Textures, Math)

**C# Code:**
- Namespaces: PascalCase (Universe, Universe.Math)
- Classes: PascalCase (GameWorld, UniObject, UniVec3, Double3, Long3)
- Methods: PascalCase (TranslatePos, TrySpaceTransition, _Ready for Godot overrides, _Process for Godot overrides)
- Fields: _camelCase with underscore prefix for private (e.g., _material, _dithering, _ship)
- Public properties: PascalCase (e.g., Dithering, ResolutionScale)
- Constants: UPPER_SNAKE_CASE (e.g., EPSILON, ShipSpeedMetersPerTick in TestSetup)

## Where to Add New Code

**New Feature (e.g., weapon system, fuel management):**
- Primary code: `Scripts/Universe/` — create new file in this namespace, e.g., `WeaponSystem.cs`
- Integration: Add a new field to UniObject struct or as a separate tracking list in GameWorld
- Tests: Extend TestSetup._Process or create a new test scene inheriting from GameWorld

**New Component/Module (e.g., orbital mechanics, sensor system):**
- Implementation: `Scripts/Universe/ComponentName.cs` if independent, or nested file if tightly coupled
- Keep within Universe namespace unless it's a shared utility
- If it's math-heavy, put utilities in `Scripts/Universe/Math/` and reference from Scripts/Universe

**New Spatial Coordinate Type:**
- All position types go in `Scripts/Universe/Math/`
- Follow UniVec3 pattern: struct with Operators, IEquatable, ToString
- Use AggressiveInlining on frequently-called methods
- Add SIMD support (Avx.IsSupported) if performance-critical

**Utilities and Helpers:**
- Shared helpers: `Scripts/Universe/Utilities.cs` (create if doesn't exist) or alongside their primary consumer
- Math helpers: `Scripts/Universe/Math/MathHelpers.cs` or extend existing type with static methods

**Post-Processing Effects:**
- New shader: `Shaders/effect_name.gdshader`
- New UI controller: `Scripts/Universe/EffectRenderer.cs` pattern (inherit from ColorRect or appropriate Godot node, load shader in _Ready, expose [Export] properties)
- Wire into Main.tscn as a CanvasLayer child

**Tests and Debug:**
- New integration test: Create new class inheriting from GameWorld in `Scripts/Universe/`, e.g., `TestOrbitMechanics.cs`
- Attach to test scene and add to Main.tscn or create alternate test scene
- Use GD.Print for debug output (no logging framework in use)

## Special Directories

**Godot Generated:**
- `.godot/` — Engine cache and import metadata (git-ignored, not committed)
  - Contains compiled scripts, shader cache, import data
  - Safe to delete; engine regenerates on open

**Version Control:**
- `.git/` — Git repository metadata
- `.gitignore` — Excludes .godot, node_modules (if present), build artifacts
- `.gitattributes` — Git line-ending configuration

**Planning:**
- `.planning/codebase/` — GSD-generated codebase analysis documents (ARCHITECTURE.md, STRUCTURE.md, etc.)
  - Not source code; safe to regenerate
  - Consumed by /gsd-plan-phase and /gsd-execute-phase

**IDE Configuration:**
- `.vscode/` — Visual Studio Code settings
- `.editorconfig` — EditorConfig settings for consistent formatting across tools

## Import Paths and Namespaces

**C# Namespace Structure:**
```csharp
namespace Universe { ... }              // GameWorld.cs, UniObject.cs, UniRenderer.cs
namespace Universe.Math { ... }         // UniVec3.cs, Double3.cs, Long3.cs
```

**Internal Imports:**
- TestSetup imports: `using Godot; using System.Collections.Generic; using Universe.Math;`
- GameWorld imports: `using Godot; using System.Collections.Generic; using Universe.Math;`
- UniObject imports: `using System; using System.Collections.Generic; using System.Linq; using Universe.Math;`
- Math types import: `using Godot; using System; using System.Runtime.CompilerServices; using System.Runtime.Intrinsics; using System.Runtime.Intrinsics.X86;`

**Godot Resource Paths:**
- Shader load: `res://Shaders/dithering.gdshader` (relative to project root)
- Scene reference: `uid://cu0h3b3qv1uhi` (Main.tscn unique ID)

---

*Structure analysis: 2026-06-12*
