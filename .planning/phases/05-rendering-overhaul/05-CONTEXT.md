# Phase 5: Rendering Overhaul - Context

**Gathered:** 2026-06-19
**Revised:** 2026-06-19 (post-05-02 play-test — architecture reversed; see banner)
**Status:** Ready for re-planning of plans 2–4 (Plan 1 complete)

> ## ⚠ ARCHITECTURE REVISION (2026-06-19, after 05-02 play-test)
>
> The original phase frame — "replace the Sky-shader skybox **entirely** with a unified
> depth-aware post-process luminous pass" — is **REVERSED**. Play-testing 05-02 proved a
> post-process `shader_type spatial` quad **cannot occlude behind opaque meshes**: distant
> stars/galaxies painted in front of planets, and the depth-texture gate was a fragile
> workaround. A `shader_type sky` renders at infinite distance behind all geometry
> automatically — the correct tool for distant bodies.
>
> **New split (locked this session):**
> - **Sky shader** (`skybox.gdshader`, KEPT — not deleted) draws DISTANT stars (points) +
>   galaxies (discs), fed by the Plan-1 `LuminousBodyDescriptor` pipeline.
> - **Post-process** (`luminous_pass.gdshader`, repurposed from 05-02) draws GLOW / HALO
>   around NEAR stars only — its correct screen-space use.
> - **Meshes** (`WorldRenderer`) still draw planets AND near stars (parent sun + in-SOI
>   siblings) as sphere meshes with glow wrapping them.
>
> What survives: **D-01, D-02, D-04, D-06, D-10** unchanged. **D-03, D-05** rationale updated.
> **D-07 reversed.** **D-08, D-09 revised.** New **D-11–D-14** added. Plan 1 (05-01 descriptor
> pipeline) stands as-is and is the single feed for both layers.

<domain>
## Phase Boundary

**A descriptor-driven luminous-body renderer with a two-layer drawer split.** Each frame, the
Plan-1 `LuminousBodyDescriptor` pipeline describes every luminous body once (direction, angular
size, brightness, color, distance→LOD weight). Two drawers consume that single descriptor:

1. **Sky shader (`skybox.gdshader`)** — draws **distant** stars as points and **galaxies** as
   procedural discs, rendered at infinite distance *behind* all geometry (no occlusion hacks).
2. **Post-process pass (`luminous_pass.gdshader`)** — adds **glow / halo** around **near** stars
   in screen space, composited in HDR before the 8-bit dither quantizes the frame.

Meshes (`WorldRenderer`) draw planets **and near stars** (parent sun + in-SOI siblings) as real
sphere bodies; the post-process glow wraps them. A star you approach goes point → glow grows →
sphere mesh resolves; leaving, it shrinks back to a point — one continuous distance blend driven
by the descriptor's LodWeight, pop-free by construction. A galaxy seen from far is a glowing disc
in the sky shader; approaching, the disc fades out as its individual stars take over; leaving, the
disc fades back in.

**Why this split (the 05-02 lesson):** a sky shader renders behind everything and is the right
tool for the distant celestial sphere; a post-process pass is the right tool for screen-space
glow/halo around near bodies. The original plan conflated both into one post-process pass, which
structurally cannot occlude behind foreground meshes. Picking the shader type by what it must
occlude against is the durable rule.

**Incremental execution:** one phase, 4 sequential play-test-gated plans (D-08). Plan 1 done.
Branch `phase-05-rendering-overhaul` for easy revert (D-10).

**Supersedes ROADMAP Phase 5 criterion #5** — the distance-fade model is still the fix for the
tracked debts (D-09), now delivered via the sky shader + near-star mesh path rather than a pure
post-process pass.

</domain>

<decisions>
## Implementation Decisions

### Rewrite Strategy
- **D-01: Consolidate, preserve the precision math.** (UNCHANGED) Architecturally a rewrite (one unified classify→describe→draw pipeline replacing scattered per-`ObjectType` branches), but the hard-won primitives are preserved/extended: `UniMath` LCA-relative walks, the floating-origin per-frame sync loop, `StarRendering`, `TierClassifier`, `body_lit.gdshader`. The problem is structural, not mathematical.

