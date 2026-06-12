<!-- refreshed: 2026-06-12 -->
# Architecture

**Analysis Date:** 2026-06-12

## System Overview

```text
┌───────────────────────────────────────────────────────────────┐
│                    Scene Graph (Godot)                        │
│  Main.tscn: Node3D with Canvas, Camera, Lights, Renderer     │
├───────────────────────────────────────────────────────────────┤
│ TestSetup (GameWorld)          UniRenderer (ColorRect)        │
│ - Game simulation             - Shader material management    │
│ - Space transition logic      - Dithering & resolution        │
│ - Object management           - Post-processing parameters    │
├──────────────────────────────────────────────────────────────┤
│                  Universe Module (C#)                         │
│                    `Scripts/Universe/`                        │
├────────────────────┬──────────────────┬──────────────────────┤
│   GameWorld        │   UniObject      │    Math Module       │
│  `GameWorld.cs`    │  `UniObject.cs`  │  `Scripts/Universe/  │
│                    │                  │   Math/`             │
│ - Lists of       │ - Space enum     │ - UniVec3           │
│   objects        │ - Type enum      │ - Double3 (SIMD)    │
│ - SOI logic      │ - Parent/child   │ - Long3             │
│ - Transitions    │   hierarchy      │                      │
│ - Position API   │ - Scale factors  │                      │
└────────────────────┴──────────────────┴──────────────────────┘
         │                    │
         └────────────────────┘
              Shared namespace: Universe
```

## Component Responsibilities

| Component | Responsibility | File |
|-----------|----------------|------|
| TestSetup | Test scene setup and object simulation loop | `Scripts/Universe/TestSetup.cs` |
| GameWorld | World management, SOI transitions, position updates | `Scripts/Universe/GameWorld.cs` |
| UniObject | Game object identity, space, parent/child hierarchy | `Scripts/Universe/UniObject.cs` |
| UniVec3 | Universal 3D position with unlimited range & precision | `Scripts/Universe/Math/UniVec3.cs` |
| Double3 | SIMD-optimized double-precision 3D vector (32 bytes, AVX2) | `Scripts/Universe/Math/Double3.cs` |
| Long3 | 64-bit integer 3D vector for unit coordinates | `Scripts/Universe/Math/Long3.cs` |
| UniRenderer | Godot UI layer shader management and parameter binding | `Scripts/Universe/UniRenderer.cs` |

## Pattern Overview

**Overall:** Hierarchical space-scaling game world with recursive sphere-of-influence (SOI) transitions

**Key Characteristics:**
- Multi-scale universe represented as a tree of nested coordinate spaces (Root → Universe → Galaxy → Star → Planet)
- Each space has a distinct scale factor (1e16 down to 1e-4 meters per unit) for high-precision rendering without floating-point drift
- Automatic reparenting when objects cross SOI boundaries
- SIMD-optimized math library (Double3 uses AVX2, Double3 uses Lerp/FMA when available)
- Godot 4.6 C# with .NET 8.0, unsafe blocks enabled for SIMD intrinsics

## Layers

**Scene Layer:**
- Purpose: Godot scene graph and rendering
- Location: `Main.tscn`
- Contains: Node3D structure, Camera3D, lighting, CanvasLayer for UI
- Depends on: TestSetup script attachment
- Used by: Game loop; draws to screen

**Simulation Layer:**
- Purpose: World state management and physics-independent object transitions
- Location: `Scripts/Universe/GameWorld.cs`
- Contains: GameObjects list, SOI logic, position translation, reparenting
- Depends on: UniObject, UniVec3, Double3
- Used by: TestSetup (_Process loop calls TranslatePos)

**Object Model:**
- Purpose: Game object identity and hierarchy
- Location: `Scripts/Universe/UniObject.cs`
- Contains: Space enum, Type enum, scale lookup, parent/child relationships
- Depends on: Math module for Scale() static values
- Used by: GameWorld for all object state

**Math Layer:**
- Purpose: Unlimited-range position representation and SIMD-accelerated operations
- Location: `Scripts/Universe/Math/`
- Contains: UniVec3 (units + offset + scale), Double3 (SIMD), Long3 (integer grid)
- Depends on: Godot.Mathf, System.Runtime.Intrinsics (SIMD)
- Used by: UniVec3 implements position arithmetic; GameWorld uses it for translations

