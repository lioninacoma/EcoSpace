---
gsd_state_version: 1.0
milestone: v1.0
milestone_name: milestone
current_phase: 05
current_phase_name: outer-tier-body-findability-galaxy-visibility
status: executing
stopped_at: Phase 5 context gathered
last_updated: "2026-06-18T19:47:20.497Z"
last_activity: 2026-06-18
last_activity_desc: Phase 05 execution started
progress:
  total_phases: 9
  completed_phases: 4
  total_plans: 15
  completed_plans: 12
  percent: 44
---

# Project State

## Project Reference

See: .planning/PROJECT.md (updated 2026-06-12)

**Core value:** The player can fly seamlessly through a massive 1:1-scale universe — from a planet's surroundings out to interstellar and intergalactic distances — with rendering and flight that stay correct and feel good across every scale.
**Current focus:** Phase 05 — outer-tier-body-findability-galaxy-visibility

## Current Position

Phase: 05 (outer-tier-body-findability-galaxy-visibility) — EXECUTING
Plan: 1 of 3
Status: Executing Phase 05
Last activity: 2026-06-18 — Phase 05 execution started

Plans:

- 04-01 (Wave 1, autonomous): tier- & target-aware speed envelope (per-tier ceiling = parent.SOIMeters × TierSpeedFactor, symmetric proximity damp, target ease-out via UniMath.Distance) + read-only Hud.ActiveTargetIndex
- 04-02 (Wave 2, blocking play-test): world-pinned target circle in Hud._Draw (min on-screen radius + edge-marker fallback); checkpoint unblocks deferred Phase 03 UAT items

Note: Phase 03 is at UAT paused (1/7), intentionally gated on Phase 04 — its deferred
UAT items (fly-out SOI behavior, intergalactic transit) get re-tested once the flight
model lands. HUD target bugs + sibling-star-distance bug fixed & verified this session.

## Performance Metrics

**Velocity:**

- Total plans completed: 3
- Average duration: 7 min
- Total execution time: ~20 min

**By Phase:**

| Phase | Plans | Total | Avg/Plan |
|-------|-------|-------|----------|
| 01-in-system-flight-mvp | 3 | ~20 min | ~7 min |

**Recent Trend:**

- Last 5 plans: 01-01 (4 min), 01-02 (12 min), 01-03 (4 min)
- Trend: —

*Updated after each plan completion*
| Phase 02 P03 | 10 min | 1 tasks | 3 files |
| Phase 03-cross-galaxy-travel P01 | 15 | 3 tasks | 4 files |
| Phase 03-cross-galaxy-travel P02 | 145 | 1 tasks | 1 files |
| Phase 03-cross-galaxy-travel P03 | 3 | 1 tasks | 1 files |
| Phase 04 P01 | 5 | 3 tasks | 2 files |
| Phase 04 P02 | 110 | 2 tasks | 1 files |

## Accumulated Context

### Decisions

Decisions are logged in PROJECT.md Key Decisions table.
Recent decisions affecting current work:

