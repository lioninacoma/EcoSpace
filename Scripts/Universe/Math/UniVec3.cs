using Godot;
using System;
using System.Runtime.CompilerServices;

namespace Universe
{
	namespace Math
	{
		/// <summary>
		/// Universal 3D position with arbitrary precision and infinite range.
		///
		/// A position is stored as:
		///   WorldPosition = Units * Scale + Offset
		///
		/// where:
		///   Units  – 64-bit integer grid coordinates  (covers ~9.2e18 cells per axis)
		///   Offset – double sub-cell offset in [0, Scale)
		///   Scale  – size of one cell in world-space meters (e.g. 1e6 = 1 megameter per unit)
		///
		/// This gives you:
		///   • double-precision (~7 significant digits) within each cell
		///   • integer-precision at the cell level (exact, no drift)
		///   • effectively unlimited universe size
		///
		/// Typical usage for a solar-system scale game:
		///   Scale = 1_000_000  (1 unit = 1 km → cells are 1 km cubes)
		///   Units reach ~9.2e12 km, far beyond the observable universe.
		///
		/// Design notes:
		///   • Normalization keeps Offset in [0, Scale) automatically after every operation.
		///   • Operations between two UniVec3 with different Scale values auto-convert via Convert().
		///   • Godot uses double internally; ToLocalDouble() gives a camera-relative double Double3
		///     for rendering (doubleing-origin pattern).
		/// </summary>
		[Serializable]
		public struct UniVec3 : IEquatable<UniVec3>
		{
			public Long3 Units;
			public Double3 Offset;   // sub-cell offset, always in [0, Scale)
			public double Scale;    // meters per unit

			// Threshold below which two doubles are considered identical
			public const double EPSILON = 1e-11;

			// ----- Constructors -----------------------------------------------

			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			public UniVec3(Long3 units, Double3 offset, double scale)
			{
				Units = units;
				Offset = offset;
				Scale = scale;
				Normalize();
			}

			/// <summary>Create from a small local offset only (Units = 0).</summary>
			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			public UniVec3(Double3 offset, double scale)
				: this(Long3.Zero, offset, scale) { }

			/// <summary>Create from raw double-precision world coordinates.</summary>
			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			public static UniVec3 FromDouble(double x, double y, double z, double scale)
			{
				// Convert doubles to unit + fractional offset
				var (units, frac) = Long3.FromDouble3(x / scale, y / scale, z / scale);
				return new UniVec3(units, frac * scale, scale);
			}

			// ----- Core math --------------------------------------------------

			/// <summary>
			/// Renormalizes so Offset stays in [0, Scale).
			/// Call automatically happens in every constructor; rarely needed manually.
			/// </summary>
			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			public void Normalize()
			{
				double s = Scale;
				if (Offset.X >= s || Offset.X < 0f ||
					Offset.Y >= s || Offset.Y < 0f ||
					Offset.Z >= s || Offset.Z < 0f)
				{
					NormalizeSlow();
				}
			}

			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			private void NormalizeSlow()
			{
				double s = Scale;
				// Floor-divide each component; keep remainder positive
				long ix = FloorDiv(Offset.X, s, out double rx);
				long iy = FloorDiv(Offset.Y, s, out double ry);
				long iz = FloorDiv(Offset.Z, s, out double rz);

				Units += new Long3(ix, iy, iz);
				Offset = new Double3(rx, ry, rz);
			}

			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			private static long FloorDiv(double v, double scale, out double remainder)
			{
				double d = v / scale;
				double floor = Mathf.Floor(d);
				remainder = (d - floor) * scale;
				return (long)floor;
			}

			// ----- Conversion -------------------------------------------------

			/// <summary>
			/// Returns a copy rescaled to a different cell size.
			///
			/// Precision note: instead of going through scalar doubles (x = Units.X * Scale + Offset.X),
			/// we pass the current world-space Double3 directly as the Offset of the new instance and
			/// let the constructor's Normalize() call do the integer split. This matches the Unity
			/// implementation and avoids an extra divide-then-multiply rounding trip through FromDouble().
			/// </summary>
			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			public UniVec3 Convert(double newScale)
			{
				if (Mathf.Abs(Scale - newScale) < EPSILON) return this;
				return new UniVec3(Long3.Zero, ToDouble3(), newScale);
			}

			/// <summary>
			/// Returns the world position as a raw Double3 (no scale decomposition).
			/// Equivalent to Unity's ToDouble3().
			/// </summary>
			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			public Double3 ToDouble3()
			{
				return new Double3(
					Units.X * Scale + Offset.X,
					Units.Y * Scale + Offset.Y,
					Units.Z * Scale + Offset.Z
				);
			}

			[MethodImpl(MethodImplOptions.AggressiveInlining)]
            public Double3 ToDouble3Units()
            {
                return (Double3)Units + Offset / Scale;
            }

			/// <summary>
			/// Returns the world position as raw double-precision coordinates.
			/// Precise for distances that fit in double (~1e15 m at meter scale).
			/// </summary>
			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			public (double x, double y, double z) ToDouble()
			{
				var v = ToDouble3();
				return (v.X, v.Y, v.Z);
			}

			/// <summary>
			/// doubleing-origin render vector: position relative to an observer.
			/// Use this to feed Godot's Node3D.Position.
			/// Result is in meters; precise within ~1e6 m of origin before double jitter.
			/// </summary>
			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			public Double3 ToLocalDouble(in UniVec3 observer)
			{
				UniVec3 delta = this - observer;
				return delta.ToDouble3();
			}

