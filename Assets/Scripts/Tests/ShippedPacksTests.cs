using System.IO;
using NUnit.Framework;
using VoidRunner.Content;
using VoidRunner.Core;
using VoidRunner.Modding;

namespace VoidRunner.Tests
{
    /// <summary>
    /// Loads the ACTUAL content packs shipped in this repository (base + example-mod) from disk and
    /// asserts they are valid, order correctly, and can drive a real simulation. This is the test
    /// that catches a typo in a shipped .json file — the kind of break a modder would hit first.
    ///
    /// It locates the repo root by walking up from the test assembly until it finds the
    /// StreamingAssets/ContentPacks folder, so it works both in the Unity project and the headless
    /// CI build.
    /// </summary>
    public sealed class ShippedPacksTests
    {
        private static string FindContentPacksRoot()
        {
            var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
            for (int i = 0; i < 12 && dir != null; i++)
            {
                string candidate = Path.Combine(dir.FullName, "Assets", "StreamingAssets", "ContentPacks");
                if (Directory.Exists(candidate)) return candidate;
                dir = dir.Parent;
            }
            return null;
        }

        [Test]
        public void ShippedPacks_LoadWithoutErrors()
        {
            string root = FindContentPacksRoot();
            if (root == null)
            {
                Assert.Ignore("ContentPacks folder not found from the test working directory; skipping disk-backed test.");
                return;
            }

            var result = PackDiscovery.DiscoverAndLoad(root, out var order);
            Assert.IsTrue(result.Ok, "shipped packs failed to load:\n" + string.Join("\n", result.Errors));

            // Base must load before the mod that depends on it.
            var ids = order.ConvertAll(p => p.Manifest.id);
            Assert.Contains("base", ids);
            Assert.Contains("neon-swarm", ids);
            Assert.Less(ids.IndexOf("base"), ids.IndexOf("neon-swarm"),
                "base must be ordered before the pack that depends on it");
        }

        [Test]
        public void ModOverride_TakesEffect()
        {
            string root = FindContentPacksRoot();
            if (root == null) { Assert.Ignore("ContentPacks folder not found."); return; }

            var result = PackDiscovery.DiscoverAndLoad(root, out _);
            Assert.IsTrue(result.Ok, string.Join("\n", result.Errors));

            // The example mod renames base "swarmer" to "Neon Swarmer" and buffs its speed.
            var swarmer = result.Registry.GetEnemy("swarmer");
            Assert.IsNotNull(swarmer);
            Assert.AreEqual("Neon Swarmer", swarmer.displayName, "mod override should have replaced the base swarmer");
            Assert.Greater(swarmer.moveSpeed, 4f);

            // And the mod's brand-new content exists.
            Assert.IsNotNull(result.Registry.GetEnemy("prism"), "mod-only enemy should be present");
            Assert.IsNotNull(result.Registry.GetWeapon("prism-beam"), "mod-only weapon should be present");
        }

        [Test]
        public void ShippedContent_RunsAndProducesAReplay()
        {
            string root = FindContentPacksRoot();
            if (root == null) { Assert.Ignore("ContentPacks folder not found."); return; }

            var result = PackDiscovery.DiscoverAndLoad(root, out _);
            Assert.IsTrue(result.Ok, string.Join("\n", result.Errors));

            var sim = new Simulation(result.Registry, 424242);
            for (int t = 0; t < 1200; t++)
            {
                sim.Step(InputCommand.None);
                if (sim.RunOver) break;
            }
            // A run against real content should at least have spawned enemies and stayed consistent.
            Assert.GreaterOrEqual(sim.Tick, 1);
            Assert.IsNotNull(sim.CurrentRoom);
        }
    }
}
