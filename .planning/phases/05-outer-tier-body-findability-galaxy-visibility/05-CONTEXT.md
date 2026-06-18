# Phase 5: Outer-tier body findability & galaxy visibility - Context

**Gathered:** 2026-06-18
**Status:** Ready for planning

<domain>
## Phase Boundary

Make distant **tier-member** bodies actually visible and findable at true 1:1 scale,
closing the two Phase-03 UAT rendering gaps and revisiting **D-28**:

1. **Galaxy-space star meshes are invisible (P1)** — flying out into Galaxy space, the home
   galaxy's stars (STAR, ALPHA CEN, BARNARD, SIRIUS) render as meshes whose radius collapses
   sub-pixel and whose emissive brightness floors to ~0 at light-year distance. You can target
   them but the circle marks black space.
2. **Galaxies vanish in Universe space (P2)** — galaxies are the current tier in Universe space,
   but the skybox only carries the *next* tier out and `WorldRenderer` skips galaxies (D-28),
   so nothing draws them and there's no approach cue.
3. **Galaxy disc tilt (polish)** — galaxy sky discs render face-on only (tilt dropped in Phase 3
   to fix a degenerate ring collapse).

The fix that emerged is larger than a clamp: a **unified, always-on star/galaxy rendering model**
— every star is always a brightness-floored point (PSF) with its sphere mesh blending in
additively on approach; galaxies stay sky-only (D-28 reaffirmed) and adopt a matching volumetric
look; space brightens semi-realistically near a star.

**In scope:** unified always-on PSF star points + additive mesh blend; min size/brightness floor
for findability; current-tier galaxies on the skybox; UniObject-driven target marker; star+galaxy
shared visual look; proximity glow/bloom; galaxy disc tilt; continuous tier handoffs.

**Out of scope:** true nebula rendering + authored nebula bodies; full volumetric light scattering /
god-rays; whole-hierarchy nav-HUD (backlog 999.1); procedural generation; cockpit art; economy/combat.
</domain>

<decisions>
## Implementation Decisions

> **⚠ Decision-number collision in prior phases:** Phase 3 used D-28..D-41 and Phase 4 re-used
> D-40..D-47 (overlapping at D-40/D-41). To avoid further collision, Phase 5 numbers start at **D-48**.
> When a decision below references an older D-number, it means the *Phase-3* one unless noted (e.g.
> D-46 is the Phase-4 target circle).

### Galaxy visibility in Universe space (D-28 fork)
- **D-48: Skybox-only — D-28 REAFFIRMED.** Galaxies never become meshes or billboards. Fix the
  Universe-space vanish by rendering **current-tier galaxies on the skybox** (today the skybox only
  carries the next tier out). The headline payoff of the phase stays the member **stars** becoming
  visible on SOI entry into Galaxy space — not a galaxy mesh. (Rejected: hybrid sky→mesh, and full
  mesh reversal of D-28.)
- **D-49: Galaxies are ALWAYS sky entities, regardless of the ship's tier** — a direct corollary of
  D-28 (never meshes → always sky). `TierClassifier` routes `Type==Galaxy` to the skybox
  unconditionally; the skybox shows galaxies whether the ship is in Galaxy space (next tier out) or
  Universe space (current tier). (Rejected: a generic "outermost tier shows current tier" special case.)
- **D-50: The target marker is derived from the body's `UniObject` configured parameters, NOT from any
  mesh — refines D-46.** Both the marker's screen **position** (`UniMath.RelativePosition` ship→body in
  the LCA frame → camera projection) and its **size** (`StarRendering.AngularRadius(RadiusMeters,
  trueDist)` with the D-46 minimum on-screen radius floor) come from the UniObject. The circle stops
  gating on `WorldRenderer.GetRenderPosition`, so it works **uniformly** for sky-only galaxies,
  sub-pixel galaxy-space stars, and planets alike. The off-screen edge-marker still applies when the
  body is behind/off-screen. *(User-corrected from the original "circle from sky direction" framing —
  the principle generalizes to all targets.)*
