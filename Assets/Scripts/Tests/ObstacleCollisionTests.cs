using NUnit.Framework;
using VoidRunner.Content;
using VoidRunner.Core;

namespace VoidRunner.Tests
{
    /// <summary>
    /// Tests for obstacle collision — the feature that was previously "parsed but unused" (rooms
    /// defined obstacle rectangles that the simulation ignored). These assert the shipped room
    /// layouts now actually matter: entities can't walk through blocks and shots die on them.
    /// </summary>
    public sealed class ObstacleCollisionTests
    {
        // A room with one big central block, so an entity pushed toward the centre must be stopped.
        private const string WalledJson = @"
{
  ""enemies"": [
    { ""id"": ""grunt"", ""maxHealth"": 8, ""moveSpeed"": 2.5, ""contactDamage"": 6, ""radius"": 0.4, ""behaviour"": ""chase"", ""scoreValue"": 10, ""dropChance"": 0.0 }
  ],
  ""weapons"": [
    { ""id"": ""blaster"", ""damage"": 4, ""fireRate"": 5, ""projectileSpeed"": 14, ""projectileLifetime"": 3.0, ""projectileRadius"": 0.12, ""rarityWeight"": 5 }
  ],
  ""waves"": [ { ""id"": ""w"", ""groups"": [ { ""enemyId"": ""grunt"", ""count"": 1, ""delay"": 99 } ] } ],
  ""rooms"": [
    { ""id"": ""walled"", ""width"": 24, ""height"": 14, ""weight"": 1, ""waveIds"": [ ""w"" ],
      ""obstacles"": [ { ""x"": 6, ""y"": 0, ""width"": 4, ""height"": 8 } ] }
  ]
}";

        private static ContentRegistry WalledRegistry()
        {
            var res = ContentLoader.LoadFiles(new[] { ("walled", WalledJson) });
            Assert.IsTrue(res.Ok, "walled fixture should load");
            return res.Registry;
        }

        [Test]
        public void PlayerCannotPassThroughAnObstacle()
        {
            var reg = WalledRegistry();
            var sim = new Simulation(reg, 1);
            // The obstacle spans x∈[4,8], y∈[-4,4]. The player starts at origin (x=0) and shoves right.
            var pushRight = InputCommand.From(new Vec2(1, 0), new Vec2(1, 0), false);
            for (int t = 0; t < 300; t++) sim.Step(pushRight);

            // With a solid block whose left face is at x=4, the player (radius ~0.35) must be stopped
            // before entering it and can never appear on the far side (x > 8).
            Assert.Less(sim.Player.Position.X, 4f,
                $"player tunnelled into/through the obstacle (x={sim.Player.Position.X})");
        }

        [Test]
        public void PlayerNeverOverlapsAnyObstacle_UnderRandomMovement()
        {
            var reg = WalledRegistry();
            var sim = new Simulation(reg, 42);
            var rng = new VoidRunner.Rng.DeterministicRandom(9999);

            for (int t = 0; t < 2000; t++)
            {
                float mx = rng.NextFloat() * 2f - 1f;
                float my = rng.NextFloat() * 2f - 1f;
                sim.Step(InputCommand.From(new Vec2(mx, my), new Vec2(1, 0), false));

                foreach (var o in sim.CurrentRoom.obstacles)
                {
                    float halfW = o.width * 0.5f, halfH = o.height * 0.5f;
                    float cx = SimMathUtil.Clamp(sim.Player.Position.X, o.x - halfW, o.x + halfW);
                    float cy = SimMathUtil.Clamp(sim.Player.Position.Y, o.y - halfH, o.y + halfH);
                    float dx = sim.Player.Position.X - cx, dy = sim.Player.Position.Y - cy;
                    float distSq = dx * dx + dy * dy;
                    // Allow a hair of tolerance for float epsilon at the exact contact point.
                    Assert.GreaterOrEqual(distSq, (sim.Player.Radius - 0.02f) * (sim.Player.Radius - 0.02f),
                        $"player penetrated obstacle at tick {t}: pos=({sim.Player.Position.X},{sim.Player.Position.Y})");
                }
            }
        }

        [Test]
        public void ProjectileIsDestroyedByAnObstacle()
        {
            var reg = WalledRegistry();
            var sim = new Simulation(reg, 7);

            // Fire straight right, toward the block at x∈[4,8]. Every projectile should die on the
            // wall — none should survive to the far side of the room.
            var fireRight = InputCommand.From(Vec2.Zero, new Vec2(1, 0), true);
            bool anyProjectilePassedWall = false;
            for (int t = 0; t < 120; t++)
            {
                sim.Step(fireRight);
                for (int i = 0; i < Simulation.MaxProjectiles; i++)
                {
                    if (sim.Projectiles[i].Active && sim.Projectiles[i].Position.X > 8.5f)
                        anyProjectilePassedWall = true;
                }
            }
            Assert.IsFalse(anyProjectilePassedWall, "a projectile passed through a solid obstacle");
        }

        [Test]
        public void ObstacleCollisionIsDeterministic()
        {
            var reg = WalledRegistry();
            var a = new Simulation(reg, 123);
            var b = new Simulation(reg, 123);
            var rng = new VoidRunner.Rng.DeterministicRandom(55);
            for (int t = 0; t < 1500; t++)
            {
                var cmd = InputCommand.From(
                    new Vec2(rng.NextFloat() * 2 - 1, rng.NextFloat() * 2 - 1),
                    SimMathUtil.FromAngle(rng.NextFloat() * 360f), (t % 3) != 0);
                a.Step(cmd);
                b.Step(cmd);
                Assert.AreEqual(a.StateHash(), b.StateHash(), $"obstacle collision diverged at tick {t}");
            }
        }
    }
}
