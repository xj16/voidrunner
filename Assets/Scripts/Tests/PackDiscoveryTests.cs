using System.Collections.Generic;
using NUnit.Framework;
using VoidRunner.Modding;

namespace VoidRunner.Tests
{
    public sealed class PackDiscoveryTests
    {
        private static PackDiscovery.DiscoveredPack Pack(string id, params string[] deps)
        {
            var m = new VoidRunner.Data.ContentPackManifest { id = id };
            m.dependencies.AddRange(deps);
            return new PackDiscovery.DiscoveredPack { Manifest = m, Enabled = true, FolderPath = id };
        }

        [Test]
        public void ManifestParses()
        {
            var m = PackDiscovery.ReadManifest(@"{
              ""id"": ""cool-mod"", ""name"": ""Cool Mod"", ""author"": ""x"", ""version"": ""1.2.3"",
              ""dependencies"": [ ""base"" ], ""files"": [ ""content.json"" ]
            }");
            Assert.IsNotNull(m);
            Assert.AreEqual("cool-mod", m.id);
            Assert.AreEqual("1.2.3", m.version);
            CollectionAssert.Contains(m.dependencies, "base");
            CollectionAssert.Contains(m.files, "content.json");
        }

        [Test]
        public void InvalidManifest_ReturnsNull()
        {
            Assert.IsNull(PackDiscovery.ReadManifest("{ not json"));
            Assert.IsNull(PackDiscovery.ReadManifest("[]"));
        }

        [Test]
        public void ResolveLoadOrder_PutsDependenciesFirst()
        {
            var packs = new List<PackDiscovery.DiscoveredPack>
            {
                Pack("mod", "base"),
                Pack("base"),
            };
            var errors = new List<string>();
            var order = PackDiscovery.ResolveLoadOrder(packs, errors);

            CollectionAssert.IsEmpty(errors);
            Assert.AreEqual("base", order[0].Manifest.id);
            Assert.AreEqual("mod", order[1].Manifest.id);
        }

        [Test]
        public void ResolveLoadOrder_TransitiveDependencies()
        {
            var packs = new List<PackDiscovery.DiscoveredPack>
            {
                Pack("c", "b"),
                Pack("b", "a"),
                Pack("a"),
            };
            var errors = new List<string>();
            var order = PackDiscovery.ResolveLoadOrder(packs, errors);

            CollectionAssert.IsEmpty(errors);
            var ids = order.ConvertAll(p => p.Manifest.id);
            Assert.Less(ids.IndexOf("a"), ids.IndexOf("b"));
            Assert.Less(ids.IndexOf("b"), ids.IndexOf("c"));
        }

        [Test]
        public void MissingDependency_IsReported()
        {
            var packs = new List<PackDiscovery.DiscoveredPack> { Pack("mod", "ghost") };
            var errors = new List<string>();
            PackDiscovery.ResolveLoadOrder(packs, errors);
            CollectionAssert.IsNotEmpty(errors);
            StringAssert.Contains("ghost", errors[0]);
        }

        [Test]
        public void DependencyCycle_IsReported()
        {
            var packs = new List<PackDiscovery.DiscoveredPack>
            {
                Pack("x", "y"),
                Pack("y", "x"),
            };
            var errors = new List<string>();
            PackDiscovery.ResolveLoadOrder(packs, errors);
            CollectionAssert.IsNotEmpty(errors);
            StringAssert.Contains("cycle", errors[0].ToLowerInvariant());
        }
    }
}
