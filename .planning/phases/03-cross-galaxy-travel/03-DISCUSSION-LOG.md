# Phase 3: Cross-Galaxy Travel - Discussion Log

> **Audit trail only.** Do not use as input to planning, research, or execution agents.
> Decisions are captured in CONTEXT.md — this log preserves the alternatives considered.

**Date:** 2026-06-16
**Phase:** 3-Cross-Galaxy Travel
**Areas discussed:** Galaxy visual form, Universe layout & scope, Intergalactic flight feel, Finding the target galaxy, Star identification model, Galaxy/Universe render model, Skybox shader capacity, Member-star authoring scope

---

## Galaxy visual form

### Render approach
| Option | Description | Selected |
|--------|-------------|----------|
| Billboard glow sprite | Camera-facing emissive sprite, soft radial falloff | (initially chosen) |
| Emissive sphere mesh | Reuse star mesh path | |
| Particle/point disc | Flattened cloud of light-points | |
| **Procedural in sky shader (user override)** | Drawn procedurally in skybox.gdshader, no world-space mesh | ✓ |

**User's choice:** User initially picked the billboard, then reconsidered: "we do not want galaxies to be rendered as billboards, they should be procedurally generated in the skybox shader." → D-28.
**Notes:** Galaxy is a sky element at a world-fixed direction; angular size grows on approach (same `_sizes` mechanism). WorldRenderer gains no galaxy path.

### Galaxy sizing
| Option | Description | Selected |
|--------|-------------|----------|
| Strict 1:1, pixel floor | True angular radius, floored at one pixel, like stars | ✓ |
| Galaxy min-disc size | Larger minimum on-screen radius for legibility | |
| You decide | Pick in playtest | |

**User's choice:** Strict 1:1, pixel floor → D-30.

### Procedural form
| Option | Description | Selected |
|--------|-------------|----------|
| Spiral disc | Procedural spiral arms + core | |
| Elliptical/noise blob | Soft radial core + noise | |
| Both, per-galaxy type | Type param (spiral vs elliptical) + orientation/seed | ✓ |

**User's choice:** Both, per-galaxy type → D-29.

### Galaxy → starfield entry transition
| Option | Description | Selected |
|--------|-------------|----------|
| Resolve into stars on approach | Member stars appear as you near; disc fades | ✓ (via LOD) |
| Instant reveal at SOI | Strict D-21 discrete swap | |
| You decide | Pick in playtest | |

**User's choice:** Resolve into stars on approach. Reconciled with D-22 tension via **procedural disc detail (LOD)** (not real out-of-tier bodies) over amending D-22 → D-31.
**Notes:** Surfaced a tension with locked D-22 (no proximity promotion). User chose to keep D-22/D-25/TierClassifier intact and achieve the feel with procedural disc LOD; real member stars appear only at SOI entry.

---

## Universe layout & scope

### Destination galaxy contents
| Option | Description | Selected |
|--------|-------------|----------|
| Full mirror system | Star + 1-2 planets + sibling stars | ✓ |
| Bare star cluster | A few stars, no planets | |
| Single star | One star | |

**User's choice:** Full mirror system → D-33.

### Intergalactic distance
| Option | Description | Selected |
|--------|-------------|----------|
| True 1:1 real distance | ~Andromeda scale | ✓ |
| Realistic-but-near | Satellite/dwarf-galaxy distance | |
| Compressed for playability | Shrink the gap | |

**User's choice:** True 1:1 real distance → D-34.

### Galaxy count
| Option | Description | Selected |
|--------|-------------|----------|
| 3 galaxies | Home + full-mirror destination + lighter elliptical cluster | ✓ |
| 2 galaxies | Home + destination only | |
| You decide | Pick during authoring | |

**User's choice:** 3 galaxies → D-32.

---

## Intergalactic flight feel

### Journey length
| Option | Description | Selected |
|--------|-------------|----------|
| Design to a target time | Tune curve so crossing lands at a deliberate duration | ✓ |
| Pure emergent extension | Extend formula, accept whatever time results | |
| You decide | Pick in playtest | |

**User's choice:** Design to a target time → D-35.

### FTL approach behavior
| Option | Description | Selected |
|--------|-------------|----------|
| Natural ease-out, trust the curve | Distance envelope decelerates; flag overshoot for research | ✓ |
| Add an approach safety guard | Clamp per-frame step / sub-step SOI | |
| You decide | Pick in research | |

