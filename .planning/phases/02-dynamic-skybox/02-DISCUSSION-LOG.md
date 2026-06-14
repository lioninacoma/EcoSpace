# Phase 2: Dynamic Skybox - Discussion Log

> **Audit trail only.** Do not use as input to planning, research, or execution agents.
> Decisions are captured in CONTEXT.md — this log preserves the alternatives considered.

**Date:** 2026-06-14
**Phase:** 02-dynamic-skybox
**Areas discussed:** Point appearance & brightness, Mesh↔skybox handoff (RND-07), Demonstration scope & test data, Backdrop richness, Luminosity data model, Sibling-star authoring, Dither integration

---

## Point Appearance & Brightness

| Option | Description | Selected |
|--------|-------------|----------|
| Real magnitude | Brightness/size from real luminosity × true distance; needs a visibility floor | ✓ |
| Uniform dots | Every point same brightness/size; readable but loses depth | |
| Category-tiered | Fixed look per category (stars vs galaxies) with mild distance influence | |

**User's choice:** Real magnitude → **D-17**

| Option | Description | Selected |
|--------|-------------|----------|
| Yes, authored hue | Point uses body BaseColor; required for RND-07 color match | ✓ |
| Whitish/dithered | Near-white, rely on dither palette; breaks color continuity | |

**User's choice:** Yes, authored hue → **D-18**

| Option | Description | Selected |
|--------|-------------|----------|
| Min-brightness floor | Clamp so every real body shows ≥1 dithered pixel; magnitude still ranks above floor | ✓ |
| Pure falloff, no floor | Faint bodies fade fully to black; most honest, sky can look empty | |
| You decide | Leave floor/curve to planner; lock only that a floor exists | |

**User's choice:** Min-brightness floor → **D-19**

| Option | Description | Selected |
|--------|-------------|----------|
| Yes, shared bloom | Points bloom through the same WorldEnvironment glow as the star mesh | ✓ |
| No, dither only | Points get only dither quantize; risks brightness pop at handoff | |

**User's choice:** Yes, shared bloom → **D-20**

---

## Mesh↔Skybox Handoff (RND-07)

| Option | Description | Selected |
|--------|-------------|----------|
| Instant exact-match swap | Point & mesh pre-matched in position/color/brightness; no blend; cleanest under dither | ✓ |
| Short crossfade | Cross-dissolve over a few frames/distance band; alpha fights the dither pipeline | |
| You decide | Lock only that the handoff is imperceptible | |

**User's choice:** Instant exact-match swap → **D-21**

| Option | Description | Selected |
|--------|-------------|----------|
| Scale-boundary only | Strictly tier/SOI crossing; sibling star stays a point until you enter its tier | ✓ |
| Proximity/screen-size | Point becomes mesh once >~1px; conflicts with locked tier model | |

**User's choice:** Scale-boundary only → **D-22**

---

## Demonstration Scope & Test Data

| Option | Description | Selected |
|--------|-------------|----------|
| Sibling stars only | Add 2-3 other systems under the Galaxy; defer point→mesh promotion demo to Phase 3 | ✓ |
| Sibling stars + Star→Galaxy exit | Also exercise a real re-tier/promotion now; pulls Phase 3 work forward | |
| Machinery only | No new bodies; RND-05 criterion 1 hard to demonstrate | |

**User's choice:** Sibling stars only → **D-23**

| Option | Description | Selected |
|--------|-------------|----------|
| Formally defer visible re-tier to P3 | Build skybox + magnitude points + no-drift + dither + re-tier LOGIC + handoff machinery; visible re-tier/promotion → Phase 3; reword Phase 2 criteria | ✓ |
| Add a minimal Star→Galaxy exit | Visible re-tier now but stars pop (no galaxy-tier meshing); rough edge | |
| You decide | Pick the cleaner split | |

**User's choice:** Formally defer visible re-tier to P3 → **D-24** (+ ROADMAP criteria-reword follow-up)

---

## Backdrop Richness

| Option | Description | Selected |
|--------|-------------|----------|
| Real bodies only | Exactly the real next-tier objects; sparse now, fills in P3+; honors honest-1:1 | ✓ |
| Faint decorative starfield | Static dim background for atmosphere; non-diegetic, blurs "only next tier out" | |
| You decide | Lock the principle, leave to planning | |

**User's choice:** Real bodies only → **D-25**

---

## Luminosity Data Model

| Option | Description | Selected |
|--------|-------------|----------|
| Explicit attribute | Add a Luminosity/abs-magnitude field to UniObject, authored per body | ✓ |
| Derive from radius+color | Compute from RadiusMeters + color-temp; no new field but couples size to brightness | |
| Authored brightness value | Per-body "skybox brightness" number; pragmatic, less physical | |

**User's choice:** Explicit attribute → **D-26**

---

## Sibling-Star Authoring

| Option | Description | Selected |
|--------|-------------|----------|
| Realistic distances + varied | True interstellar distances, varied colors/luminosities; exercises magnitude + floor | ✓ |
| Artistically placed | Positions/brightness chosen for a legible sky; doesn't stress true-distance attenuation | |
| You decide | Lock "varied, real-ish"; leave coords/colors to planner | |

**User's choice:** Realistic distances + varied (exact coords/colors = planner discretion) → **D-23**

---

## Dither Integration

| Option | Description | Selected |
|--------|-------------|----------|
| Yes, same dither pass | Points quantized by dithering.gdshader like meshes; one palette; essential for RND-07 | ✓ |
| Separate treatment | Sky quantized independently; risks palette mismatch at handoff | |

**User's choice:** Yes, same dither pass → **D-27**

---

## Claude's Discretion

- Sky **technique** (Godot `Sky` resource + sky shader vs inverted far-sphere mesh vs other) and the half-resolution sky pass — flagged "moderately novel"; left to phase research.
- Exact min-brightness floor value and the luminosity→apparent-brightness curve.
- Exact sibling-star coordinates, colors, and luminosity values.
- `Luminosity` field name/units/default; re-tier logic unit-test design.

## Deferred Ideas

- Visible Star↔Galaxy re-tier + point→mesh promotion demo — Phase 3.
- Other-galaxy point data; in-galaxy stars-as-meshes — Phase 3.
- Decorative/ambient background starfield — rejected for v1 (D-25).
- Derive-luminosity-from-radius / authored-brightness-only — rejected (D-26).
- Crossfade/blended handoff — rejected (D-21).
