---
status: testing
phase: 02-dynamic-skybox
source: [02-VERIFICATION.md]
started: 2026-06-15
updated: 2026-06-15
---

## Current Test

number: 2
name: Skybox does not drift with camera rotation (SC2)
expected: |
  Rotate the ship (pitch, yaw, roll) while looking at a sibling star point. The point stays
  pinned to its world direction — it must NOT rotate or drift with the camera.
awaiting: user response

## Tests

### 1. Sibling stars visible in-system
expected: Fly in Star/Planet space; ALPHA CEN, BARNARD, SIRIUS appear as discrete lit points (not black sky, not a screen-filling color), differing in brightness (Sirius brightest, Barnard dimmest) and showing their hues (blue-white / warm-white / red).
result: pass
note: Confirmed during the 02-02 play-test approval (2026-06-15) — user approved the corrected hues + brightness ranking.

### 2. No camera drift (SC2)
expected: Rotate the ship (pitch/yaw/roll) while watching a star point; it stays pinned to world space and does not move with the camera (EYEDIR world-space projection).
result: pending

### 3. Dither pass-through
expected: Toggle PostProcessRenderer dithering on/off; the sky points quantize/de-quantize along with the rest of the scene (no second dither pass added).
result: pending

## Summary

total: 3
passed: 1
issues: 0
pending: 2
skipped: 0
blocked: 0

## Gaps
