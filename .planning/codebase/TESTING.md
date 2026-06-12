# Testing Patterns

**Analysis Date:** 2026-06-12

## Test Framework

**Runner:**
- Not detected. No dedicated test runner configured in project.

**Build System:**
- Godot 4.6 with C# via Godot.NET.Sdk
- Target framework: `.NET 8.0` (`.NET 9.0` on Android)
- Configuration: `EcoSpace.csproj` — standard Godot C# project structure

**Run Commands:**
- No automated test commands found
- Project is designed as an executable game, not a test suite
- Manual testing via Godot editor (`F5` to play scene)

## Test File Organization

**Location:**
- `TestSetup.cs` located at `Scripts/Universe/TestSetup.cs` — **not a unit test file**
- This is a game setup/demo scene that inherits from `GameWorld`
- Tests are manual/integration-style: validates coordinate transforms and object hierarchies

**Actual Testing Approach:**
- **Integration testing via Godot scene execution:**
  - `TestSetup` class demonstrates complete workflow: object creation, positioning, space transitions
  - Output validated via `GD.Print()` console logs
  - Scene runs in `_Ready()` and `_Process()` callbacks

**Naming:**
- Test file names follow standard C# convention (PascalCase matching class name)
- No separate test project or test framework

## Test Structure

**Demo/Integration Test** (from `TestSetup.cs`):

```csharp
public partial class TestSetup : GameWorld
{
    // Setup constants
    private const double PlanetA_Z = 1.496e11;
    private const double PlanetB_Z = 2.279e11;
    private const double StarSOI = 1.5e15;
    
    // Godot lifecycle
    public override void _Ready()
    {
        base._Ready();
        SetupScene();
        PrintState("Initial state");
    }
    
    public override void _Process(double delta)
    {
        base._Process(delta);
        // Simulation logic
        TranslatePos(_ship, new Double3(0, 0, ShipSpeedMetersPerTick));
        // Assertions via GD.Print output
        GD.Print($"[Ship] space={ship.CurrentSpace...}");
    }
    
    // Setup methods
    private void SetupScene() { /* build object hierarchy */ }
    
    // Validation methods
    private void PrintState(string label) { /* debug output */ }
}
```

**Patterns:**
- **Setup:** `_Ready()` calls `SetupScene()` and `PrintState()` to log initial state
- **Execution:** `_Process(double delta)` simulates object movement and transitions
- **Validation:** `PrintState()` prints hierarchies, `GD.Print()` logs state changes

## What's Tested

**Via Integration Test** (`TestSetup`):
1. **Object hierarchy creation:** Root → Galaxy → Star → Planet A/B → Ship
2. **Coordinate conversions:** Positions expressed in local space per parent
3. **Space transitions:** Movement triggers automatic parent/space changes
4. **Distance calculations:** `UniVec3.Distance()` used to validate separation

**Example test sequence** (from `_Process`):
```csharp
TranslatePos(_ship, new Double3(0, 0, ShipSpeedMetersPerTick));  // Move ship
UniVec3 shipInStar = ship.LocalPos;
// Convert to star-space via parent chain
while (p >= 0 && GameObjects[p].CurrentSpace != UniObject.Space.Galaxy)
    shipInStar = ChildPosToParentSpace(shipInStar, GameObjects[p]);
    p = GameObjects[p].ParentIndex;
}
double distToB = UniVec3.Distance(shipInStar, planetB.LocalPos);  // Validate distance
GD.Print($"distToB={distToB:e5} m");  // Log result
if (ship.ParentIndex == _planetB)
    _arrived = true;  // Test completion
```

## Math Type Validation

**Unit-like testing patterns** (implicit in code structure):

**Double3 operations** (from `Double3.cs`):
```csharp
// Implicit validation through test of arithmetic
Double3 a = new(1.0, 2.0, 3.0);
Double3 b = new(4.0, 5.0, 6.0);
Double3 sum = a + b;  // SIMD or scalar
// Usage in TestSetup validates correctness via distance calculations
```

