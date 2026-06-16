# Phase 3: Cross-Galaxy Travel - Context

**Gathered:** 2026-06-16
**Status:** Ready for planning

<domain>
## Phase Boundary

Deliver the second sequenced milestone (**TRV-02**): the player flies from inside one galaxy's SOI, out into intergalactic (Universe) space, and into a second galaxy's SOI — with full Root → Universe → Galaxy → Star → Planet SOI transitions, the skybox re-tiering correctly at each scale boundary, and the **visible** Star↔Galaxy point↔mesh promotion/demotion that Phase 2 built the machinery for. Concretely, Phase 3 delivers:

1. **Galaxy/universe-scale test data (WORLD extension):** Extend `TestSetup` from a single galaxy to **3 real, flyable galaxies** at true 1:1 intergalactic distances (galaxy-tier + universe-tier portions of the hand-authored world).
2. **Galaxy rendering (new):** Galaxies rendered **procedurally in the sky shader** (per-galaxy spiral/elliptical type), growing as you approach, never as world-space meshes.
3. **Galaxy-tier mesh rendering (galaxy portions of RND-02/RND-04):** In Galaxy space, the current galaxy's stars/suns render as emissive sphere meshes (promoted from skybox light-points).
4. **Visible skybox↔mesh handoff (galaxy portion of RND-05/RND-07):** The Star↔Galaxy point→mesh promotion/demotion is wired and visibly continuous (Phase 2 built the baseline cache; Phase 3 makes it visible). In Galaxy space the skybox carries **only other galaxies**.
5. **Intergalactic flight (TRV-02):** FTL-equivalent speed via the existing distance-based auto-scale, tuned so the crossing completes in a reasonable play session.

**Out of scope (later phases / backlog):** whole-hierarchy target selector + world-pinned target outline + tracking label (Backlog 999.1, overrides D-12); procedural generation, cockpit art, economy, combat, CRT overlay, boost/audio (v2). No new HUD beyond reusing Phase-1 machinery.

</domain>

<decisions>
## Implementation Decisions

> Decision numbering continues from prior phases (Phase 1: D-01..D-16, Phase 2: D-17..D-27).

### Galaxy Visual Form (galaxy portions of RND-02/RND-05/RND-07)
- **D-28: Galaxies render entirely in the sky shader — never as world-space meshes or billboards.** A galaxy is a structured procedural sky element at a world-fixed direction whose angular size grows as you approach (same `_sizes` mechanism as star points). `WorldRenderer` gains **no** galaxy code path; galaxy rendering lives wholly in `SkyboxRenderer` + `skybox.gdshader`. (Supersedes an initial billboard-mesh idea, reconsidered by the user.)
- **D-29: Per-galaxy procedural type.** Each galaxy carries a **type (spiral vs elliptical)** plus orientation/seed/color so galaxies look distinct; the shader branches on type. Generated fully in-shader — **no texture assets** (honors v1 "no art" scope). Needs galaxy type/orientation params on the body model + per-galaxy uniform arrays in `skybox.gdshader`.
- **D-30: Strict 1:1, pixel-floored galaxy sizing.** The galaxy's apparent angular size follows its true angular radius (`StarRendering.AngularRadius`), floored at one screen pixel — same rule as stars/skybox points (honors D-15/D-25). Brightness and color derive from the shared `Luminosity`/`BaseColor` + `StarRendering` model, so the point↔disc handoff auto-matches (RND-07). A far galaxy starts as ~a pixel and grows on approach.
- **D-31: "Resolve into stars on approach" via procedural disc LOD only.** As a galaxy's disc grows, the galaxy's **own sky rendering** gains procedural grain/point detail (NOT real out-of-tier member-star bodies). This preserves the locked tier model — D-22 (no proximity-based promotion), D-25, and `TierClassifier` ("Beyond" = not rendered) stay fully intact. The galaxy's **real** member stars appear only at SOI entry (Universe→Galaxy); by then the disc is dense enough to mask the discrete swap. (Rejected the alternative of amending D-22 to proximity-promote real member stars.)

### Universe Layout & Scope (WORLD / TRV-02)
- **D-32: 3 galaxies, all real and flyable (honors D-25).** Home (spiral, full system) + a **full-mirror destination** + a **third lighter elliptical bare-star cluster**. Gives spiral+elliptical type variety on display and a 2-other-galaxy skybox while in Galaxy space.
- **D-33: Destination galaxy = full mirror system** — its own star + 1–2 planets + sibling stars, mirroring the home galaxy, so the entire Root→Universe→Galaxy→Star→Planet SOI chain and the full skybox re-tier are exercised at the far end (strongest proof of success criteria 1 & 2).
- **D-34: True 1:1 intergalactic separation.** Galaxies placed at realistic intergalactic distances (~Andromeda scale, ~2.4e22 m; within `UniVec3`/`Long3` precision). Galaxies are **Universe-space siblings under Root** (intergalactic space = Universe space) — extends the existing index-1 Universe-space galaxy-body pattern. Galaxy SOI ≈ galaxy physical radius (you "enter" at the disc edge). **Note:** the current `_galaxy` SOI placeholder (`5e3`) must be replaced with a real galaxy-scale value.

