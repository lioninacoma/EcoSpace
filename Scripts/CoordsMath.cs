using Godot;
using System;

namespace Universe
{
    /// <summary>
    /// Convenience helpers for working with universe-scale coordinates.
    ///
    /// Unit conventions (all in meters):
    ///   1 AU  = 1.495978707e11 m
    ///   1 LY  = 9.4607304725808e15 m
    ///   1 km  = 1000 m
    ///   1 Mm  = 1e6 m
    /// </summary>
    public static class CoordsMath
    {
        // ----- Physical constants (meters) --------------------------------

        public const double AU_IN_METERS = 1.495978707e11;
        public const double LY_IN_METERS = 9.4607304725808e15;
        public const double KM_IN_METERS = 1_000.0;
        public const double MM_IN_METERS = 1_000_000.0;

        // ----- Recommended scale presets ----------------------------------

        /// <summary>1 unit = 1 meter. Use for ships/stations (precision ±0.03 mm at 1e6 m).</summary>
        public const float SCALE_METER         = 1f;

        /// <summary>1 unit = 1 km. Use for planetary systems (precision ±30 m at 1e9 m).</summary>
        public const float SCALE_KILOMETER      = 1_000f;

        /// <summary>1 unit = 1 AU. Use for solar-system-level positioning.</summary>
        public const float SCALE_AU             = (float)AU_IN_METERS;

        /// <summary>1 unit = 1 light-year. Use for galactic-scale positioning.</summary>
        public const float SCALE_LIGHT_YEAR     = (float)LY_IN_METERS;

        // ----- Factory helpers --------------------------------------------

        /// <summary>Position from astronomical units.</summary>
        public static UniVec3 FromAU(double x, double y, double z, float scale = SCALE_KILOMETER)
        {
            return UniVec3.FromDouble(x * AU_IN_METERS, y * AU_IN_METERS, z * AU_IN_METERS, scale);
        }

        /// <summary>Position from light-years.</summary>
        public static UniVec3 FromLY(double x, double y, double z, float scale = SCALE_LIGHT_YEAR)
        {
            return UniVec3.FromDouble(x * LY_IN_METERS, y * LY_IN_METERS, z * LY_IN_METERS, scale);
        }

        /// <summary>Position from kilometers.</summary>
        public static UniVec3 FromKm(double x, double y, double z, float scale = SCALE_KILOMETER)
        {
            return UniVec3.FromDouble(x * KM_IN_METERS, y * KM_IN_METERS, z * KM_IN_METERS, scale);
        }

        /// <summary>Position from raw meters.</summary>
        public static UniVec3 FromMeters(double x, double y, double z, float scale = SCALE_METER)
        {
            return UniVec3.FromDouble(x, y, z, scale);
        }

        // ----- Readback helpers -------------------------------------------

        /// <summary>Distance between two universe positions, in AU.</summary>
        public static double DistanceAU(UniVec3 a, UniVec3 b) =>
            UniVec3.Distance(a, b) / AU_IN_METERS;

        /// <summary>Distance in light-years.</summary>
        public static double DistanceLY(UniVec3 a, UniVec3 b) =>
            UniVec3.Distance(a, b) / LY_IN_METERS;

        // ----- Chunk helpers (for spatial hashing / LOD streaming) --------

        /// <summary>
        /// Returns the chunk (cell) coordinates for a universe position.
        /// Two positions in the same chunk can be rendered together without precision loss.
        /// ChunkSize should equal the Scale used when creating the UniVec3.
        /// </summary>
        public static Long3 GetChunk(UniVec3 pos) => pos.Units;

        /// <summary>
        /// Manhattan distance in chunks (integer, no float error).
        /// Fast first-pass LOD / streaming check.
        /// </summary>
        public static long ChunkManhattan(UniVec3 a, UniVec3 b)
        {
            Long3 d = a.Units - b.Units;
            return Math.Abs(d.X) + Math.Abs(d.Y) + Math.Abs(d.Z);
        }

        // ----- Godot integration helpers ----------------------------------

        /// <summary>
        /// Converts a Godot Transform3D (float) into a universe-scale transform.
        /// Useful when importing positions from physics or animation.
        /// </summary>
        public static UniVec3 FromTransform(Transform3D t, float scale = SCALE_METER)
        {
            return new UniVec3((Double3)t.Origin, scale);
        }

        /// <summary>
        /// Builds a Godot Transform3D for rendering, given a universe body's world pos
        /// and an observer position.  Basis comes from the body's own rotation.
        /// </summary>
        public static Transform3D ToRenderTransform(UniVec3 worldPos, UniVec3 observer, Basis basis)
        {
            return new Transform3D(basis, worldPos.ToLocalDouble(observer));
        }
    }
}