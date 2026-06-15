---
phase: 02-dynamic-skybox
verified: 2026-06-15T00:00:00Z
status: passed
score: 4/4 must-haves verified (SC2 confirmed via UAT play-test 2026-06-15)
human_verification_result: all 3 items PASS (02-UAT.md, 2026-06-15)
overrides_applied: 0
human_verification:
  - test: "Fly in Star space (or Planet space) and confirm the three sibling stars (ALPHA CEN, BARNARD, SIRIUS) appear as discrete light points on the sky — not a black sky, not a screen-filling color"
    expected: "2-3 distinct lit points visible, differing in brightness (Sirius brightest, Barnard dimmest per inverse-square magnitude model)"
    why_human: "sky() shader output and star_dirs/star_colors/star_sizes uniform-array acceptance from C# in Godot 4.6.2 Mono/DX12 cannot be confirmed by grep"
  - test: "Rotate the ship (pitch, yaw, roll) while looking at the sibling star points and confirm the points stay pinned to world directions — they must NOT rotate or drift with the camera"
    expected: "Points remain stationary as the ship rotates; EYEDIR world-space projection confirmed"
    why_human: "Runtime-only: the EYEDIR-is-world-space property of Godot 4.6 sky shaders (documented in header) must be confirmed against the actual engine build on DX12/Forward+"
  - test: "Toggle dithering (PostProcessRenderer FullscreenRect) on/off and confirm the sky points' pixelation changes — proving the sky passes through the existing dither post-process"
    expected: "Sky points quantize/de-quantize along with the rest of the scene; no second dither pass added"
    why_human: "Requires visual inspection of the dither-to-sky integration (hint_screen_texture captures sky output in DX12/Forward+ — research Open Question 1/A5)"
---

# Phase 02: Dynamic Skybox — Verification Report

**Phase Goal:** A stable spherical skybox represents only the next scale tier out as distant light points — other systems' stars (and galaxies) while in-system, and only other galaxies while in Galaxy space — updates when the player crosses a scale boundary, never drifts with camera rotation, and hands off seamlessly to/from meshes. Render scope is the current scale TIER, not the immediate SOI parent.
**Verified:** 2026-06-15
**Status:** human_needed
**Re-verification:** No — initial verification

---

## Goal Achievement

### Observable Truths (Roadmap Success Criteria)

| # | Truth | Status | Evidence |
|---|-------|--------|----------|
| SC1 | Re-tier logic classifies which bodies are skybox points vs current-tier meshes for the ship's space; verified by unit tests; in-system sibling-star points projected at world-fixed directions | VERIFIED | `TierClassifier.Classify` in `Scripts/TierClassifier.cs` (75 lines, no Godot dep); 18/18 xUnit tests green (`dotnet test EcoSpace.Tests/EcoSpace.Tests.csproj`); `SkyboxRenderer.SyncSkyPoints` uses `TierClassifier.Classify` per body per frame with double-precision `AbsolutePositionInRoot` hierarchy walk |
| SC2 | The skybox does not rotate or drift as the player rotates the ship — fixed to world space | UNCERTAIN | `skybox.gdshader` uses `EYEDIR` (Godot sky shader world-space built-in) and header comment cites the official source; cannot confirm at the engine level without runtime play-test |
| SC3 | Only the next tier out is on the skybox; bodies of the current tier are meshes; bodies beyond the next tier are neither | VERIFIED | `TierClassifier.Classify` returns `CurrentTierMesh`, `NextTierSkybox`, or `Beyond`; `SkyboxRenderer` only pushes bodies classified `NextTierSkybox`; `WorldRenderer` renders bodies in the ship's current space (same-parent children); partitioning is exhaustively tested |
| SC4 | RND-07/D-21 handoff baseline: a skybox point and its corresponding mesh can be aligned to the same screen position with matched color and brightness for an instant, pop-free swap (no crossfade) | VERIFIED | `SkyboxRenderer.GetSkyDirection(int, out Vector3)` caches per-body world-fixed directions each frame; `WorldRenderer.GetRenderPosition(int, out Vector3)` exposes last render-space mesh position; both read `body.BaseColor` and call `StarRendering.ApparentBrightness` — color/brightness match by construction; no Tween/crossfade/alpha-blend found (grep confirmed) |

