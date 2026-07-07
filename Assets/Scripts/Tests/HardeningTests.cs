using System;
using System.Text;
using NUnit.Framework;
using VoidRunner.Content;
using VoidRunner.Core;
using VoidRunner.Replay;
using VoidRunner.Rng;

namespace VoidRunner.Tests
{
    /// <summary>
    /// Adversarial tests: content packs and .vrplay files are shared between strangers, so the loader,
    /// the JSON parser and the replay codec must treat every input as hostile. These assert the parser
    /// never blows the stack, the loader never throws (it reports errors), non-finite content is
    /// refused, and a maliciously large replay can't make the verifier OOM or hang.
    /// </summary>
    public sealed class HardeningTests
    {
        // ---- JSON parser ----

        [Test]
        public void DeeplyNestedJson_IsRefused_NotStackOverflow()
        {
            var sb = new StringBuilder();
            for (int i = 0; i < 100_000; i++) sb.Append('[');
            // A pathological nesting bomb must throw a clean parse error, never crash the process.
            Assert.Throws<JsonParseException>(() => Json.Parse(sb.ToString()));
        }

        [Test]
        public void LegalNestingUpToLimit_StillParses()
        {
            // A modest, legal depth must still work.
            var sb = new StringBuilder();
            int depth = 20;
            for (int i = 0; i < depth; i++) sb.Append('[');
            sb.Append('1');
            for (int i = 0; i < depth; i++) sb.Append(']');
            Assert.DoesNotThrow(() => Json.Parse(sb.ToString()));
        }

        [Test]
        public void FuzzedJson_NeverCrashesTheParser()
        {
            var rng = new DeterministicRandom(0xF0F0);
            const string alphabet = "{}[]\":,0123456789.-+eEtruefalsn \t\n\\/x";
            for (int i = 0; i < 5000; i++)
            {
                int len = rng.Range(0, 120);
                var sb = new StringBuilder(len);
                for (int k = 0; k < len; k++) sb.Append(alphabet[rng.Range(0, alphabet.Length)]);
                // Either it parses or it throws JsonParseException — never any other exception.
                try { Json.Parse(sb.ToString()); }
                catch (JsonParseException) { /* expected for malformed input */ }
            }
            Assert.Pass("parser survived 5000 random inputs without an unexpected exception");
        }

        // ---- Content loader ----

        [Test]
        public void NaNContent_IsRefused()
        {
            // JSON has no NaN literal, so a hostile pack would smuggle it via a huge exponent that
            // overflows to Infinity. Assert such values are caught by the loader as errors.
            const string json = @"{ ""enemies"": [ { ""id"": ""x"", ""maxHealth"": 1e400, ""moveSpeed"": 2, ""contactDamage"": 1, ""radius"": 0.4, ""dropChance"": 0.1 } ],
                                    ""weapons"": [ { ""id"": ""w"", ""rarityWeight"": 1 } ],
                                    ""rooms"": [ { ""id"": ""r"", ""width"": 20, ""height"": 12, ""waveIds"": [] } ] }";
            var res = ContentLoader.LoadFiles(new[] { ("hostile", json) });
            Assert.IsFalse(res.Ok, "content with a non-finite number must be refused");
        }

        [Test]
        public void AbsurdlyLargeNumbers_AreRefused()
        {
            const string json = @"{ ""enemies"": [ { ""id"": ""x"", ""maxHealth"": 1, ""moveSpeed"": 999999999, ""contactDamage"": 1, ""radius"": 0.4, ""dropChance"": 0.1, ""behaviour"": ""chase"" } ],
                                    ""weapons"": [ { ""id"": ""w"", ""rarityWeight"": 1 } ],
                                    ""rooms"": [ { ""id"": ""r"", ""width"": 20, ""height"": 12, ""waveIds"": [] } ] }";
            var res = ContentLoader.LoadFiles(new[] { ("hostile", json) });
            Assert.IsFalse(res.Ok, "an out-of-range magnitude must be refused");
        }

        [Test]
        public void LoaderNeverThrows_OnRandomGarbage()
        {
            var rng = new DeterministicRandom(0xABCD);
            for (int i = 0; i < 2000; i++)
            {
                int len = rng.Range(0, 200);
                var sb = new StringBuilder(len);
                for (int k = 0; k < len; k++) sb.Append((char)rng.Range(32, 127));
                // The loader is expected to REPORT errors, not throw them.
                Assert.DoesNotThrow(() => ContentLoader.LoadFiles(new[] { ("garbage", sb.ToString()) }),
                    $"loader threw on input #{i}");
            }
        }

        [Test]
        public void HugeProjectilesPerShot_IsCapped()
        {
            const string json = @"{ ""enemies"": [ { ""id"": ""x"", ""maxHealth"": 5, ""behaviour"": ""chase"" } ],
                                    ""weapons"": [ { ""id"": ""w"", ""projectilesPerShot"": 100000, ""fireRate"": 5, ""rarityWeight"": 1 } ],
                                    ""waves"": [ { ""id"": ""wv"", ""groups"": [ { ""enemyId"": ""x"", ""count"": 1 } ] } ],
                                    ""rooms"": [ { ""id"": ""r"", ""width"": 20, ""height"": 12, ""waveIds"": [ ""wv"" ] } ] }";
            var res = ContentLoader.LoadFiles(new[] { ("cap", json) });
            Assert.IsTrue(res.Ok, "capping should keep the pack usable, not reject it");
            var w = res.Registry.GetWeapon("w");
            Assert.LessOrEqual(w.projectilesPerShot, 256, "projectilesPerShot must be capped");
        }

        // ---- Replay codec ----

        [Test]
        public void ReplayWithTooManyTicks_IsRefused_NotOom()
        {
            // Craft a replay whose RLE claims billions of ticks from a tiny payload: a single record
            // with run=65535 repeated many times would exceed MaxTicks. Build the blob directly.
            var bytes = new System.Collections.Generic.List<byte>();
            // Each record: run(2 bytes)=65535, then 5 payload bytes. Enough records to blow the cap.
            int records = (ReplayCodec.MaxTicks / 65535) + 10;
            for (int i = 0; i < records; i++)
            {
                bytes.Add(0xFF); bytes.Add(0xFF); // run = 65535
                bytes.Add(0); bytes.Add(0); bytes.Add(0); bytes.Add(0); bytes.Add(0);
            }
            string blob = Convert.ToBase64String(bytes.ToArray());
            string replay = "{\"magic\":\"VRPLAY\",\"version\":1,\"seed\":\"1\",\"contentFingerprint\":\"1\"," +
                            "\"label\":\"\",\"recordedAt\":0,\"tickCount\":0,\"finalScore\":0,\"finalRoom\":0," +
                            "\"finalStateHash\":\"0\",\"inputs\":\"" + blob + "\"}";

            Assert.Throws<FormatException>(() => ReplayCodec.Deserialize(replay),
                "a replay that expands past the tick cap must be refused before allocating");
        }

        [Test]
        public void GarbageReplay_ThrowsFormatException_NotSomethingElse()
        {
            Assert.Throws<FormatException>(() => ReplayCodec.Deserialize("not json at all"));
            Assert.Throws<FormatException>(() => ReplayCodec.Deserialize("{\"magic\":\"VRPLAY\",\"version\":999}"));
        }
    }
}