			/// <summary>
			/// Floating-origin render vector in UNIT space: position relative to an observer,
			/// expressed in the OBSERVER's cell units (Units + Offset/Scale form). Using the
			/// observer's scale as the single unit basis keeps all bodies in a frame consistent
			/// even when they live in spaces with different Scale values. Universe/SOI math stays
			/// 1:1 (meters); only rendering consumes unit space.
			/// </summary>
			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			public Double3 ToLocalDoubleUnits(in UniVec3 observer)
			{
				UniVec3 delta = this - observer;
				return delta.ToDouble3() * (1.0 / observer.Scale);   // observer-scale unit basis
			}

			// ----- Magnitude --------------------------------------------------

			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			public double MagnitudeSq()
			{
				var (x, y, z) = ToDouble();
				return x * x + y * y + z * z;
			}

			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			public double Magnitude() => System.Math.Sqrt(MagnitudeSq());

			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			public UniVec3 Normalized()
			{
				double mag = Magnitude();
				if (mag < EPSILON) return new UniVec3(Double3.Zero, Scale);
				var (x, y, z) = ToDouble();
				return FromDouble(x / mag, y / mag, z / mag, Scale);
			}

			// ----- Static operations ------------------------------------------

			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			public static UniVec3 Lerp(UniVec3 a, UniVec3 b, double t)
			{
				if (Mathf.Abs(a.Scale - b.Scale) > EPSILON)
					b = b.Convert(a.Scale);

				// Lerp units as doubles to avoid integer snap
				double ux = Mathf.Lerp(a.Units.X, b.Units.X, t);
				double uy = Mathf.Lerp(a.Units.Y, b.Units.Y, t);
				double uz = Mathf.Lerp(a.Units.Z, b.Units.Z, t);

				var (uInt, frac) = Long3.FromDouble3(ux, uy, uz);

				Double3 offsetLerp = a.Offset.Lerp(b.Offset, t);
				Double3 offset = offsetLerp + frac * a.Scale;

				return new UniVec3(uInt, offset, a.Scale);
			}

			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			public static double Distance(UniVec3 a, UniVec3 b) => (a - b).Magnitude();

			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			public static UniVec3 Min(UniVec3 a, UniVec3 b)
			{
				EnsureSameScale(ref b, a.Scale);
				var (ax, ay, az) = a.ToDouble();
				var (bx, by, bz) = b.ToDouble();
				return FromDouble(Mathf.Min(ax, bx), Mathf.Min(ay, by), Mathf.Min(az, bz), a.Scale);
			}

			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			public static UniVec3 Max(UniVec3 a, UniVec3 b)
			{
				EnsureSameScale(ref b, a.Scale);
				var (ax, ay, az) = a.ToDouble();
				var (bx, by, bz) = b.ToDouble();
				return FromDouble(Mathf.Max(ax, bx), Mathf.Max(ay, by), Mathf.Max(az, bz), a.Scale);
			}

			// ----- Operators --------------------------------------------------

			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			public static UniVec3 operator +(UniVec3 a, UniVec3 b)
			{
				EnsureSameScale(ref b, a.Scale);
				return new UniVec3(a.Units + b.Units, a.Offset + b.Offset, a.Scale);
			}

			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			public static UniVec3 operator -(UniVec3 a, UniVec3 b)
			{
				EnsureSameScale(ref b, a.Scale);
				return new UniVec3(a.Units - b.Units, a.Offset - b.Offset, a.Scale);
			}

			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			public static UniVec3 operator -(UniVec3 a) => new(-a.Units, -a.Offset, a.Scale);

			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			public static UniVec3 operator +(UniVec3 a, Double3 b) =>
				new(a.Units, a.Offset + b, a.Scale);

			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			public static UniVec3 operator -(UniVec3 a, Double3 b) =>
				new(a.Units, a.Offset - b, a.Scale);

			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			public static UniVec3 operator *(UniVec3 a, double s)
			{
				// Scale both parts; double intermediates avoid precision loss
				double wx = (a.Units.X * a.Scale + a.Offset.X) * s;
				double wy = (a.Units.Y * a.Scale + a.Offset.Y) * s;
				double wz = (a.Units.Z * a.Scale + a.Offset.Z) * s;
				return FromDouble(wx, wy, wz, a.Scale);
			}

			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			public static UniVec3 operator /(UniVec3 a, double s)
			{
				if (Mathf.Abs(s) < EPSILON) s = Mathf.Sign(s) * EPSILON;
				return a * (1f / s);
			}

			// ----- Helpers ----------------------------------------------------

			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			private static void EnsureSameScale(ref UniVec3 b, double scale)
			{
				if (Mathf.Abs(b.Scale - scale) > EPSILON)
					b = b.Convert(scale);
			}

			// ----- IEquatable / object ----------------------------------------

			public bool Equals(UniVec3 other) =>
				Units == other.Units &&
				Offset == other.Offset &&
				Mathf.Abs(Scale - other.Scale) < EPSILON;

			public override bool Equals(object obj) => obj is UniVec3 o && Equals(o);
			public override int GetHashCode() => HashCode.Combine(Units, Offset, Scale);

			public static bool operator ==(UniVec3 a, UniVec3 b) => a.Equals(b);
			public static bool operator !=(UniVec3 a, UniVec3 b) => !a.Equals(b);

			public override string ToString() =>
				$"UniVec3(units={Units}, offset={Offset}, scale={Scale})";
		}
	}
}