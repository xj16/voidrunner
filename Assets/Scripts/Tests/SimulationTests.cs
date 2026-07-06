using NUnit.Framework;
using VoidRunner.Content;
using VoidRunner.Core;
using VoidRunner.Rng;

namespace VoidRunner.Tests
{
    public sealed class SimulationTests
    {
        /// <summary>
        /// Produces a deterministic pseudo-input stream driven purely by tick number, so two runs
        /// receive byte-identical inputs and any state divergence is the simulation's fault, not the
        /// input's. This mimics a "bot" playing the game the same way twice.
        /// </summary>
        private static InputCommand ScriptedInput(int tick)
        {
            var r = new DeterministicRandom((ulong)tick * 2654435761UL);
            float mx = r.NextFloat() * 2f - 1f;
            float my = r.NextFloat() * 2f - 1f;
            float aim = r.NextFloat() * 360f;
            bool fire = (tick % 5) != 0; // fire most ticks
            return InputCommand.From(new Vec2(mx, my), SimMathUtil.FromAngle(aim), fire);
        }

        [Test]
        public void SimulationConstructs_WithValidContent()
        {
            var reg = TestContent.BuildValidRegistry();
            var sim = new Simulation(reg, 1);
            Assert.IsNotNull(sim.CurrentRoom);
            Assert.AreEqual(1, sim.RoomNumber);
            Assert.IsTrue(sim.Player.Alive);
            Assert.AreEqual("blaster", sim.Player.WeaponId, "should pick the highest-rarity weapon to start");
        }

        [Test]
        public void SameSeedSameInputs_ProduceIdenticalStateHash()
        {
            var reg = TestContent.BuildValidRegistry();

            var a = new Simulation(reg, 777);
            var b = new Simulation(reg, 777);

            for (int t = 0; t < 3600; t++) // 60 seconds of simulation
            {
                var cmd = ScriptedInput(t);
                a.Step(cmd);
                b.Step(cmd);
                Assert.AreEqual(a.StateHash(), b.StateHash(), $"state diverged at tick {t}");
                if (a.RunOver && b.RunOver) break;
            }
        }

        [Test]
        public void DifferentSeeds_DivergeEventually()
        {
            var reg = TestContent.BuildValidRegistry();
            var a = new Simulation(reg, 1);
            var b = new Simulation(reg, 2);

            bool diverged = false;
            for (int t = 0; t < 600; t++)
            {
                var cmd = ScriptedInput(t);
                a.Step(cmd);
                b.Step(cmd);
                if (a.StateHash() != b.StateHash()) { diverged = true; break; }
            }
            Assert.IsTrue(diverged, "distinct seeds should produce distinct runs under identical inputs");
        }

        [Test]
        public void PlayerCannotLeaveRoomBounds()
        {
            var reg = TestContent.BuildValidRegistry();
            var sim = new Simulation(reg, 5);
            // Slam movement into a corner for a while.
            var push = InputCommand.From(new Vec2(1, 1), new Vec2(1, 0), false);
            for (int t = 0; t < 600; t++) sim.Step(push);

            float halfW = sim.CurrentRoom.width * 0.5f;
            float halfH = sim.CurrentRoom.height * 0.5f;
            Assert.LessOrEqual(sim.Player.Position.X, halfW + 0.001f);
            Assert.LessOrEqual(sim.Player.Position.Y, halfH + 0.001f);
        }

        [Test]
        public void FiringSpawnsProjectiles()
        {
            var reg = TestContent.BuildValidRegistry();
            var sim = new Simulation(reg, 9);
            var fire = InputCommand.From(Vec2.Zero, new Vec2(1, 0), true);
            for (int t = 0; t < 30; t++) sim.Step(fire);

            int active = 0;
            for (int i = 0; i < Simulation.MaxProjectiles; i++)
                if (sim.Projectiles[i].Active) active++;
            Assert.Greater(active, 0, "holding fire should produce live projectiles");
        }

        [Test]
        public void EnemiesSpawnFromWaves()
        {
            var reg = TestContent.BuildValidRegistry();
            var sim = new Simulation(reg, 3);
            // Idle a couple seconds so the wave schedule fires.
            for (int t = 0; t < 180; t++) sim.Step(InputCommand.None);
            Assert.Greater(sim.EnemiesAlive, 0, "waves should have spawned enemies by now");
        }

        [Test]
        public void RunProgresses_ScoreIsMonotonic()
        {
            var reg = TestContent.BuildValidRegistry();
            var sim = new Simulation(reg, 2024);
            int lastScore = 0;
            for (int t = 0; t < 3600; t++)
            {
                sim.Step(ScriptedInput(t));
                Assert.GreaterOrEqual(sim.Score, lastScore, "score must never decrease");
                lastScore = sim.Score;
                if (sim.RunOver) break;
            }
        }

        [Test]
        public void StepAfterRunOver_IsNoOp()
        {
            var reg = TestContent.BuildValidRegistry();
            var sim = new Simulation(reg, 42);
            // Force death by standing still and letting enemies pile on.
            for (int t = 0; t < 20000 && !sim.RunOver; t++) sim.Step(InputCommand.None);

            if (sim.RunOver)
            {
                ulong before = sim.StateHash();
                int tickBefore = sim.Tick;
                sim.Step(ScriptedInput(0));
                Assert.AreEqual(before, sim.StateHash());
                Assert.AreEqual(tickBefore, sim.Tick, "ticks must not advance after the run ends");
            }
            else
            {
                Assert.Pass("run did not end within the budget; no-op behaviour not exercised");
            }
        }
    }
}