### Unified Render Pipeline & Pop-Free Handoff (criterion #2)
- **D-02: One descriptor, shared by all drawers.** (UNCHANGED — DELIVERED in Plan 1, 05-01) Each frame each luminous body is described once — direction (via `UniMath`), angular size, brightness, color (via `StarRendering`), plus a distance LOD weight. Sky-shader point/disc, post-process glow/halo, and mesh drawers all consume the SAME descriptor, so a crossing routes the same numbers to a different drawer — pops are mathematically impossible. `LuminousDescriptorBuilder` (Render namespace, process_priority −10) is the single feed.
- **D-03: Crossfades are distance-driven and continuous; occlusion handled per-layer.** (RATIONALE UPDATED) Star near↔far and galaxy far↔near are smooth functions of distance, not discrete SOI swaps. Occlusion is now correct **by layer, not by a depth gate**: the sky shader renders distant points/discs *behind* all geometry automatically; near-star glow/halo is screen-space over near meshes that are themselves depth-sorted by the mesh renderer. (Supersedes the old "post-process pass must read the depth buffer to occlude distant points" mechanism that failed at the 05-02 play-test.)

### Star Handoff & Near-Star Findability
- **D-11: Three-stage continuous star handoff (point → glow → mesh).** A star transitions: FAR = sky-shader point; MID = sky point fades out as post-process glow/halo fades in; NEAR = `WorldRenderer` sphere mesh resolves with glow wrapping it. The stage weights come from the descriptor's `LodWeight` (Plan-1 `LuminousLod.StarMeshWeight`), so position/brightness/color match across the blend. (Rejected: 2-stage point↔mesh; sky-shader-does-everything.)
- **D-12: Near stars render as sphere meshes + glow; the missing-sun regression is fixed in-scope.** Parent sun and in-SOI sibling stars MUST render as real `WorldRenderer` sphere meshes (like planets) with post-process glow, NOT as sky-shader points or glow-only sprites. The 05-02 play-test bug — home sun not rendering up close, planet/star space showing "only galaxy" — is treated as an in-scope regression to fix during this rework. (Rejected: defer the fix; glow-sprite-only with no sphere.)

### Galaxy Rendering
- **D-13: Galaxies render in the sky shader, reusing the procedural disc math.** Galaxies are drawn in `skybox.gdshader` reusing its existing procedural disc (spiral/elliptical/tilt/seed). Distance crossfade: far disc fades out as individual stars take over approaching; fades back in leaving (the "galaxies are made of stars" headline). The "galaxy pops out of nowhere" bug seen at the 05-02 play-test (crossfade-threshold / antipodal-gate) is fixed here. (Rejected: simpler sprite disc; galaxies in post-process.)

### Acceptance Bar
- **D-04: No-regression, improvement welcome, judged subjectively per tier.** (UNCHANGED) Bar is "nothing gets worse"; look may improve. Verified by per-tier in-game play-test (Planet / Star / Galaxy / Universe), not pixel-matching.

### Post-Process Composition (dither)
- **D-05: The near-star glow/halo pass composes in HDR/linear BEFORE the dither quantizes the frame.** (SCOPE NARROWED) The post-process glow pass blends in linear/HDR; the existing 8-bit dither (`dithering.gdshader` / `PostProcessRenderer`) then quantizes the composited frame — one unified palette edge-to-edge. The sky shader already renders into the frame before post-process. Ordering of glow pass vs dither pass must be correct.
- **D-06: CRT scanlines (RND-01) are OUT of scope.** (UNCHANGED) Deferred to a later task. Final plan = dither composition + cleanup only.

