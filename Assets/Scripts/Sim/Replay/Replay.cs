using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using VoidRunner.Content;
using VoidRunner.Core;

namespace VoidRunner.Replay
{
    /// <summary>
    /// The shareable record of a run. Because the simulation is deterministic, a replay only needs
    /// the seed, the content-pack fingerprint and the ordered input stream — no snapshots of enemy
    /// positions. That keeps files tiny (a five-minute run is a few KB) and makes them trivially
    /// diffable and shareable as text.
    ///
    /// The <see cref="ContentFingerprint"/> lets a viewer detect "this replay was made with a
    /// different mod set" and refuse to play a mismatched one, instead of silently desyncing.
    /// </summary>
    public sealed class ReplayData
    {
        public const int FormatVersion = 1;

        public ulong Seed;

        /// <summary>Stable hash of the loaded content, so mismatched packs are detected.</summary>
        public ulong ContentFingerprint;

        /// <summary>Optional human label (e.g. seed text the player typed).</summary>
        public string Label = "";

        /// <summary>Unix seconds when recorded (informational only; not part of determinism).</summary>
        public long RecordedAtUnix;

        /// <summary>One input per simulation tick, in order.</summary>
        public readonly List<InputCommand> Inputs = new List<InputCommand>();

        /// <summary>Final score, stored for quick display without replaying. Verified on load.</summary>
        public int FinalScore;

        /// <summary>Final room reached.</summary>
        public int FinalRoom;

        /// <summary>State hash at the end of the run, used to verify a replay reproduces exactly.</summary>
        public ulong FinalStateHash;

        public int TickCount => Inputs.Count;
    }

    /// <summary>Records inputs while a run is played, then produces a <see cref="ReplayData"/>.</summary>
    public sealed class ReplayRecorder
    {
        private readonly ReplayData _data = new ReplayData();

        public ReplayRecorder(ulong seed, ulong contentFingerprint, string label = "")
        {
            _data.Seed = seed;
            _data.ContentFingerprint = contentFingerprint;
            _data.Label = label ?? "";
            _data.RecordedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        }

        public void Record(InputCommand cmd) => _data.Inputs.Add(cmd);

        /// <summary>Finalises the replay with results read from the finished simulation.</summary>
        public ReplayData Finish(Simulation sim)
        {
            _data.FinalScore = sim.Score;
            _data.FinalRoom = sim.RoomNumber;
            _data.FinalStateHash = sim.StateHash();
            return _data;
        }

        public ReplayData Data => _data;
    }

    /// <summary>
    /// Serialises/deserialises replays. The format is a small JSON header followed by a run-length
    /// encoded, base64 input blob — human-inspectable header, compact payload.
    /// </summary>
    public static class ReplayCodec
    {
        // Magic prefix so tooling can sniff the format.
        private const string Magic = "VRPLAY";

        /// <summary>
        /// Hard ceiling on how many ticks a decoded replay may contain. Replays are untrusted input
        /// that strangers share; run-length encoding means a few dozen bytes can claim billions of
        /// ticks (count is a uint16 per record, and a file can have many records), which would make
        /// the verifier allocate an enormous list and then simulate forever. We refuse anything above
        /// this bound. 216 000 ticks = one hour of real play at 60 Hz — far beyond any real run.
        /// </summary>
        public const int MaxTicks = 216_000;

        /// <summary>Maximum number of RLE records (7 bytes each) accepted in one replay blob.</summary>
        public const int MaxRecords = MaxTicks; // worst case: every record is a run of length 1

        public static string Serialize(ReplayData r)
        {
            var sb = new StringBuilder();
            sb.Append('{');
            sb.Append("\"magic\":").Append(Json.Escape(Magic)).Append(',');
            sb.Append("\"version\":").Append(ReplayData.FormatVersion).Append(',');
            // 64-bit values are written as JSON *strings*: a JSON number is an IEEE-754 double,
            // which cannot represent every ulong exactly (only 53 mantissa bits), so a raw numeric
            // seed/fingerprint/hash would lose its low bits on round-trip. Strings survive exactly.
            sb.Append("\"seed\":").Append(Json.Escape(r.Seed.ToString(CultureInfo.InvariantCulture))).Append(',');
            sb.Append("\"contentFingerprint\":").Append(Json.Escape(r.ContentFingerprint.ToString(CultureInfo.InvariantCulture))).Append(',');
            sb.Append("\"label\":").Append(Json.Escape(r.Label ?? "")).Append(',');
            sb.Append("\"recordedAt\":").Append(r.RecordedAtUnix.ToString(CultureInfo.InvariantCulture)).Append(',');
            sb.Append("\"tickCount\":").Append(r.TickCount).Append(',');
            sb.Append("\"finalScore\":").Append(r.FinalScore).Append(',');
            sb.Append("\"finalRoom\":").Append(r.FinalRoom).Append(',');
            sb.Append("\"finalStateHash\":").Append(Json.Escape(r.FinalStateHash.ToString(CultureInfo.InvariantCulture))).Append(',');
            sb.Append("\"inputs\":").Append(Json.Escape(EncodeInputs(r.Inputs)));
            sb.Append('}');
            return sb.ToString();
        }

