using NUnit.Framework;
using VoidRunner.Content;
using VoidRunner.Data;

namespace VoidRunner.Tests
{
    public sealed class ContentLoaderTests
    {
        [Test]
        public void LoadsValidContent()
        {
            var result = ContentLoader.LoadFiles(new[] { ("base", TestContent.ValidJson) });
            Assert.IsTrue(result.Ok, string.Join("\n", result.Errors));
            Assert.AreEqual(3, result.Registry.EnemyCount);
            Assert.AreEqual(2, result.Registry.WeaponCount);
            Assert.AreEqual(1, result.Registry.RoomCount);
            Assert.AreEqual(2, result.Registry.WaveCount);
        }

        [Test]
        public void LaterPackOverridesEarlierById()
        {
            const string overridePack = @"{ ""enemies"": [ { ""id"": ""grunt"", ""maxHealth"": 999 } ] }";
            var result = ContentLoader.LoadFiles(new[]
            {
                ("base", TestContent.ValidJson),
                ("mod", overridePack)
            });
            Assert.IsTrue(result.Ok, string.Join("\n", result.Errors));
            Assert.AreEqual(999f, result.Registry.GetEnemy("grunt").maxHealth);
            // Count unchanged: override, not add.
            Assert.AreEqual(3, result.Registry.EnemyCount);
        }

        [Test]
        public void UnknownWaveEnemyReference_IsError()
        {
            const string bad = @"{
              ""enemies"": [ { ""id"": ""a"" } ],
              ""weapons"": [ { ""id"": ""w"" } ],
              ""waves"": [ { ""id"": ""wv"", ""groups"": [ { ""enemyId"": ""nope"", ""count"": 1 } ] } ],
              ""rooms"": [ { ""id"": ""r"", ""waveIds"": [ ""wv"" ] } ]
            }";
            var result = ContentLoader.LoadFiles(new[] { ("bad", bad) });
            Assert.IsFalse(result.Ok);
            CollectionAssert.IsNotEmpty(result.Errors);
        }

        [Test]
        public void RoomReferencingUnknownWave_IsError()
        {
            const string bad = @"{
              ""enemies"": [ { ""id"": ""a"" } ],
              ""weapons"": [ { ""id"": ""w"" } ],
              ""rooms"": [ { ""id"": ""r"", ""waveIds"": [ ""ghostwave"" ] } ]
            }";
            var result = ContentLoader.LoadFiles(new[] { ("bad", bad) });
            Assert.IsFalse(result.Ok);
        }

        [Test]
        public void MissingId_IsError()
        {
            const string bad = @"{ ""enemies"": [ { ""maxHealth"": 5 } ] }";
            var result = ContentLoader.LoadFiles(new[] { ("bad", bad) });
            Assert.IsFalse(result.Ok);
        }

        [Test]
        public void EmptyContent_IsError()
        {
            var result = ContentLoader.LoadFiles(new[] { ("empty", "{}") });
            Assert.IsFalse(result.Ok, "a pack with no enemies/weapons/rooms must be rejected");
        }

        [Test]
        public void NonPositiveMaxHealth_WarnsAndClamps()
        {
            const string json = @"{
              ""enemies"": [ { ""id"": ""a"", ""maxHealth"": -3 } ],
              ""weapons"": [ { ""id"": ""w"" } ],
              ""rooms"": [ { ""id"": ""r"" } ]
            }";
            var result = ContentLoader.LoadFiles(new[] { ("json", json) });
            Assert.IsTrue(result.Ok, string.Join("\n", result.Errors));
            Assert.AreEqual(1f, result.Registry.GetEnemy("a").maxHealth);
            CollectionAssert.IsNotEmpty(result.Warnings);
        }

        [Test]
        public void BehaviourParsing_MapsAllKnownValues()
        {
            Assert.AreEqual(AiBehaviour.Chase, ContentLoader.ParseBehaviour("chase"));
            Assert.AreEqual(AiBehaviour.Kite, ContentLoader.ParseBehaviour("KITE"));
            Assert.AreEqual(AiBehaviour.Wander, ContentLoader.ParseBehaviour("wander"));
            Assert.AreEqual(AiBehaviour.Charger, ContentLoader.ParseBehaviour("charger"));
            Assert.AreEqual(AiBehaviour.Chase, ContentLoader.ParseBehaviour("garbage"));
        }

        [Test]
        public void SyntaxError_ReportsSourceName()
        {
            var result = ContentLoader.LoadFiles(new[] { ("mypack/enemies.json", "{ bad json") });
            Assert.IsFalse(result.Ok);
            StringAssert.Contains("mypack/enemies.json", result.Errors[0]);
        }

        [Test]
        public void Fingerprint_ChangesWithBalance_StableWithCosmetics()
        {
            var baseReg = ContentLoader.LoadFiles(new[] { ("b", TestContent.ValidJson) }).Registry;
            ulong baseFp = ContentFingerprint.Compute(baseReg);

            // Cosmetic-only change (displayName/tint) must NOT change the fingerprint.
            const string cosmetic = @"{ ""enemies"": [ { ""id"": ""grunt"", ""displayName"": ""Renamed"", ""tint"": ""#FF0000"", ""maxHealth"": 8, ""moveSpeed"": 2.5, ""contactDamage"": 6, ""radius"": 0.4, ""behaviour"": ""chase"", ""scoreValue"": 10, ""dropChance"": 0.2 } ] }";
            var cosmeticReg = ContentLoader.LoadFiles(new[] { ("b", TestContent.ValidJson), ("c", cosmetic) }).Registry;
            Assert.AreEqual(baseFp, ContentFingerprint.Compute(cosmeticReg), "cosmetic edits must not change fingerprint");

            // Balance change (maxHealth) MUST change the fingerprint.
            const string balance = @"{ ""enemies"": [ { ""id"": ""grunt"", ""maxHealth"": 50, ""moveSpeed"": 2.5, ""contactDamage"": 6, ""radius"": 0.4, ""behaviour"": ""chase"", ""scoreValue"": 10, ""dropChance"": 0.2 } ] }";
            var balanceReg = ContentLoader.LoadFiles(new[] { ("b", TestContent.ValidJson), ("m", balance) }).Registry;
            Assert.AreNotEqual(baseFp, ContentFingerprint.Compute(balanceReg), "balance edits must change fingerprint");
        }
    }
}
