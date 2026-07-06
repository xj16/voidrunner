using System.Collections.Generic;
using System.IO;
using VoidRunner.Content;
using VoidRunner.Data;

namespace VoidRunner.Modding
{
    /// <summary>
    /// Discovers content packs on disk and loads them in dependency order into a single registry.
    ///
    /// A pack is a folder containing a <c>pack.json</c> manifest plus one or more content JSON
    /// files it lists. Packs live under <c>StreamingAssets/ContentPacks/</c> in a build, so players
    /// can drop a new folder there and it appears in the game with zero recompilation — the core
    /// promise of VoidRunner's data-driven design.
    ///
    /// This class is pure .NET (System.IO only) so its ordering/merge logic is unit-testable without
    /// the Unity player.
    /// </summary>
    public static class PackDiscovery
    {
        public sealed class DiscoveredPack
        {
            public ContentPackManifest Manifest;
            public string FolderPath;
            public bool Enabled = true;
        }

        /// <summary>Scans a root directory and returns every pack that has a valid manifest.</summary>
        public static List<DiscoveredPack> Scan(string contentPacksRoot)
        {
            var packs = new List<DiscoveredPack>();
            if (!Directory.Exists(contentPacksRoot)) return packs;

            foreach (var dir in Directory.GetDirectories(contentPacksRoot))
            {
                string manifestPath = Path.Combine(dir, "pack.json");
                if (!File.Exists(manifestPath)) continue;

                var manifest = ReadManifest(File.ReadAllText(manifestPath));
                if (manifest == null || string.IsNullOrEmpty(manifest.id)) continue;

                packs.Add(new DiscoveredPack { Manifest = manifest, FolderPath = dir });
            }

            return packs;
        }

        /// <summary>Parses a manifest JSON string into a <see cref="ContentPackManifest"/>.</summary>
        public static ContentPackManifest ReadManifest(string json)
        {
            JsonValue root;
            try { root = Json.Parse(json); }
            catch (JsonParseException) { return null; }

            if (root == null || !root.IsObject) return null;

            var m = new ContentPackManifest
            {
                id = root.GetString("id"),
                name = root.GetString("name", root.GetString("id")),
                author = root.GetString("author", "unknown"),
                version = root.GetString("version", "1.0.0"),
                description = root.GetString("description", ""),
                format = root.GetInt("format", 1)
            };

            var deps = root["dependencies"];
            if (deps != null && deps.IsArray)
                foreach (var d in deps.AsArray)
                    if (d.Type == JsonType.String) m.dependencies.Add(d.AsString);

            var files = root["files"];
            if (files != null && files.IsArray)
                foreach (var f in files.AsArray)
                    if (f.Type == JsonType.String) m.files.Add(f.AsString);

            return m;
        }

        /// <summary>
        /// Orders enabled packs so every dependency comes before the pack that needs it
        /// (topological sort). Cycles and missing dependencies are reported as errors rather than
        /// throwing, so one broken mod does not take down the whole game.
        /// </summary>
        public static List<DiscoveredPack> ResolveLoadOrder(
            IReadOnlyList<DiscoveredPack> packs, List<string> errors)
        {
            var byId = new Dictionary<string, DiscoveredPack>();
            foreach (var p in packs)
            {
                if (!p.Enabled) continue;
                byId[p.Manifest.id] = p;
            }

            var ordered = new List<DiscoveredPack>();
            var state = new Dictionary<string, int>(); // 0=unvisited,1=visiting,2=done

            void Visit(DiscoveredPack p)
            {
                if (p == null) return;
                state.TryGetValue(p.Manifest.id, out int s);
                if (s == 2) return;
                if (s == 1)
                {
                    errors.Add($"dependency cycle involving pack '{p.Manifest.id}'");
                    return;
                }

                state[p.Manifest.id] = 1;
                foreach (var dep in p.Manifest.dependencies)
                {
                    if (byId.TryGetValue(dep, out var depPack))
                    {
                        Visit(depPack);
                    }
                    else
                    {
                        errors.Add($"pack '{p.Manifest.id}' depends on missing pack '{dep}'");
                    }
                }
                state[p.Manifest.id] = 2;
                ordered.Add(p);
            }

            foreach (var p in packs)
            {
                if (p.Enabled) Visit(p);
            }

            return ordered;
        }

        /// <summary>
        /// Loads the given ordered packs into a single content result. Reads each pack's listed
        /// files from disk. Uses <see cref="ContentLoader"/> for parsing/validation.
        /// </summary>
        public static ContentLoadResult LoadPacks(IReadOnlyList<DiscoveredPack> orderedPacks)
        {
            var files = new List<(string source, string json)>();
            foreach (var pack in orderedPacks)
            {
                foreach (var rel in pack.Manifest.files)
                {
                    string full = Path.Combine(pack.FolderPath, rel);
                    if (!File.Exists(full)) continue;
                    files.Add(($"{pack.Manifest.id}/{rel}", File.ReadAllText(full)));
                }
            }
            return ContentLoader.LoadFiles(files);
        }

        /// <summary>Full pipeline: scan → order → load. The single call the game uses at startup.</summary>
        public static ContentLoadResult DiscoverAndLoad(string contentPacksRoot, out List<DiscoveredPack> loadOrder)
        {
            var scanned = Scan(contentPacksRoot);
            var orderErrors = new List<string>();
            loadOrder = ResolveLoadOrder(scanned, orderErrors);

            var result = LoadPacks(loadOrder);
            result.Errors.InsertRange(0, orderErrors);
            return result;
        }
    }
}
