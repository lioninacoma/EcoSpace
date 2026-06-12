# Codebase Concerns

**Analysis Date:** 2026-06-12

## Tech Debt

**Null Slot Compaction in GameWorld**
- Issue: `RemoveGameObject()` sets a list slot to null rather than removing the element, leaving "holes" in the `GameObjects` list.
- Files: `Scripts/Universe/GameWorld.cs` (line 200)
- Impact: Over time, null slots accumulate, causing index-based lookups to access null entries. This creates fragmentation and memory waste, and requires null checks everywhere indices are used.
- Fix approach: Implement compaction logic that either: (a) maintains a free-list for O(1) reuse, or (b) rebuilds parent-child indices when compacting. Add a `CompactGameObjects()` method and call it periodically or after bulk removals.

**Unsafe SIMD Intrinsics in Double3**
- Issue: `Double3.cs` uses unsafe pointer manipulation with `fixed (double* p = &X)` for loading SIMD vectors, relying on struct field layout assumptions.
- Files: `Scripts/Universe/Math/Double3.cs` (lines 62-72)
- Impact: Behavior is undefined if struct layout changes, the JIT optimization order shifts, or unsafe code is compiled differently. Crashes are hard to debug. No runtime safety guard.
- Fix approach: Add compile-time struct layout verification via a static constructor check. Document the exact layout contract (`[StructLayout(LayoutKind.Sequential, Size = 32)]` already exists but needs validation). Consider asserting the byte offset of X in unsafe blocks.

**Missing Validation in UniObject Space Transitions**
- Issue: `TryExitParentSOI()` and `TryEnterChildSOI()` assume parent/sibling objects exist without null checks after index lookup.
- Files: `Scripts/Universe/GameWorld.cs` (lines 59, 86, 93)
- Impact: If `RemoveGameObject()` leaves a null slot and that index is referenced in a parent-child relationship, dereferencing it crashes. Null propagation cascades through recursive transitions.
- Fix approach: Add null checks after every `GameObjects[index]` lookup. Consider storing actual object references instead of indices for parent/sibling relationships.

**Floating-Point Precision Edge Case in UniVec3.Convert()**
- Issue: `Convert(double newScale)` compares scales with `Mathf.Abs(Scale - newScale) < EPSILON` (1e-11), but floating-point rounding errors may accumulate after multiple transitions, violating the epsilon contract.
- Files: `Scripts/Universe/Math/UniVec3.cs` (line 123)
- Impact: Objects may get rescaled when they shouldn't (performance penalty) or skipped when they should (precision loss in very large universes or many cascading transitions).
- Fix approach: Use a relative epsilon: `Mathf.Abs((newScale - Scale) / newScale) < EPSILON`. Add logging in debug builds to track scale changes and detect epsilon violations.

**Hardcoded Shader Path in UniRenderer**
- Issue: `UniRenderer._Ready()` hardcodes the shader path as `"res://Shaders/dithering.gdshader"` with no fallback or validation.
- Files: `Scripts/Universe/UniRenderer.cs` (line 89)
- Impact: If the shader is moved, renamed, or deleted, the game fails silently or crashes at runtime. No clear error message about the missing asset.
- Fix approach: Add a try-catch around `GD.Load<Shader>()`. Log a clear error and disable the renderer if the shader is not found. Alternatively, embed the shader as a fallback or use EditorScript hints to validate asset references.

## Known Bugs

**Double-Reparenting in TryEnterChildSOI**
- Symptoms: When a ship enters a sibling's SOI (e.g., moves from Planet A's space into Planet B's space), it is reparented and rescaled. If the rescaling math is off by even a small factor, the ship's local position becomes incorrect.
- Files: `Scripts/Universe/GameWorld.cs` (lines 100-109)
- Trigger: Move an object within a star system such that it crosses multiple planet SOI boundaries in quick succession.
- Workaround: Log intermediate state with `PrintPositions()` to verify the rescaling. Frame-step through the transitions if precision issues appear.