**Rendering Layer:**
- Purpose: Post-processing shader effects and parameter management
- Location: `Scripts/Universe/UniRenderer.cs`
- Contains: ShaderMaterial binding, property export, parameter updates
- Depends on: Godot shader loading (`res://Shaders/dithering.gdshader`)
- Used by: CanvasLayer/ColorRect node in Main.tscn

## Data Flow

### Primary Request Path (Object Movement & Transition)

1. **TestSetup._Process(delta)** → `Scripts/Universe/TestSetup.cs:54-82`
   - Called once per frame by Godot engine
   - Calls `TranslatePos(_ship, deltaVelocity)` with movement vector

2. **GameWorld.TranslatePos(index, delta)** → `Scripts/Universe/GameWorld.cs:18-28`
   - Public API; overloaded for UniVec3 and Double3
   - Calls private overload with resolved UniObject

3. **GameWorld.TranslatePos(obj, delta)** → `Scripts/Universe/GameWorld.cs:30-40`
   - Updates obj.LocalPos by adding delta (auto-normalizes via UniVec3.Normalize)
   - Calls TrySpaceTransition(obj) to handle boundary crossings

4. **GameWorld.TrySpaceTransition(obj)** → `Scripts/Universe/GameWorld.cs:48-52`
   - Recursive loop: first try exit parent SOI, then try enter child SOI
   - Repeats until stable (no more transitions)

5. **GameWorld.TryExitParentSOI(obj)** → `Scripts/Universe/GameWorld.cs:54-78`
   - Checks obj.LocalPos.Magnitude() vs parent.SOIMeters
   - If outside: reparents to grandparent, converts position space, updates CurrentSpace
   - Prints debug log `[Transition ↑] Exited SOI...`

6. **GameWorld.TryEnterChildSOI(obj)** → `Scripts/Universe/GameWorld.cs:82-117`
   - Iterates parent's ChildIndices (siblings)
   - For each: calculates relPos and distance to sibling candidate
   - If inside candidate's SOI: reparents, converts to candidate's child space
   - Prints debug log `[Transition ↓] Entered SOI...`

**State Management:**
- Position state stored as `UniVec3(Long3 units, Double3 offset, double scale)` per object
- Parent-child hierarchy tracked via `obj.ParentIndex` and `parent.ChildIndices`
- Space hierarchy immutable via `Space` enum and static conversion methods (ChildSpace, ParentSpace)

### Secondary Flow: Coordinate Conversion

When crossing SOI boundary:
1. **ChildPosToParentSpace(childSpacePos, parent)** → `Scripts/Universe/GameWorld.cs:126-130`
   - Formula: `parentSpacePos = childSpacePos.Convert(parentScale) + parent.LocalPos`
   - Handles automatic rescaling when moving between different scale contexts

### Rendering Path

1. **UniRenderer._Ready()** → `Scripts/Universe/UniRenderer.cs:85-95`
   - Loads shader from `res://Shaders/dithering.gdshader`
   - Creates ShaderMaterial and assigns to ColorRect
   - Writes initial property values to shader

2. **UniRenderer property setters** → `Scripts/Universe/UniRenderer.cs:19-83`
   - On property change (Export in Godot inspector or programmatic), calls SetShaderParameter
   - Updates dithering, resolution_scale, threshold, white/black colors in real time

## Key Abstractions

**UniVec3 Position Representation:**
- Purpose: Store unlimited-range high-precision positions without floating-point drift
- Examples: `Scripts/Universe/Math/UniVec3.cs:36-295`
- Pattern: Composite of Units (Long3) + Offset (Double3) + Scale (double)
  - WorldPosition = Units × Scale + Offset
  - Offset always kept in [0, Scale) via Normalize() after each operation
  - Supports Convert(newScale) to rescale between coordinate spaces

**Space Hierarchy:**
- Purpose: Define which game objects exist in which coordinate frame (Root → Universe → Galaxy → Star → Planet)
- Examples: `Scripts/Universe/UniObject.cs:15-66` (Space enum and static conversion methods)
- Pattern: Static lookup tables (Scale method, ChildSpace, ParentSpace) allow zero-allocation space arithmetic

**Game Object Identity:**
- Purpose: Lightweight struct holding index, space, parent, position, SOI radius, and children
- Examples: `Scripts/Universe/UniObject.cs:8-79`
- Pattern: Array-of-structs in GameWorld.GameObjects list; pointer-free (uses integer indices)

**SIMD Math:**
- Purpose: Fast vector operations with automatic fallback to scalar
- Examples: `Scripts/Universe/Math/Double3.cs:100-250`
- Pattern: MethodImpl(AggressiveInlining) + Avx.IsSupported check in every operator
  - Loads/stores via fixed pointers + Avx.Load/Store
  - Scalar fallback for non-AVX2 CPUs

