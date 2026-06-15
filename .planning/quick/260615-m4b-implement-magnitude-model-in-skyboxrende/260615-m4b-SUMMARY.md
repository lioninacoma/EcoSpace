---
quick_id: 260615-m4b
slug: implement-magnitude-model-in-skyboxrende
description: Implement magnitude model in SkyboxRenderer
date: 2026-06-15
status: complete
commits:
  - 9f60ada
  - ee69779
---

# Quick Task 260615-m4b: Summary

## What Was Done

Replaced the flat `alpha = 1.0` placeholder in `SkyboxRenderer.SyncSkyPoints()` with a physically-based inverse-square luminosity model. Also raised the minimum disc size from `1e-7` to `MinStarSize = 3e-6f` so sub-pixel point sources are visible as bright screen points.

## Changes

**Scripts/Render/SkyboxRenderer.cs** (`9f60ada`)
- Added `[Export] public double LuminosityScale { get; set; } = 2e35`
- Added `[Export] public float MinBrightFloor { get; set; } = 0.1f`
- Added `[Export] public float MinStarSize { get; set; } = 3e-6f`
- Replaced `alpha = 1.0f` placeholder with: `rawAlpha = L/D² × LuminosityScale`, floored to `MinBrightFloor`
- Replaced both `1e-7f` disc-size literals with `MinStarSize`

**Scripts/TestSetup.cs** (`ee69779`)
- `GameObjects[_planetA].Luminosity = 0.0` — routes to MinBrightFloor, not full solar brightness
- `GameObjects[_planetB].Luminosity = 0.0` — same

## Expected Runtime Brightness

| Body | alpha |
|------|-------|
| Sirius (L=25.4, 8.6 ly) | ~795 — strong HDR bloom |
| Alpha Cen (L=1.519, 4.2 ly) | ~200 — moderate glow |
| Barnard (L=0.0035, 5.96 ly) | 0.07 → floored to 0.1 |
| Home star (L=1.0, ~1.5e11m) | ~9e12 — blinding, tone mapper handles |
| Planets | 0 → floored to 0.1 |
