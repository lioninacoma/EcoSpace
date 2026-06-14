// TDD RED: Tests for Hud.FormatSpeed and Hud.FormatDistance (HUD-01/D-10).
// These assertions are checked at runtime during _Ready() via GD.Print.
// They exercise the adaptive-unit ladder per 01-04-PLAN.md <behavior>.
//
// Compile-time: this file references Hud.FormatSpeed / Hud.FormatDistance,
// which do NOT exist yet — so the build FAILS until Task 1 GREEN adds them.

using Godot;

namespace Hud
{
    /// <summary>
    /// Compile-time-failing RED tests for FormatSpeed / FormatDistance.
    /// Once GREEN implementation is added to Hud.cs these compile and run.
    /// </summary>
    public static class HudFormatTests
    {
        /// <summary>Run assertions; prints PASS/FAIL for each case.</summary>
        public static void Run()
        {
            int pass = 0, fail = 0;

            // ── FormatSpeed ──────────────────────────────────────────────────
            // m/s band: < 1000
            Check("FormatSpeed(500) contains m/s",
                  Hud.FormatSpeed(500).Contains("m/s"), ref pass, ref fail);

            // km/s band: 5000 m/s = 5 km/s
            Check("FormatSpeed(5000) contains km/s",
                  Hud.FormatSpeed(5000).Contains("km/s"), ref pass, ref fail);

            // AU/s band: 3e11 m/s ≈ 2 AU/s
            Check("FormatSpeed(3e11) contains AU/s",
                  Hud.FormatSpeed(3e11).Contains("AU/s"), ref pass, ref fail);

            // ly/s band: 2e16 m/s ≈ 2.1 ly/s
            Check("FormatSpeed(2e16) contains ly/s",
                  Hud.FormatSpeed(2e16).Contains("ly/s"), ref pass, ref fail);

            // Zero speed: stays in m/s band
            Check("FormatSpeed(0) contains m/s",
                  Hud.FormatSpeed(0).Contains("m/s"), ref pass, ref fail);

            // ── FormatDistance ───────────────────────────────────────────────
            // m band: 200 m
            Check("FormatDistance(200) contains m",
                  Hud.FormatDistance(200).Contains(" m"), ref pass, ref fail);

            // km band: 5e6 m = 5000 km
            Check("FormatDistance(5e6) contains km",
                  Hud.FormatDistance(5e6).Contains("km"), ref pass, ref fail);

            // AU band: 3e11 m
            Check("FormatDistance(3e11) contains AU",
                  Hud.FormatDistance(3e11).Contains("AU"), ref pass, ref fail);

            // ly band: 2e16 m
            Check("FormatDistance(2e16) contains ly",
                  Hud.FormatDistance(2e16).Contains("ly"), ref pass, ref fail);

            GD.Print($"[HudFormatTests] {pass} passed, {fail} failed");
        }

        private static void Check(string name, bool condition, ref int pass, ref int fail)
        {
            if (condition) { pass++; GD.Print($"  PASS: {name}"); }
            else           { fail++; GD.PrintErr($"  FAIL: {name}"); }
        }
    }
}
