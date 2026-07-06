using NUnit.Framework;
using VoidRunner.Rng;

namespace VoidRunner.Tests
{
    public sealed class DeterministicRandomTests
    {
        [Test]
        public void SameSeed_ProducesIdenticalSequence()
        {
            var a = new DeterministicRandom(12345);
            var b = new DeterministicRandom(12345);
            for (int i = 0; i < 10000; i++)
            {
                Assert.AreEqual(a.NextULong(), b.NextULong(), $"diverged at draw {i}");
            }
        }

        [Test]
        public void DifferentSeeds_ProduceDifferentSequences()
        {
            var a = new DeterministicRandom(1);
            var b = new DeterministicRandom(2);
            bool anyDifferent = false;
            for (int i = 0; i < 100; i++)
            {
                if (a.NextULong() != b.NextULong()) { anyDifferent = true; break; }
            }
            Assert.IsTrue(anyDifferent, "two distinct seeds produced the same stream");
        }

        [Test]
        public void Range_StaysWithinBounds()
        {
            var r = new DeterministicRandom(99);
            for (int i = 0; i < 100000; i++)
            {
                int v = r.Range(5, 12);
                Assert.GreaterOrEqual(v, 5);
                Assert.Less(v, 12);
            }
        }

        [Test]
        public void Range_DegenerateSpan_ReturnsMin()
        {
            var r = new DeterministicRandom(7);
            Assert.AreEqual(3, r.Range(3, 3));
            Assert.AreEqual(3, r.Range(3, 2));
        }

        [Test]
        public void NextFloat_IsInUnitInterval()
        {
            var r = new DeterministicRandom(555);
            for (int i = 0; i < 100000; i++)
            {
                float f = r.NextFloat();
                Assert.GreaterOrEqual(f, 0f);
                Assert.Less(f, 1f);
            }
        }

        [Test]
        public void WeightedIndex_RespectsZeroWeights()
        {
            var r = new DeterministicRandom(2024);
            var weights = new float[] { 0f, 0f, 5f, 0f };
            for (int i = 0; i < 1000; i++)
            {
                Assert.AreEqual(2, r.WeightedIndex(weights));
            }
        }

        [Test]
        public void WeightedIndex_AllZero_ReturnsMinusOne()
        {
            var r = new DeterministicRandom(2024);
            Assert.AreEqual(-1, r.WeightedIndex(new float[] { 0f, 0f }));
            Assert.AreEqual(-1, r.WeightedIndex(new float[0]));
        }

        [Test]
        public void WeightedIndex_RoughlyProportional()
        {
            var r = new DeterministicRandom(1234);
            var weights = new float[] { 1f, 3f }; // expect ~25% / ~75%
            int[] counts = new int[2];
            const int n = 200000;
            for (int i = 0; i < n; i++) counts[r.WeightedIndex(weights)]++;
            double p1 = counts[1] / (double)n;
            Assert.That(p1, Is.EqualTo(0.75).Within(0.02), $"got p(index1)={p1}");
        }

        [Test]
        public void FromString_IsStableAndCaseSensitive()
        {
            var a = DeterministicRandom.FromString("COSMIC-DRIFT");
            var b = DeterministicRandom.FromString("COSMIC-DRIFT");
            var c = DeterministicRandom.FromString("cosmic-drift");
            Assert.AreEqual(a.Seed, b.Seed);
            Assert.AreNotEqual(a.Seed, c.Seed);
        }

        [Test]
        public void SnapshotRestore_ResumesExactly()
        {
            var r = new DeterministicRandom(42);
            for (int i = 0; i < 50; i++) r.NextULong();
            var snap = r.Snapshot();

            ulong first = r.NextULong();
            // Advance further, then restore and confirm we reproduce 'first'.
            for (int i = 0; i < 20; i++) r.NextULong();
            r.Restore(snap);
            Assert.AreEqual(first, r.NextULong());
        }
    }
}