**Missing _Process Implementation in GameWorld**
- Symptoms: `GameWorld._Process(double delta)` is empty (line 203), but `TestSetup._Process(double delta)` performs all the physics updates. This breaks the abstraction: derived classes must remember to override _Process or the base class silently does nothing.
- Files: `Scripts/Universe/GameWorld.cs` (line 203), `Scripts/Universe/TestSetup.cs` (lines 54-82)
- Trigger: Implement a derived class that forgets to call or override _Process; objects won't move.
- Workaround: Always override _Process in derived classes. Document this contract in GameWorld class comments.

**Null Dereference in PrintState (Defensive Code Missing)**
- Symptoms: `TestSetup.PrintState()` iterates over `GameObjects` and skips null entries (line 104: `if (o == null) continue;`), but `PrintPositions()` in GameWorld does not—it will crash if a null slot exists.
- Files: `Scripts/Universe/GameWorld.cs` (line 134-150)
- Trigger: Call `PrintPositions()` after `RemoveGameObject()` leaves a null slot.
- Workaround: Don't call `PrintPositions()` on lists with null slots, or add the null check.

## Security Considerations

**No Input Validation on SOI Distances**
- Risk: `AddGameObject()` accepts any double for `soiMeters`, including negative, zero, or infinity. Negative SOI causes space-transition logic to malfunction unpredictably.
- Files: `Scripts/Universe/GameWorld.cs` (line 155)
- Current mitigation: None.
- Recommendations: Validate that `soiMeters >= 0.0` in `AddGameObject()`. Treat zero SOI as "no children allowed" and infinity as "never exit." Document the valid range.

**Unsafe SIMD on Non-AVX2 Hardware**
- Risk: Code uses `if (Avx.IsSupported)` guards, but if the guard check is incorrect or the CPU reports support falsely, SIMD calls on unsupported hardware cause crashes.
- Files: `Scripts/Universe/Math/Double3.cs` (multiple lines with `if (Avx.IsSupported)`)
- Current mitigation: Scalar fallback path exists.
- Recommendations: Test on non-AVX2 hardware (older CPUs, ARM). Log CPU capability detection on startup. Consider using `Vector.IsHardwareAccelerated` as a more general guard.

**No Bounds Checking on Enum-to-Index Conversions**
- Risk: `UniObject.IndexToSpace()` and `SpaceToIndex()` perform enum-array conversions without verifying that the Space enum still has the same count.
- Files: `Scripts/Universe/UniObject.cs` (lines 56-66)
- Current mitigation: Clamp to array bounds in IndexToSpace (line 59).
- Recommendations: Add unit tests that verify enum count consistency. Consider using a dictionary instead of array indexing to avoid bounds errors entirely.

## Performance Bottlenecks

**Recursive Space Transition Stacking**
- Problem: `TrySpaceTransition()` is recursive, calling itself after each parent SOI exit or child SOI entry. With a deep hierarchy (many nested spaces), a single movement can trigger O(depth) recursive calls.
- Files: `Scripts/Universe/GameWorld.cs` (lines 48-52)
- Cause: No state machine or loop; instead, recursion. For a 5-level hierarchy, one misplaced movement causes 5+ recursive calls.
- Improvement path: Convert to an iterative loop: `while (NeedsTransition(obj)) { ... Transition(obj); }`. Measure recursion depth in a test scenario to confirm the impact.