### Intergalactic Flight Feel (TRV-02 / FLT-03)
- **D-35: Design to a target crossing time.** Tune the single locked distance→speed curve (D-06/07/08) so a full-throttle galaxy-to-galaxy crossing lands at a deliberate, reasonable duration (criterion 4). Stays one continuous model — validated/extended to Universe scale, not a new mode. Exact target + curve shape = planner tuning; the intent is locked.
- **D-36: Natural ease-out FTL approach — trust the curve.** Rely on the distance-based envelope: as nearest-body distance shrinks (galaxy edge → star → planet) max speed drops smoothly, decelerating you automatically. Galaxies count as "nearest body" for the curve. FTL overshoot / single-frame SOI-tunneling is **flagged as a research/test item**; add no special braking unless it proves real. HUD already covers FTL magnitudes (D-10 ladder → ly/s).

### Finding the Target Galaxy (HUD reuse)
- **D-37: Reuse the Phase-1 HUD as-is — no new navigation HUD.** Navigate by sight (per-galaxy-type discs with distinct colors grow on approach) + the D-12 current-space target cycle (name+distance) + the 01-04 off-screen edge marker, all active once in Universe space. Honors the Backlog 999.1 deferral and the 1:1 ethos. The ship keeps starting **inside the home system** so the full "inside-galaxy → intergalactic → second galaxy" journey (criterion 2) is exercised end-to-end. (Per D-12, the destination galaxy becomes a cyclable target only once the ship is in Universe space — accepted.)