- Roadmap: Phase 1 folds STAB-01 fix + render sync + flight + HUD into one vertical slice (coarse granularity; avoids thin "Phase 0" stub)
- Roadmap: Dynamic skybox isolated as Phase 2 — flagged for shallow phase research per SUMMARY.md
- Roadmap: Cross-galaxy travel is Phase 3 (second sequenced acceptance goal)
- 01-01: Iterative SOI transition (MaxIterations=32) replaces recursive form
- 01-01: Floating origin anchored on ship.LocalPos in RenderBridge (not parent body)
- 01-01: SkeletonSpeed=1e8 m/s placeholder; context-scaled speed deferred to Plan 02
- 01-01: RenderBridge snapshots ChildIndices before foreach to prevent mutation exception
- 01-01: HUD computes speed from prev-frame position delta — read-only consumer pattern
- 01-02: Ambient-floor-only lighting ACCEPTED for MVP; cross-space directional terminator deferred (no day/night terminator while orbiting in planet space)
- 01-02 REVISION (RND-02/D-16): Re-evaluated planet-space lighting — added DirectionalLight3D oriented along true cross-frame sun direction (ship→planet→star hierarchy, Star-space meters); ambient floor reduced 0.08→0.03 for visible terminator; visible sun mesh in Planet space deferred to Phase 3 tiered renderer (sun at 1 AU = ~1.5e7 render units, ~15x beyond far plane)
- 01-03: Mouse mode = T-key toggle Confined/Captured; steering accumulation in _Input (not _UnhandledInput) so HUD Controls cannot swallow MouseMotion
- 01-03: Distance-scaled speed envelope: contextMax = Lerp(contextMax, parentSurfaceDist*SpeedPerMeter, easing*dt); distance = ship.LocalPos.Magnitude() - parent.RadiusMeters
- 01-03: DESIGN REFINEMENT D-03: throttle range [-1,1] (W=forward, S=reverse, X eases to 0) — approved in play-test; reverse thrust without turning was required in practice
- 01-03: SteeringReticle mouse_filter=Ignore (2) required; default Stop swallows MouseMotion before _Input accumulation
- 01-render (pre-01-04 fix): RenderBridge stale-radius bug fixed (commit 5dd542d) — body radii now recompute per frame (r = RadiusMeters / ship.LocalPos.Scale * factor applied as mesh.Scale) so bodies are correctly sized across SOI transitions; at true 1:1 scale distant bodies are correctly tiny specks; findability of tiny specks deferred to 01-04 HUD marker + Phase 3 tiered renderer
- 01-render (pre-01-04 fix, commit 2b29b66): Star-space planet under-shading fixed — OmniLight3D.OmniAttenuation set to 0 (constant brightness up to OmniRange, no distance falloff) and StarLightEnergy lowered 2.0→1.8 to match PlanetSunLightEnergy; OmniLight remains positional so terminator direction still derives from the star-mesh render position; StarLightAttenuation exposed as [Export] for runtime tuning; lit-side brightness in Star space now matches Planet space
- 01-02 SHADER REVISION (pre-01-04, commit 253cb35): Body lighting moved from Godot OmniLight3D (Star space) + DirectionalLight3D (Planet space) to a single unshaded spatial shader (Shaders/body_lit.gdshader). Lambert terminator computed per body from star_dir uniform (world-space direction from body surface toward nearest star, set each frame by RenderBridge). Shading character is IDENTICAL in every space — no node-type change on cross-space transitions. OmniLight3D and DirectionalLight3D fully removed. Exports StarLightEnergy/StarLightRange/StarLightAttenuation/PlanetSunLightEnergy replaced by BodyLightEnergy (default 1.8) and BodyAmbient (default 0.03). Star stays emissive StandardMaterial3D. Dither post-process, per-frame radius scaling, and floating-origin positioning unchanged.
- [Phase ?]: 02-03: xUnit test strategy: xunit-godotsharp-linked — GodotSharp 4.6.2 + linked Compile Include files; TierClassifier full matrix verified green
- 02-02 POST-CHECKPOINT REDESIGN (play-test feedback, APPROVED 2026-06-15): The 02-02 magnitude model produced huge white star spheres. Reworked star rendering to be physically coherent and unified across mesh + skybox. New `Scripts/Render/StarRendering.cs` is the SINGLE source of truth: a star's appearance derives ONLY from its per-instance Luminosity / RadiusMeters / BaseColor. Size = physical angular radius (R/d), identical for mesh and sky (sky disc floored at one screen pixel). Brightness = inverse-square flux (L/d²) through a magnitude (log10) curve, clamped to [0,1] so BaseColor hue is never washed to white. ONE global knob `StarRendering.Exposure` (editor handle: `WorldRenderer.StarBrightness`, default 0) shifts every star (mesh + sky) together. Star mesh emission set per-frame from the same `ApparentBrightness`. REMOVED all prior global star knobs: SkyboxRenderer LuminosityScale/MinBrightFloor/MaxBright/StarAngularSize/MinStarSize/MaxStarSize/SizePerBright and WorldRenderer StarEmissionEnergy. Rationale: Sun@1AU vs sibling@8ly differ ~1e10 in flux — no single LINEAR scale renders both without one saturating to white; the log curve compresses the range so one exposure works for both and stays in the hue-preserving band. Commits: bf… (point-source), then coherent-refactor, then unify-brightness (3 commits on main after 02-02 SUMMARY).
- [Phase ?]: 03-01: int[] uniform path confirmed for galaxy_types in Godot 4.6.2 Mono (no float-packing fallback); GALAXY_DISC_SCALE=80.0/LOD_THRESHOLD=2e-4 flagged as play-test tuning knobs; 3 galaxies authored at true 1:1 intergalactic distances
- [Phase ?]: _maxSpeed default set to 2e20 m/s for ~2-minute intergalactic crossing (D-35 tuning knob; adjust in FlightController.cs)
- [Phase ?]: SpeedOfLight cap removed from MaxSpeed setter (Plan 03-02); double.IsFinite guard in ApplyMotion closes T-03-04 threat
- [Phase ?]: 03-03: WorldRenderer.IsStarBody replaced body.Name=="STAR" with body.ObjectType==UniObject.Type.Star (D-38); Type.Galaxy skip guards in both parent and sibling render paths close T-03-06; GalaxyRenderFactor=1e-8f confirmed correct for Galaxy space (D-39); RND-07 handoff aligned by shared per-frame UniMath math (Pattern 7) — no explicit at-transition code needed
- [Phase ?]: D-40: tierCeiling = parent.SOIMeters x TierSpeedFactor; no per-tier switch table
- [Phase ?]: D-42: symmetric proximity damp capped at tierCeiling, not global _maxSpeed — core fix for Phase-03 in-system over-speed and galaxy-SOI-exit dead zone
- [Phase ?]: D-43/D-44: target ease-out uses UniMath.Distance (LCA path); tier ceiling still caps; Hud.ActiveTargetIndex is read-only (D-45/D-12 preserved)
- [Phase ?]: D-46: world-pinned phosphor-green outline in Hud._Draw gated on WorldRenderer.GetRenderPosition; DrawArc not DrawCircle (unfilled outline); MIN_CIRCLE_RADIUS=20f floor
- [Phase ?]: Phase 04 follow-ons captured as backlog (user 2026-06-17): 999.2 shader target-sphere outline (matches body distortion); 999.3 distance-based cross-space targeting + autopilot warp drive (free roam km/s only, warp autopilot-only, intergalactic in minutes); 999.4 warp-drive visual FX

