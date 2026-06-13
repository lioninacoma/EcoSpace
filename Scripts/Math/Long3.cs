using System;
using System.Runtime.CompilerServices;
using Godot;

/// <summary>
/// 64-bit integer 3D vector. Represents discrete "chunk" or "unit" coordinates
/// in the large-scale universe grid. Supports ~9.2 × 10^18 units per axis.
/// </summary>
[Serializable]
public struct Long3 : IEquatable<Long3>
{
    public long X, Y, Z;

    public static readonly Long3 Zero = new(0, 0, 0);
    public static readonly Long3 One = new(1, 1, 1);
    public static readonly Long3 UnitX = new(1, 0, 0);
    public static readonly Long3 UnitY = new(0, 1, 0);
    public static readonly Long3 UnitZ = new(0, 0, 1);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Long3(long x, long y, long z) { X = x; Y = y; Z = z; }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Long3(long n) { X = n; Y = n; Z = n; }

    // ----- Conversions ------------------------------------------------

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static explicit operator Long3(Double3 v) =>
        new((long)Mathf.Floor(v.X), (long)Mathf.Floor(v.Y), (long)Mathf.Floor(v.Z));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator Double3(Long3 v) =>
        new(v.X, v.Y, v.Z);

    // ----- Arithmetic -------------------------------------------------

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Long3 operator +(Long3 a, Long3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Long3 operator -(Long3 a, Long3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Long3 operator -(Long3 a) => new(-a.X, -a.Y, -a.Z);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Long3 operator *(Long3 a, long s) => new(a.X * s, a.Y * s, a.Z * s);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(Long3 a, Long3 b) => a.X == b.X && a.Y == b.Y && a.Z == b.Z;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(Long3 a, Long3 b) => !(a == b);

    // ----- Math helpers -----------------------------------------------

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Long3 Min(Long3 a, Long3 b) =>
        new(System.Math.Min(a.X, b.X), System.Math.Min(a.Y, b.Y), System.Math.Min(a.Z, b.Z));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Long3 Max(Long3 a, Long3 b) =>
        new(System.Math.Max(a.X, b.X), System.Math.Max(a.Y, b.Y), System.Math.Max(a.Z, b.Z));

    /// <summary>Squared distance (stays in integer domain, no sqrt loss).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long DistanceSq(Long3 a, Long3 b)
    {
        Long3 d = a - b;
        return d.MagnitudeSq();
    }

    // ----- Magnitude --------------------------------------------------

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public long MagnitudeSq()
    {
        return X * X + Y * Y + Z * Z;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public double Magnitude() => System.Math.Sqrt(MagnitudeSq());

    // ----- Split double → units + fraction ---------------------------

    /// <summary>
    /// Splits a double-precision value into integer unit + fractional remainder.
    /// Used when converting raw doubles into the chunked representation.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static (Long3 units, Double3 frac) FromDouble3(double x, double y, double z)
    {
        SplitDouble(x, out long ux, out double fx);
        SplitDouble(y, out long uy, out double fy);
        SplitDouble(z, out long uz, out double fz);
        return (new Long3(ux, uy, uz), new Double3(fx, fy, fz));
    }

    /// <summary>
    /// Splits a double-precision value into integer unit + fractional remainder.
    /// Used when converting raw doubles into the chunked representation.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Long3 Floor3(double x, double y, double z)
    {
        SplitDouble(x, out long ux, out _);
        SplitDouble(y, out long uy, out _);
        SplitDouble(z, out long uz, out _);
        return new Long3(ux, uy, uz);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void SplitDouble(double v, out long integer, out double frac)
    {
        double floor = Mathf.Floor(v);
        integer = (long)floor;
        frac = v - floor;
    }

    // ----- IEquatable / object ----------------------------------------

    public bool Equals(Long3 other) => this == other;
    public override bool Equals(object obj) => obj is Long3 o && Equals(o);
    public override int GetHashCode() => HashCode.Combine(X, Y, Z);
    public override string ToString() => $"Long3({X}, {Y}, {Z})";
}
