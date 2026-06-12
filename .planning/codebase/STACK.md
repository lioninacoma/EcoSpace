# Technology Stack

**Analysis Date:** 2026-06-12

## Languages

**Primary:**
- C# 12 - Game logic, math utilities, renderers, universe simulation
- GDScript - UI scripting (FPS display)
- GLSL (GDShader) - Post-processing shaders for dithering and CRT effects

**Secondary:**
- XML - Project configuration and solution files

## Runtime

**Environment:**
- Godot Engine 4.6.2 (Mono build with C# support)

**Package Manager:**
- .NET NuGet - C# package management (implicit via Godot.NET.Sdk)
- Godot Asset Library - Shader resources

**Build System:**
- .NET CLI via Godot.NET.Sdk/4.6.2
- Visual Studio / Godot Editor

## Frameworks

**Core:**
- Godot 4.6.2 - Game engine and 3D rendering framework

**Physics:**
- Jolt Physics Engine - 3D physics simulation (`project.godot` physics/3d/physics_engine)

**Graphics/Rendering:**
- Forward Plus Renderer - 3D rendering backend
- Direct3D 12 - Windows graphics API (`project.godot` rendering_device/driver.windows)

**Math/SIMD:**
- System.Runtime.Intrinsics - AVX2/AVX-512 SIMD operations (part of .NET runtime)
- System.Runtime.Intrinsics.X86 - x86-specific SIMD intrinsics for performance

## Key Dependencies

**Critical:**
- Godot.NET.Sdk 4.6.2 - C# binding and compilation for Godot
- System.Runtime.CompilerServices - Method inlining attributes
- System.Runtime.InteropServices - Memory layout control for SIMD
- System.Runtime.Intrinsics - AVX2/FMA vector operations

**Standard Library:**
- System.Collections.Generic - Generic collections (List<T>)
- System (base types and Math functions)
- System.Linq - LINQ for enumeration

## Configuration

**Environment:**
- No external environment configuration required
- All settings embedded in `project.godot`
- .csproj targets net8.0 (net9.0 for Android target)

**Build:**
- `EcoSpace.csproj` - C# project configuration
  - Target Framework: .NET 8.0 (primary), .NET 9.0 (Android)
  - AllowUnsafeBlocks: true - Required for SIMD pointer operations
  - EnableDynamicLoading: true - Support runtime assembly loading

**Godot Configuration:**
- `project.godot` - Engine settings
  - Name: "EcoSpace"
  - Main Scene: Main.tscn
  - Features: 4.6, C#, Forward Plus
  - Display: 768x432 viewport with integer scale mode
  - Physics: Jolt Physics
  - Graphics Driver: DirectX 12 (Windows)
  - Icon: icon.svg

## Platform Requirements

**Development:**
- Godot 4.6.2 Mono build (Windows, Linux, macOS)
- .NET 8.0 SDK or compatible runtime
- C# IDE support (Visual Studio Code + Godot Tools extension recommended)
- AVX2 CPU support recommended (falls back to scalar math on non-AVX2 systems)

**Production:**
- Godot 4.6 runtime (platform-specific builds available)
- Windows: DirectX 12 capable GPU
- Android: .NET 9.0 runtime support
- Minimum display: 768x432 (configurable in project.godot)

---

*Stack analysis: 2026-06-12*