- **D-51: Approach/distance cue for a sky-only galaxy = disc growth + HUD distance.** The procedural sky
  disc grows by true angular size on approach (D-30, existing mechanism) AND the HUD shows target
  distance via adaptive units (D-10, already reaches ly). Two reinforcing cues. (Rejected: disc growth
  only — UAT specifically flagged the missing distance sense.)
- **D-52: Galaxy→Universe exit disc appearance is an in-game verification item, not a locked mechanism.**
  Default behavior: the home galaxy's disc switches on at the Galaxy→Universe SOI crossing (D-22) as its
  member-star meshes demote. Tune (brief fade-in / sizing) **only if** it visibly pops in play-test.

### Star findability & unified rendering model (P1 fix)
- **D-53: Always-on brightness-floored PSF point + additive sphere-mesh blend — SUPERSEDES D-21 & D-22.**
  Every star is **always** drawn as a brightness-floored point (PSF-style), in addition to the skybox,
  at all tiers. Its sphere **mesh blends in additively** as true distance shrinks — a continuous
  distance-driven blend, **not** the D-21 instant point↔mesh swap and **not** the D-22
  scale-crossing-only promotion. This solves the invisible-mesh bug structurally: even a sub-pixel star
  is always a visible point. Floor/saturation governed by `min_size`-style + `luminosity_cap`-style knobs
  (godot-starlight pattern).
- **D-54: Unify the star and galaxy LOOK now; defer nebula.** Star points and galaxy discs share one
  visual family (PSF/glow for stars; a Star-Nest-style volumetric *look* for galaxy discs) so they read
  as the same universe. **True nebula rendering and authored nebula bodies are deferred** to a future
  phase (no nebula bodies exist yet). Keeps Phase 5 on findability + galaxy visibility.
- **D-55: Proximity brightening = distance-driven glow halo + bloom (semi-realistic).** A volumetric glow
  halo (PSF wings / additive radial falloff) around a star intensifies as true distance shrinks, feeding
  the existing WorldEnvironment bloom so the screen genuinely brightens near a star. Inverse-square-ish,
  clamped by the `luminosity_cap`, reusing the `StarRendering` brightness curve + existing glow pipeline.
  No full atmospheric/volumetric scattering. (Deferred: full volumetric scattering / god-rays.)
- **D-56: One unified star-point renderer (MultiMesh PSF) for every star at every tier.** Introduce a
  single star-point renderer (godot-starlight-style `MultiMeshInstance3D` PSF) driven by `UniObject` +
  `StarRendering`; the sphere mesh layers on top for close stars. The **skybox shader's star-point loop
  is retired**; galaxies stay in the skybox shader. One source of truth for star points (the reason
  `StarRendering` exists — prevents the mesh/sky paths drifting). (Rejected: extending the two split paths.)
- **D-57: Borrow godot-starlight's technique, custom-fit to EcoSpace.** Adopt the techniques (MultiMesh
  PSF, `min_size_ratio` floor, `luminosity_cap`, far-plane-clip trick) but implement against EcoSpace's
  model — `UniObject`, `StarRendering.ApparentBrightness/AngularRadius`, floating-origin, 8-bit dither.
  **Confirm the MIT license in planning.** (Rejected: importing the addon directly — assumes its own data
  model / far-plane handling that may clash with the tiered SOI / floating-origin renderer.)

### Floor consistency & handoff continuity
- **D-58: Implement the new rendering model first; address floor-release/consistency only if it manifests.**
  Do NOT pre-design the floor-release curve or fine star/galaxy/marker consistency rules. Build the
  always-additive PSF point + mesh blend (D-53–D-57) and treat these as **in-game verification items**:
  (a) does the floor release cleanly to true 1:1 as you approach a star (no stuck-at-floor / no pop);
  (b) do star points, galaxy discs, and the UniObject-driven target marker stay visually consistent
  (e.g. the circle hugs the visible point). Fix only what actually shows up.