**Score:** 3/4 truths verified (SC2 requires runtime confirmation)

---

## Required Artifacts

| Artifact | Expected | Status | Details |
|----------|----------|--------|---------|
| `Scripts/TierClassifier.cs` | Pure C# SkyTier enum + TierClassifier.Classify | VERIFIED | Exists, 75 lines, no `using Godot`, `enum SkyTier { Skip, CurrentTierMesh, NextTierSkybox, Beyond }`, `public static SkyTier Classify(UniObject, UniObject)` walking all ancestor spaces |
| `Scripts/Render/SkyboxRenderer.cs` | Read-only per-frame sky uniform push with handoff cache | VERIFIED | 257 lines; `namespace Render`; calls `TierClassifier.Classify`, `StarRendering.AngularRadius`, `StarRendering.ApparentBrightness`; pushes `star_count`/`star_dirs`/`star_colors`/`star_sizes` via `SetShaderParameter`; `_skyDirs` cache + `GetSkyDirection` accessor; no `TranslatePos`/`LocalPos`/`ChildIndices` writes |
| `Scripts/Render/StarRendering.cs` | Single source of truth for star appearance (post-rework) | VERIFIED | 68 lines; `ApparentBrightness(double lum, double dist)` inverse-square flux through log10 magnitude curve clamped to [0,1]; `AngularRadius(double r, double d)`; `Exposure` global knob exposed as `WorldRenderer.StarBrightness` |
| `Scripts/Render/WorldRenderer.cs` | `GetRenderPosition` handoff accessor | VERIFIED | `_lastRenderPositions` dict populated in `SyncBodies`; `public bool GetRenderPosition(int, out Vector3)` returns last render-space position; cleared each frame (T-02-05) |
| `Shaders/skybox.gdshader` | `shader_type sky`, EYEDIR disc loop, no RADIANCE | VERIFIED | `shader_type sky`, `const int MAX_STARS = 8`, `star_dirs`/`star_colors`/`star_sizes`/`star_count` uniforms, `void sky()` loop, no RADIANCE sampling |
| `Scripts/UniObject.cs` | `public double Luminosity = 1.0` field | VERIFIED | Line 94: `public double Luminosity = 1.0;` |
| `Scripts/TestSetup.cs` | 3 sibling star systems with distinct colors and luminosities | VERIFIED | ALPHA CEN (L=1.519, warm white), BARNARD (L=0.0035, red M-dwarf), SIRIUS (L=25.4, blue-white A-type) — all in Galaxy space under `_galaxy` with `BaseColor`, `RadiusMeters`, `Luminosity` set |
| `EcoSpace.Tests/EcoSpace.Tests.csproj` | Standalone net8.0 xUnit test project | VERIFIED | `Microsoft.NET.Sdk`, `net8.0`, xunit 2.9.3 + GodotSharp 4.6.2; links `TierClassifier.cs`, `UniObject.cs`, `StarRendering.cs`, Math types; `AllowUnsafeBlocks=true` |
| `EcoSpace.Tests/TierClassifierTests.cs` | Full Classify matrix + brightness tests | VERIFIED | 18 [Fact] tests: 4 Skip cases, 4 ship-in-Star cases, 5 ship-in-Planet cases, 1 real in-system demo (ALPHA CEN/BARNARD/SIRIUS), 4 `StarRendering.ApparentBrightness` tests calling the actual API |
| `Main.tscn` | `background_mode = 2`, Sky/ShaderMaterial/skybox.gdshader, SkyboxRenderer node | VERIFIED | `background_mode = 2`, `Sky_main` sub-resource with `sky_material = SubResource("ShaderMaterial_sky")`, `ShaderMaterial_sky` references `skybox.gdshader`, `SkyboxRenderer` node at line 179 |

---

## Key Link Verification

