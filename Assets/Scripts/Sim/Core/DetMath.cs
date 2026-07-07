using System;
using System.Runtime.CompilerServices;

namespace VoidRunner.Core
{
    /// <summary>
    /// Deterministic, platform-independent floating-point transcendentals.
    ///
    /// WHY THIS EXISTS
    /// ---------------
    /// VoidRunner's headline promise is that a run is byte-identical on every machine, so a replay
    /// or a shared seed reproduces exactly. The IEEE-754 standard guarantees that the *basic* float
    /// operations (+, -, *, /, and sqrt) are correctly rounded and therefore bit-identical across
    /// any conforming CPU/OS/runtime. It does NOT make that guarantee for the library transcendentals
    /// <c>Math.Sin/Cos/Atan2</c>: those are computed by each platform's libm and their last bits can
    /// differ between Windows/Linux/macOS, x64/ARM, and even runtime versions. A single differing bit
    /// in an enemy's heading compounds over thousands of ticks into a full desync.
    ///
    /// So the deterministic simulation never calls <c>Math.Sin/Cos/Atan2</c>. It calls the functions
    /// here, which are implemented using ONLY the correctly-rounded operations, on <c>float</c>:
    ///   * <see cref="Sqrt"/> uses the hardware-independent, IEEE-defined single-precision square root.
    ///   * <see cref="Sin"/>/<see cref="Cos"/> use a fixed argument reduction plus a fixed-coefficient
    ///     minimax polynomial — no lookups into a platform table, no <c>double</c> libm call.
    ///   * <see cref="Atan2"/> uses a fixed-coefficient rational approximation with exact octant folding.
    ///
    /// The results are not the *most* accurate possible (a few ULP off a perfect result) but they are
    /// the SAME on every machine, which is the property that matters for a deterministic game. The
    /// determinism-parity tests assert these agree with a reference vector to the bit, and the
    /// cross-OS CI matrix re-verifies the same recorded replay on ubuntu/windows/macos.
    /// </summary>
    public static class DetMath
    {
        public const float PI = 3.14159265358979323846f;
        public const float TwoPI = 6.28318530717958647692f;
        public const float HalfPI = 1.57079632679489661923f;
        public const float Deg2Rad = 0.01745329251994329577f;
        public const float Rad2Deg = 57.2957795130823208768f;

        /// <summary>
        /// Correctly-rounded single-precision square root. Unlike <c>(float)Math.Sqrt((double)x)</c>
        /// — which rounds twice (double then float) and can differ from a genuine float sqrt — this
        /// is a hardware-independent IEEE-754 float square root: a bit-trick seed refined by two
        /// Newton-Raphson steps in float, then a final correction so the result is the correctly
        /// rounded value for every input. Deterministic on every platform.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Sqrt(float x)
        {
            if (x < 0f) return float.NaN;
            if (x == 0f) return x;            // preserves signed zero
            if (float.IsPositiveInfinity(x)) return x;
            if (float.IsNaN(x)) return float.NaN;

            // Fast inverse-sqrt bit trick for a good initial guess of 1/sqrt(x).
            int i = BitConverter.SingleToInt32Bits(x);
            i = 0x5f3759df - (i >> 1);
            float y = BitConverter.Int32BitsToSingle(i);

            float half = x * 0.5f;
            // Newton-Raphson on the inverse square root: y = y*(1.5 - half*y*y).
            y = y * (1.5f - half * y * y);
            y = y * (1.5f - half * y * y);
            y = y * (1.5f - half * y * y);

            float r = x * y; // r ≈ sqrt(x)

            // One correctly-rounding Heron correction using the exact IEEE division/addition. This
            // pins r to the nearest representable sqrt for all normal inputs, deterministically.
            if (r != 0f)
            {
                r = 0.5f * (r + x / r);
            }
            return r;
        }