## Entry Points

**Godot Scene Entry:**
- Location: `Main.tscn`
- Triggers: Godot engine loads scene and calls _Ready(), then _Process each frame
- Responsibilities: Hosts TestSetup script on Main Node3D

**Game Simulation Entry:**
- Location: `Scripts/Universe/TestSetup.cs:43-82` (_Ready and _Process)
- Triggers: Godot framework
- Responsibilities: Initializes scene hierarchy (planets, ship, star), updates ship position each frame, detects arrival

**World Management Entry:**
- Location: `Scripts/Universe/GameWorld.cs:11-14` (_Ready)
- Triggers: Godot framework
- Responsibilities: Initializes GameObjects empty list for TestSetup to populate

**Shader Rendering Entry:**
- Location: `Scripts/Universe/UniRenderer.cs:85-95` (_Ready)
- Triggers: Godot framework
- Responsibilities: Loads dithering shader and binds parameters

## Architectural Constraints

- **Threading:** Single-threaded. Godot runs _Process in the main thread; no multithreading used.
- **Global state:** GameWorld.GameObjects list is instance-level (per scene), not truly global. All state is owned by TestSetup instance.
- **Circular imports:** None detected. Clear dependency graph: TestSetup → GameWorld, UniObject, UniRenderer; Math module has no dependency on higher layers.
- **Precision model:** Double3 uses double (64-bit IEEE 754) within each 32-byte SIMD chunk. Long3 uses long (64-bit signed integer) for unit grid. Combined, UniVec3 provides arbitrary range (9.2e18 units per axis) with double precision within each cell.
- **SOI recursion:** TrySpaceTransition recurses up to `max(depth of object, depth of deepest child) * 2` calls in worst case (one exit, one entry per level). Unbounded but stable in practice for small hierarchies.
- **Shader hot-reloading:** Not supported. Shader is loaded once in _Ready; changes require scene reload.

## Anti-Patterns

### Null Object Reference in GameObjects List

**What happens:** RemoveGameObject sets `GameObjects[index] = null` to avoid re-indexing. Later code must check `if (o == null) continue;` when iterating (see TestSetup.PrintState, GameWorld._Process).

**Why it's wrong:** Sparse list creates dead slots. Forgetting a null check causes NullReferenceException. Makes debugging harder (which indices are valid?).

**Do this instead:** Use a proper free-list data structure or mark objects with a "IsAlive" flag instead of setting to null. Alternatively, maintain a separate HashMap<int, UniObject> or LinkedList<UniObject> for safe iteration.

### Direct Mutable Access to ChildIndices

**What happens:** GameWorld directly modifies `parent.ChildIndices.Add/Remove` when reparenting (lines 67, 69, 101-102, 182). No encapsulation; ChildIndices is a public List<int>.

**Why it's wrong:** External code can corrupt hierarchy by adding/removing indices without updating ParentIndex. UniObject has no invariant enforcement.

**Do this instead:** Add a Reparent(newParentIndex) method on GameWorld or UniObject that atomically updates both ChildIndices and ParentIndex. Encapsulate ChildIndices as internal or use a custom collection that enforces invariants.

## Error Handling

**Strategy:** Defensive bounds checking with silent failures (returns false/invalid index)

**Patterns:**
- Index safety: `if ((uint)index < (uint)GameObjects.Count)` (see GameWorld.TranslatePos, RemoveGameObject) — cast to uint to check both >= 0 and < Count in one comparison.
- Parent existence: `if (obj.ParentIndex < 0) return false;` — negative index indicates no parent.
- SOI boundary: If object far outside parent, TryExitParentSOI returns true; no exception.
- Shader parameter safety: UniRenderer checks `_material?.SetShaderParameter(...)` with null-coalescing — no crash if material not loaded yet.

## Cross-Cutting Concerns

**Logging:** Godot GD.Print used throughout. Used for debug output in SOI transitions (`[Transition ↑]`, `[Transition ↓]`), position tracing (PrintPositions), and state dumps (PrintState). No production log filtering; all prints go to console.

**Validation:** Limited. UniVec3.Normalize ensures Offset stays in [0, Scale). Double3 and Long3 have no range validation. GameWorld assumes valid object indices (no exception throwing, just silent early-return on invalid access).

**Authentication:** Not applicable (no network or multi-user context).

---

*Architecture analysis: 2026-06-12*