### Roadmap Evolution

- 2026-06-17: Phase 4 added — "Flight Model v2 — tier & target-aware speed" (from P1 tech debt `flight-speed-model-tier-and-target-aware`; absorbs cross-SOI targeting + target-circle from backlog 999.1). Phase 3 left at UAT paused (1/7), gated on Phase 4.
- 2026-06-18: Phase 5 added — "Outer-tier body findability & galaxy visibility" (from Phase 03 UAT partial). Bundles `galaxy-space-star-meshes-invisible` (P1), `galaxy-visibility-in-universe-space` (P2, design fork), and `galaxy-disc-tilt-foreshortening` (polish), fix order #1→#2→#3. Phase 3 UAT (3/7 pass) now gated on Phase 5.

### Pending Todos

From Phase 03 UAT play-test (2026-06-17), priority order:

- **P1** `flight-speed-model-tier-and-target-aware` — OPEN. Single global MaxSpeed across all tiers; needs per-tier ceiling + optional target-distance ease-out. Now also absorbs cross-SOI targeting (D-12 override) + world-pinned target circle (999.1 slice). Supersedes `thrust-zero-at-galaxy-soi-exit`. Phase-sized → **next up**.
- **P2** `galaxy-visibility-in-universe-space` — OPEN. Galaxies vanish in Universe space (D-28 skips galaxy meshes; skybox only carries next-tier-out). Blocks intergalactic confirm. Needs design decision.
- ~~P3 `hud-target-nearest-galaxy-space`~~ — flicker FIXED (UniMath + ROOT name); cross-SOI-nearest part folded into P1.
- ~~P4 `hud-cycle-target-not-working`~~ — RESOLVED (Tab keybinding Escape→Tab + _Input/SetInputAsHandled).
- ~~`sibling-star-distances-1e4-too-close`~~ — RESOLVED (quick 260617-lip; positions ×1e4 to true ly metres).

Plan: ✅ revert done · ✅ HUD bugs fixed (/gsd-debug) · ✅ star-distance fixed (quick) → **P1 flight model v2 as a discussed phase** (per-tier ceiling + target ease-out + minimal 999.1 targeting slice) → P2 galaxy visibility.

### Blockers/Concerns

- Phase 2: Sky shader direction encoding and half-res pass are moderately novel; shallow phase research recommended before planning

_(Resolved: STAB-01 recursion fixed in 01-01; floating-origin established in 01-01; 01-02 human-verify approved with ambient-floor lighting.)_

## Deferred Items