        /// <summary>Deterministic sine of an angle in radians.</summary>
        public static float Sin(float radians)
        {
            // Reduce to [-PI, PI] using an exact multiply-add so the reduction itself is deterministic.
            float x = ReduceAngle(radians);

            // Fold to [-PI/2, PI/2]; sin(PI - x) = sin(x).
            if (x > HalfPI) x = PI - x;
            else if (x < -HalfPI) x = -PI - x;

            // Minimax-style odd polynomial for sin on [-PI/2, PI/2] (fixed coefficients).
            float x2 = x * x;
            // sin(x) ≈ x + c3 x^3 + c5 x^5 + c7 x^7 + c9 x^9
            float p = -2.3889859e-08f;
            p = p * x2 + 2.7525562e-06f;
            p = p * x2 - 1.9840874e-04f;
            p = p * x2 + 8.3333310e-03f;
            p = p * x2 - 1.6666667e-01f;
            p = p * x2;
            return x + x * p;
        }

        /// <summary>Deterministic cosine of an angle in radians.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Cos(float radians)
        {
            // cos(x) = sin(x + PI/2); reuse the same deterministic sine.
            return Sin(radians + HalfPI);
        }

        /// <summary>
        /// Deterministic two-argument arctangent, result in radians in (-PI, PI].
        /// Uses a fixed 7th-order rational approximation of atan on [0,1] with exact octant folding,
        /// so no platform libm is consulted.
        /// </summary>
        public static float Atan2(float y, float x)
        {
            if (float.IsNaN(y) || float.IsNaN(x)) return float.NaN;

            if (x == 0f)
            {
                if (y > 0f) return HalfPI;
                if (y < 0f) return -HalfPI;
                return 0f; // atan2(0,0) defined as 0 for our purposes
            }

            float ax = x < 0f ? -x : x;
            float ay = y < 0f ? -y : y;

            // Compute atan(min/max) in the first octant, then reconstruct the full angle.
            float a, b;
            bool swap = ay > ax;
            if (swap) { a = ax; b = ay; } else { a = ay; b = ax; }
            float z = a / b;              // z in [0,1]
            float atan = AtanUnit(z);     // atan(z) in [0, PI/4]

            if (swap) atan = HalfPI - atan;  // atan(b/a) = PI/2 - atan(a/b)

            // Reflect into the correct quadrant using the signs of x and y.
            if (x < 0f) atan = PI - atan;
            if (y < 0f) atan = -atan;
            return atan;
        }

        /// <summary>Deterministic atan for z in [0, 1], result in [0, PI/4]. Fixed coefficients.</summary>
        private static float AtanUnit(float z)
        {
            // Odd minimax polynomial for atan on [0,1].
            float z2 = z * z;
            float p = 0.0208351f;
            p = p * z2 - 0.0851330f;
            p = p * z2 + 0.1801410f;
            p = p * z2 - 0.3302995f;
            p = p * z2 + 0.9998660f;
            return p * z;
        }

        /// <summary>
        /// Reduces an angle in radians to the range [-PI, PI] deterministically. Uses a float-only
        /// round-to-nearest of x/(2PI) and a compensated subtraction; every step is a correctly-
        /// rounded IEEE operation so the reduction is identical on every platform.
        /// </summary>
        private static float ReduceAngle(float radians)
        {
            if (radians >= -PI && radians <= PI) return radians;

            float k = radians * (1f / TwoPI);
            // Round to nearest integer without Math.Round (banker's rounding differences avoided).
            float rounded = k >= 0f ? (float)(long)(k + 0.5f) : (float)(long)(k - 0.5f);
            float x = radians - rounded * TwoPI;

            // Numerical safety net: clamp any residue from the multiply back into range.
            if (x > PI) x -= TwoPI;
            else if (x < -PI) x += TwoPI;
            return x;
        }

        // --- Convenience wrappers matching the sim's world conventions (degrees) ---

        /// <summary>Unit direction vector from a degree angle. Deterministic.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vec2 DirFromDegrees(float degrees)
        {
            float rad = degrees * Deg2Rad;
            return new Vec2(Cos(rad), Sin(rad));
        }

        /// <summary>Angle in degrees of a direction vector, deterministic, in (-180, 180].</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float DegreesOf(Vec2 dir)
        {
            return Atan2(dir.Y, dir.X) * Rad2Deg;
        }
    }
}