        public static ReplayData Deserialize(string text)
        {
            JsonValue root;
            try
            {
                root = Json.Parse(text);
            }
            catch (JsonParseException ex)
            {
                // Present a single, uniform failure type to callers: whether the bytes are malformed
                // JSON or a structurally-wrong replay, it's all "this is not a valid replay file".
                throw new FormatException("replay is not valid JSON: " + ex.Message, ex);
            }
            if (root == null || !root.IsObject) throw new FormatException("replay is not a JSON object");

            string magic = root.GetString("magic");
            if (magic != Magic) throw new FormatException($"not a VoidRunner replay (magic='{magic}')");

            int version = root.GetInt("version");
            if (version != ReplayData.FormatVersion)
                throw new FormatException($"unsupported replay version {version} (expected {ReplayData.FormatVersion})");

            var r = new ReplayData
            {
                Seed = ParseULong(root, "seed"),
                ContentFingerprint = ParseULong(root, "contentFingerprint"),
                Label = root.GetString("label", ""),
                RecordedAtUnix = ParseLong(root, "recordedAt"),
                FinalScore = root.GetInt("finalScore"),
                FinalRoom = root.GetInt("finalRoom"),
                FinalStateHash = ParseULong(root, "finalStateHash")
            };

            string blob = root.GetString("inputs", "");
            DecodeInputs(blob, r.Inputs);
            return r;
        }

        // --- Input stream encoding: run-length pairs, packed into bytes, base64'd. ---
        // Each input is 5 bytes: moveX, moveY, aimLo, aimHi, firing. Consecutive identical inputs
        // are run-length encoded as (count:uint16, 5 bytes payload).

        private static string EncodeInputs(IReadOnlyList<InputCommand> inputs)
        {
            var bytes = new List<byte>(inputs.Count * 2);
            int i = 0;
            while (i < inputs.Count)
            {
                InputCommand cur = inputs[i];
                int run = 1;
                while (i + run < inputs.Count && run < ushort.MaxValue && inputs[i + run].Equals(cur))
                {
                    run++;
                }

                bytes.Add((byte)(run & 0xFF));
                bytes.Add((byte)((run >> 8) & 0xFF));
                bytes.Add(unchecked((byte)cur.MoveX));
                bytes.Add(unchecked((byte)cur.MoveY));
                bytes.Add((byte)(cur.AimDegrees & 0xFF));
                bytes.Add((byte)((cur.AimDegrees >> 8) & 0xFF));
                bytes.Add((byte)(cur.Firing ? 1 : 0));

                i += run;
            }
            return Convert.ToBase64String(bytes.ToArray());
        }

        private static void DecodeInputs(string blob, List<InputCommand> outInputs)
        {
            outInputs.Clear();
            if (string.IsNullOrEmpty(blob)) return;

            byte[] bytes = Convert.FromBase64String(blob);
            if (bytes.Length % 7 != 0) throw new FormatException("corrupt replay input stream (bad length)");

            int recordCount = bytes.Length / 7;
            if (recordCount > MaxRecords)
                throw new FormatException($"replay has too many records ({recordCount} > {MaxRecords})");

            // Bound the TOTAL expanded tick count as we go, so a maliciously large run field can't
            // make us allocate gigabytes before we notice. We check before appending each run.
            long total = 0;
            for (int p = 0; p < bytes.Length; p += 7)
            {
                int run = bytes[p] | (bytes[p + 1] << 8);
                total += run;
                if (total > MaxTicks)
                    throw new FormatException($"replay expands to too many ticks (> {MaxTicks}); refusing to allocate");

                var cmd = new InputCommand
                {
                    MoveX = unchecked((sbyte)bytes[p + 2]),
                    MoveY = unchecked((sbyte)bytes[p + 3]),
                    AimDegrees = (short)(bytes[p + 4] | (bytes[p + 5] << 8)),
                    Firing = bytes[p + 6] != 0
                };
                for (int k = 0; k < run; k++) outInputs.Add(cmd);
            }
        }

        private static ulong ParseULong(JsonValue root, string key)
        {
            var v = root[key];
            if (v == null) return 0UL;
            // Numbers may lose precision as doubles for very large ulongs; accept string form too.
            if (v.Type == JsonType.String && ulong.TryParse(v.AsString, NumberStyles.Integer, CultureInfo.InvariantCulture, out var us))
                return us;
            return (ulong)Math.Max(0.0, v.AsNumber);
        }

        private static long ParseLong(JsonValue root, string key)
        {
            var v = root[key];
            if (v == null) return 0L;
            if (v.Type == JsonType.String && long.TryParse(v.AsString, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ls))
                return ls;
            return (long)v.AsNumber;
        }
    }

    /// <summary>
    /// Re-runs a replay headlessly through a fresh <see cref="Simulation"/> and reports whether it
    /// reproduces the recorded outcome bit-for-bit. This is the determinism guarantee made testable.
    /// </summary>
    public static class ReplayVerifier
    {
        public struct VerifyResult
        {
            public bool Reproduced;
            public int ReplayedScore;
            public int ReplayedRoom;
            public ulong ReplayedStateHash;
            public string Message;
        }

        public static VerifyResult Verify(ReplayData replay, ContentRegistry registry, ulong expectedContentFingerprint)
        {
            var result = new VerifyResult();

            if (replay.ContentFingerprint != expectedContentFingerprint)
            {
                result.Reproduced = false;
                result.Message = "content fingerprint mismatch: replay was recorded with a different content-pack set";
                return result;
            }

            var sim = new Simulation(registry, replay.Seed);
            foreach (var cmd in replay.Inputs)
            {
                sim.Step(cmd);
                if (sim.RunOver) break;
            }

            result.ReplayedScore = sim.Score;
            result.ReplayedRoom = sim.RoomNumber;
            result.ReplayedStateHash = sim.StateHash();
            result.Reproduced = sim.StateHash() == replay.FinalStateHash
                                && sim.Score == replay.FinalScore
                                && sim.RoomNumber == replay.FinalRoom;
            result.Message = result.Reproduced
                ? "replay reproduced exactly"
                : $"desync: expected hash {replay.FinalStateHash}/score {replay.FinalScore}, got {result.ReplayedStateHash}/score {result.ReplayedScore}";
            return result;
        }
    }
}
