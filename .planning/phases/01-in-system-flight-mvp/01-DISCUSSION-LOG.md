# Phase 1: In-System Flight MVP - Discussion Log

> **Audit trail only.** Do not use as input to planning, research, or execution agents.
> Decisions are captured in CONTEXT.md — this log preserves the alternatives considered.

**Date:** 2026-06-12
**Phase:** 1-In-System Flight MVP
**Areas discussed:** Flight control feel, Speed auto-scaling, HUD layout & style, Body look & scale

---

## Flight control feel

### Control model
| Option | Description | Selected |
|--------|-------------|----------|
| Virtual-joystick cursor | Mouse moves a cursor within a screen deadzone; ship continuously turns toward it. Classic WC/Elite. | ✓ |
| Direct captured-mouse | Raw mouse delta maps 1:1 to pitch/yaw rate (FPS-style). | |

### Stabilization
| Option | Description | Selected |
|--------|-------------|----------|
| Hold attitude | Rotation stops cleanly, no drift, no auto-level. | ✓ |
| Auto-level roll only | Stops rotation and slowly levels roll to system plane. | |
| Full recenter | Eases ship back toward neutral/forward on release. | |

### Throttle
| Option | Description | Selected |
|--------|-------------|----------|
| Persistent throttle (W/S sets level) | W/S set a throttle that persists hands-off; X = full stop. | ✓ |
| Hold-to-thrust | Hold W to accelerate, release to coast/brake. | |

### Roll binding
| Option | Description | Selected |
|--------|-------------|----------|
| Q/E keys | Roll left/right; mouse stays purely for aiming the nose. | ✓ |
| A/D keys | Roll on A/D. | |
| Mouse + modifier | Horizontal mouse rolls while a modifier is held. | |

### Cursor / crosshair
| Option | Description | Selected |
|--------|-------------|----------|
| Visible cursor + fixed crosshair | Fixed center crosshair (nose-forward) + separate moving steering reticle. | ✓ |
| Cursor IS the crosshair | Single moving reticle, no fixed center marker. | |

**User's choice:** Virtual-joystick cursor · hold attitude · persistent W/S throttle · Q/E roll · visible cursor + fixed crosshair
**Notes:** Targeting a Wing Commander feel throughout.

---

## Speed auto-scaling

### Scale basis
| Option | Description | Selected |
|--------|-------------|----------|
| Distance to nearest body | Max speed grows with distance to closest planet/star surface. | ✓ |
| Current parent SOI scale | Fixed multiple of current space's SOI radius; steps at transitions. | |
| Hybrid | Base from space scale, modulated by body proximity. | |

### Curve
| Option | Description | Selected |
|--------|-------------|----------|
| Continuous & smooth | Eased frame-to-frame, no snap at SOI boundaries. | ✓ |
| Stepped per space level | Envelope changes only at SOI transitions. | |

### Throttle mapping
| Option | Description | Selected |
|--------|-------------|----------|
| Throttle = fraction of context max | speed = throttle% × current context-max. | ✓ |
| Throttle = absolute, capped by context | Throttle sets real velocity, clamped to context max. | |

**User's choice:** Distance-to-nearest-body · continuous/smooth · throttle = fraction of context max
**Notes:** Numeric tuning (curve shape, min/max, easing) left to planner.

---

## HUD layout & style

### Aesthetic
| Option | Description | Selected |
|--------|-------------|----------|
| Phosphor green | Monochrome green vector-terminal CRT look. | ✓ |
| Amber CRT | Monochrome amber/orange terminal. | |
| Keep magenta / multi-color | Existing magenta accent or a few colors. | |

### Speed units
| Option | Description | Selected |
|--------|-------------|----------|
| Auto-pick m/s → km/s → AU/s → ly/s | Largest readable real unit, auto-switching. | ✓ |
| Real units + 'c' multiples | Metric ladder + multiples of c above light speed. | |
| Abstract speed bar | 0–100% bar, no real units. | |

### Context label
| Option | Description | Selected |
|--------|-------------|----------|
| Space level + nearest body | e.g. 'STAR SPACE · nearest: PLANET A'. | ✓ |
| Nearest body only | Just closest body / 'DEEP SPACE'. | |
| Space level only | Just the SOI tier. | |

### Target readout
| Option | Description | Selected |
|--------|-------------|----------|
| Bodies in current space, name + distance | Cycle bodies in current parent space; name + distance. | ✓ |
| All known bodies | Cycle every body in the universe. | |
| Name + distance + relative bearing | Current-space cycling plus a direction indicator. | |

**User's choice:** Phosphor green · auto unit ladder m/s→km/s→AU/s→ly/s · space level + nearest body · current-space target name+distance
**Notes:** Replaces the current magenta FPSLabel accent.

---

## Body look & scale

### Palette
| Option | Description | Selected |
|--------|-------------|----------|
| Per-body colors + global dither quantize | Distinct authored base colors; dither post-process quantizes the frame. | ✓ |
| Fixed shared palette, shape-only distinction | One small palette, distinguish by size/position. | |

### Star look
| Option | Description | Selected |
|--------|-------------|----------|
| Emissive sphere + glow/bloom | Unshaded bright sphere with bloom halo. | ✓ |
| Flat bright sphere, no bloom | Emissive sphere, no glow post-process. | |

### Body scale
| Option | Description | Selected |
|--------|-------------|----------|
| True 1:1 radii | Real radii; specks at distance, grow on approach. | ✓ |
| Exaggerated radii | Inflated radii for visibility. | |
| 1:1 with a visibility floor | Real radii + minimum on-screen size. | |

### Lighting
| Option | Description | Selected |
|--------|-------------|----------|
| Point light at the star | OmniLight at star position; correct terminator. | ✓ |
| Keep directional light | Reuse existing DirectionalLight3D as sun direction. | |

**User's choice:** Per-body colors + dither quantize · emissive star + glow/bloom · true 1:1 radii · point light at star
**Notes:** Findability tension flagged in CONTEXT.md (1:1 radii + no skybox yet + no target bearing) — resolve during planning within HUD scope.

---

## Claude's Discretion

- Iterative/null-safe rewrite of `TrySpaceTransition` (STAB-01) and related `GameWorld` hardening.
- Floating-origin sync mechanism and per-frame `UniVec3`→`Node3D` transform.
- All numeric tuning: speed curve, deadzone, turn rates, throttle steps, bloom amount, light range, palette specifics, mesh subdivision.
- HUD implementation tech (GDScript vs C#), following the existing `FPS.gd` / CanvasLayer pattern.

## Deferred Ideas

- Dynamic skybox (RND-05) — Phase 2.
- Cross-galaxy travel + galaxy/universe-scale data (TRV-02) — Phase 3.
- CRT scanline overlay (crt.gdshader, PRES-01), boost/afterburner (FLT-04), audio (PRES-02), 1-bit toggle (PRES-03) — v2.
- HUD 'c'-multiple FTL flavor; all-universe target cycling; relative bearing indicator — considered, deferred.
- Deeper GameWorld tech-debt (null-slot compaction, DistanceSq, SOI input validation, SIMD layout asserts) — only what STAB-01 needs now.