**Full List Iteration in TryEnterChildSOI**
- Problem: Every frame, the code iterates over all siblings (line 88: `foreach (int siblingIndex in parent.ChildIndices)`) to check SOI entry. With many siblings, this is O(n) per object per frame.
- Files: `Scripts/Universe/GameWorld.cs` (lines 82-117)
- Cause: Linear search for any sibling within SOI range.
- Improvement path: Use spatial partitioning (octree or grid) to cull candidates before iteration. Or use a physics engine (Godot's built-in or Bullet) to detect collisions/overlaps and trigger transitions.

**Magnitude Computation on Every Distance Check**
- Problem: `Distance()` calls `Magnitude()` which calls `Sqrt()` (expensive). In hot loops (like checking distance to all planets), this adds up.
- Files: `Scripts/Universe/Math/UniVec3.cs` (line 207)
- Cause: No `DistanceSq()` method exposed in UniVec3; `TestSetup` compares `distToB` by computing the full magnitude.
- Improvement path: Export `DistanceSq()` and compare squared distances to planets' squared SOI radii. Update SOI checks to use squared distances throughout.

**Repeated Property Access in Shader Parameter Updates**
- Problem: `UniRenderer` checks `if (Avx.IsSupported)` in each property setter (lines 20-83) to decide whether to update the shader. This is not expensive (CPU check), but the logic is repetitive and could be optimized.
- Files: `Scripts/Universe/UniRenderer.cs` (lines 85-106)
- Cause: No caching of Material reference before _Ready(); Material is lazily created in _Ready.
- Improvement path: Minor—ensure `_material` is not null in setters. Consider batch updates if many parameters change at once.

## Fragile Areas

**Space Hierarchy Invariants**
- Files: `Scripts/Universe/GameWorld.cs`, `Scripts/Universe/UniObject.cs`
- Why fragile: The code maintains parent-child relationships by index reference. If an object is removed (leaving a null slot) and then a new object takes its place at a different index, old parent-child links become dangling pointers. The invariant "ParentIndex always points to a valid parent" breaks silently.
- Safe modification: Always validate indices before dereferencing. Consider using object references instead of indices. Add assertion methods to verify invariants at the end of each transition.
- Test coverage: Gaps—no unit tests for space transitions. Add test cases for: (a) ship moving between planets, (b) removing a planet and checking orphaned ships, (c) deep hierarchies (root→galaxy→star→planet→moon→...→object).

**SIMD Vector Layout in Double3**
- Files: `Scripts/Universe/Math/Double3.cs`
- Why fragile: The struct relies on `[StructLayout(LayoutKind.Sequential, Size = 32)]` to guarantee that fields X, Y, Z are contiguous in memory at byte offsets 0, 8, 16. Unsafe pointer code assumes this. A future .NET version might reorder fields or change layout rules.
- Safe modification: Document the struct layout in comments. Add compile-time assertions (e.g., `static Double3 { Assert(typeof(Double3).GetField("X").FieldHandle.ToUIntPtr() == IntPtr.Zero); }`). Test on different .NET versions and architectures (x86, ARM).
- Test coverage: No SIMD unit tests—missing tests to verify that `Avx.Add()` produces the same result as scalar fallback.

**Enum-to-Space Mapping**
- Files: `Scripts/Universe/UniObject.cs` (lines 20-66)
- Why fragile: `ChildSpace()`, `ParentSpace()`, and static conversion methods use switch expressions that hardcode the Space enum tree. If a new Space is added (e.g., `Asteroid`), all these methods must be updated, and old code that doesn't know about it will silently treat it as Planet.
- Safe modification: When adding a new Space, search for all uses of Space enum and update exhaustively. Consider using a database or data-driven approach instead of hardcoded switches.
- Test coverage: No tests for enum conversions. Add parameterized tests: `[InlineData(Space.Root, Space.Universe), ...]` to verify the hierarchy is consistent.

**GameWorld Remove → Null Slots**
- Files: `Scripts/Universe/GameWorld.cs` (lines 192-201)
- Why fragile: Once `RemoveGameObject()` creates a null slot, any code path that expects a valid object can crash. The fragility spreads to all users of `GameObjects[index]`.
- Safe modification: Use compaction immediately after removal, or maintain a generation counter on each slot to detect stale references. Or switch to a generational arena allocator.
- Test coverage: No tests for object removal. Add: (a) add, remove, add (check no crashes), (b) remove from middle of list, verify next access, (c) remove while transitioning (concurrent modification).

## Scaling Limits

**Universe Size via Long3 (Grid Coordinates)**
- Current capacity: ~9.2 × 10^18 units per axis
- Limit: 64-bit signed integer max
- Scaling path: If universe needs to grow beyond this, switch to BigInteger (slow) or use a different coordinate system (e.g., three-level hierarchy: super-region → region → unit).

**Recursive Depth in TrySpaceTransition**
- Current capacity: Limited by call stack depth (~1000-5000 frames on typical systems)
- Limit: If object moves through >1000 nested spaces in one frame, stack overflow
- Scaling path: Convert recursion to iteration. Measure max nesting depth in test scenario; currently capped by solar system (5 levels: root → universe → galaxy → star → planet), so not an issue yet.

**GameObjects List Memory**
- Current capacity: Limited by available heap (~1 GB → ~10^7 objects at ~100 bytes per object)
- Limit: If the game spawns millions of objects, list allocation becomes a bottleneck
- Scaling path: Use object pooling (reuse removed objects). Switch to a custom sparse array or handle-based store instead of List<T>. Consider off-heap storage (C++ interop).

## Dependencies at Risk

**Godot.NET.Sdk 4.6.2**
- Risk: Hardcoded in `.csproj`. No patch strategy if a security issue is found in 4.6.2 or if a critical bug blocks your use case.
- Impact: Must rebuild entire project. No easy rollback if update breaks compatibility.
- Migration plan: Define a minimum SDK version range in `.csproj` (e.g., `<Version>4.6.2+</Version>`). Test against Godot 4.7+ when available. Maintain a changelog of SDK-specific code (unsafe SIMD, Godot API usage) to track upgrade risk.

**Unsafe SIMD Hardware Assumption (Avx, Fma)**
- Risk: Code gracefully falls back to scalar, but Avx/Fma support detection could fail silently or be spoofed by hardware.
- Impact: Performance regression on old hardware if fallback is slower than expected.
- Migration plan: Add performance benchmarks for SIMD vs. scalar paths. Test on low-end CPUs. Consider removing SIMD and using vectorized LINQ / `Span<T>` instead (slower but safer).

## Missing Critical Features

**No Physics Integration**
- Problem: Space transitions are computed manually via distance checks. There's no collision detection, gravity, or orbital mechanics. If the game needs to add realistic physics (gravity between planets, orbital decay), the current manual system must be replaced.
- Blocks: Realistic multi-body simulation, AI autopilot to planets, collision-based damage.

**No Persistence / Serialization**
- Problem: Game state (GameObjects list, positions, hierarchies) cannot be saved or loaded. Every scene reset creates a fresh world.
- Blocks: Save games, checkpoint system, world migration between scenes.

**No Networking / Multiplayer**
- Problem: All logic is single-player, single-thread. No RPC, replication, or state sync.
- Blocks: Multiplayer gameplay, distributed world server.

**No Editor Integration**
- Problem: Objects are created via `AddGameObject()` in code; no visual scene editor for the universe hierarchy.
- Blocks: Level design, quick iteration, drag-and-drop object placement.

## Test Coverage Gaps

**Space Transition Logic (High Priority)**
- What's not tested: Core gameplay—moving objects between spaces, rescaling, reparenting.
- Files: `Scripts/Universe/GameWorld.cs` (entire TrySpaceTransition() family)
- Risk: Bugs in transition math go unnoticed until production. Edge cases (null slots, invalid indices) cause crashes.
- Priority: High

**SIMD Intrinsics Correctness (High Priority)**
- What's not tested: AVX2 SIMD paths vs. scalar fallback equivalence.
- Files: `Scripts/Universe/Math/Double3.cs`
- Risk: SIMD code produces slightly different results due to ordering or precision; crashes or ghost objects on systems with AVX2 enabled.
- Priority: High

**Enum Hierarchy Consistency (Medium Priority)**
- What's not tested: Space enum conversions (ChildSpace, ParentSpace, IndexToSpace).
- Files: `Scripts/Universe/UniObject.cs`
- Risk: Adding a new Space breaks silently; conversion methods return wrong values.
- Priority: Medium

**GameWorld Remove & Null Slot Handling (Medium Priority)**
- What's not tested: Removing objects and then accessing the list.
- Files: `Scripts/Universe/GameWorld.cs` (RemoveGameObject, PrintPositions)
- Risk: Null dereference crashes if PrintPositions is called after removal.
- Priority: Medium

**Long3 and Double3 Precision (Low Priority)**
- What's not tested: Conversion accuracy, rounding, overflow.
- Files: `Scripts/Universe/Math/Long3.cs`, `Scripts/Universe/Math/Double3.cs`
- Risk: Silent data corruption in edge cases (very large coordinates, near-overflow values).
- Priority: Low

---

*Concerns audit: 2026-06-12*
