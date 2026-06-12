# Coding Conventions

**Analysis Date:** 2026-06-12

## Naming Patterns

**Files:**
- PascalCase for class files: `UniObject.cs`, `GameWorld.cs`, `Double3.cs`
- Namespace-aware organization: Math types in `Scripts/Universe/Math/` subdirectory
- File names match primary type declaration

**Classes:**
- PascalCase: `UniObject`, `GameWorld`, `UniRenderer`, `TestSetup`
- Struct names also PascalCase: `Double3`, `Long3`, `UniVec3`
- Enum names PascalCase: `Type` (with members: `Orb`, `Asteroid`, `Ship`, `None`), `Space` (with members: `Root`, `Universe`, `Galaxy`, `Star`, `Planet`)

**Functions/Methods:**
- PascalCase for public methods: `_Ready()`, `_Process()`, `TranslatePos()`, `AddGameObject()`, `ToDouble3()`
- Godot lifecycle methods use Godot convention: `_Ready()`, `_Process(double delta)`
- Static helper methods PascalCase: `Min()`, `Max()`, `Distance()`, `Dot()`, `Cross()`

**Properties:**
- PascalCase with backing fields: Property `Dithering` backed by `_dithering`
- Export properties use `[Export]` attribute: `Dithering`, `DitheringSize`, `ResolutionScale`, `Threshold`, `White`, `Black`

**Variables & Fields:**
- Private fields use underscore prefix: `_material`, `_dithering`, `_ditheringSize`, `_resolutionScale`, `_threshold`, `_white`, `_black`
- Local variables use camelCase: `distMeters`, `shipInStar`, `relPos`, `childSpace`, `parentScale`
- Public fields use PascalCase: `Index`, `CurrentSpace`, `ParentIndex`, `SOIMeters`, `LocalPos`, `ChildIndices` (in `UniObject`)
- Public fields in structs use PascalCase: `X`, `Y`, `Z` (in `Double3`, `Long3`, `UniVec3`), `Units`, `Offset`, `Scale` (in `UniVec3`)
- Constants use UPPER_SNAKE_CASE: `EPSILON = 1e-11`, `PlanetA_Z = 1.496e11`, `PlanetB_Z = 2.279e11`, `StarSOI`, `PlanetSOI`

## Code Style

**Formatting:**
- EditorConfig configured at repository root (`.editorconfig`): UTF-8 charset enforcement
- 4-space indentation (inferred from code samples)
- Braces on same line (Allman style for methods, inline for properties and short blocks)
- Namespace nesting uses nested namespace syntax: `namespace Universe { namespace Math { ... } }` in math files

**Operator Spacing:**
- Spaces around binary operators: `a + b`, `a - b`, `a * b`, `a / b`
- Operators on inline methods use compact spacing: `public static Long3 operator +(Long3 a, Long3 b) => new(...)`
- Method chaining allows trailing operators on same line

**Inlining & Performance:**
- Performance-critical code uses `[MethodImpl(MethodImplOptions.AggressiveInlining)]` throughout:
  - All constructors in `Double3`, `Long3`, `UniVec3`
  - All arithmetic operators
  - All small helper methods
  - See `Scripts/Universe/Math/Double3.cs`, `Scripts/Universe/Math/Long3.cs`, `Scripts/Universe/Math/UniVec3.cs`
- SIMD operations marked with `[MethodImpl(MethodImplOptions.AggressiveInlining)]` for optimal code generation

**Unsafe Code:**
- Unsafe blocks allowed (project enables `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>`)
- Used for SIMD vector loading: `fixed (double* p = &X) return Avx.LoadVector256(p);` in `Double3.cs`
- Used for performance-critical pointer operations in math structures

## Import Organization

**Order:**
1. Framework imports: `using System;`, `using System.Collections.Generic;`, `using System.Linq;`
2. Runtime intrinsics (SIMD): `using System.Runtime.CompilerServices;`, `using System.Runtime.InteropServices;`, `using System.Runtime.Intrinsics;`, `using System.Runtime.Intrinsics.X86;`
3. Engine imports: `using Godot;`
4. Custom namespace imports: `using Universe.Math;`, `using Universe;`

**Path Aliases:**
- Namespace-based organization, no explicit path aliases (`using` directives)
- Nested namespaces use dot notation: `Universe.Math`

**Example ordering** (from `Double3.cs`):
```csharp
using Godot;
using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Universe
{
    namespace Math
    {
        // implementation
    }
}
```

## Comments & Documentation