| From | To | Via | Status | Details |
|------|----|-----|--------|---------|
| `SkyboxRenderer.cs` | `skybox.gdshader` | `SetShaderParameter("star_count"/"star_dirs"/"star_colors"/"star_sizes")` | VERIFIED | Lines 169-175 of SkyboxRenderer.cs; shader uniforms present |
| `SkyboxRenderer.cs` | `TierClassifier.cs` | `TierClassifier.Classify(body, ship) == SkyTier.NextTierSkybox` | VERIFIED | Line 123 of SkyboxRenderer.cs |
| `SkyboxRenderer.cs` | `StarRendering.cs` | `StarRendering.AngularRadius` + `StarRendering.ApparentBrightness` | VERIFIED | Lines 152, 162 of SkyboxRenderer.cs |
| `WorldRenderer.cs` | `StarRendering.cs` | `StarRendering.ApparentBrightness` for star mesh emission | VERIFIED | Line 453 of WorldRenderer.cs |
| `Main.tscn` | `SkyboxRenderer.cs` | SkyboxRenderer node wired with `WorldPath = ".."` | VERIFIED | Line 179 of Main.tscn; ext_resource id `7_skybox` at line 9 |
| `TierClassifierTests.cs` | `TierClassifier.cs` | Compile-linked source file + `TierClassifier.Classify` calls | VERIFIED | `<Compile Include="..\Scripts\TierClassifier.cs" />` in test csproj; all 18 tests call `TierClassifier.Classify` or `Render.StarRendering.ApparentBrightness` |
| `TierClassifierTests.cs` | `StarRendering.cs` | `Render.StarRendering.ApparentBrightness` called directly | VERIFIED | Lines 197, 207, 215-221, 231-238 in test file; CR-01 fix confirmed |

---

## Data-Flow Trace (Level 4)

| Artifact | Data Variable | Source | Produces Real Data | Status |
|----------|---------------|--------|--------------------|--------|
| `SkyboxRenderer.SyncSkyPoints` | `_dirs[]`, `_colors[]`, `_sizes[]` | `AbsolutePositionInRoot` (double-precision hierarchy walk) + `StarRendering.ApparentBrightness(body.Luminosity, len)` + `StarRendering.AngularRadius(body.RadiusMeters, len)` | Yes — uses authored `Luminosity`, `RadiusMeters`, `BaseColor` from `TestSetup` | FLOWING |
| `WorldRenderer.GetRenderPosition` | `_lastRenderPositions` | Populated in `SyncBodies` from per-frame render positions (real floating-origin math) | Yes — populated each frame from real body positions | FLOWING |
| `SkyboxRenderer.GetSkyDirection` | `_skyDirs` | Populated in `SyncSkyPoints` from `AbsolutePositionInRoot` direction computation | Yes — populated each frame when bodies classify as NextTierSkybox | FLOWING |

---

## Behavioral Spot-Checks

| Behavior | Command | Result | Status |
|----------|---------|--------|--------|
| 18 unit tests pass | `dotnet test EcoSpace.Tests/EcoSpace.Tests.csproj --no-build` | `erfolgreich: 18, Fehler: 0` | PASS |
| Game project builds clean | `dotnet build EcoSpace.csproj -clp:ErrorsOnly` | `0 Fehler, 0 Warnung(en)` | PASS |
| SkyboxRenderer contains no TranslatePos | grep | No match in non-comment code | PASS |
| skybox.gdshader contains no RADIANCE sampling | grep | `RADIANCE` appears only in DO-NOT-SAMPLE warning comment | PASS |
| No crossfade/Tween in handoff path | grep `Tween` in SkyboxRenderer.cs / WorldRenderer.cs | No matches in code (only in doc comments describing what is NOT done) | PASS |

---

## Probe Execution

No conventional `scripts/*/tests/probe-*.sh` probes found. No probes declared in PLAN files.

---

## Requirements Coverage

| Requirement | Source Plan | Description | Status | Evidence |
|-------------|------------|-------------|--------|----------|
| RND-05 | 02-01-PLAN, 02-02-PLAN, 02-03-PLAN | Dynamic spherical skybox represents bodies just outside current render tier as distant light points; updates on scale boundary crossing; never drifts with camera rotation | SATISFIED (automated portion) / HUMAN (drift-with-rotation) | TierClassifier + SkyboxRenderer deliver the classification and per-frame push; world-fixed projection via EYEDIR documented; drift behavior needs runtime confirmation (SC2) |
| RND-07 | 02-02-PLAN | Skybox↔mesh handoff is visually continuous; phase 2 baseline only (visible swap is Phase 3) | SATISFIED (baseline) | `GetSkyDirection` + `GetRenderPosition` both exist and are wired to real per-frame data; no crossfade added; color/brightness match by construction |

REQUIREMENTS.md traceability: both RND-05 and RND-07 marked "Complete" for their Phase 2 portions.

