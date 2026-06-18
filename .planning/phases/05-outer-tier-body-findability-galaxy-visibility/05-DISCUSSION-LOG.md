# Phase 5: Outer-tier body findability & galaxy visibility - Discussion Log

> **Audit trail only.** Do not use as input to planning, research, or execution agents.
> Decisions are captured in CONTEXT.md — this log preserves the alternatives considered.

**Date:** 2026-06-18
**Phase:** 05-outer-tier-body-findability-galaxy-visibility
**Areas discussed:** Galaxy visibility in Universe space (D-28 fork), Star-mesh findability floor, Floor consistency & handoff continuity, Galaxy disc tilt (polish)

> Session started by discarding a prior DISCUSS-CHECKPOINT (user chose "Start fresh").

---

## Galaxy visibility in Universe space (D-28 fork)

| Option | Description | Selected |
|--------|-------------|----------|
| Skybox-only (reaffirm D-28) | Galaxies never meshes; render current-tier galaxies on skybox | ✓ |
| Hybrid: sky disc → mesh on approach | Promote disc to mesh on close approach (RND-07 pattern) | |
| Mesh: reverse D-28 | Galaxies become emissive meshes | |

**User's choice:** Skybox-only (reaffirm D-28) → D-48.

| Option | Description | Selected |
|--------|-------------|----------|
| Circle from sky direction | Project circle from GetSkyDirection | |
| Edge-marker + HUD distance only | No on-screen circle for sky-only galaxy | |

**User's choice:** Rejected both — **corrected** the framing: the marker's position AND size must be derived from the body's `UniObject` configured parameters (true position + RadiusMeters), not any mesh. → D-50 (refines D-46).

| Option | Description | Selected |
|--------|-------------|----------|
| Disc growth + HUD distance | Angular-size disc growth (D-30) + adaptive HUD distance (D-10) | ✓ |
| Disc growth only | Navigate by sight, no distance number | |

**User's choice:** Disc growth + HUD distance → D-51.

| Option | Description | Selected |
|--------|-------------|----------|
| Galaxies are always sky entities | Type==Galaxy routed to sky unconditionally | ✓ |
| Generalize: top tier shows current tier | Special-case the outermost tier | |

**User's choice:** Galaxies are always sky entities → D-49.

| Option | Description | Selected |
|--------|-------------|----------|
| Disc appears at SOI crossing | Default D-22 behavior on exit | |
| Flag continuity as a test item | Verify in-game; tune only if it pops | ✓ |

**User's choice:** Flag continuity as a test item → D-52.

**Notes:** The UniObject-driven-marker correction is the pivotal insight of this area — it generalizes the target marker to work for every body type regardless of mesh existence.

---

## Star-mesh findability floor

| Option | Description | Selected |
|--------|-------------|----------|
| Min-size clamp + emission floor | Clamp existing mesh radius/emission | |
| Billboard/point promotion | Billboard sized to pixel floor, swap to mesh close | |
| Tune GalaxyRenderFactor only | Retune the placeholder factor | |

**User's choice:** Rejected all three — **redirected** to a richer model (see below), citing godot-starlight, Star Nest (ShaderToy XlfGRj), and the Defold ShaderToy tutorial as references: stars always rendered in addition to the skybox, proximity brightening, seamless mesh↔skybox integration, unified star/nebula/galaxy look.

| Option | Description | Selected |
|--------|-------------|----------|
| Yes — always-on point + additive mesh blend | Continuous blend, supersedes D-21/D-22 | ✓ |
| Yes, but keep swap as fallback | Retain a hard swap threshold | |
| No — simpler min-size clamp | Step back to the cheap clamp | |

**User's choice:** Always-on point + additive mesh blend → D-53.

| Option | Description | Selected |
|--------|-------------|----------|
| Star + galaxy now, nebula deferred | Unify star+galaxy look; defer nebula | ✓ |
| Full unified star+nebula+galaxy now | Build the whole volumetric shader + nebula data | |
| Minimal: shared PSF only | Just share PSF, galaxies keep current disc | |

**User's choice:** Star + galaxy now, nebula deferred → D-54.

| Option | Description | Selected |
|--------|-------------|----------|
| Glow halo + bloom, distance-driven | PSF wings + additive falloff feeding bloom | ✓ |
| Full volumetric scattering | Raymarched god-rays / in-scattering | |
| You decide (planner/research) | Lock intent, defer technique | |

**User's choice:** Glow halo + bloom, distance-driven → D-55.

| Option | Description | Selected |
|--------|-------------|----------|
| Unified star-point renderer (MultiMesh PSF) | One renderer for all stars; retire skybox star loop | ✓ |
| Extend existing split paths | Keep two paths, add points to current tier | |
| You decide (research/planner) | Lock requirement, defer architecture | |

**User's choice:** Unified star-point renderer (MultiMesh PSF) → D-56.

| Option | Description | Selected |
|--------|-------------|----------|
| Borrow technique, custom-fit | Adopt techniques, implement against EcoSpace model | ✓ |
| Use the addon directly | Import godot-starlight as an addon | |
| You decide (research/planner) | Let research evaluate | |

**User's choice:** Borrow technique, custom-fit → D-57.

**Notes:** This area expanded the phase from a clamp-fix into a unified always-on PSF rendering model. Nebula and full scattering explicitly scoped out.

---

## Floor consistency & handoff continuity

| Option | Description | Selected |
|--------|-------------|----------|
| Floor only below threshold, true 1:1 above | max(floor, true) sizing; floor releases on approach | |
| Always-on floor blended | Small floor at all distances | |
| You decide (research/planner) | Lock intent, defer curve | |

**User's choice:** Rejected the question — **directed** that the new rendering technique be implemented first, and the floor-release/consistency problem addressed afterward only if it occurs. → D-58 (implement-first; in-game verification items).

**Notes:** Area collapsed from decisions into a watch-list because the always-additive model (D-53) removes the hard swap that would pop.

---

## Galaxy disc tilt (polish)

| Option | Description | Selected |
|--------|-------------|----------|
| Include, built into the new shader | Add tilt as part of the unified galaxy-shader rework, safe basis | ✓ |
| Defer to its own polish phase | Keep galaxies face-on for Phase 5 | |
| You decide (research/planner) | Let planning decide based on rework size | |

**User's choice:** Include, built into the new shader → D-59.

**Notes:** Efficient since the galaxy shader is being reworked for the D-54 look regardless. The Phase-3 ring-collapse landmine and safe-basis approach are mandatory.

---

## Claude's Discretion

- Exact PSF kernel / min_size / luminosity_cap / glow-falloff values (in-game tuning knobs).
- Far-plane-clip technique specifics for never culling distant star points.
- Galaxy-disc volumetric-look shader specifics within the "shared family, no true nebula" guideline.
- Exact foreshorten clamp-floor value (D-59).
- How MultiMesh per-instance data is fed from GameWorld/UniObject each frame under floating origin.
- Whether new tunables live on WorldRenderer, a new star-point renderer node, or the shader.

## Deferred Ideas

- True nebula rendering + authored nebula bodies (full Star-Nest volumetric shader) — future phase.
- Full volumetric light scattering / god-rays near stars — future phase.
- Whole-hierarchy nav-HUD + tracking label — backlog 999.1.
- Shader-rendered ellipsoid target outline — backlog 999.2.
- Floor-release curve / fine consistency tuning — only if it manifests in play-test (D-58).
