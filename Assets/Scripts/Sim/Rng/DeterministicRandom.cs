using System;

namespace VoidRunner.Rng
{
    /// <summary>
    /// A fully deterministic, platform-independent pseudo-random number generator.
    ///
    /// This is intentionally NOT <see cref="UnityEngine.Random"/>: Unity's RNG state is global,
    /// shared across the engine, and not guaranteed to be stable across versions. For a seed-based
    /// roguelite with shareable replays we need a generator whose entire behaviour is defined by
    /// this file alone, so that a given seed produces the exact same run on every machine and every
    /// build.
    ///
    /// Algorithm: SplitMix64 for seeding, then xoshiro256** for the main stream. Both are public
    /// domain reference designs with excellent statistical quality and no hidden platform state.
    /// </summary>
    public sealed class DeterministicRandom
    {
        private ulong _s0, _s1, _s2, _s3;

        /// <summary>The seed this generator was constructed with (for logging / replay headers).</summary>
        public ulong Seed { get; }

        public DeterministicRandom(ulong seed)
        {
            Seed = seed;

            // SplitMix64 to expand a single 64-bit seed into the 256-bit xoshiro state.
            ulong sm = seed;
            _s0 = SplitMix64(ref sm);
            _s1 = SplitMix64(ref sm);
            _s2 = SplitMix64(ref sm);
            _s3 = SplitMix64(ref sm);

            // Guard against the (astronomically unlikely) all-zero state.
            if ((_s0 | _s1 | _s2 | _s3) == 0UL)
            {
                _s0 = 0x9E3779B97F4A7C15UL;
            }
        }

        /// <summary>
        /// Creates a random from a human-readable string seed (e.g. "COSMIC-DRIFT").
        /// Uses a deterministic FNV-1a hash so the same text always yields the same run.
        /// </summary>
        public static DeterministicRandom FromString(string textSeed)
        {
            if (string.IsNullOrEmpty(textSeed))
            {
                return new DeterministicRandom(0UL);
            }

            const ulong fnvOffset = 1469598103934665603UL;
            const ulong fnvPrime = 1099511628211UL;
            ulong hash = fnvOffset;
            foreach (char c in textSeed)
            {
                hash ^= c;
                hash *= fnvPrime;
            }

            return new DeterministicRandom(hash);
        }

        private static ulong SplitMix64(ref ulong state)
        {
            state += 0x9E3779B97F4A7C15UL;
            ulong z = state;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        private static ulong Rotl(ulong x, int k) => (x << k) | (x >> (64 - k));

        /// <summary>Returns the next raw 64-bit value in the stream (xoshiro256**).</summary>
        public ulong NextULong()
        {
            ulong result = Rotl(_s1 * 5UL, 7) * 9UL;
            ulong t = _s1 << 17;

            _s2 ^= _s0;
            _s3 ^= _s1;
            _s1 ^= _s2;
            _s0 ^= _s3;
            _s2 ^= t;
            _s3 = Rotl(_s3, 45);

            return result;
        }

        /// <summary>Returns a non-negative 32-bit integer.</summary>
        public int NextInt()
        {
            // Take the high 31 bits; they have the best distribution in xoshiro output.
            return (int)(NextULong() >> 33);
        }

        /// <summary>Returns an integer in [minInclusive, maxExclusive). Unbiased rejection sampling.</summary>
        public int Range(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive)
            {
                return minInclusive;
            }

            ulong span = (ulong)(maxExclusive - minInclusive);
            // Rejection sampling to avoid modulo bias.
            ulong limit = ulong.MaxValue - (ulong.MaxValue % span);
            ulong sample;
            do
            {
                sample = NextULong();
            } while (sample >= limit);

            return minInclusive + (int)(sample % span);
        }

        /// <summary>Returns a double in [0, 1).</summary>
        public double NextDouble()
        {
            // 53 bits of mantissa precision.
            return (NextULong() >> 11) * (1.0 / 9007199254740992.0);
        }

        /// <summary>Returns a float in [0, 1).</summary>
        public float NextFloat() => (float)NextDouble();

        /// <summary>Returns a float in [min, max).</summary>
        public float RangeFloat(float min, float max) => min + (NextFloat() * (max - min));

        /// <summary>Returns true with the given probability in [0, 1].</summary>
        public bool Chance(float probability) => NextFloat() < probability;

        /// <summary>
        /// Picks one entry from a list of weights, proportional to weight. Returns the chosen index,
        /// or -1 if the list is empty or every weight is non-positive.
        /// </summary>
        public int WeightedIndex(System.Collections.Generic.IReadOnlyList<float> weights)
        {
            if (weights == null || weights.Count == 0)
            {
                return -1;
            }

            float total = 0f;
            for (int i = 0; i < weights.Count; i++)
            {
                if (weights[i] > 0f)
                {
                    total += weights[i];
                }
            }

            if (total <= 0f)
            {
                return -1;
            }

            float roll = NextFloat() * total;
            float cursor = 0f;
            for (int i = 0; i < weights.Count; i++)
            {
                if (weights[i] <= 0f)
                {
                    continue;
                }

                cursor += weights[i];
                if (roll < cursor)
                {
                    return i;
                }
            }

            // Floating point fall-through: return the last positive-weight entry.
            for (int i = weights.Count - 1; i >= 0; i--)
            {
                if (weights[i] > 0f)
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>Snapshot of the internal state, so a run can be saved and resumed bit-exactly.</summary>
        public RandomState Snapshot() => new RandomState(_s0, _s1, _s2, _s3);

        /// <summary>Restores a previously captured state.</summary>
        public void Restore(RandomState state)
        {
            _s0 = state.S0;
            _s1 = state.S1;
            _s2 = state.S2;
            _s3 = state.S3;
        }
    }

    /// <summary>Immutable snapshot of a <see cref="DeterministicRandom"/> state.</summary>
    [Serializable]
    public readonly struct RandomState
    {
        public readonly ulong S0, S1, S2, S3;

        public RandomState(ulong s0, ulong s1, ulong s2, ulong s3)
        {
            S0 = s0;
            S1 = s1;
            S2 = s2;
            S3 = s3;
        }
    }
}
