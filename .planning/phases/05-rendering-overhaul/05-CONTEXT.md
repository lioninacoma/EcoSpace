# Phase 5: Rendering Overhaul - Context

**Gathered:** 2026-06-19
**Status:** Ready for planning

<domain>
## Phase Boundary

**A unified, post-process-driven luminous-body renderer that replaces the Sky-shader skybox entirely.** Every star and galaxy, in every space, becomes a screen-projected point of light drawn in a post-processing pass; near bodies get glow/halo and resolve into their sphere meshes, distant bodies are points/placeholders, and the transitions between representations are continuous by construction.

**The vision (user direction, 2026-06-19):**
- **Stars (every space):** a post-process pass adds **glow + halo** to nearby stars and a **point light** for distant stars, blending seamlessly along distance — a star you approach grows its halo and resolves into its sphere mesh; a star you leave shrinks back to a point.
- **Galaxies (distance crossfade, like real life):**
  - *Far* → galaxy renders as a post-process **placeholder/disc**.
  - *Approaching* → the galaxy representation **fades out** as its individual **stars take over** (it resolves into stars).
  - *Leaving a galaxy* → the galaxy post-process **fades in by distance**, blending with the outgoing galaxy's still-visible stars when you turn back to look.
  - Only from far away does the galaxy placeholder render in post-processing.
- **Skybox replaced:** `SkyboxRenderer` + `skybox.gdshader` (the Sky-background star points and galaxy discs) are **removed** and replaced by this post-process layer.
- **Meshes stay:** planets and the close sun(s) remain real sphere **meshes** in `WorldRenderer`; the post-process glow blends *over* them (depth-aware) and the distant-point / galaxy work happens in post.

**Why post-process and not the existing Sky shader:** a Sky background renders *behind* everything and cannot blend with foreground meshes. A depth-aware post-process pass can wrap glow around a near star mesh, let planets occlude distant points, and drive the mesh↔point and galaxy↔stars crossfades — which the Sky shader structurally cannot.

**This is a foundational rewrite executed incrementally** — one phase, 4 sequential play-test-gated plans (see D-08). Working on branch `phase-05-rendering-overhaul` for easy revert (D-10).

**This supersedes ROADMAP Phase 5 criterion #5** ("expose seams *without* implementing the findability fix; debts stay out of scope"). The distance-fade model **is** the fix for the tracked debts, which are now folded in and closed here (D-09). ROADMAP + STATE need updating to reflect the redefined scope.

</domain>

<decisions>
## Implementation Decisions

### Rewrite Strategy
- **D-01: Consolidate, preserve the precision math.** Architecturally a rewrite (one unified classify→describe→draw pipeline replacing the scattered per-`ObjectType` special-case branches), but the hard-won primitives are **preserved/extended, not re-derived**: `UniMath` LCA-relative walks, the floating-origin per-frame sync loop, `StarRendering` (appearance source of truth), `TierClassifier` (pure, unit-tested), and `body_lit.gdshader`. Lowest regression risk; the real problem is structural, not mathematical. (Rejected: full clean-slate rewrite that re-implements the precision math.)

### Unified Render Pipeline & Pop-Free Handoff (criterion #2)
- **D-02: One descriptor, shared by all drawers.** Each frame, each luminous body is described **once** — direction (projected via `UniMath`), angular size, brightness, color (via `StarRendering`), plus a distance-driven representation/LOD weight. Point, halo/glow, galaxy-disc, and mesh drawers all consume the **same** descriptor. A tier/distance crossing = the *same numbers* routed to a different drawer, so position/brightness/color match **by construction** — pops are mathematically impossible, not coincidentally avoided. Collapses today's two separate caches (`SkyboxRenderer._skyDirs` + `WorldRenderer._lastRenderPositions`) into one source. (Rejected: keep two caches that merely call shared functions.)
- **D-03: Crossfades are distance-driven, continuous, and depth-aware.** Star near↔far (mesh+halo ↔ point) and galaxy far↔near (disc ↔ resolved stars) are smooth functions of distance, not discrete SOI-boundary swaps. This evolves the prior D-22 ("swap on scale-boundary only") — the new model is a continuous distance crossfade, which the post-process pass enables. The pass must read the **depth buffer** so planets/near meshes occlude distant points and glow wraps correctly around near star meshes.

