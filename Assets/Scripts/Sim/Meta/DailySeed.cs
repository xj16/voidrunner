using VoidRunner.Rng;

namespace VoidRunner.Meta
{
    /// <summary>
    /// Derives the "seed of the day": a single seed every player shares on a given UTC date, so the
    /// daily challenge is the same run for everyone and scores are directly comparable. Because the
    /// simulation is deterministic, the daily is a fair, verifiable competition with no server —
    /// everyone's inputs run against the identical world.
    ///
    /// Engine-agnostic and pure: the date is passed in (the Unity layer supplies DateTime.UtcNow),
    /// so the mapping is unit-testable and stable.
    /// </summary>
    public static class DailySeed
    {
        /// <summary>
        /// A human-readable label for the daily on the given UTC date, e.g. "DAILY-2026-07-07".
        /// This is what a player would type to reproduce the daily manually.
        /// </summary>
        public static string LabelFor(int utcYear, int utcMonth, int utcDay)
        {
            return $"DAILY-{utcYear:D4}-{utcMonth:D2}-{utcDay:D2}";
        }

        /// <summary>
        /// The numeric seed for the daily on the given UTC date. Derived from the label via the same
        /// deterministic string hash the rest of the game uses, so <c>SeedFor(y,m,d)</c> equals
        /// typing <c>LabelFor(y,m,d)</c> into the seed box.
        /// </summary>
        public static ulong SeedFor(int utcYear, int utcMonth, int utcDay)
        {
            return DeterministicRandom.FromString(LabelFor(utcYear, utcMonth, utcDay)).Seed;
        }
    }
}
