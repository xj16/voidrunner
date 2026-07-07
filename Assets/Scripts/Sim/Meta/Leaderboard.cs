using System.Collections.Generic;
using System.Globalization;
using System.Text;
using VoidRunner.Content;
using VoidRunner.Replay;

namespace VoidRunner.Meta
{
    /// <summary>
    /// A local, self-verifying high-score table.
    ///
    /// VoidRunner's pitch is "shareable, verifiable scores", but a raw number is trivially faked.
    /// Here every entry carries the <see cref="ReplayData"/> that produced it, so a score is only
    /// admitted if its replay actually reproduces that score under <see cref="ReplayVerifier"/>. The
    /// table is keyed by <c>(seed, contentFingerprint)</c>: scores are only comparable when they were
    /// played on the same world and the same content, which is exactly the fairness guarantee the
    /// deterministic core provides. This gives the "a tournament can verify a score" story a concrete,
    /// backend-free home — and the daily challenge a place to rank.
    ///
    /// Engine-agnostic: no UnityEngine, no direct file IO. The Unity layer persists the serialized
    /// string; tests drive it in-memory.
    /// </summary>
    public sealed class Leaderboard
    {
        public sealed class Entry
        {
            public string Name = "";
            public ulong Seed;
            public ulong ContentFingerprint;
            public string SeedLabel = "";
            public int Score;
            public int Room;
            public int TickCount;
            public long RecordedAtUnix;

            /// <summary>The serialized .vrplay that proves this score. Kept so the entry stays verifiable.</summary>
            public string ReplayText = "";
        }

        private readonly List<Entry> _entries = new List<Entry>();

        /// <summary>Maximum entries kept per (seed, fingerprint) board.</summary>
        public int MaxPerBoard { get; set; } = 10;

        public IReadOnlyList<Entry> All => _entries;

        /// <summary>
        /// Attempts to record a run. The replay is re-verified against the registry before being
        /// admitted, so a tampered or mismatched replay is rejected and never pollutes the board.
        /// Returns true if the entry was accepted (verified and within the per-board cap).
        /// </summary>
        public bool TrySubmit(ReplayData replay, ContentRegistry registry, ulong expectedFingerprint,
                              string name, out string reason)
        {
            reason = "";
            if (replay == null) { reason = "no replay"; return false; }

            var verify = ReplayVerifier.Verify(replay, registry, expectedFingerprint);
            if (!verify.Reproduced)
            {
                reason = "replay did not verify: " + verify.Message;
                return false;
            }

            var entry = new Entry
            {
                Name = Sanitize(name),
                Seed = replay.Seed,
                ContentFingerprint = replay.ContentFingerprint,
                SeedLabel = replay.Label ?? "",
                Score = replay.FinalScore,
                Room = replay.FinalRoom,
                TickCount = replay.TickCount,
                RecordedAtUnix = replay.RecordedAtUnix,
                ReplayText = ReplayCodec.Serialize(replay)
            };

            _entries.Add(entry);
            TrimBoard(entry.Seed, entry.ContentFingerprint);
            return true;
        }

        /// <summary>Returns the entries for one board (seed+fingerprint), best score first.</summary>
        public List<Entry> Board(ulong seed, ulong fingerprint)
        {
            var list = new List<Entry>();
            foreach (var e in _entries)
                if (e.Seed == seed && e.ContentFingerprint == fingerprint) list.Add(e);
            SortByScoreDesc(list);
            return list;
        }

        /// <summary>Best score on a board, or -1 if none.</summary>
        public int BestScore(ulong seed, ulong fingerprint)
        {
            int best = -1;
            foreach (var e in _entries)
                if (e.Seed == seed && e.ContentFingerprint == fingerprint && e.Score > best) best = e.Score;
            return best;
        }

        private void TrimBoard(ulong seed, ulong fingerprint)
        {
            var board = new List<Entry>();
            foreach (var e in _entries)
                if (e.Seed == seed && e.ContentFingerprint == fingerprint) board.Add(e);
            if (board.Count <= MaxPerBoard) return;

            SortByScoreDesc(board);
            for (int i = MaxPerBoard; i < board.Count; i++) _entries.Remove(board[i]);
        }

        private static void SortByScoreDesc(List<Entry> list)
        {
            list.Sort((a, b) =>
            {
                int c = b.Score.CompareTo(a.Score);
                if (c != 0) return c;
                c = b.Room.CompareTo(a.Room);
                if (c != 0) return c;
                return a.TickCount.CompareTo(b.TickCount); // fewer ticks (faster) wins ties
            });
        }

        private static string Sanitize(string name)
        {
            if (string.IsNullOrEmpty(name)) return "anon";
            name = name.Trim();
            if (name.Length > 16) name = name.Substring(0, 16);
            var sb = new StringBuilder(name.Length);
            foreach (char c in name)
                sb.Append(c < ' ' || c == '"' || c == '\\' ? '_' : c);
            return sb.Length == 0 ? "anon" : sb.ToString();
        }

        // ---- Serialization: a compact JSON array the Unity layer writes to disk. ----

        public string Serialize()
        {
            var sb = new StringBuilder();
            sb.Append("{\"version\":1,\"entries\":[");
            for (int i = 0; i < _entries.Count; i++)
            {
                if (i > 0) sb.Append(',');
                var e = _entries[i];
                sb.Append('{');
                sb.Append("\"name\":").Append(Json.Escape(e.Name)).Append(',');
                sb.Append("\"seed\":").Append(Json.Escape(e.Seed.ToString(CultureInfo.InvariantCulture))).Append(',');
                sb.Append("\"fingerprint\":").Append(Json.Escape(e.ContentFingerprint.ToString(CultureInfo.InvariantCulture))).Append(',');
                sb.Append("\"seedLabel\":").Append(Json.Escape(e.SeedLabel)).Append(',');
                sb.Append("\"score\":").Append(e.Score).Append(',');
                sb.Append("\"room\":").Append(e.Room).Append(',');
                sb.Append("\"ticks\":").Append(e.TickCount).Append(',');
                sb.Append("\"recordedAt\":").Append(e.RecordedAtUnix).Append(',');
                sb.Append("\"replay\":").Append(Json.Escape(e.ReplayText));
                sb.Append('}');
            }
            sb.Append("]}");
            return sb.ToString();
        }

        public static Leaderboard Deserialize(string text)
        {
            var lb = new Leaderboard();
            if (string.IsNullOrEmpty(text)) return lb;

            JsonValue root;
            try { root = Json.Parse(text); }
            catch (JsonParseException) { return lb; }
            if (root == null || !root.IsObject) return lb;

            var arr = root["entries"];
            if (arr == null || !arr.IsArray) return lb;

            foreach (var node in arr.AsArray)
            {
                if (!node.IsObject) continue;
                var e = new Entry
                {
                    Name = node.GetString("name", "anon"),
                    Seed = ParseULong(node.GetString("seed", "0")),
                    ContentFingerprint = ParseULong(node.GetString("fingerprint", "0")),
                    SeedLabel = node.GetString("seedLabel", ""),
                    Score = node.GetInt("score"),
                    Room = node.GetInt("room"),
                    TickCount = node.GetInt("ticks"),
                    RecordedAtUnix = (long)node.GetFloat("recordedAt"),
                    ReplayText = node.GetString("replay", "")
                };
                lb._entries.Add(e);
            }
            return lb;
        }

        private static ulong ParseULong(string s) =>
            ulong.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0UL;
    }
}
