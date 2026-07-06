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

        public float Magnitude => (float)Math.Sqrt(SqrMagnitude);

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

        /// <summary>Returns this vector rotated by the given angle in degrees (CCW).</summary>
        public Vec2 Rotate(float degrees)
        {
            float rad = degrees * (float)(Math.PI / 180.0);
            float c = (float)Math.Cos(rad);
            float s = (float)Math.Sin(rad);
            return new Vec2((X * c) - (Y * s), (X * s) + (Y * c));
        }

        public bool Equals(Vec2 other) => X.Equals(other.X) && Y.Equals(other.Y);
        public override bool Equals(object obj) => obj is Vec2 v && Equals(v);
        public override int GetHashCode() => (X.GetHashCode() * 397) ^ Y.GetHashCode();
        public override string ToString() => $"({X:0.###}, {Y:0.###})";
    }

    public static class SimMathUtil
    {
        public const float Deg2Rad = (float)(Math.PI / 180.0);
        public const float Rad2Deg = (float)(180.0 / Math.PI);

        public static float Clamp(float v, float min, float max) => v < min ? min : (v > max ? max : v);
        public static float Clamp01(float v) => Clamp(v, 0f, 1f);

        /// <summary>Angle in degrees of a direction vector.</summary>
        public static float Angle(Vec2 dir) => (float)Math.Atan2(dir.Y, dir.X) * Rad2Deg;

        /// <summary>Unit vector from a degree angle.</summary>
        public static Vec2 FromAngle(float degrees)
        {
            float rad = degrees * Deg2Rad;
            return new Vec2((float)Math.Cos(rad), (float)Math.Sin(rad));
        }
    }
}