### Galaxy disc tilt (polish)
- **D-59: Build disc tilt into the new unified galaxy shader.** Since the galaxy shader is being reworked
  for the D-54 look, add believable 3D tilt (elliptical foreshortening + in-plane roll from `disc_normal`)
  as part of that rework. **MANDATORY safe approach** (Phase-3 landmine): build a stable basis from the
  galaxy *direction* (`galaxy_dir`), then foreshorten toward `disc_normal` with a clamp **floor** so it
  never collapses to a sky-spanning ring when `disc_normal` ≈ `galaxy_dir` or world-up; keep the
  `dot(EYEDIR, galaxy_dir) > 0` front-hemisphere gate (no antipode ghost). Re-wire the currently-unused
  `galaxy_orientations[i].xyz`. Requires in-game shader iteration.

### Claude's Discretion
- Exact PSF kernel / `min_size` / `luminosity_cap` / glow-falloff values — tune by feel in-game
  (play-test `[Export]` knobs, like GALAXY_DISC_SCALE / StarBrightness precedent).
- The far-plane-clip technique specifics for never culling distant star points.
- Galaxy-disc volumetric-look shader specifics (how much Star-Nest-style detail vs the current
  procedural spiral/elliptical functions) within the D-54 "shared family, no true nebula" guideline.
- Exact foreshorten clamp-floor value for D-59.
- How the MultiMesh per-instance data (position/luminosity/color) is fed from `GameWorld`/`UniObject`
  each frame within the floating-origin model.
- Whether any new tunable belongs on `WorldRenderer`, a new star-point renderer node, or the shader.

### Source Todos (this phase's origin — addressed, not separately tracked)
- **`galaxy-space-star-meshes-invisible.md`** (P1) — fixed by D-53–D-58 (always-on floored PSF point).
- **`galaxy-visibility-in-universe-space.md`** (P2) — fixed by D-48–D-52 (current-tier galaxies on skybox + UniObject marker).
- **`galaxy-disc-tilt-foreshortening.md`** — addressed by D-59 (tilt in the reworked shader).

</decisions>

<canonical_refs>
## Canonical References

**Downstream agents MUST read these before planning or implementing.**

### Phase requirements & scope
- `.planning/ROADMAP.md` §"Phase 5: Outer-tier body findability & galaxy visibility" — goal + the D-28 revisit note.
- `.planning/todos/pending/galaxy-space-star-meshes-invisible.md` — P1 source: root-cause (sub-pixel radius + ~0 emission) + "done" definition + approach options.
- `.planning/todos/pending/galaxy-visibility-in-universe-space.md` — P2 source: galaxies vanish in Universe space + the D-28 design question.
- `.planning/todos/pending/galaxy-disc-tilt-foreshortening.md` — polish source: face-on-only + the safe-basis approach sketch + the ring-collapse landmine (commit fef1d91, `.planning/debug/galaxy-sky-disc-antipode.md`).

### Prior-phase decisions this phase builds on / revises (do NOT re-litigate the un-revised ones)
- `.planning/phases/03-cross-galaxy-travel/03-CONTEXT.md` — **D-28** (galaxies sky-only — REAFFIRMED here, D-48), D-29/D-30 (per-galaxy procedural type + strict 1:1 pixel-floored sizing), D-31 (disc-LOD only), D-38 (`UniObject.Type` routing), D-39 (`GalaxyRenderFactor`), D-40 (galaxy uniform set in skybox.gdshader). **Note D-21 (instant swap) and D-22 (scale-crossing-only promotion) are SUPERSEDED by D-53.**
- `.planning/phases/04-flight-model-v2-tier-and-target-aware-speed/04-CONTEXT.md` — **D-46** (world-pinned target circle + min on-screen radius — REFINED here, D-50), D-45 (current-tier targeting), backlog 999.1/999.2 (deferred nav-HUD / shader sphere outline).
- `.planning/phases/02-dynamic-skybox/02-CONTEXT.md` — D-17..D-27: the magnitude brightness model, BaseColor points, min-floor, shared bloom, real-bodies-only, `Luminosity` attribute, shared dither (the appearance model the PSF unification must preserve).
- `.planning/phases/01-in-system-flight-mvp/01-CONTEXT.md` — D-10 (adaptive units → ly), D-12 (current-space targeting), D-15 (true 1:1 radii), RND-06 unit-space render reframe.