### Skybox: KEEP and refeed (reverses original D-07)
- **D-07 (REVERSED): Keep the Sky-shader skybox; refeed it from the descriptor.** `SkyboxRenderer.cs` + `skybox.gdshader` + the Sky `Environment` wiring are **NOT removed** — they become the distant-body drawer, fed by the Plan-1 `LuminousBodyDescriptor` (replacing `SkyboxRenderer`'s own `SyncSkyPoints` cache with the shared descriptor). Planets and near stars stay sphere meshes in `WorldRenderer`. (The original D-07 "remove the skybox" is void.)

### Incremental Structure
- **D-08 (REVISED): One phase, 4 sequential play-test-gated plans — findability-first re-slice of 2–4.** Plan 1 is complete. Remaining:
  1. ✅ **DONE (05-01):** Descriptor/projection foundation (`LuminousBodyDescriptor` + `LuminousLod` + `LuminousDescriptorBuilder`), validated alongside the still-running skybox, zero visual change.
  2. **Sky-shader refeed + near-star findability:** drive `skybox.gdshader` from the descriptor so distant stars + galaxies render correctly in every space; fix the near-sun / in-SOI-sibling sphere-mesh rendering (D-12). Repurpose the 05-02 `luminous_pass`/`LuminousPassRenderer` as needed or hold for Plan 3. Play-test Planet/Star space (sun visible, distant stars visible).
  3. **Post-process glow/halo + 3-stage handoff (D-11):** narrow `luminous_pass.gdshader` to near-star glow/halo; wire the point → glow → mesh continuous blend via LodWeight. Play-test Star space (no pop).
  4. **Galaxy disc→stars crossfade (D-13) + HDR dither composition (D-05) + cleanup:** galaxy far-disc ↔ resolved-stars crossfade and pop-bug fix; ensure glow composes in HDR before dither; delete dead code. Play-test Galaxy/Universe space.
  (Rejected: galaxy-focused middle ordering; planner-decides slicing.)

### Debt Relationship
- **D-09 (REVISED): Fold P1 + P2 in and close them here — skybox is NOT removed.** P2 (`galaxy-visibility-in-universe-space`) closed by the sky-shader galaxy disc + distance crossfade (D-13). P1 (`galaxy-space-star-meshes-invisible`) closed by sky-shader distant star points with an always-visible brightness floor PLUS the near-star mesh fix (D-12). Both verified at the play-test gates. `galaxy-disc-tilt-foreshortening`: re-confirm after the refeed. Supersedes ROADMAP criterion #5. (The original "remove the skybox in Plan 3" mechanism is void — the skybox stays.)

### Workflow
- **D-10: Work on branch `phase-05-rendering-overhaul`.** (UNCHANGED) Full revert trivial; all Phase 5 work including planning docs lives here.

### Carried Forward (locked upstream — do not re-litigate)
- ⛔ **No manual clip-space billboard MultiMesh** (the abandoned `StarPointRenderer` anti-pattern, HANDOFF c98f56c). The sky shader + descriptor-fed points is a different, allowed technique.
- ✅ `UniMath` LCA-relative precision math must be preserved (CLAUDE.md "Position Math" — catastrophic-cancellation guard at ~1e30 m). Never form absolute-from-root metres and subtract.
- ✅ `StarRendering` remains the single source of truth for star appearance.
- ✅ 8-bit dither (D-27), unit-space render (RND-06), per-space `1e-8` render factors, `1e6` camera far plane, `body_lit.gdshader` space-independent shading.
- ✅ `EYEDIR` is sky-shader-only; in a `shader_type spatial` pass reconstruct the world view ray via `normalize((INV_VIEW_MATRIX * vec4(view.xyz, 0.0)).xyz)` (05-02 fix 22e4bc8).

### Claude's Discretion (for researcher/planner)
- Exact mechanism for feeding the descriptor array into `skybox.gdshader` uniforms (reuse today's `star_dirs`/galaxy uniform arrays, fed from `LuminousDescriptorBuilder.Descriptors` instead of `SkyboxRenderer._skyDirs`).
- The near-star glow/halo kernel and the exact `LodWeight` thresholds for the point→glow→mesh stages (Plan-1 `LuminousLod.StarMeshWeight` knobs `StarNearStart`/`StarNearEnd` are play-test-calibrated).
- The galaxy disc↔stars crossfade thresholds and the antipodal/threshold fix for the "pops out of nowhere" bug.
- Whether the 05-02 `LuminousPassRenderer` node is repurposed (glow/halo host) or its quad is reworked; correct ordering of the glow pass vs the dither pass.
- Always-visible brightness floor for distant star findability (P1).

</decisions>

<canonical_refs>
## Canonical References

**Downstream agents MUST read these before planning or implementing.**

### Phase requirements & scope (scope revised twice — see banner + D-01..D-14)
- `.planning/ROADMAP.md` §"Phase 5: Rendering Overhaul" — goal + criteria; criterion #5 superseded by D-09.
- `.planning/REQUIREMENTS.md` §Rendering — RND-01 (CRT deferred, D-06), RND-02/04/05/07 (tier-member mesh/point + continuous handoff), RND-03/RND-06 (dithered look, unit-space render).
- `.planning/PROJECT.md` §Context "Skybox model (tiered)" + §Key Decisions — tiered model + render-scale rationale.
- `.planning/phases/02-dynamic-skybox/02-CONTEXT.md` — D-17..D-27 appearance/magnitude/dither rules carry over; D-22 evolves to continuous crossfade (D-03).
- `.planning/phases/05-rendering-overhaul/05-01-SUMMARY.md` — what Plan 1 delivered (the descriptor pipeline that feeds both layers).

### Engine / render code (preserve precision; refeed structure)
- `Scripts/Render/LuminousDescriptorBuilder.cs` + `LuminousBodyDescriptor.cs` + `LuminousLod.cs` — **Plan-1, the single feed.** Both layers consume `Descriptors[]`. Preserve; extend if needed.
- `Scripts/Render/SkyboxRenderer.cs` — **KEPT (D-07 reversed).** Refeed from the descriptor (replace its `SyncSkyPoints`/`_skyDirs` self-cache with `LuminousDescriptorBuilder.Descriptors`). Keep home-galaxy suppression + `PixelAngularSize`.
- `Shaders/skybox.gdshader` — **KEPT.** The distant-star-points + procedural galaxy-disc drawer (D-13). Driven by descriptor-fed uniforms.
- `Scripts/Render/LuminousPassRenderer.cs` + `Shaders/luminous_pass.gdshader` — **05-02 partial work, repurposed.** Narrowed to near-star glow/halo (D-11); `world_view_dir` reconstruction (not EYEDIR) is in place. Committed 8d28b90 / f6287c3 / 22e4bc8; NOT reverted.
- `Scripts/Render/WorldRenderer.cs` — floating-origin mesh sync. **Draws planets AND near stars as sphere meshes** (D-12); feed from the descriptor; near-sun rendering fix lives here.
- `Scripts/Render/StarRendering.cs` — **single source of truth, preserve** (`ApparentBrightness`, `AngularRadius`, `Exposure`).
- `Scripts/TierClassifier.cs` — **pure, unit-tested, preserve/extend.**
- `Scripts/Render/PostProcessRenderer.cs` + `Shaders/dithering.gdshader` — 8-bit dither; the glow pass composes BEFORE it (D-05).
- `Shaders/body_lit.gdshader` — space-independent Lambert body shading for planet/star meshes.
- `Scripts/Math/UniMath.cs` (+ `UniVec3.cs`, `Double3.cs`, `Long3.cs`) — LCA-relative position math; mandatory. CLAUDE.md §"Position Math".
- `Scripts/TestSetup.cs` — authored hierarchy + per-body `Luminosity`/`RadiusMeters`/`BaseColor`/`GalaxyType`/orientation. Validation scene.
- `Main.tscn` — `Camera3D` (far=1e6), `WorldEnvironment` (glow), the Sky `Environment`/`ShaderMaterial` (KEPT), the post-process `ColorRect`/`CanvasLayer`, `LuminousDescriptorBuilder` (priority −10), `LuminousPassRenderer` (Camera3D child).

### Tracked debts folded in & closed here (D-09)
- `.planning/todos/pending/galaxy-space-star-meshes-invisible.md` (P1) — sky-shader points + floor + near-star mesh fix.
- `.planning/todos/pending/galaxy-visibility-in-universe-space.md` (P2) — sky-shader galaxy disc + crossfade.
- `.planning/todos/pending/galaxy-disc-tilt-foreshortening.md` — re-confirm after refeed.

### Codebase maps
- `.planning/codebase/ARCHITECTURE.md`, `STRUCTURE.md`, `CONVENTIONS.md` — `Render` namespace conventions, layer boundaries.

</canonical_refs>

<code_context>
## Existing Code Insights

### Reusable Assets
- `LuminousDescriptorBuilder` (Plan 1) — the per-frame classify→project→appearance loop already exists and is tested; both drawers consume `Descriptors[]`. No re-derivation needed.
- `SkyboxRenderer` + `skybox.gdshader` — the existing, working distant-bodies drawer (star points + galaxy discs). Refeed it from the descriptor instead of rewriting from scratch.
- `StarRendering` (`ApparentBrightness` / `AngularRadius` / `Exposure`) — shared mesh+sky+glow appearance rule.
- `WorldRenderer` floating-origin loop — already positions/sizes meshes per frame; extend to render near stars as sphere meshes (D-12).
- `LuminousPassRenderer` + `luminous_pass.gdshader` (05-02) — repurpose to near-star glow/halo.
- `PostProcessRenderer` + `dithering.gdshader` — full-screen post-process host + 8-bit quantization to compose into (D-05).
- `WorldEnvironment` glow (Main.tscn) — existing bloom the glow/halo can lean on.

### Established Patterns
- Read-only renderer consuming `GameWorld` state — MUST NOT mutate `UniVec3` / call `TranslatePos`.
- Per-frame node reposition, never spawn/free per frame; lazy `GetOrCreate` for mesh instances.
- Integer-index object model with `(uint)i < (uint)Count` bounds checks; `Space` enum + `ParentSpace` drive tier classification.
- `UniMath` LCA-relative subtraction — the only sanctioned cross-space position math.

### Integration Points
- **Descriptor pass (Plan 1, done)** → describes every luminous body once; feeds all drawers.
- **Sky shader (`SkyboxRenderer`/`skybox.gdshader`)** → distant star points + galaxy discs, fed by the descriptor; renders behind geometry.
- **Post-process glow pass (`LuminousPassRenderer`/`luminous_pass.gdshader`)** → near-star glow/halo, composes in HDR before dither.
- **Mesh path (`WorldRenderer`)** → planets AND near stars (parent sun + siblings) as sphere meshes (D-12); glow wraps them.

</code_context>

<specifics>
## Specific Ideas

- **Real-life galaxy resolution:** far galaxy = glowing disc in the sky shader; flying in, the disc fades out as its constituent stars take over; flying out, the disc fades in and blends with the outgoing galaxy's still-visible stars. The headline of the phase.
- **Seamless star LOD (3-stage):** distant = sky-shader point; mid = point fades, glow/halo grows; near = sphere mesh + glow. One continuous distance blend, no swap.
- **Near stars are real bodies:** the parent sun must render as a sphere mesh up close (the 05-02 missing-sun bug), exactly like planets do.
- **Reference feel:** Elite/Frontier vastness; honest 1:1 scale where distant things are genuinely small but kept findable via a brightness floor.
- **"Take time, do it correctly":** incremental (D-08), play-test gate per plan, branch for easy revert (D-10).

</specifics>

<deferred>
## Deferred Ideas

- **CRT scanline effect (RND-01)** — deferred to its own later task (D-06).
- **`galaxy-disc-tilt-foreshortening`** — re-confirm after the refeed, not actively reworked.
- **Procedural universe generation, cockpit art, economy, combat** — milestone out-of-scope.

</deferred>

---

*Phase: 5-rendering-overhaul*
*Context gathered: 2026-06-19 · Revised after 05-02 play-test: 2026-06-19*
