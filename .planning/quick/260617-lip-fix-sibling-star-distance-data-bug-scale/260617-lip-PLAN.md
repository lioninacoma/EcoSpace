---
phase: quick-260617-lip
plan: "01"
type: execute
files_modified:
  - Scripts/TestSetup.cs
  - .planning/todos/pending/sibling-star-distances-1e4-too-close.md
autonomous: false
must_haves:
  truths:
    - "Within-galaxy star positions are in true metres at ly distances (ALPHA CEN 4.2 ly = 3.97e16 m, BARNARD 5.96 ly = 5.63e16 m, SIRIUS 8.6 ly = (-6.0e16, 5.6e16))."
    - "DEST SIB 1/2 and CLUSTER STAR 1/2/3 offsets are scaled to e16 (ly-scale within their galaxies)."
    - "No star SOI overlaps another: each member star is far outside the home star's SOI (StarSOI = 1.5e15 m)."
    - "Planet (AU-scale), galaxy (e22), and StarSOI values are unchanged."
  artifacts:
    - path: "Scripts/TestSetup.cs"
      provides: "Sibling/cluster/dest star positions in true metres"
---

<objective>
Fix the sibling-star-distance data bug. Within-galaxy star positions in TestSetup.cs were
authored in "Galaxy units" (1 unit = 1e4 m) but AddGameObject takes METRES, so the stars
land ~1e4× too close (ALPHA CEN at 26.5 AU instead of 4.2 ly), deep inside the home star's
SOI (StarSOI = 1.5e15 m = 10,000 AU) → star SOIs overlap. Same units-vs-metres bug as the
galaxy fix (6f5f728), which missed the stars. Scale the offending values ×1e4 to true metres.
</objective>

<tasks>
<task type="auto">
  <name>Task 1: Scale within-galaxy star positions ×1e4 to true metres</name>
  <files>Scripts/TestSetup.cs</files>
  <action>
- Home-galaxy siblings (constants): Sibling1_GalX 3.97e12→3.97e16, Sibling2_GalX 5.63e12→5.63e16,
  Sibling3_GalX -6.0e12→-6.0e16, Sibling3_GalZ 5.6e12→5.6e16. Update the comments so they say
  "metres" (the value IS metres now), not "Galaxy units".
- DEST galaxy siblings (inline Double3): DEST SIB 1 (-3.2e12)→(-3.2e16); DEST SIB 2 (4.5e12, 0, 3.1e12)→(4.5e16, 0, 3.1e16).
- Elliptical cluster members (inline Double3): CLUSTER STAR 1 (1.5e12)→(1.5e16); CLUSTER STAR 2 (-2.1e12, 0, 1.2e12)→(-2.1e16, 0, 1.2e16); CLUSTER STAR 3 (0.8e12, 0, -2.5e12)→(0.8e16, 0, -2.5e16).
- Do NOT touch: planet positions (PlanetA_Z/PlanetB_Z, AU-scale), galaxy positions (Galaxy2_Z, Galaxy3_X/Z, e22), StarSOI, GalaxySOI, ship orbit, radii, luminosities, colors.
  </action>
  <verify>
    <automated>dotnet build EcoSpace.csproj -clp:ErrorsOnly</automated>
    <automated>dotnet test EcoSpace.Tests</automated>
  </verify>
  <done>Build 0/0; tests 30/30; all within-galaxy star offsets are e16; primary/galaxy/SOI values unchanged.</done>
</task>

<task type="checkpoint:human-verify" gate="blocking">
  <what-built>Within-galaxy stars moved to true ly distances. Ripples into Phase 2/3 skybox sky-points (now ~1e8× dimmer in flux).</what-built>
  <how-to-verify>
1. Launch Main.tscn. From the home system, confirm the 3 sibling sky-points (ALPHA CEN warm, BARNARD dim red, SIRIUS blue-white) are still VISIBLE at sensible relative brightness (D-19 floor holds) and in distinct sky directions.
2. Fly outward: confirm leaving the home star's SOI behaves cleanly — no SOI overlap/transition thrash now that the stars are far apart.
3. Confirm galaxy discs + other Phase 3 visuals are not regressed.
  </how-to-verify>
  <resume-signal>Type "confirmed" if sky-points are still visible/correct and SOI behavior is clean; else describe what's off.</resume-signal>
</task>

<task type="auto">
  <name>Task 2: Resolve the tech-debt todo</name>
  <files>.planning/todos/pending/sibling-star-distances-1e4-too-close.md</files>
  <action>Only after the checkpoint is approved: set status: resolved + resolved date and add a one-line Resolution note (positions scaled ×1e4 to true metres; SOIs no longer overlap; skybox re-verified in-game).</action>
  <done>Todo marked resolved.</done>
</task>
</tasks>

<output>
Create .planning/quick/260617-lip-fix-sibling-star-distance-data-bug-scale/260617-lip-SUMMARY.md when done.
</output>
