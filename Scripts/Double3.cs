using Godot;
using System;
using System.Runtime.CompilerServices;

namespace Universe
{
    [Serializable]
    public struct Double3 : IEquatable<Double3>
    {
        public double X, Y, Z;

        public static readonly Double3 Zero  = new(0, 0, 0);
        public static readonly Double3 One   = new(1, 1, 1);
        public static readonly Double3 UnitX = new(1, 0, 0);
        public static readonly Double3 UnitY = new(0, 1, 0);
        public static readonly Double3 UnitZ = new(0, 0, 1);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Double3(double x, double y, double z) { X = x; Y = y; Z = z; }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Double3(double n) { X = n; Y = n; Z = n; }

        // ----- Conversions ------------------------------------------------

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static explicit operator Double3(Vector3 v) =>
            new(v.X, v.Y, v.Z);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator Vector3(Double3 v) =>
            new((float)v.X, (float)v.Y, (float)v.Z);

        // ----- Arithmetic -------------------------------------------------

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double3 operator +(Double3 a, Double3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double3 operator -(Double3 a, Double3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double3 operator -(Double3 a) => new(-a.X, -a.Y, -a.Z);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double3 operator *(Double3 a, double s) => new(a.X * s, a.Y * s, a.Z * s);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator ==(Double3 a, Double3 b) => a.X == b.X && a.Y == b.Y && a.Z == b.Z;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator !=(Double3 a, Double3 b) => !(a == b);

        // ----- Math helpers -----------------------------------------------

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double3 Min(Double3 a, Double3 b) =>
            new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Min(a.Z, b.Z));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double3 Max(Double3 a, Double3 b) =>
            new(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y), Math.Max(a.Z, b.Z));

        /// <summary>Squared distance (stays in integer domain, no sqrt loss).</summary>
        public static double DistanceSq(Double3 a, Double3 b)
        {
            double dx = a.X - b.X, dy = a.Y - b.Y, dz = a.Z - b.Z;
            return dx * dx + dy * dy + dz * dz;
        }

		// ----- Magnitude --------------------------------------------------

		public double MagnitudeSq()
		{
			return X * X + Y * Y + Z * Z;
		}

		public double Magnitude() => Math.Sqrt(MagnitudeSq());

        // ----- IEquatable / object ----------------------------------------

        public bool Equals(Double3 other) => this == other;
        public override bool Equals(object obj) => obj is Double3 o && Equals(o);
        public override int GetHashCode() => HashCode.Combine(X, Y, Z);
        public override string ToString() => $"Double3({X}, {Y}, {Z})";

        internal Double3 Lerp(Double3 offset, double t)
        {
            return new Double3(
				Mathf.Lerp(X, offset.X, t), 
				Mathf.Lerp(Y, offset.Y, t), 
				Mathf.Lerp(Z, offset.Z, t));
        }
    }
}