---

## Anti-Patterns Found

| File | Line | Pattern | Severity | Impact |
|------|------|---------|----------|--------|
| `Scripts/TestSetup.cs` | 137, 144, 151 | `int _sib1`, `int _sib2`, `int _sib3` — `_` prefix on local variables (reserved for private fields by CLAUDE.md) | Info (IN-04 from code review) | Cosmetic; no behavioral impact |
| `Scripts/Render/WorldRenderer.cs` | ~239-241 (pre-fix) | Per-frame `new HashSet<int>()` and `new Dictionary<int, Vector3>()` — WR-01 from code review | Warning | The current code uses `_activeIndices` and `_renderPositions` as readonly fields cleared each frame — WR-01 was ALREADY FIXED in the codebase (fields `_activeIndices = []` at line 156, `_renderPositions = []` at line 157, both cleared in `SyncBodies`). No live issue. |
| `Shaders/skybox.gdshader` | 42-44 | Comment references non-existent `StarAngularSize` (stale after rework) | Info (IN-01) | Misleading for future maintainers; no behavioral impact |

No TBD/FIXME/XXX debt markers found in phase-modified files. No unreferenced unresolved markers.

**Note on post-rework plan deviations:** The plan 02-02 `must_haves` listed five specific `[Export]` properties (`LuminosityScale`, `MinBrightFloor`, `SizePerBright`, `MinStarSize`, `MaxStarSize`) that were **removed** in the post-play-test rework (approved by user per 02-02-SUMMARY addendum). These exports are absent from the codebase. The rework replaced the linear model with the `StarRendering.cs` logarithmic magnitude model. The phase instruction explicitly notes this rework was approved and should not be flagged as a gap. The *intent* of the original plan-02 must-haves (magnitude ranking, minimum visibility floor, BaseColor carry, bloom feed) is fully delivered — by a different mechanism. The plan's acceptance criteria were superseded by an approved architectural change.

---

## Human Verification Required

### 1. Sibling star points visible in-system

**Test:** Launch the scene in Godot 4.6.2 (Forward+, DX12). Fly to Star space (the starting space, or remain there). Confirm the three sibling star systems (ALPHA CEN, BARNARD, SIRIUS) appear as discrete light points on the sky.
**Expected:** 2-3 distinct lit points visible, none fully black; Sirius (L=25.4) is brightest/largest, Barnard (L=0.0035) is dimmest. Uniform-array acceptance for `Color[]`/`Vector3[]`/`float[]` from C# in Godot 4.6.2 Mono confirmed by no shader/uniform errors in console.
**Why human:** Sky shader output, uniform-array push acceptance, and the complete SkyboxRenderer→shader pipeline cannot be confirmed by static analysis.

### 2. Sky points are world-fixed — no camera drift

**Test:** While looking at a sibling star point, pitch and yaw the ship aggressively. Watch the point.
**Expected:** Point stays pinned to its world-space direction — it does not move as the camera rotates. EYEDIR world-space property of Godot 4.6 sky shaders confirmed.
**Why human:** EYEDIR world-space behavior is documented but can only be confirmed at runtime against the actual engine version; a Godot regression could change this without source changes.

### 3. Sky passes through existing dither post-process

**Test:** Toggle dithering on/off (PostProcessRenderer inspector). Observe the sky points.
**Expected:** Sky points show visible dithering quantization when dithering is on; they de-quantize when off. This proves the sky passes through the same CanvasLayer dither as the meshes (D-27), with no second dither pass.
**Why human:** Requires observing shader output; `hint_screen_texture` capturing sky output in DX12/Forward+ is a research open question (A5) that needs runtime confirmation.

---

## Gaps Summary

No automated gaps. All artifacts exist, are substantive, and are wired. Unit tests call the real production API (`StarRendering.ApparentBrightness`) and pass 18/18. The CR-01 code review finding (tests validating a deleted formula) was fixed before this verification ran.

SC2 (world-fixed projection) is UNCERTAIN, not FAILED — the mechanism is correctly implemented (`EYEDIR` is Godot's world-space sky built-in), and the shader header cites the source. The uncertainty is engine-behavioral and requires a one-time runtime confirmation.

---

_Verified: 2026-06-15T00:00:00Z_
_Verifier: Claude (gsd-verifier)_
