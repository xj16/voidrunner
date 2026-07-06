using System.Collections.Generic;
using UnityEngine;

namespace VoidRunner.Gameplay
{
    /// <summary>
    /// Provides sprites for entities. Content packs reference a sprite by a key string; this
    /// factory resolves it in three tiers:
    ///   1. A PNG named "&lt;key&gt;.png" placed next to the pack (loaded by the pack layer) — this
    ///      is where Blender-exported art drops in.
    ///   2. A built-in procedural shape ("circle", "square", "triangle", "diamond", "bolt") so the
    ///      game is fully playable and visually clear with zero imported assets.
    ///   3. A magenta fallback square for an unknown key, so missing art is obvious but non-fatal.
    ///
    /// Generated sprites are cached and reused. All are drawn into small textures at runtime, so the
    /// repository ships as source-only with no binary art required to run.
    /// </summary>
    public sealed class SpriteFactory
    {
        private readonly Dictionary<string, Sprite> _cache = new Dictionary<string, Sprite>();
        private const int Size = 64;
        private const float PixelsPerUnit = 64f;

        /// <summary>Optional externally-supplied sprites (e.g. loaded Blender PNGs) keyed by name.</summary>
        private readonly Dictionary<string, Sprite> _external = new Dictionary<string, Sprite>();

        public void RegisterExternal(string key, Sprite sprite)
        {
            if (!string.IsNullOrEmpty(key) && sprite != null) _external[key] = sprite;
        }

        public Sprite Get(string key)
        {
            if (string.IsNullOrEmpty(key)) key = "circle";
            if (_external.TryGetValue(key, out var ext)) return ext;
            if (_cache.TryGetValue(key, out var cached)) return cached;

            Sprite s = Generate(key);
            _cache[key] = s;
            return s;
        }

        private Sprite Generate(string key)
        {
            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            var clear = new Color(0, 0, 0, 0);
            var px = new Color[Size * Size];
            for (int i = 0; i < px.Length; i++) px[i] = clear;

            switch (key)
            {
                case "circle": DrawCircle(px, Size * 0.42f); break;
                case "square": DrawRect(px, 0.72f); break;
                case "triangle": DrawTriangle(px); break;
                case "diamond": DrawDiamond(px); break;
                case "bolt": DrawBolt(px); break;
                case "ring": DrawRing(px, Size * 0.42f, Size * 0.28f); break;
                default: DrawUnknown(px); break;
            }

            tex.SetPixels(px);
            tex.Apply();

            return Sprite.Create(tex, new Rect(0, 0, Size, Size), new Vector2(0.5f, 0.5f), PixelsPerUnit);
        }

        private static int Idx(int x, int y) => y * Size + x;
        private static readonly Color White = Color.white;

        private static void DrawCircle(Color[] px, float radius)
        {
            float cx = Size * 0.5f, cy = Size * 0.5f;
            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                float dx = x + 0.5f - cx, dy = y + 0.5f - cy;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Clamp01(radius - d);
                if (a > 0) px[Idx(x, y)] = new Color(1, 1, 1, a);
            }
        }

        private static void DrawRing(Color[] px, float outer, float inner)
        {
            float cx = Size * 0.5f, cy = Size * 0.5f;
            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                float dx = x + 0.5f - cx, dy = y + 0.5f - cy;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float aOut = Mathf.Clamp01(outer - d);
                float aIn = Mathf.Clamp01(d - inner);
                float a = Mathf.Min(aOut, aIn);
                if (a > 0) px[Idx(x, y)] = new Color(1, 1, 1, a);
            }
        }

        private static void DrawRect(Color[] px, float fill)
        {
            int margin = Mathf.RoundToInt(Size * (1f - fill) * 0.5f);
            for (int y = margin; y < Size - margin; y++)
            for (int x = margin; x < Size - margin; x++)
                px[Idx(x, y)] = White;
        }

        private static void DrawTriangle(Color[] px)
        {
            for (int y = 0; y < Size; y++)
            {
                float t = y / (float)(Size - 1); // 0 bottom .. 1 top
                float halfWidth = (1f - t) * (Size * 0.5f);
                int cx = Size / 2;
                for (int x = 0; x < Size; x++)
                {
                    if (Mathf.Abs(x + 0.5f - cx) <= halfWidth) px[Idx(x, y)] = White;
                }
            }
        }

        private static void DrawDiamond(Color[] px)
        {
            float cx = Size * 0.5f, cy = Size * 0.5f, r = Size * 0.46f;
            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                float dx = Mathf.Abs(x + 0.5f - cx), dy = Mathf.Abs(y + 0.5f - cy);
                if (dx + dy <= r) px[Idx(x, y)] = White;
            }
        }

        private static void DrawBolt(Color[] px)
        {
            // A short thick capsule pointing +X, used for projectiles.
            float cy = Size * 0.5f;
            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                float dy = Mathf.Abs(y + 0.5f - cy);
                bool inBody = x > Size * 0.25f && x < Size * 0.85f && dy < Size * 0.14f;
                if (inBody) px[Idx(x, y)] = White;
            }
        }

        private static void DrawUnknown(Color[] px)
        {
            // Obvious magenta checker so missing sprites are visible but non-fatal.
            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                bool on = ((x / 8) + (y / 8)) % 2 == 0;
                px[Idx(x, y)] = on ? new Color(1f, 0f, 1f, 1f) : new Color(0.1f, 0.1f, 0.1f, 1f);
            }
        }
    }
}
