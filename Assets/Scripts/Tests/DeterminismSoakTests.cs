using System.Diagnostics;
using NUnit.Framework;
using VoidRunner.Content;
using VoidRunner.Core;
using VoidRunner.Replay;
using VoidRunner.Rng;

namespace VoidRunner.Tests
{
    /// <summary>
    /// Heavy determinism + performance guards over the simulation — the subsystem the whole pitch
    /// rests on. These are the "flagship" tests: they exercise many seeds over long runs and assert
    /// every one re-verifies bit-for-bit, and they hold the brute-force collision loop to a tick-time
    /// budget so a future change can't quietly make heavy waves stutter.
    /// </summary>
    public sealed class DeterminismSoakTests
    {
        private static InputCommand ScriptedInput(int tick, ulong salt)
        {
            var r = new DeterministicRandom(((ulong)tick * 2654435761UL) ^ salt);
            float mx = r.NextFloat() * 2f - 1f;
            float my = r.NextFloat() * 2f - 1f;
            float aim = r.NextFloat() * 360f;
            bool fire = (tick % 3) != 0;
            return InputCommand.From(new Vec2(mx, my), SimMathUtil.FromAngle(aim), fire);
        }

        [Test]
        public void ManySeeds_EveryReplayReverifiesExactly()
        {
            var reg = TestContent.BuildValidRegistry();
            ulong fp = ContentFingerprint.Compute(reg);

            // 60 distinct seeds, each a multi-thousand-tick run recorded then re-verified.
            for (int s = 0; s < 60; s++)
            {
                ulong seed = (ulong)(s * 1_000_003 + 17);
                var sim = new Simulation(reg, seed);
                var rec = new ReplayRecorder(seed, fp, "soak");
                for (int t = 0; t < 4000; t++)
                {
                    var cmd = ScriptedInput(t, seed);
                    rec.Record(cmd);
                    sim.Step(cmd);
                    if (sim.RunOver) break;
                }
                var replay = rec.Finish(sim);

                // Round-trip through serialization too, so the codec is part of the guarantee.
                var restored = ReplayCodec.Deserialize(ReplayCodec.Serialize(replay));
                var verify = ReplayVerifier.Verify(restored, reg, fp);
                Assert.IsTrue(verify.Reproduced, $"seed {seed} failed to re-verify: {verify.Message}");
            }
        }

        [Test]
        public void RerunningTheSameSeedGivesTheSameFinalHash()
        {
            var reg = TestContent.BuildValidRegistry();
            for (ulong seed = 1; seed <= 20; seed++)
            {
                ulong h1 = FinalHash(reg, seed);
                ulong h2 = FinalHash(reg, seed);
                Assert.AreEqual(h1, h2, $"seed {seed} produced different final hashes on re-run");
            }
        }

        private static ulong FinalHash(ContentRegistry reg, ulong seed)
        {
            var sim = new Simulation(reg, seed);
            for (int t = 0; t < 3000; t++)
            {
                sim.Step(ScriptedInput(t, seed));
                if (sim.RunOver) break;
            }
            return sim.StateHash();
        }

        [Test]
        [Category("Performance")]
        public void HeavyWave_StaysUnderTickBudget()
        {
            // A modded pack that dumps a huge swarm and a 360° nova weapon — the worst case for the
            // O(projectiles × enemies) collision. We assert a full run stays well under a generous
            // per-tick budget so the brute-force loop can't silently regress into a stutter.
            var reg = HeavyRegistry();
            var sim = new Simulation(reg, 12345);

            // Warm up JIT.
            for (int t = 0; t < 200; t++) sim.Step(FullFire(t));

            var sw = Stopwatch.StartNew();
            const int ticks = 3000;
            for (int t = 0; t < ticks && !sim.RunOver; t++) sim.Step(FullFire(t));
            sw.Stop();

            double msPerTick = sw.Elapsed.TotalMilliseconds / ticks;
            // A 60 Hz tick has 16.6 ms; even heavily loaded we should be a large multiple under it.
            Assert.Less(msPerTick, 4.0, $"heavy-wave tick averaged {msPerTick:F3} ms/tick (budget 4 ms)");
        }

        private static InputCommand FullFire(int tick)
        {
            // Circle-strafe while firing constantly.
            float aim = (tick * 7) % 360;
            return InputCommand.From(new Vec2(1, 0), SimMathUtil.FromAngle(aim), true);
        }

        private static ContentRegistry HeavyRegistry()
        {
            const string json = @"
{
  ""enemies"": [
    { ""id"": ""swarm"", ""maxHealth"": 2, ""moveSpeed"": 3.0, ""contactDamage"": 1, ""radius"": 0.3, ""behaviour"": ""chase"", ""scoreValue"": 1, ""dropChance"": 0.0 }
  ],
  ""weapons"": [
    { ""id"": ""nova"", ""damage"": 3, ""fireRate"": 8, ""projectileSpeed"": 12, ""projectileLifetime"": 1.2, ""projectilesPerShot"": 24, ""spreadDegrees"": 360, ""pierce"": 2, ""projectileRadius"": 0.12, ""rarityWeight"": 5 }
  ],
  ""waves"": [
    { ""id"": ""flood"", ""groups"": [ { ""enemyId"": ""swarm"", ""count"": 120, ""delay"": 0.0 }, { ""enemyId"": ""swarm"", ""count"": 120, ""delay"": 1.0 } ] }
  ],
  ""rooms"": [
    { ""id"": ""pit"", ""width"": 30, ""height"": 18, ""weight"": 1, ""waveIds"": [ ""flood"" ],
      ""obstacles"": [ { ""x"": 0, ""y"": 0, ""width"": 3, ""height"": 3 } ] }
  ]
}";
            var res = ContentLoader.LoadFiles(new[] { ("heavy", json) });
            Assert.IsTrue(res.Ok, "heavy fixture should load");
            return res.Registry;
        }
    }
}
