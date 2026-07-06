using System.Collections.Generic;
using System.IO;
using UnityEngine;
using VoidRunner.Content;
using VoidRunner.Modding;

namespace VoidRunner.Gameplay
{
    /// <summary>
    /// Unity-side entry point for loading content packs at runtime. Wraps the engine-agnostic
    /// <see cref="PackDiscovery"/> with the correct StreamingAssets path and logs the outcome.
    ///
    /// Packs live in <c>Application.streamingAssetsPath/ContentPacks</c>. On desktop/editor builds
    /// this is a normal directory that players can add folders to. (WebGL, where StreamingAssets is
    /// served over HTTP and can't be enumerated, would use a shipped manifest list instead — out of
    /// scope for this reference build, which targets desktop.)
    /// </summary>
    public static class RuntimeContent
    {
        public static string ContentPacksRoot =>
            Path.Combine(Application.streamingAssetsPath, "ContentPacks");

        public struct LoadOutcome
        {
            public ContentRegistry Registry;
            public ulong Fingerprint;
            public List<string> Warnings;
            public List<string> Errors;
            public List<PackDiscovery.DiscoveredPack> LoadOrder;
            public bool Ok;
        }

        public static LoadOutcome Load()
        {
            var result = PackDiscovery.DiscoverAndLoad(ContentPacksRoot, out var order);
            var outcome = new LoadOutcome
            {
                Registry = result.Registry,
                Warnings = result.Warnings,
                Errors = result.Errors,
                LoadOrder = order,
                Ok = result.Ok
            };

            if (result.Ok)
            {
                outcome.Fingerprint = ContentFingerprint.Compute(result.Registry);
                Debug.Log($"[VoidRunner] Loaded {order.Count} pack(s): " +
                          $"{result.Registry.EnemyCount} enemies, {result.Registry.WeaponCount} weapons, " +
                          $"{result.Registry.RoomCount} rooms. Fingerprint={outcome.Fingerprint}");
            }
            else
            {
                foreach (var e in result.Errors) Debug.LogError($"[VoidRunner] content error: {e}");
            }

            foreach (var w in result.Warnings) Debug.LogWarning($"[VoidRunner] content warning: {w}");
            return outcome;
        }
    }
}