### Position / scale conventions (critical)
- `CLAUDE.md` §"Position Math (UniVec3 / UniMath)" — MANDATORY. Use `UniMath` LCA helpers for any
  cross-frame distance/direction (the marker direction in D-50, glow distance in D-55); never raw
  absolute-from-root `ToDouble3()` accumulation at galaxy/universe scale.

### Code to extend (consume, do not replace)
- `Scripts/Render/WorldRenderer.cs` — `RenderBodyAt` (mesh radius `RadiusMeters/scale×factor` + `EmissionEnergyMultiplier`), `RenderFactorFor`/`GalaxyRenderFactor`, `GetRenderPosition` (mesh-side handoff), `StarBrightness`/`StarRendering.Exposure` handle. The additive mesh blend (D-53) lives here.
- `Scripts/Render/StarRendering.cs` — the single shared brightness/size model (`ApparentBrightness`, `AngularRadius`, `Exposure`). The PSF point, mesh, glow, AND marker must all derive from this so nothing drifts.
- `Scripts/Render/SkyboxRenderer.cs` — per-frame sky uniform push; the star-point loop here is RETIRED by D-56 (galaxies stay). `GetSkyDirection` cache.
- `Scripts/TierClassifier.cs` — route `Type==Galaxy` to sky unconditionally (D-49); stars become always-on points regardless of classification (D-53).
- `Scripts/Hud/Hud.cs` — `BuildTargetableList`, `UpdateTargetReadout`, `UpdateDirectionMarker`, target-circle draw. Rework the circle to be UniObject-driven (D-50).
- `Scripts/Math/UniMath.cs` — `RelativePosition`/`RelativeMetres`/`Distance` (sanctioned cross-frame math for marker + glow distance).
- `Scripts/UniObject.cs` — `Type`/`Space`/`Luminosity`/`RadiusMeters`/`BaseColor`/galaxy orientation+seed; source of truth for marker + PSF instance data.
- `Shaders/skybox.gdshader` — galaxy uniform set + procedural disc loop (reworked for the D-54 look + D-59 tilt); star-point loop removed (D-56).
- `Scripts/TestSetup.cs` — authored galaxies/stars (the bodies being made findable); galaxy `disc_normal` authored ≈ (0.2, 0.98, 0) (the tilt landmine input).
- `Main.tscn` — `Camera3D` + Environment glow/bloom; mount point for a new MultiMesh star-point renderer node.

### Codebase maps
- `.planning/codebase/ARCHITECTURE.md`, `STRUCTURE.md`, `CONVENTIONS.md` — `Render` namespace + layer conventions.
- `.planning/codebase/CONCERNS.md` — SOI/transition robustness (relevant to tier handoffs).

### External technique references (user-provided)
- `https://github.com/tiffany352/godot-starlight` — **PSF MultiMesh star rendering** (MIT, confirm). The reference technique for D-53/D-56/D-57: `min_size_ratio`, `luminosity_cap`, `emission_energy`, far-plane-clip trick, MultiMeshInstance3D.
- `https://www.shadertoy.com/view/XlfGRj` — **Star Nest** (Pablo Roman Andrioli). Volumetric fractal-fold star+nebula+galaxy look — the visual reference for the unified star/galaxy family (D-54) and galaxy-disc volumetric look.
- `https://defold.com/tutorials/shadertoy` — ShaderToy→engine GLSL porting reference (general guidance).

</canonical_refs>

<code_context>
## Existing Code Insights

### Reusable Assets
- `StarRendering.ApparentBrightness` / `AngularRadius` / `Exposure` — the single shared brightness+size
  model; the PSF point, additive mesh emission, glow halo, AND the UniObject-driven marker all reuse it
  so the unified look cannot drift (this is precisely what `StarRendering` was created for).
- `WorldRenderer.RenderBodyAt` — already computes per-frame true-metres distance + emission; the additive
  mesh-blend term layers onto this.
