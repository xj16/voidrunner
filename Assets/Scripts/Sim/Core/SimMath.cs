using System;

namespace VoidRunner.Core
{
    /// <summary>
    /// A minimal 2D vector used by the deterministic simulation.
    ///
    /// The sim deliberately does NOT use UnityEngine.Vector2 so the entire game logic compiles and
    /// runs in plain .NET (for unit tests and headless replay verification). The Unity view layer
    /// converts these to Vector2 at the boundary.
    ///
    /// All arithmetic uses <c>float</c> to match what the Unity presentation layer would use, and
    /// the simulation advances on a FIXED timestep so results are reproducible.
    /// </summary>
    [Serializable]
    public struct Vec2 : IEquatable<Vec2>
    {
        public float X;
        public float Y;

        public Vec2(float x, float y) { X = x; Y = y; }

        public static readonly Vec2 Zero = new Vec2(0f, 0f);

        public static Vec2 operator +(Vec2 a, Vec2 b) => new Vec2(a.X + b.X, a.Y + b.Y);
        public static Vec2 operator -(Vec2 a, Vec2 b) => new Vec2(a.X - b.X, a.Y - b.Y);
        public static Vec2 operator *(Vec2 a, float s) => new Vec2(a.X * s, a.Y * s);
        public static Vec2 operator *(float s, Vec2 a) => new Vec2(a.X * s, a.Y * s);
        public static Vec2 operator -(Vec2 a) => new Vec2(-a.X, -a.Y);

        public float SqrMagnitude => (X * X) + (Y * Y);

        // Uses the deterministic IEEE float sqrt (NOT (float)Math.Sqrt((double)x), which double-
        // rounds and can differ across platforms) so entity magnitudes are bit-identical everywhere.
        public float Magnitude => DetMath.Sqrt(SqrMagnitude);

        public Vec2 Normalized
        {
            get
            {
                float m = Magnitude;
                if (m < 1e-6f) return Zero;
                float inv = 1f / m;
                return new Vec2(X * inv, Y * inv);
            }
        }

        public static float Distance(Vec2 a, Vec2 b) => (a - b).Magnitude;
        public static float SqrDistance(Vec2 a, Vec2 b) => (a - b).SqrMagnitude;
        public static float Dot(Vec2 a, Vec2 b) => (a.X * b.X) + (a.Y * b.Y);

        /// <summary>Clamps a vector's magnitude to <paramref name="max"/>.</summary>
        public Vec2 ClampMagnitude(float max)
        {
            float sq = SqrMagnitude;
            if (sq <= max * max) return this;
            return Normalized * max;
        }

        /// <summary>Returns this vector rotated by the given angle in degrees (CCW). Deterministic.</summary>
        public Vec2 Rotate(float degrees)
        {
            float rad = degrees * DetMath.Deg2Rad;
            float c = DetMath.Cos(rad);
            float s = DetMath.Sin(rad);
            return new Vec2((X * c) - (Y * s), (X * s) + (Y * c));
        }

        public bool Equals(Vec2 other) => X.Equals(other.X) && Y.Equals(other.Y);
        public override bool Equals(object obj) => obj is Vec2 v && Equals(v);
        public override int GetHashCode() => (X.GetHashCode() * 397) ^ Y.GetHashCode();
        public override string ToString() => $"({X:0.###}, {Y:0.###})";
    }

    public static class SimMathUtil
    {
        // These route through DetMath so the simulation never touches the platform libm. Both the
        // constants and the trig below are deterministic across OS/CPU/runtime.
        public const float Deg2Rad = DetMath.Deg2Rad;
        public const float Rad2Deg = DetMath.Rad2Deg;

        public static float Clamp(float v, float min, float max) => v < min ? min : (v > max ? max : v);
        public static float Clamp01(float v) => Clamp(v, 0f, 1f);

        /// <summary>Angle in degrees of a direction vector. Deterministic (no platform atan2).</summary>
        public static float Angle(Vec2 dir) => DetMath.DegreesOf(dir);

        /// <summary>Unit vector from a degree angle. Deterministic (no platform sin/cos).</summary>
        public static Vec2 FromAngle(float degrees) => DetMath.DirFromDegrees(degrees);
    }
}
