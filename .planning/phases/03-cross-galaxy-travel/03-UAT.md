---
status: testing
phase: 03-cross-galaxy-travel
source: [03-VERIFICATION.md]
started: 2026-06-16T00:00:00Z
updated: 2026-06-16T00:00:00Z
---

## Current Test

number: 1
name: From the home system, confirm exactly 2 galaxy discs visible (home galaxy suppressed), world-fixed
expected: |
  Two colored procedural discs (one warm orange-gold spiral, one warm golden elliptical) visible in
  different sky directions. No HOME GALAXY disc. Discs are world-fixed — rotating the ship does not move them.
awaiting: user response

## Tests

### 1. Exactly 2 galaxy discs visible from home system, world-fixed
expected: Two colored procedural discs (warm orange-gold spiral + warm golden elliptical) in two distinct sky directions. No HOME GALAXY disc. Discs do not drift on ship rotation.
result: [pending]

### 2. Home-galaxy stars become emissive meshes in Galaxy space
expected: Flying outward past the home star SOI into Galaxy space, the home galaxy's stars (STAR, ALPHA CEN, BARNARD, SIRIUS) render as emissive sphere meshes — not sky points. No home-galaxy star sky-points remain in Galaxy space.
result: [pending]

### 3. Galaxy-space sky shows only the 2 other galaxies; no galaxy is a sphere mesh
expected: Zero star-point dots in the Galaxy-space sky. The two other galaxies appear as sky discs. No opaque sphere geometry for any galaxy object (D-28).
result: [pending]

### 4. Star↔Galaxy SOI-boundary handoff is pop-free (RND-07)
expected: Crossing a home-galaxy star's SOI boundary, the sky point and the appearing mesh occupy the same screen pixel. No jump in position, no brightness flash, no color shift.
result: [pending]

### 5. Full intergalactic transit: FTL speed, crash-free SOI entry, auto-deceleration
expected: Full-throttle from the home system into Universe space toward DEST GALAXY — (a) speed climbs to FTL magnitudes (HUD shows ly/s-class), (b) ship enters DEST GALAXY SOI with no crash, pop, or loading screen, (c) speed eases down automatically on approach.
result: [pending]

### 6. Symmetrical behavior at DEST GALAXY (far-end integration)
expected: After entering DEST GALAXY SOI, its member stars (DEST STAR, DEST SIB 1, DEST SIB 2) appear as emissive meshes, its sky shows only the 2 non-ancestor galaxies, and the Star↔Galaxy handoff is equally pop-free at the far end. Full Root→Universe→Galaxy→Star→Planet chain traversable at the destination.
result: [pending]

### 7. Record observed home→destination crossing time at _maxSpeed=2e20 (D-35 tuning)
expected: Crossing takes approximately 2 minutes at full throttle (or the developer decides to re-tune). D-35 tuning decision recorded.
result: [pending]

## Summary

total: 7
passed: 0
issues: 0
pending: 7
skipped: 0
blocked: 0

## Gaps