**When to Comment:**
- Mathematical complexity requires documentation: `Double3` struct explains SIMD strategy in header comment
- Complex algorithms documented: see `UniVec3.Normalize()` comment explaining renormalization
- Architectural decisions documented: see `UniObject` hierarchy comment in `TestSetup.cs`

**XML Documentation (Triple-slash comments):**
- Used for public types and methods: `/// <summary>..Explanation..</summary>`
- Examples:
  - `Double3` header documents SIMD layout and strategy
  - `UniVec3` documents position representation: `WorldPosition = Units * Scale + Offset`
  - Method `Lerp()` documents implementation: `/// <summary>Component-wise linear interpolation. Uses FMA when available...</summary>`
  - Single-line doc comments for properties: `/// <summary>Fused multiply-add: (a * b) + c.</summary>`

**Inline Comments:**
- Sparse, used only for non-obvious SIMD tricks: `// XOR the sign bit of all 4 lanes via a -0.0 mask`
- Sector comments demarcate logical sections: `// ── Constants ────────────────────`, `// ── Constructors ─────────`, `// ── SIMD helpers ──────────`

## Error Handling

**Patterns:**
- Bounds checking using unsigned comparison trick: `if ((uint)index < (uint)GameObjects.Count)` in `GameWorld.cs`
- Division by zero protection: `if (Mathf.Abs(s) < EPSILON) s = Mathf.Sign(s) * EPSILON;` in `UniVec3.cs`
- Return early on invalid state: `if (obj.ParentIndex < 0) return false;` in `GameWorld.cs`
- Null-coalescing in cleanup: check `if (o == null) continue;` in `TestSetup.cs` before processing

**No exceptions thrown** in performance-critical paths. Returns negative indices (`-1`) for error states:
- `AddGameObject()` returns `-1` on failure to add object
- `RemoveGameObject()` silently no-ops on out-of-bounds index

**Null handling:**
- Explicit null checks: `if (o == null) continue;`
- Objects stored in `List<UniObject>` can be null (see `RemoveGameObject()` which sets `GameObjects[index] = null`)

## Logging

**Framework:** Godot's `GD.Print()` (no custom logging framework)

**Patterns:**
- Diagnostic output for game state: `GD.Print($"[Ship] space={ship.CurrentSpace...}")` in `TestSetup.cs`
- Transition events logged with direction markers: `GD.Print($"[Transition ↑] Exited SOI...")` and `GD.Print($"[Transition ↓] Entered SOI...")`
- Debug output only (not performance-critical), can be controlled by debug build flags
- String interpolation used throughout for formatting: `$"value={value,-10}  index={index,2}"`

## Function Design

**Size:** Prefer small, focused methods; mathematical operations are single-expression when possible:
- Example: `public static Double3 Cross(Double3 a, Double3 b) => new(...)`
- Complex operations split into well-named helpers: `Normalize()` delegates to `NormalizeSlow()`

**Parameters:**
- Pass by value for small types (`struct`): `Double3`, `Long3`, `UniVec3`
- Pass by reference (`in`) for read-only SIMD optimization: `ToVec256(in Double3 v)` in `Double3.cs`
- Out parameters used for multiple return values: `SplitDouble(v, out long integer, out double frac)` in `Long3.cs`
- No null parameters (structs are value types, classes use assertions)

**Return Values:**
- Return new instances for immutable operations: `a + b` returns `new Double3(...)`
- Return boolean for state checks: `TrySpaceTransition()`, `TryExitParentSOI()`, `TryEnterChildSOI()`
- Multiple returns via tuple: `FromDouble3()` returns `(Long3 units, Double3 frac)` in `Long3.cs`
- Return negative sentinel for error: `AddGameObject()` returns `-1` on failure

## Module Design

**Exports:**
- Math types use `public struct` and `public static` methods
- Core game logic uses `public partial class` (Godot C# pattern) with `Node3D` base
- No public fields except in simple data types (`UniObject` data class)

**Namespace Organization:**
- `Universe` root namespace for game logic
- `Universe.Math` nested namespace for math structures
- Clear separation: math is pure and reusable, game logic depends on math

**Struct vs Class Decisions:**
- Structs for math types (`Double3`, `Long3`, `UniVec3`): immutable value semantics, SIMD friendly
- Classes for game entities (`UniObject`, `GameWorld`): mutable reference semantics, game state
- `UniRenderer` extends Godot's `ColorRect` class for rendering integration

---

*Convention analysis: 2026-06-12*