### Acceptance Bar
- **D-04: No-regression, improvement welcome, judged subjectively per tier.** The bar is "nothing gets worse"; the look may legitimately *improve* (tighter handoff, more consistent brightness/glow) as the cleaner structure allows. Verified by **per-tier in-game play-test** (Planet / Star / Galaxy / Universe), not pixel-matching against the old build. (Rejected: strict pixel-parity bars — the user wants freedom to improve during the overhaul.)

### Post-Process Composition (dither)
- **D-05: Glow/point/galaxy pass composes in HDR/linear BEFORE the dither quantizes the whole frame.** The new luminous pass blends in linear/HDR space; the existing 8-bit dither (`dithering.gdshader` / `PostProcessRenderer`) then quantizes the *composited* frame — one unified palette edge-to-edge (preserves D-27). The new pass and the dither pass must be ordered correctly in the post-process stack.
- **D-06: CRT scanlines (RND-01) are OUT of scope** — deferred to their own later task. `crt.gdshader` was deleted in a prior refactor; reintroducing it is not part of this phase. This phase stays focused on the luminous-body rendering vision. Plan 4 = dither composition + cleanup only.

### Skybox Replacement & Mesh Boundary
- **D-07: Remove the Sky-shader skybox; keep meshes in `WorldRenderer`.** `SkyboxRenderer.cs` + `skybox.gdshader` are removed/replaced by the post-process layer. Planets and the close sun(s) stay sphere meshes in `WorldRenderer` (floating-origin). The descriptor that fed the Sky shader now feeds the post-process pass instead.

### Incremental Structure
- **D-08: One phase, 4 sequential play-test-gated plans.** Don't implement too much in one execution (it breaks things). Each plan is executed and **play-tested before the next**:
  1. **Branch + descriptor/projection foundation** — build the unified per-body descriptor + screen projection alongside the *still-running* skybox (no visual change yet; validates projection + precision).
  2. **Post-process star glow + point lights** — build the depth-aware post-process pass; near stars get glow/halo, distant stars are points; blend with the existing sphere meshes; replace the Sky-shader star points. Play-test Star/Galaxy space.
  3. **Galaxy distance crossfade** — galaxy disc fades out approaching (resolves into stars) / fades in leaving; remove the Sky-shader skybox entirely. Play-test intergalactic.
  4. **Dither composition + cleanup** — ensure the luminous pass composes cleanly into the 8-bit dither (D-05); delete dead skybox code; final per-tier parity/improvement play-test across all spaces.

### Debt Relationship
- **D-09: Fold P1 + P2 in and close them here.** The galaxy distance-crossfade (Plan 3) makes galaxies visible/approachable in Universe space → **closes `galaxy-visibility-in-universe-space` (P2)** and revisits/supersedes D-28. Post-process point lights with an always-visible floor keep galaxy-space stars findable → **closes `galaxy-space-star-meshes-invisible` (P1)**. Both marked resolved-by-Phase-5, verified at the play-test gates. (`galaxy-disc-tilt-foreshortening` polish: re-confirm after the skybox path is reworked.) This supersedes ROADMAP criterion #5.

### Workflow
- **D-10: Work on branch `phase-05-rendering-overhaul`** (created 2026-06-19) so a full revert of the rendering rewrite is trivial. All Phase 5 work, including planning docs, lives on this branch.