- `SkyboxRenderer.GetSkyDirection` + galaxy procedural loop in `skybox.gdshader` — kept for galaxies
  (D-48/D-49); extended for the D-54 look and D-59 tilt. Its star-point loop is removed (D-56).
- `Hud` target machinery + `UniMath.RelativeMetres`/`RelativePosition` — the marker rework (D-50) reuses
  these for UniObject-driven position + distance.
- `WorldEnvironment` glow/bloom — fed by the proximity glow halo (D-55).

### Established Patterns
- Read-only renderers consuming `GameWorld` state (never mutate `UniVec3`/`TranslatePos`); per-frame
  reposition, lazy `GetOrCreate`, `(uint)i < (uint)Count` bounds checks. The new MultiMesh star-point
  renderer follows this.
- One shared appearance model (StarRendering) + one render-scale model (RND-06) + one speed model — extend,
  don't add scale-specific modes. The PSF unification continues this discipline.
- Play-test tuning knobs as `[Export]` floats (GALAXY_DISC_SCALE, StarBrightness) — PSF/glow knobs follow.
- `UniMath` LCA distance math (CLAUDE.md) for all cross-frame distances at galaxy/universe scale.

### Integration Points
- **Star points:** new MultiMesh PSF renderer (D-56) reads every star's `UniObject` (pos via UniMath,
  Luminosity/RadiusMeters/BaseColor) → floored point every frame; far-plane-clip so never culled.
- **Mesh blend:** `WorldRenderer.RenderBodyAt` adds the additive sphere-mesh contribution as true distance
  shrinks (D-53), releasing the floor toward true 1:1 (D-58 watch item).
- **Galaxies:** `TierClassifier` → always sky (D-49); `SkyboxRenderer` pushes current-tier galaxies in
  Universe space (D-48); shader reworked for D-54 look + D-59 tilt.
- **Marker:** `Hud` circle computed from `UniObject` (D-50), min-radius floor (D-46), edge-marker fallback.
- **Glow:** distance-driven halo (D-55) feeds WorldEnvironment bloom.

</code_context>

<specifics>
## Specific Ideas

- **User's verbatim direction (2026-06-18):** "Stars should always be rendered additionally to the skybox;
  when approaching a star the space should also be brighter in a semi-realistic way... The mesh rendering
  of stars should integrate seamlessly with the skybox rendering when near a star. Star rendering and galaxy
  rendering should be similar looking, maybe ... one for all shader that has a good trade off for visually
  good looking and realistic, that covers star, nebula and galaxy rendering."
- **User principle (2026-06-18):** the target marker's position and size must come from the actual configured
  `UniObject` parameters, not the mesh (D-50) — generalizes findability to every target.
- **Build-first discipline (2026-06-18):** implement the new rendering technique first; address
  floor-release/consistency problems only if they actually occur (D-58).
- **Reference feel:** Elite/Frontier vast traversable cosmos; godot-starlight's "100k stars, always
  visible" PSF model + Star Nest's volumetric star/nebula/galaxy look as the visual north star.

</specifics>

<deferred>
## Deferred Ideas

- **True nebula rendering + authored nebula bodies** (the full Star-Nest volumetric star+nebula+galaxy
  shader) — future phase; Phase 5 unifies star+galaxy LOOK only (D-54).
- **Full volumetric light scattering / god-rays near stars** — future phase; Phase 5 uses glow halo + bloom (D-55).
- **Whole-hierarchy nav-HUD + tracking name/distance label** — backlog 999.1; Phase 5 only reworks the
  existing target circle to be UniObject-driven (D-50).
- **Shader-rendered ellipsoid target outline** — backlog 999.2; the D-50 circle stays a 2D analytic circle.
- **Floor-release curve / fine consistency tuning** — only if it manifests in play-test (D-58), not pre-designed.

None beyond the above — discussion stayed within the findability/visibility domain (nebula + scattering
explicitly scoped out by the user).

</deferred>

---

*Phase: 5-outer-tier-body-findability-galaxy-visibility*
*Context gathered: 2026-06-18*
