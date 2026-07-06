using VoidRunner.Content;

namespace VoidRunner.Tests
{
    /// <summary>Shared content fixtures used across the simulation/replay tests.</summary>
    internal static class TestContent
    {
        /// <summary>A minimal but complete, self-consistent content set as a single JSON file.</summary>
        public const string ValidJson = @"
{
  ""enemies"": [
    { ""id"": ""grunt"", ""displayName"": ""Grunt"", ""maxHealth"": 8, ""moveSpeed"": 2.5, ""contactDamage"": 6, ""radius"": 0.4, ""behaviour"": ""chase"", ""scoreValue"": 10, ""dropChance"": 0.2 },
    { ""id"": ""darter"", ""displayName"": ""Darter"", ""maxHealth"": 5, ""moveSpeed"": 3.5, ""contactDamage"": 4, ""radius"": 0.3, ""behaviour"": ""charger"", ""scoreValue"": 15, ""dropChance"": 0.1 },
    { ""id"": ""ghost"", ""displayName"": ""Ghost"", ""maxHealth"": 12, ""moveSpeed"": 1.8, ""contactDamage"": 8, ""radius"": 0.5, ""behaviour"": ""kite"", ""scoreValue"": 20, ""dropChance"": 0.25 }
  ],
  ""weapons"": [
    { ""id"": ""blaster"", ""displayName"": ""Blaster"", ""damage"": 4, ""fireRate"": 5, ""projectileSpeed"": 14, ""projectileLifetime"": 1.0, ""rarityWeight"": 5 },
    { ""id"": ""scatter"", ""displayName"": ""Scatter"", ""damage"": 2, ""fireRate"": 2, ""projectilesPerShot"": 5, ""spreadDegrees"": 40, ""rarityWeight"": 2 }
  ],
  ""waves"": [
    { ""id"": ""w_grunts"", ""groups"": [ { ""enemyId"": ""grunt"", ""count"": 4, ""delay"": 0 } ] },
    { ""id"": ""w_mixed"", ""groups"": [ { ""enemyId"": ""darter"", ""count"": 2, ""delay"": 0 }, { ""enemyId"": ""ghost"", ""count"": 1, ""delay"": 1 } ] }
  ],
  ""rooms"": [
    { ""id"": ""arena"", ""displayName"": ""Arena"", ""width"": 24, ""height"": 14, ""waveIds"": [ ""w_grunts"", ""w_mixed"" ], ""weight"": 1 }
  ]
}";

        public static ContentRegistry BuildValidRegistry()
        {
            var result = ContentLoader.LoadFiles(new[] { ("test", ValidJson) });
            Assert(result.Ok, "test content should load cleanly");
            return result.Registry;
        }

        private static void Assert(bool cond, string msg)
        {
            if (!cond) throw new System.Exception("TestContent fixture invalid: " + msg);
        }
    }
}