### Carried Forward (locked upstream — do not re-litigate)
- ⛔ **No manual clip-space billboard MultiMesh** (the abandoned `StarPointRenderer` anti-pattern, HANDOFF c98f56c). A depth-aware post-process overlay reading projected positions is a **different, allowed** technique — constraint stays satisfied.
- ✅ `UniMath` LCA-relative precision math must be preserved (CLAUDE.md "Position Math" convention — catastrophic-cancellation guard at ~1e30 m Universe scale). Never form absolute-from-root metres and subtract.
- ✅ `StarRendering` remains the single source of truth for star appearance (Luminosity/RadiusMeters/BaseColor → magnitude curve + one global `Exposure` knob).
- ✅ 8-bit dither (D-27), unit-space render (RND-06), per-space `1e-8` render factors, `1e6` camera far plane, `body_lit.gdshader` space-independent shading.
- ✅ D-21 instant exact-match (no crossfade *of the palette* — but D-03's distance LOD blend is in HDR before quantization, so it is not a palette cross-dissolve).

### Claude's Discretion (for researcher/planner)
- The exact post-process technique for feeding N projected luminous-body positions to a full-screen shader (uniform arrays like today's `star_dirs`, vs a written point/ID buffer, vs additive sprite accumulation) — **flag for research**; must be depth-aware.
- Depth-buffer access in the post-process pass (Godot 4.6 Forward+ DEPTH_TEXTURE / screen-space) and correct ordering relative to the dither pass.
- Exact glow/halo kernel, the distance→LOD-weight curves for star mesh↔point and galaxy disc↔stars crossfades, and the always-visible brightness floor (P1).
- Galaxy "placeholder" representation in post (reuse the procedural disc look from `skybox.gdshader` vs a simpler post sprite) and where the disc↔stars crossfade thresholds sit.
- How the descriptor/projection is structured for unit-testability (extend `TierClassifier` tests with the representation/LOD-weight logic).

</decisions>

<canonical_refs>
## Canonical References

**Downstream agents MUST read these before planning or implementing.**

### Phase requirements & scope (note: scope redefined this session — see D-01..D-10)
- `.planning/ROADMAP.md` §"Phase 5: Rendering Overhaul" — goal + original success criteria. **NOTE:** criterion #5 (debts out of scope / seams only) is **superseded** by D-09; criteria #1–4 still apply. ROADMAP needs updating to the post-process/skybox-replacement scope.
- `.planning/REQUIREMENTS.md` §Rendering — RND-01 (8-bit, CRT deferred per D-06), RND-02/04/05/07 (tier-member mesh/point rendering + continuous point↔mesh handoff), RND-03/RND-06 (dithered look, unit-space render).
- `.planning/PROJECT.md` §Context "Skybox model (tiered)" + §Key Decisions (RND-06 unit-space render, 8-bit palette, sequence) — the tiered model and render-scale rationale.
- `.planning/phases/02-dynamic-skybox/02-CONTEXT.md` — D-17..D-27 (magnitude model, color/brightness from Luminosity, dither integration, handoff baseline). The appearance rules carry over; **D-22 evolves** to a continuous distance crossfade (D-03) and the Sky-shader delivery is **replaced** (D-07).

### Engine / render code to consolidate (preserve precision, replace structure)
- `Scripts/Render/WorldRenderer.cs` — floating-origin mesh sync (`SyncBodies`, `RenderBodyAt`, `RenderFactorFor`, per-space factors, `GetRenderPosition`/`GetRenderRadius` handoff accessors, `ComputeStarRenderPosFromHierarchy`). **Keep meshes here**; feed the unified descriptor; the two handoff caches collapse into D-02's single descriptor.
- `Scripts/Render/SkyboxRenderer.cs` — **to be removed/replaced** (D-07). Its per-frame classify→project→appearance loop (`SyncSkyPoints`, `_skyDirs`, home-galaxy suppression guard, `PixelAngularSize`) is the reference for what the post-process pass must reproduce — migrate the logic, drop the Sky-shader delivery.
- `Shaders/skybox.gdshader` — **to be removed/replaced** (D-07). Reference for the procedural galaxy-disc look (`galaxy_types`/orientation/seed) the post-process galaxy placeholder may reuse.
- `Scripts/Render/StarRendering.cs` — **single source of truth, preserve** (`ApparentBrightness`, `AngularRadius`, `Exposure`). The descriptor's brightness/size come from here for both points and meshes.
- `Scripts/TierClassifier.cs` — **pure, unit-tested, preserve/extend** (`Classify` → `SkyTier`). Extend toward the representation/LOD-weight decision; keep it Godot-free for testing.
- `Scripts/Render/PostProcessRenderer.cs` + `Shaders/dithering.gdshader` — the 8-bit dither pass; the new luminous pass composes BEFORE it (D-05). The new post-process glow/point/galaxy pass likely lives alongside / ahead of this.
- `Shaders/body_lit.gdshader` — space-independent Lambert body shading; preserved for planet/body meshes.
- `Scripts/Math/UniMath.cs` (+ `UniVec3.cs`, `Double3.cs`, `Long3.cs`) — LCA-relative position math; **mandatory** for all projection. See CLAUDE.md §"Position Math (UniVec3 / UniMath)".
- `Scripts/TestSetup.cs` — authored hierarchy (Root→Universe→Galaxy→Star→Planet, sibling stars, 3 galaxies at 1:1 distances) + per-body `Luminosity`/`RadiusMeters`/`BaseColor`/`GalaxyType`/orientation. The scene the rewrite is validated against.
- `Main.tscn` — `Camera3D` (far=1e6), `WorldEnvironment` (glow), the Sky `Environment`/`ShaderMaterial` that gets **removed** when the skybox is replaced, and the post-process `ColorRect`/`CanvasLayer` host the new pass plugs into.

### Tracked debts folded in & closed here (D-09)
- `.planning/todos/pending/galaxy-space-star-meshes-invisible.md` (P1) — closed by post-process point lights + always-visible floor.
- `.planning/todos/pending/galaxy-visibility-in-universe-space.md` (P2) — closed by galaxy distance crossfade; revisits D-28.
- `.planning/todos/pending/galaxy-disc-tilt-foreshortening.md` — re-confirm after the skybox path rework.

### Codebase maps
- `.planning/codebase/ARCHITECTURE.md`, `.planning/codebase/STRUCTURE.md`, `.planning/codebase/CONVENTIONS.md` — `Render` namespace conventions, layer boundaries, where the new renderer lives.

</canonical_refs>

<code_context>
## Existing Code Insights

### Reusable Assets
- `SkyboxRenderer.SyncSkyPoints()` — the classify→project→appearance loop (LCA-relative direction, `StarRendering` size/brightness, BaseColor, home-galaxy suppression, pixel-angular floor) is the blueprint for the post-process descriptor pass; migrate the logic, change only the delivery (Sky shader → depth-aware post-process).
- `StarRendering` (`ApparentBrightness` / `AngularRadius` / `Exposure`) — already the shared mesh+sky appearance rule; becomes the descriptor's appearance source so near/far/galaxy representations match.
- `WorldRenderer` floating-origin loop + `GetRenderPosition`/`GetRenderRadius` — mesh positions/radii already computed per frame; these collapse into D-02's single descriptor instead of a parallel cache.
- `TierClassifier` (pure, 30+ green tests) — extend for representation/LOD-weight; keep Godot-free.
- `PostProcessRenderer` + `dithering.gdshader` — established full-screen post-process host + 8-bit quantization to compose into (D-05).
- `WorldEnvironment` glow (Main.tscn) — existing bloom; the new glow/halo can lean on or replace it.

### Established Patterns
- Read-only renderer consuming `GameWorld` state — MUST NOT mutate `UniVec3` / call `TranslatePos`. The new layer keeps this contract.
- Per-frame node reposition, never spawn/free per frame; lazy `GetOrCreate` for mesh instances.
- Integer-index object model with `(uint)i < (uint)Count` bounds checks; `Space` enum + `ParentSpace` drive tier classification.
- `UniMath` LCA-relative subtraction (same-scale integer Units cancellation, single `ToDouble3()` on the small delta) — the only sanctioned cross-space position math.

### Integration Points
- **Descriptor pass** → for the ship's space, describe every luminous body once (dir via `UniMath`, size/brightness/color via `StarRendering`, distance→LOD weight); feed all drawers.
- **Post-process luminous pass** → consumes descriptor + depth buffer; draws point/glow/halo for stars and disc placeholder for far galaxies; composes in HDR before dither (D-05).
- **Mesh path (`WorldRenderer`)** → still draws planets + near sun spheres; the post-process glow wraps them depth-aware.
- **Skybox removal** → delete `SkyboxRenderer` + `skybox.gdshader` + the Sky `Environment` wiring in Main.tscn once the post-process pass carries their content (Plan 3).

</code_context>

<specifics>
## Specific Ideas

- **Real-life galaxy resolution:** a galaxy seen from far is a glowing disc placeholder; as you fly in it dissolves into its constituent stars (the disc fades out, the star points take over); flying back out, the disc fades in by distance and blends with the outgoing galaxy's still-visible stars when you turn to look. This physical "galaxies are made of stars" crossfade is the headline of the phase.
- **Seamless star LOD:** nearby star = sphere mesh + glow/halo; distant star = point light; one continuous distance blend, no visible swap.
- **Reference feel:** Elite/Frontier vastness; honest 1:1 scale where distant things are genuinely small but kept findable via the post-process glow floor.
- **"Take time, do it correctly":** explicitly incremental (D-08), play-test gate per plan, branch for easy revert (D-10) — the user expects this to be careful, not rushed.

</specifics>

<deferred>
## Deferred Ideas

- **CRT scanline effect (RND-01)** — deferred to its own later task (D-06); `crt.gdshader` was deleted and is not reintroduced here.
- **`galaxy-disc-tilt-foreshortening`** — RESOLVED-PENDING-VERIFY; re-confirm after the skybox→post-process rework (not actively reworked this phase beyond verification).
- **Procedural universe generation, cockpit art, economy, combat** — milestone out-of-scope, unchanged.

None — discussion stayed within (the redefined) phase scope.

</deferred>

---

*Phase: 5-rendering-overhaul*
*Context gathered: 2026-06-19*