**User's choice:** Natural ease-out, trust the curve → D-36. Overshoot/tunneling flagged for research, no special braking.

---

## Finding the target galaxy

| Option | Description | Selected |
|--------|-------------|----------|
| Reuse Phase-1 HUD as-is | Sight + D-12 current-space target cycle + edge marker | ✓ |
| Extend targeting to visible sky bodies | Target galaxy discs from Galaxy space (overrides D-12) | |
| Add a destination beacon | Persistent cross-space waypoint (999.1 territory) | |

**User's choice:** Reuse Phase-1 HUD as-is → D-37. Ship starts in home system for the full journey; galaxy targetable once in Universe space (per D-12).

---

## Star identification model

| Option | Description | Selected |
|--------|-------------|----------|
| Extend the Type enum | Add Star/Galaxy/Planet to UniObject.Type | ✓ |
| Boolean flags | IsStar + IsGalaxy bools | |
| Luminosity heuristic + galaxy flag | Luminosity>0 = star + IsGalaxy flag | |

**User's choice:** Extend the Type enum → D-38. IsStarBody → Type==Star; WorldRenderer renders Planet+Star, skips Galaxy; SkyboxRenderer draws Galaxy+Star.
**Notes:** Surfaced that galaxies (sky-only) must be excluded from WorldRenderer's mesh set, and Luminosity>0 alone can't separate emissive stars from emissive galaxies — hence a real type distinction.

---

## Galaxy/Universe render model

| Option | Description | Selected |
|--------|-------------|----------|
| Extend, tune factors | One RND-06 model across tiers; tune GalaxyRenderFactor | ✓ |
| Galaxy tier distinct treatment | Separate far plane/LOD/light model | |
| You decide | Validate in research | |

**User's choice:** Extend, tune factors → D-39. Universe space renders no meshes (galaxies are sky).

---

## Skybox shader capacity

| Option | Description | Selected |
|--------|-------------|----------|
| Separate galaxy uniform set + path | galaxy_* arrays + procedural loop alongside star loop | ✓ |
| One unified typed loop | Single loop branches on shape/type | |
| You decide | Pick in research | |

**User's choice:** Separate galaxy uniform set + path → D-40. SkyboxRenderer partitions by Type; MAX_GALAXIES ~4.
**Notes:** In-system the sky shows both sibling stars (points) and galaxies (discs); in Galaxy space only galaxies — both techniques must run at once.

---

## Member-star authoring scope

| Option | Description | Selected |
|--------|-------------|----------|
| Mirror + small cluster | Home reuses 4 stars; destination full mirror; third 3-5 star cluster | ✓ |
| Minimal | Destination 1 star + 1 planet; third 2-3 stars | |
| Richer | 6-10 stars per galaxy | |

**User's choice:** Mirror + small cluster → D-41.

---

## Claude's Discretion

- Exact galaxy SOI values (≈ physical radius); replace `_galaxy` SOI placeholder `5e3`.
- `GalaxyRenderFactor` value, star light range at galaxy scale, any tier-specific tuning if needed.
- Target intergalactic crossing-time number + distance→speed curve shape/easing at Universe scale.
- Procedural galaxy shader specifics (spiral/elliptical functions, disc-LOD thresholds, orientation encoding, dither/bloom).
- Exact galaxy/star coordinates, colors, luminosities, types, member-star counts within D-41.
- Star→Galaxy visible handoff wiring (consume GetSkyDirection + GetRenderPosition; honor D-21/D-22).
- Whether FTL overshoot needs a safety guard (default none).

## Deferred Ideas

- Whole-hierarchy target selector + world-pinned outline + tracking label — Backlog 999.1 (overrides D-12).
- Proximity-promoting real member stars on galaxy approach (amend D-22) — rejected for procedural disc LOD (D-31).
- FTL overshoot safety guard — deferred unless tunneling proves real (D-36).
- Galaxy-tier distinct render treatment — deferred unless extended model breaks (D-39).
- Richer galaxies (6-10 stars) — cut for v1 scope (D-41).
- Texture-based / billboard galaxies — rejected for fully procedural sky-shader galaxies (D-28/D-29).