| Category | Item | Status | Deferred At |
|----------|------|--------|-------------|
| HUD / targeting | Extended targeting system: hierarchy tree selector (any object, overrides D-12) + world-pinned target outline w/ minimum on-screen radius + name/distance label pinned to & tracking the object | Backlog 999.1 | 2026-06-14 (01-04 checkpoint) |

## Quick Tasks Completed

| Date | Slug | Summary |
|------|------|---------|
| 2026-06-13 | clarify-system-mesh-vs-skybox-rendering | Docs: tiered mesh/skybox model — in-system → planets + sun(s) as meshes; in-galaxy → that galaxy's stars as meshes; skybox = next tier out; added RND-07 (continuous skybox↔mesh handoff) |
| 2026-06-13 | align-roadmap-state-to-tier-model | Docs: propagated the tier model into ROADMAP Phase 2 (goal/criteria/overview + RND-07); added RND-06 to Phase 1 reqs (ROADMAP + 01-CONTEXT); fixed STATE current-position (01-02 tasks 1-3 done, task-4 human-verify pending) |
| 2026-06-13 | tiered-rendering-phase-coverage | Docs: marked RND-02/04/05/07 phase-spanning in traceability (in-system early, galaxy tier in Phase 3); gave ROADMAP Phase 3 explicit galaxy-tier rendering coverage + success criterion (closes audit finding #5) |
| 2026-06-15 | implement-magnitude-model-in-skyboxrende | feat: inverse-square luminosity model (L/D²×LuminosityScale) replaces flat alpha=1.0 in SkyboxRenderer; MinBrightFloor=0.1 floor for faint stars; MinStarSize=3e-6 minimum disc; planets set Luminosity=0 |
| 2026-06-16 | fix-precision-loss-in-skyboxrenderer-pos | perf: replaced SkyboxRenderer absolute-from-root position math (`AbsolutePositionInRoot`) with an LCA-relative walk (`FindLca` + `PositionRelativeToAncestor` + `RelativePosition`) — ship→body vector is subtracted in the lowest-common-ancestor frame so the large common-ancestor offset is never formed, killing catastrophic cancellation at 1:1 Universe scale. No overflow existed. Outputs identical at MVP scale (LCA=Galaxy@origin); read-only contract preserved; build clean. Commit 83dfce4 |
| 2026-06-16 | refactor-math-to-maximize-univec3-precis | refactor: new global `UniMath` helper (`Scripts/Math/UniMath.cs`) does hierarchy-aware position math entirely in UniVec3 — `FindLca`/`ToAncestorFrame`/`RelativePosition`/`RelativeMetres`/`Distance`. Accumulates up to the LCA via per-level `Convert+add`, subtracts two SAME-scale UniVec3 (exact integer Units cancellation), collapses to metres via a single `ToDouble3()` on the small delta. SkyboxRenderer + WorldRenderer.ComputeStarRenderPosFromHierarchy refactored onto it (260615-v69's in-file helpers removed); Flight/Hud audited (single-frame, already exact, no change). Added a durable `## Position Math (UniVec3 / UniMath)` convention to CLAUDE.md. 28 tests green (16 TierClassifier + 12 new UniMath incl. a precision-headroom test: 1.0 m gap at 4e16 m recovered to <1e-9 m). Build clean. Commits 0d3795c, 181ca56, f370857, 7567e80 |
| 2026-06-16 | suppress-the-home-galaxy-in-skyboxrender | feat: read-only `UniMath.FindLca(ship, body, objs) == body.Index` ancestry guard at the top of SkyboxRenderer's galaxy branch suppresses the home galaxy disc while the ship is inside its SOI — only the 2 OTHER galaxies render, satisfying phase-03 must-have truth #2. Resolves the open home-galaxy in-SOI visibility design question deferred from 03-01 (SUMMARY line 135); user-locked decision: suppress (not Milky-Way band). Star branch + `_skyDirs` untouched; no shader/TestSetup change. 30 tests green (28 + 2 ancestry-predicate facts). Build clean. In-game visual confirm DEFERRED to 03-02 play-test (user choice). Commits a94305e, a2588d2 |
| 2026-06-17 | 260617-j6b-fix-thrust-zero-at-galaxy-soi-exit | REJECTED + REVERTED: direction-aware (receding-exempt) speed clamp built (f343cc3, build 0/0, 30/30) but play-test failed — exempting the clamp jumps to global intergalactic MaxSpeed, making in-system travel unusable. Reverted (76f6b3b). Exposed root issue: single global MaxSpeed across all tiers. Superseded thrust-zero-at-galaxy-soi-exit; spun off 4 tech debts (flight speed model P1, galaxy visibility P2, HUD nearest/target P3, target cycling P4). |
| 2026-06-17 | 260617-lip-fix-sibling-star-distance-data-bug-scale | COMPLETE: within-galaxy star positions were authored in "Galaxy units" (1e4 m/unit) but AddGameObject takes metres → stars 1e4× too close (~26 AU), SOIs overlapping. Scaled all sibling/cluster/dest-sib offsets ×1e4 to true metres (ALPHA CEN 4.2 ly, etc.); now ~26× StarSOI radius apart, no overlap. Build 0/0, 30/30. User-verified: sky-points still visible, no regression; fly-out SOI re-test deferred to P1 flight model. |

## Session Continuity

Last session: 2026-06-18T19:16:20.294Z
Stopped at: Phase 5 context gathered
Resume file: .planning/phases/05-outer-tier-body-findability-galaxy-visibility/05-CONTEXT.md
Prior resume note: Decision locked — (1) revert done; (2) fix target/HUD bugs first via /gsd-debug; (3) then flight speed model v2 as a discussed phase (P1, per-tier ceiling + target ease-out, minimal slice of backlog 999.1); (4) then galaxy-visibility-in-universe-space (P2, blocks intergalactic UAT 5-7). Phase 03 stays open at UAT until the flight model is reworked.

## Refactor Notes

**2026-06-13 — Repo refactor: folder-aligned namespaces**

Applied behaviour-preserving structural refactor (5 commits + 1 doc commit):

- `namespace Universe` / `Scripts/Universe/UniRenderer.cs` → `namespace Render` / `Scripts/Render/PostProcessRenderer.cs` (class renamed PostProcessRenderer)
- `namespace Universe` / `Scripts/Render/RenderBridge.cs` → `namespace Render` / `Scripts/Render/WorldRenderer.cs` (class renamed WorldRenderer; using Universe; added)
- `namespace Universe` / `Scripts/Flight/FlightController.cs` → `namespace Flight` (using Universe; added; file/class unchanged)
- `Scripts/HUD/` → `Scripts/Hud/` (case rename); `Scripts/HUD/Hud.cs` → `namespace Hud` (using Universe; using Universe.Math; added)
- `Scripts/FPS.gd` → `Scripts/Hud/Fps.gd` (GDScript; no namespace)
- Deleted unreferenced shaders: crt.gdshader, tilemap_shader.gdshader (+ .uid sidecars)
- Main.tscn ext_resource paths updated (uids unchanged)
- CLAUDE.md factual updates: component table, namespace convention, layers, entry points, arch constraints
- Scripts/Universe/Math/ UNTOUCHED (namespace Universe.Math preserved)
- dotnet build: 0 errors, 0 warnings

**2026-06-13 — Repo refactor: Universe namespace/folder fully eliminated**

Applied behaviour-preserving structural refactor (5 commits):

- `Scripts/Universe/Math/Double3.cs` → `Scripts/Math/Double3.cs` (global namespace; namespace Universe.Math removed)
- `Scripts/Universe/Math/Long3.cs` → `Scripts/Math/Long3.cs` (global namespace; namespace Universe.Math removed)
- `Scripts/Universe/Math/UniVec3.cs` → `Scripts/Math/UniVec3.cs` (global namespace; namespace Universe.Math removed)
- `Scripts/Universe/GameWorld.cs` → `Scripts/GameWorld.cs` (global namespace; namespace Universe + using Universe.Math removed)
- `Scripts/Universe/UniObject.cs` → `Scripts/UniObject.cs` (global namespace; namespace Universe + using Universe.Math removed)
- `Scripts/Universe/TestSetup.cs` → `Scripts/TestSetup.cs` (global namespace; namespace Universe + using Universe.Math removed)
- `Scripts/Universe/` folder fully deleted (no files remain)
- All .uid sidecars moved alongside source files (content unchanged, uids preserved)
- `using Universe;` and `using Universe.Math;` removed from WorldRenderer.cs, FlightController.cs, Hud.cs
- Namespaces Render, Flight, Hud retained unchanged
- Main.tscn: TestSetup path updated (res://Scripts/Universe/TestSetup.cs → res://Scripts/TestSetup.cs); uid unchanged
- project.godot: no Universe references found; no changes needed
- CLAUDE.md: all factual file paths and namespace descriptions updated
- dotnet build: 0 errors, 0 warnings
