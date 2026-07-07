using NUnit.Framework;
using VoidRunner.Content;
using VoidRunner.Core;
using VoidRunner.Meta;
using VoidRunner.Replay;
using VoidRunner.Rng;

namespace VoidRunner.Tests
{
    /// <summary>
    /// Tests for the verified local leaderboard and the seed-of-the-day — the concrete home for the
    /// "shareable, verifiable score" pitch. The key property: a score is only admitted if its replay
    /// actually reproduces it, so the board can't be gamed with a fabricated number.
    /// </summary>
    public sealed class LeaderboardTests
    {
        private static InputCommand ScriptedInput(int tick)
        {
            var r = new DeterministicRandom(((ulong)tick * 2654435761UL) ^ 0xBADF00D);
            return InputCommand.From(
                new Vec2(r.NextFloat() * 2 - 1, r.NextFloat() * 2 - 1),
                SimMathUtil.FromAngle(r.NextFloat() * 360f), (tick % 3) != 0);
        }

        private static (ReplayData replay, ContentRegistry reg, ulong fp) Record(ulong seed, string label)
        {
            var reg = TestContent.BuildValidRegistry();
            ulong fp = ContentFingerprint.Compute(reg);
            var sim = new Simulation(reg, seed);
            var rec = new ReplayRecorder(seed, fp, label);
            for (int t = 0; t < 3000; t++)
            {
                var cmd = ScriptedInput(t);
                rec.Record(cmd);
                sim.Step(cmd);
                if (sim.RunOver) break;
            }
            return (rec.Finish(sim), reg, fp);
        }

        [Test]
        public void VerifiedRun_IsAccepted()
        {
            var (replay, reg, fp) = Record(2024, "run");
            var lb = new Leaderboard();
            bool ok = lb.TrySubmit(replay, reg, fp, "alice", out var reason);
            Assert.IsTrue(ok, reason);
            Assert.AreEqual(1, lb.Board(replay.Seed, fp).Count);
            Assert.AreEqual(replay.FinalScore, lb.BestScore(replay.Seed, fp));
        }

        [Test]
        public void TamperedScore_IsRejected()
        {
            var (replay, reg, fp) = Record(99, "run");
            replay.FinalScore += 100_000; // claim a score the replay does not produce
            var lb = new Leaderboard();
            bool ok = lb.TrySubmit(replay, reg, fp, "cheater", out var reason);
            Assert.IsFalse(ok, "a fabricated score must not be admitted");
            StringAssert.Contains("verify", reason);
            Assert.AreEqual(0, lb.All.Count);
        }

        [Test]
        public void MismatchedFingerprint_IsRejected()
        {
            var (replay, reg, fp) = Record(7, "run");
            var lb = new Leaderboard();
            bool ok = lb.TrySubmit(replay, reg, fp ^ 0x1, "modder", out var reason);
            Assert.IsFalse(ok, "a replay from a different content set must be rejected");
        }

        [Test]
        public void Board_IsSortedByScoreThenSpeed_AndTrimmed()
        {
            var reg = TestContent.BuildValidRegistry();
            ulong fp = ContentFingerprint.Compute(reg);
            var lb = new Leaderboard { MaxPerBoard = 3 };

            // Submit several runs on the SAME seed so they share a board.
            ulong seed = 555;
            int accepted = 0;
            for (int i = 0; i < 6; i++)
            {
                var sim = new Simulation(reg, seed);
                var rec = new ReplayRecorder(seed, fp, "b");
                int ticks = 500 + i * 300; // different lengths → different scores
                for (int t = 0; t < ticks; t++) { var c = ScriptedInput(t + i); rec.Record(c); sim.Step(c); if (sim.RunOver) break; }
                if (lb.TrySubmit(rec.Finish(sim), reg, fp, "p" + i, out _)) accepted++;
            }

            var board = lb.Board(seed, fp);
            Assert.LessOrEqual(board.Count, 3, "board must be trimmed to MaxPerBoard");
            for (int i = 1; i < board.Count; i++)
                Assert.GreaterOrEqual(board[i - 1].Score, board[i].Score, "board must be sorted by score desc");
        }

        [Test]
        public void Serialization_RoundTrips_AndEntriesStayVerifiable()
        {
            var (replay, reg, fp) = Record(31337, "run");
            var lb = new Leaderboard();
            Assert.IsTrue(lb.TrySubmit(replay, reg, fp, "bob", out _));

            var restored = Leaderboard.Deserialize(lb.Serialize());
            Assert.AreEqual(1, restored.All.Count);
            var e = restored.Board(replay.Seed, fp)[0];
            Assert.AreEqual(replay.FinalScore, e.Score);

            // The stored replay text still verifies on its own.
            var storedReplay = ReplayCodec.Deserialize(e.ReplayText);
            var verify = ReplayVerifier.Verify(storedReplay, reg, fp);
            Assert.IsTrue(verify.Reproduced, verify.Message);
        }

        [Test]
        public void DailySeed_IsStableForADate_AndMatchesTypingTheLabel()
        {
            // Same date → same seed, every time.
            ulong a = DailySeed.SeedFor(2026, 7, 7);
            ulong b = DailySeed.SeedFor(2026, 7, 7);
            Assert.AreEqual(a, b);

            // Different dates → different seeds (overwhelmingly likely).
            Assert.AreNotEqual(a, DailySeed.SeedFor(2026, 7, 8));

            // The daily seed equals what you'd get typing its label into the seed box.
            string label = DailySeed.LabelFor(2026, 7, 7);
            Assert.AreEqual(DeterministicRandom.FromString(label).Seed, a);
            Assert.AreEqual("DAILY-2026-07-07", label);
        }

        [Test]
        public void DailyRun_ProducesSameOutcomeForEveryone()
        {
            var reg = TestContent.BuildValidRegistry();
            ulong seed = DailySeed.SeedFor(2026, 1, 1);

            ulong Play()
            {
                var sim = new Simulation(reg, seed);
                for (int t = 0; t < 1500; t++) { sim.Step(ScriptedInput(t)); if (sim.RunOver) break; }
                return sim.StateHash();
            }
            Assert.AreEqual(Play(), Play(), "the daily must be identical for every player");
        }
    }
}
