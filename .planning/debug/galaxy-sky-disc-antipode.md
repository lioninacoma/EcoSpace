---
status: paused
trigger: "Home-galaxy skybox suppression not working at runtime — the whole skybox is completely covered in light when flying in-system, instead of showing only the 2 other galaxies."
created: 2026-06-16
updated: 2026-06-16
resolution: fixed-and-verified
---

## Resolution (fix applied + user-verified, session paused)

Reported symptom RESOLVED and confirmed in-game by the user ("looks good now —
galaxies shown untilted, no giant ring").

Two commits:
- `6f5f728` fix(03-debug): author galaxy positions and SOI in metres, not Universe
  units (TestSetup.cs) — the primary root cause (galaxies were ~1e16× too close).
- `fef1d91` fix(03-debug): build galaxy disc basis from view dir + gate front
  hemisphere (skybox.gdshader) — removed the sky-spanning ring and the antipode
  ghost.

Verification: runtime dump confirmed DEST GALAXY len 2.40e22 m (+Z) and ELLIPTICAL
CLUSTER 1.80e22 m (45°) with size ~2e-4; visual confirmed clean discs, no ring,
no antipode. Diagnostic instrumentation removed before commit.

Two follow-ups spun off as tech-debt todos (NOT fixed in this session, per user):
- `.planning/todos/pending/galaxy-disc-tilt-foreshortening.md` — disc renders
  face-on; `disc_normal` tilt was dropped for robustness.
- `.planning/todos/pending/thrust-zero-at-galaxy-soi-exit.md` — thrust dies at the
  galaxy SOI boundary because the distance→speed envelope clamps on exit too.

Paused at user request. Resume with `/gsd-debug continue galaxy-sky-disc-antipode`
if any regression appears.

# Debug Session: galaxy-sky-disc-antipode

## Symptoms

- **Expected:** From inside the home system the sky shows only the 2 OTHER galaxies as procedural discs; the home galaxy is suppressed; star sky-points appear at correct sizes.
- **Actual (refined by user observation):**
  - A white→yellow radial gradient radiating from the home star, behind the home planet, in the center of the initial view (camera faces −Z toward the star at game start).
  - Two large, slightly darker yellow spheres at ~90° to the left and ~90° to the right of center (i.e. ~180° apart from each other). They are LARGER than the central star gradient.
- **Error messages:** none (visual only; Godot compiles shaders at runtime).
- **Timeline:** First visual play-test of Phase 03 galaxy rendering + the quick-task (260616-riw) home-galaxy suppression guard. Build is the current HEAD after plans 03-02/03-03.
- **Reproduction:** Launch the game; observe the sky from the starting in-system position.

## Current Focus

hypothesis: The galaxy disc shader renders each galaxy on BOTH its true direction and the exact antipode, because `galaxy_disc_coords` (skybox.gdshader) projects EYEDIR onto the plane perpendicular to the galaxy direction (`delta = eye − dot(eye,gdir)·gdir`), which is identical for EYEDIR and −EYEDIR, and there is no `dot(EYEDIR, galaxy_dir) > 0` front-hemisphere gate. So one galaxy paints two antipodal blobs (the observed 90° L + 90° R = 180°-apart pair). Disc apparent size may additionally be too large.
test: Instrument SkyboxRenderer.SyncSkyPoints to dump, once/sec, each galaxy that passes the NextTierSkybox+ObjectType filter: name, suppressed(FindLca==Index), dir3, len(metres), theta, size. Confirms (a) is the home galaxy suppressed, (b) how many galaxies actually render, (c) their sizes/directions.
expecting: If antipode bug: only 1–2 galaxies pass the filter (home suppressed) yet the user sees them doubled → ghost is shader-side. If suppression broken: home galaxy appears in the dump with FindLca != its Index.
next_action: Add throttled diagnostic dump to SkyboxRenderer; ask user to run and paste console output.

## Evidence

- timestamp 2026-06-16: WorldRenderer does NOT mesh the home star in Planet space (Scripts/Render/WorldRenderer.cs:278-283 — star is the grandparent, position derived for star_dir only). So the central star gradient is a SKYBOX sky-point, not a mesh. Rules out the 03-03 mesh-routing change as the source of the central gradient.
- timestamp 2026-06-16: AngularRadius = radiusMeters/distMeters (small-angle, StarRendering.cs:65). Galaxy RadiusMeters=5e20; the 2 distant galaxies at ~2.4e22 m give theta≈0.021 rad (~1.2°), ~4× the home star's apparent radius — so "galaxy discs larger than the star" is partly expected at current tuning.
- timestamp 2026-06-16: galaxy_disc_coords (skybox.gdshader:96-110) builds uv from the EYEDIR component perpendicular to galaxy_dir; |uv| = sin(angle), which is 0 at both the galaxy direction (φ=0) and its antipode (φ=180°) → bright disc center appears in BOTH places. No front-hemisphere gate present.

## Root Cause

CONFIRMED via runtime dump (frame dump, ship space=Planet):
- `HOME GALAXY ... suppressed=True` → suppression guard works.
- `DEST GALAXY` and `ELLIPTICAL CLUSTER`: `dir=(0,0,-1.00) len=1.50E+011m theta=3.34E+009 size=0.1224` — i.e. both far galaxies collapse onto the home-star direction at ~1 AU and render as giant clamped discs.

Mechanism: `GameWorld.AddGameObject(parentIndex, Double3 localPos, double soiMeters)` interprets `localPos` as **metres** (GameWorld.cs:273 → `new UniVec3(localPos, scale)` → UniVec3.cs:54 stores the Double3 as the metres Offset, Normalize() splits into integer Units). All planets/stars correctly pass metres (PlanetA_Z=1.496e11 m). The three galaxies were authored in **Universe units** instead:
- `Galaxy2_UniZ = 2.4e6` (intended 2.4e6 units × 1e16 = 2.4e22 m) → placed at 2.4e6 m.
- `Galaxy3_UniX = Galaxy3_UniZ = 1.27e6` → placed at ~1.27e6 m (intended ~1.8e22 m at 45°).
- `GalaxySOI = 5e4` → 50 km SOI (intended 5e4 units = 5e20 m).

Result: galaxies sit ~2.4e6 m from the origin (on top of the home system), so from the ship they appear at 1.5e11 m in the home-star direction with theta=RadiusMeters/len = 5e20/1.5e11 = 3.3e9 → clamped to MaxDiscAngle → size 0.1224 → giant discs flooding the sky.

Fix: author galaxy positions and SOI in metres (× the 1e16 Universe scale), consistent with every other body in TestSetup.

Secondary (latent, not the reported symptom): `galaxy_disc_coords` (skybox.gdshader) has no front-hemisphere gate, so a galaxy disc also renders a faint ghost at its antipode. Tracked for a follow-up; the primary fix resolves the flood.

## Eliminated

- hypothesis: The home star is being rendered as an over-bright emissive MESH due to the 03-03 ObjectType star-routing change. — ELIMINATED: in Planet space the star is the grandparent and is not in WorldRenderer's render set (WorldRenderer.cs:278-283); it is a skybox point.