### Body Type & Renderer Routing
- **D-38: Extend `UniObject.Type` with Star/Galaxy/Planet** (currently Orb/Asteroid/Ship/None). `IsStarBody` becomes `Type==Star`. `WorldRenderer` renders **Planet (lit) + Star (emissive)** meshes and **skips Galaxy**; `SkyboxRenderer` renders **Galaxy (procedural) + Star (as points)**. One explicit field solves both galaxy-tier star-emissive rendering AND excluding galaxies (Universe-space siblings) from the mesh set. Chosen over boolean flags or a `Luminosity>0` heuristic (which can't separate emissive stars from emissive galaxies).

### Render Model at Galaxy/Universe Scale (RND-06 extension)
- **D-39: Extend the one RND-06 unit-space render model across all tiers.** Tune `GalaxyRenderFactor` + star light range for Galaxy space (galaxy-tier star distances ~light-years map inside the 1e6 far plane at ~1e-8; radii are true-1:1 sub-pixel as expected). Universe space renders **no meshes** (galaxies are sky), so `UniverseRenderFactor` is essentially moot. No tier-specific render code unless galaxy-scale rendering visibly breaks in testing.

### Sky Shader Architecture
- **D-40: Separate galaxy uniform set + procedural render path** in `skybox.gdshader`, alongside the existing star smoothstep-point loop (both must run at once: in-system the sky shows sibling stars AND galaxies; in Galaxy space it shows only galaxies). Add `galaxy_dirs/_sizes/_colors/_types/_orientations/_count` + a dedicated procedural-galaxy loop. `SkyboxRenderer` partitions `NextTierSkybox` bodies by `Type` (Star → star arrays, Galaxy → galaxy arrays). `MAX_GALAXIES` small (~4).

### Member-Star Authoring Scope
- **D-41: Mirror + small cluster.** Home galaxy **reuses the existing 4 stars** (home `STAR` + Alpha Cen/Barnard/Sirius siblings) + PlanetA/B — in Galaxy space these become the home galaxy's star meshes (covers criterion 5, home side). Destination galaxy = full mirror (1 star + 1–2 planets + 2–3 sibling stars). Third galaxy = small elliptical cluster of ~3–5 stars, no planets. Enough to populate Galaxy-space meshes + a believable multi-galaxy sky without bloating the hand-authored scene.

### Claude's Discretion
- Exact galaxy SOI values (≈ physical radius), and replacing the `_galaxy` SOI placeholder `5e3`.
- Exact `GalaxyRenderFactor` value, star light range at galaxy scale, and any tier-specific tuning if research demands it.
- The target intergalactic crossing-time number and the distance→speed curve shape/easing at Universe scale (derive from how the existing curve already behaves so it stays one continuous model).
- Procedural galaxy shader specifics: spiral-arm/elliptical functions, disc-LOD detail thresholds, orientation encoding, dither/bloom integration.
- Exact sibling/destination/third-galaxy coordinates, colors, luminosities, galaxy types, and member-star counts within the D-41 guideline.
- Star→Galaxy visible point→mesh handoff wiring specifics (consume the Phase-2 `SkyboxRenderer.GetSkyDirection` cache + `WorldRenderer.GetRenderPosition`; honor D-21 instant swap / D-22 scale-crossing trigger).
- Whether FTL overshoot needs a safety guard (default: none; add only if tunneling proves real).

</decisions>

<canonical_refs>
## Canonical References

**Downstream agents MUST read these before planning or implementing.**

### Phase requirements & scope
- `.planning/ROADMAP.md` §"Phase 3: Cross-Galaxy Travel" — goal, 5 success criteria, requirements (TRV-02; galaxy-tier portions of RND-02/04/05/07).
- `.planning/REQUIREMENTS.md` §Rendering + §Travel — TRV-02 full text and the phase-spanning note splitting RND-02/04/05/07 into in-system (Phase 1/2) vs galaxy-tier (Phase 3) delivery; traceability table.
- `.planning/PROJECT.md` §Context (tiered skybox model, RND-06 unit-space render) + §Key Decisions — the multi-scale model and 8-bit/1:1 rationale.

### Prior-phase decisions this phase builds on (do not re-litigate)
- `.planning/phases/02-dynamic-skybox/02-CONTEXT.md` — D-17..D-27 (magnitude model, BaseColor points D-18, min-floor D-19, shared bloom D-20, **instant exact-match swap D-21**, **scale-crossing-only promotion D-22**, sibling-star test data D-23, in-system-build-vs-Phase-3-visible split D-24, real-bodies-only D-25, `Luminosity` attribute D-26, shared dither D-27).
- `.planning/phases/01-in-system-flight-mvp/01-CONTEXT.md` — D-06/07/08 (distance-based auto-scale speed), D-10 (adaptive units → ly/s), D-12 (current-space target cycle), D-13 hues, D-14 emissive stars, D-15 true 1:1 radii, D-16 star point light, RND-06 unit-space reframe.

### Engine / render code to extend (consume, do not replace)
- `Scripts/TestSetup.cs` — authored `Root→Galaxy→Star→PlanetA/B→Ship` + 3 Galaxy-space siblings; **extend here** with 2 more galaxies (Universe-space siblings under Root), the destination mirror system, the elliptical cluster, galaxy `Type`/orientation/seed, and a real galaxy SOI (replace `5e3` placeholder).
- `Scripts/UniObject.cs` — `Type` enum (extend: Star/Galaxy/Planet, D-38), `Space` enum + `ChildSpace`/`ParentSpace`/`Scale`, `Luminosity`/`BaseColor`/`RadiusMeters`; add galaxy type/orientation/seed fields here.
- `Scripts/TierClassifier.cs` — pure star/mesh/sky/Beyond classifier (D-22 model); galaxies (Universe space) classify as `NextTierSkybox` from Galaxy space. Stays intact under D-31.
- `Scripts/Render/SkyboxRenderer.cs` — per-frame sky uniform push; **partition by `Type`** (D-40), add galaxy arrays + procedural params; `GetSkyDirection` cache is the sky-side of the RND-07 handoff.
- `Scripts/Render/WorldRenderer.cs` — floating-origin mesh sync; `IsStarBody` (→ `Type==Star`, D-38), **skip Galaxy bodies**, per-space `*RenderFactor` (tune Galaxy, D-39), `GetRenderPosition` is the mesh-side of the RND-07 handoff.
- `Scripts/Render/StarRendering.cs` — shared `AngularRadius`/`ApparentBrightness`/`Exposure` rules; galaxies and galaxy-tier stars reuse these so point↔mesh/disc brightness auto-matches.
- `Shaders/skybox.gdshader` — `MAX_STARS=8` smoothstep-point sky; **add galaxy uniform set + procedural loop** (D-40); raise/add `MAX_GALAXIES`.
- `Scripts/Flight/FlightController.cs` — distance-based auto-scale speed (D-06/07/08); tune/validate the curve to Universe scale for the target crossing time (D-35/36).
- `Scripts/GameWorld.cs` — iterative SOI transitions (STAB-01); the Universe↔Galaxy and Galaxy↔Star crossings that trigger the visible handoff key off transitions here.
- `Scripts/Hud/Hud.cs` — reused as-is (D-37); adaptive units already reach ly/s (D-10).
- `Main.tscn` — `Camera3D` (Environment→Sky→ShaderMaterial wiring for the skybox), `WorldRenderer`/`SkyboxRenderer` mounts.

### Codebase maps
- `.planning/codebase/ARCHITECTURE.md`, `STRUCTURE.md`, `CONVENTIONS.md` — namespace/layer conventions (`Render` namespace).
- `.planning/codebase/CONCERNS.md` — SOI transition robustness (relevant to FTL overshoot/tunneling, D-36).

</canonical_refs>

<code_context>
## Existing Code Insights

### Reusable Assets
- `SkyboxRenderer.SyncSkyPoints()` + `_skyDirs` cache + `MaxStars` arrays — the per-frame sky pass to extend with a galaxy partition and galaxy uniform arrays (D-40).
- `StarRendering.AngularRadius`/`ApparentBrightness`/`Exposure` — the single shared brightness/size model reused for galaxy discs and galaxy-tier stars (RND-07 auto-match).
- `TierClassifier.Classify` — already returns `NextTierSkybox` for ancestor-space bodies and `CurrentTierMesh` for same-space; galaxy-tier stars and galaxies flow through it unchanged (D-31 keeps it intact).
- `WorldRenderer.SyncBodies()` renders parent + its children of any space — galaxy-tier stars (children of the galaxy, in Galaxy space) already render through this loop once `IsStarBody`/Type and `GalaxyRenderFactor` are addressed.
- `WorldRenderer.GetRenderPosition` + `SkyboxRenderer.GetSkyDirection` — the two halves of the Phase-2 RND-07/D-21 handoff baseline; Phase 3 wires the visible swap from them.
- `TestSetup.SetupScene()` — the authored-data entry point to extend with galaxies + destination system + cluster.

### Established Patterns
- Read-only renderers consuming `GameWorld` state (never mutate `UniVec3`/`TranslatePos`); per-frame node reposition, lazy `GetOrCreate`, `(uint)i < (uint)Count` bounds checks.
- `UniMath.RelativePosition` LCA-relative `UniVec3` walk for cross-space directions/distances (exact integer Units cancellation; one `ToDouble3()` on the small delta) — **mandatory** for intergalactic distances; never naive cross-scale `UniVec3` operator subtraction (precision collapse per CLAUDE.md UniMath rules).
- Integer-index object model; `Space` enum drives tier classification and the context label.

### Integration Points
- **Body type** → extend `UniObject.Type`; `TestSetup` tags each body; `WorldRenderer` (Planet+Star, skip Galaxy) and `SkyboxRenderer` (Galaxy+Star) route off it.
- **Galaxy sky render** → `SkyboxRenderer` pushes per-galaxy dir/size/type/orientation/color to new `skybox.gdshader` uniforms; disc grows by true angular size with procedural LOD detail.
- **Visible Star↔Galaxy handoff** → on the Universe↔Galaxy / Star↔Galaxy crossing (`GameWorld` transition), align a body's `GetSkyDirection` and `GetRenderPosition` for an instant exact-match swap (D-21/D-22).
- **Intergalactic flight** → `FlightController` distance-based speed validated/tuned at Universe scale for the target crossing time; ease-out on approach.

</code_context>

<specifics>
## Specific Ideas

- **Reference feel:** Elite/Frontier vast traversable cosmos — the procedural galaxies growing from points into spiral/elliptical discs on approach is the headline visual of the phase.
- **Honest 1:1 held firm:** true intergalactic distances (D-34) + strict 1:1 pixel-floored galaxy sizing (D-30), consistent with the user's prior pure-1:1 choices (D-15, D-25). The galaxy disc growing on approach is what makes a true-1:1 distant galaxy navigable without breaking scale.
- **One-model discipline:** speed (one distance curve, D-35), brightness/size (one `StarRendering` model), render scale (one RND-06 unit-space model, D-39) — Phase 3 extends each rather than adding scale-specific modes.

</specifics>

<deferred>
## Deferred Ideas

- **Whole-hierarchy target selector + world-pinned target outline + tracking label** — Backlog 999.1 (overrides D-12). Phase 3 reuses the Phase-1 HUD only (D-37).
- **Proximity-promoting real member stars on galaxy approach** (amending D-22) — considered and rejected in favor of procedural disc LOD (D-31), which keeps the tier model honest.
- **FTL overshoot safety guard** (per-frame step clamp / SOI sub-stepping) — deferred unless tunneling proves real in testing (D-36).
- **Galaxy-tier distinct render treatment** (separate far plane/LOD/light model) — deferred unless the extended RND-06 model visibly breaks at galaxy scale (D-39).
- **Richer galaxies** (6–10 stars each, denser skies) — cut for v1 scope in favor of mirror + small cluster (D-41).
- **Texture-based / billboard galaxies** — considered then rejected by the user in favor of fully procedural sky-shader galaxies (D-28/D-29).

None beyond the above — discussion stayed within phase scope.

</deferred>

---

*Phase: 3-Cross-Galaxy Travel*
*Context gathered: 2026-06-16*
