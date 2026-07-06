using NUnit.Framework;
using VoidRunner.Content;
using VoidRunner.Core;
using VoidRunner.Replay;
using VoidRunner.Rng;

namespace VoidRunner.Tests
{
    public sealed class ReplayTests
    {
        private static InputCommand ScriptedInput(int tick)
        {
            var r = new DeterministicRandom(((ulong)tick * 2654435761UL) ^ 0xABCDEF);
            float mx = r.NextFloat() * 2f - 1f;
            float my = r.NextFloat() * 2f - 1f;
            float aim = r.NextFloat() * 360f;
            bool fire = (tick % 4) != 0;
            return InputCommand.From(new Vec2(mx, my), SimMathUtil.FromAngle(aim), fire);
        }

        /// <summary>Plays a full run, recording it, and returns (replay, registry, fingerprint).</summary>
        private static (ReplayData replay, ContentRegistry reg, ulong fp) RecordRun(ulong seed, int maxTicks)
        {
            var reg = TestContent.BuildValidRegistry();
            ulong fp = ContentFingerprint.Compute(reg);

            var sim = new Simulation(reg, seed);
            var rec = new ReplayRecorder(seed, fp, "test-run");
            for (int t = 0; t < maxTicks; t++)
            {
                var cmd = ScriptedInput(t);
                rec.Record(cmd);
                sim.Step(cmd);
                if (sim.RunOver) break;
            }
            return (rec.Finish(sim), reg, fp);
        }

        [Test]
        public void RecordedReplay_ReproducesExactly()
        {
            var (replay, reg, fp) = RecordRun(12321, 3000);
            var result = ReplayVerifier.Verify(replay, reg, fp);
            Assert.IsTrue(result.Reproduced, result.Message);
        }

        [Test]
        public void ReplaySerialization_RoundTrips()
        {
            var (replay, reg, fp) = RecordRun(55555, 1500);

            string text = ReplayCodec.Serialize(replay);
            var restored = ReplayCodec.Deserialize(text);

            Assert.AreEqual(replay.Seed, restored.Seed);
            Assert.AreEqual(replay.ContentFingerprint, restored.ContentFingerprint);
            Assert.AreEqual(replay.TickCount, restored.TickCount);
            Assert.AreEqual(replay.FinalScore, restored.FinalScore);
            Assert.AreEqual(replay.FinalStateHash, restored.FinalStateHash);

            for (int i = 0; i < replay.TickCount; i++)
            {
                Assert.IsTrue(replay.Inputs[i].Equals(restored.Inputs[i]), $"input {i} changed through serialization");
            }

            // And the deserialized replay still verifies.
            var verify = ReplayVerifier.Verify(restored, reg, fp);
            Assert.IsTrue(verify.Reproduced, verify.Message);
        }

        [Test]
        public void SerializedReplay_IsCompact()
        {
            // Run-length encoding should keep a run of many identical-ish inputs small.
            var (replay, _, _) = RecordRun(1, 3000);
            string text = ReplayCodec.Serialize(replay);
            // A few thousand ticks should serialize to well under 200 KB of text.
            Assert.Less(text.Length, 200_000, $"replay text unexpectedly large: {text.Length} chars for {replay.TickCount} ticks");
        }

        [Test]
        public void FingerprintMismatch_IsRefused()
        {
            var (replay, reg, fp) = RecordRun(7, 300);
            var result = ReplayVerifier.Verify(replay, reg, fp ^ 0x1); // corrupt the expected fingerprint
            Assert.IsFalse(result.Reproduced);
            StringAssert.Contains("fingerprint", result.Message);
        }

        [Test]
        public void CorruptReplayMagic_Throws()
        {
            Assert.Throws<System.FormatException>(() =>
                ReplayCodec.Deserialize("{\"magic\":\"NOPE\",\"version\":1}"));
        }

        [Test]
        public void EmptyRun_RoundTrips()
        {
            var reg = TestContent.BuildValidRegistry();
            ulong fp = ContentFingerprint.Compute(reg);
            var sim = new Simulation(reg, 88);
            var rec = new ReplayRecorder(88, fp, "");
            var replay = rec.Finish(sim); // zero inputs

            string text = ReplayCodec.Serialize(replay);
            var restored = ReplayCodec.Deserialize(text);
            Assert.AreEqual(0, restored.TickCount);
            var verify = ReplayVerifier.Verify(restored, reg, fp);
            Assert.IsTrue(verify.Reproduced, verify.Message);
        }

        [Test]
        public void RunLengthEncoding_HandlesLongIdenticalRuns()
        {
            var reg = TestContent.BuildValidRegistry();
            ulong fp = ContentFingerprint.Compute(reg);
            var sim = new Simulation(reg, 3);
            var rec = new ReplayRecorder(3, fp, "idle");
            var idle = InputCommand.None;
            for (int t = 0; t < 5000; t++) { rec.Record(idle); sim.Step(idle); if (sim.RunOver) break; }
            var replay = rec.Finish(sim);

            string text = ReplayCodec.Serialize(replay);
            var restored = ReplayCodec.Deserialize(text);
            Assert.AreEqual(replay.TickCount, restored.TickCount);
            var verify = ReplayVerifier.Verify(restored, reg, fp);
            Assert.IsTrue(verify.Reproduced, verify.Message);
        }
    }
}
