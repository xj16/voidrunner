using System;
using NUnit.Framework;
using VoidRunner.Core;

namespace VoidRunner.Tests
{
    /// <summary>
    /// Golden-vector tests for <see cref="DetMath"/> — the deterministic sin/cos/sqrt/atan2 the
    /// simulation uses instead of the platform libm.
    ///
    /// These assert the EXACT 32-bit result for a fixed grid of inputs. That is deliberately strict:
    /// if a different OS/CPU/runtime computes even one bit differently, this test fails on that
    /// platform, catching a determinism break before it can silently desync a shared replay. The
    /// values were generated once and committed; they are the contract every platform must meet. The
    /// cross-OS CI matrix runs this suite on ubuntu, windows and macos.
    /// </summary>
    public sealed class DetMathTests
    {
        private static int Bits(float f) => BitConverter.SingleToInt32Bits(f);

        [Test]
        public void Sin_MatchesGoldenBits()
        {
            var g = new (float x, int bits)[]
            {
                (0f, 0x00000000), (0.5f, 0x3EF57744), (1f, 0x3F576AA4), (1.5707963f, 0x3F800000),
                (2f, 0x3F68C7B8), (3.1415927f, 0x00000000), (-1f, unchecked((int)0xBF576AA4)),
                (-2.5f, unchecked((int)0xBF19357A)), (10f, unchecked((int)0xBF0B44F4)),
                (100f, unchecked((int)0xBF01A156)), (1000f, 0x3F53AD71),
            };
            foreach (var (x, bits) in g)
                Assert.AreEqual(bits, Bits(DetMath.Sin(x)), $"Sin({x}) diverged from golden bits");
        }

        [Test]
        public void Cos_MatchesGoldenBits()
        {
            var g = new (float x, int bits)[]
            {
                (0f, 0x3F7FFFFF), (0.5f, 0x3F60A940), (1f, 0x3F0A513F), (1.5707963f, 0x34800000),
                (2f, unchecked((int)0xBED51135)), (3.1415927f, unchecked((int)0xBF800000)),
                (-1f, 0x3F0A5141), (-2.5f, unchecked((int)0xBF4D17BF)), (10f, unchecked((int)0xBF56CD6A)),
                (100f, 0x3F5CC0BB), (1000f, 0x3F0FF937),
            };
            foreach (var (x, bits) in g)
                Assert.AreEqual(bits, Bits(DetMath.Cos(x)), $"Cos({x}) diverged from golden bits");
        }

        [Test]
        public void Sqrt_MatchesGoldenBits()
        {
            var g = new (float x, int bits)[]
            {
                (0f, 0x00000000), (1f, 0x3F800000), (2f, 0x3FB504F3), (3f, 0x3FDDB3D8),
                (4f, 0x40000000), (9f, 0x40400000), (16f, 0x40800000), (2.5f, 0x3FCA62C2),
                (0.25f, 0x3F000000), (100f, 0x41200000), (123.456f, 0x4131C6F7), (1_000_000f, 0x447A0000),
            };
            foreach (var (x, bits) in g)
                Assert.AreEqual(bits, Bits(DetMath.Sqrt(x)), $"Sqrt({x}) diverged from golden bits");
        }

        [Test]
        public void Atan2_MatchesGoldenBits()
        {
            var g = new (float y, float x, int bits)[]
            {
                (1f, 1f, 0x3F49109B), (1f, 0f, 0x3FC90FDB), (0f, 1f, 0x00000000),
                (-1f, 1f, unchecked((int)0xBF49109B)), (1f, -1f, 0x4016CBB4),
                (-1f, -1f, unchecked((int)0xC016CBB4)), (3f, 4f, 0x3F24BCA0),
                (-3f, 4f, unchecked((int)0xBF24BCA0)), (0.5f, 2f, 0x3E7ADB16), (100f, 1f, 0x3FC7C83B),
            };
            foreach (var (y, x, bits) in g)
                Assert.AreEqual(bits, Bits(DetMath.Atan2(y, x)), $"Atan2({y},{x}) diverged from golden bits");
        }

        [Test]
        public void FromAngle_MatchesGoldenBits()
        {
            var g = new (float deg, int bx, int by)[]
            {
                (0f, 0x3F7FFFFF, 0x00000000), (30f, 0x3F5DB3D8, 0x3F000000),
                (45f, 0x3F3504F4, 0x3F3504F3), (90f, 0x00000000, 0x3F7FFFFF),
                (135f, unchecked((int)0xBF3504F4), 0x3F3504F4), (180f, unchecked((int)0xBF800000), 0x00000000),
                (270f, 0x00000000, unchecked((int)0xBF800000)), (359f, 0x3F7FF604, unchecked((int)0xBC8EF924)),
                (-45f, 0x3F3504F3, unchecked((int)0xBF3504F3)),
            };
            foreach (var (deg, bx, by) in g)
            {
                var v = SimMathUtil.FromAngle(deg);
                Assert.AreEqual(bx, Bits(v.X), $"FromAngle({deg}).X diverged");
                Assert.AreEqual(by, Bits(v.Y), $"FromAngle({deg}).Y diverged");
            }
        }

        // --- Accuracy sanity (looser): the deterministic approximations must still be close to the
        //     mathematically correct result, not just self-consistent. ---

        [Test]
        public void Sin_Cos_Sqrt_Atan2_AreAccurateEnough()
        {
            for (int i = -720; i <= 720; i++)
            {
                float rad = i * (DetMath.PI / 180f);
                Assert.That(DetMath.Sin(rad), Is.EqualTo((float)Math.Sin(rad)).Within(1e-3f));
                Assert.That(DetMath.Cos(rad), Is.EqualTo((float)Math.Cos(rad)).Within(1e-3f));
            }
            for (int i = 0; i < 500; i++)
            {
                float v = i * 3.37f;
                Assert.That(DetMath.Sqrt(v), Is.EqualTo((float)Math.Sqrt(v)).Within(1e-3f));
            }
            for (int yi = -50; yi <= 50; yi += 5)
                for (int xi = -50; xi <= 50; xi += 5)
                {
                    if (xi == 0 && yi == 0) continue;
                    Assert.That(DetMath.Atan2(yi, xi),
                        Is.EqualTo((float)Math.Atan2(yi, xi)).Within(1e-3f),
                        $"atan2({yi},{xi})");
                }
        }

        [Test]
        public void Sqrt_OfPerfectSquares_IsExact()
        {
            for (int n = 0; n <= 1000; n++)
                Assert.AreEqual((float)n, DetMath.Sqrt((float)(n * n)), $"sqrt({n * n}) should be exactly {n}");
        }

        [Test]
        public void Sqrt_HandlesEdgeCases()
        {
            Assert.IsTrue(float.IsNaN(DetMath.Sqrt(-1f)));
            Assert.AreEqual(0f, DetMath.Sqrt(0f));
            Assert.IsTrue(float.IsPositiveInfinity(DetMath.Sqrt(float.PositiveInfinity)));
        }
    }
}