**Long3 / UniVec3 conversions** (from `Long3.cs` and `UniVec3.cs`):
```csharp
// Tested implicitly in TestSetup:
var (units, frac) = Long3.FromDouble3(x / scale, y / scale, z / scale);
// TestSetup uses coordinate conversions and validates object positions
```

**No formal unit tests for math.** Validation occurs through integration tests in `TestSetup` which exercises all math operations via game simulation.

## Mocking

**Not applicable:** Project uses no mock frameworks (no xUnit, NUnit, Moq, etc.)

**Godot Testing Approach:**
- No dependency injection; dependencies are direct references
- Scene-based testing via Godot's scene tree
- Object state validated via `GD.Print()` logs in editor console

## Manual Debugging & Validation

**Debug Methods** (from `GameWorld.cs`):
```csharp
private void PrintPositions(int index)
{
    if ((uint)index >= (uint)GameObjects.Count) return;
    var obj = GameObjects[index];
    var localPos = obj.LocalPos;
    int currentParent = obj.ParentIndex;
    GD.Print(localPos);
    
    while (currentParent >= 0)
    {
        var parent = GameObjects[currentParent];
        localPos = ChildPosToParentSpace(localPos, parent);
        GD.Print(localPos);
        currentParent = parent.ParentIndex;
    }
}
```

**Logging helpers** (from `TestSetup.cs`):
```csharp
private void PrintState(string label)
{
    GD.Print($"\n=== {label} ===");
    for (int i = 0; i < GameObjects.Count; i++)
    {
        var o = GameObjects[i];
        if (o == null) continue;
        GD.Print($"  [{i}] {o.CurrentSpace,-10}  parent={o.ParentIndex,2}  " +
                 $"children=[{string.Join(",", o.ChildIndices)}]  " +
                 $"pos={o.LocalPos}");
    }
    GD.Print("");
}
```

## Coverage

**Requirements:** Not enforced. No code coverage tools configured.

**Implicit Coverage:**
- Math operations (`Double3`, `Long3`, `UniVec3`) exercised throughout `TestSetup` simulation
- Game logic (`GameWorld` space transitions) validated by moving ship through orbit
- SIMD paths tested on AVX2-capable hardware (fallback scalar paths always available)

## Test Execution

**Manual testing workflow:**
1. Open `EcoSpace` project in Godot 4.6
2. Load `Main.tscn` (configured as run/main_scene in `project.godot`)
3. Press F5 or click play button
4. `TestSetup._Ready()` initializes scene and logs initial state to console
5. `TestSetup._Process()` simulates ship movement each frame
6. Monitor console output for state transitions and distance calculations
7. Simulation completes when ship reaches Planet B orbit

**Expected console output format:**
```
=== Initial state ===
  [0] Root        parent= 0  children=[1]  pos=UniVec3(...)
  [1] Universe    parent= 0  children=[2]  pos=UniVec3(...)
  ...
[Ship] space=Planet       parentIdx= 3   shipPos=...  distToB=...
[Transition ↓] Entered SOI of Star (index 2), now in Galaxy
...
=== Arrived at Planet B ===
```

## Assertions & Validation

**No formal assertions.** Validation via:
1. **Manual inspection of console output:** Check state transitions logged with `[Transition ↑]` and `[Transition ↓]` markers
2. **Arrival condition:** `_arrived` flag set when `ship.ParentIndex == _planetB`
3. **Distance tracking:** `distToB` distance printed each frame — should decrease then jump to new parent

## No Test Infrastructure

**What's missing:**
- No unit test project (no separate `.Tests.csproj`)
- No NUnit/xUnit setup
- No CI/CD test pipeline configured
- No mock framework (Moq, NSubstitute)
- No code coverage tools (OpenCover, Coverlet)
- No assertion library beyond console logging

**Design philosophy:** This is a game simulation. Testing is interactive and visual — developer watches object movements and space transitions in real-time via Godot editor.

---

*Testing analysis: 2026-06-12*
