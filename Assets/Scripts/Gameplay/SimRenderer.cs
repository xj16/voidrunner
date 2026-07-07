using System.Collections.Generic;
using UnityEngine;
using VoidRunner.Content;
using VoidRunner.Core;

namespace VoidRunner.Gameplay
{
    /// <summary>
    /// Draws the current <see cref="Simulation"/> state each frame using pooled SpriteRenderers.
    ///
    /// The renderer is a pure "view" — it never mutates the simulation. The sim advances on a fixed
    /// 60 Hz step while the display may run at 120+ Hz, so to avoid visible stutter the renderer
    /// INTERPOLATES: it remembers each entity's position from the previous tick and blends toward the
    /// current one by the fractional-tick <c>interpolation</c> factor the game loop passes in
    /// (accumulator / fixedDelta ∈ [0,1)). This is view-only smoothing — the deterministic world is
    /// untouched, so replays and verification are unaffected.
    ///
    /// It also adds cheap, view-only "juice" that likewise never touches the sim: a hit-flash and a
    /// short deterministic-free screenshake driven off the player's i-frames and enemy deaths, plus
    /// obstacles are drawn so the now-solid room cover is visible.
    /// </summary>
    public sealed class SimRenderer
    {
        private readonly Transform _root;
        private readonly SpriteFactory _sprites;
        private readonly ContentRegistry _registry;

        private readonly List<SpriteRenderer> _enemyPool = new List<SpriteRenderer>();
        private readonly List<SpriteRenderer> _projPool = new List<SpriteRenderer>();
        private readonly List<SpriteRenderer> _pickupPool = new List<SpriteRenderer>();
        private readonly List<SpriteRenderer> _obstaclePool = new List<SpriteRenderer>();
        private SpriteRenderer _player;
        private SpriteRenderer _floor;

        // --- Interpolation state: previous-tick positions, keyed by entity slot index. ---
        private Vec2 _playerPrev;
        private bool _hasPlayerPrev;
        private readonly Vec2[] _enemyPrev = new Vec2[Simulation.MaxEnemies];
        private readonly bool[] _enemyPrevValid = new bool[Simulation.MaxEnemies];
        private readonly Vec2[] _projPrev = new Vec2[Simulation.MaxProjectiles];
        private readonly bool[] _projPrevValid = new bool[Simulation.MaxProjectiles];
        private int _lastTick = -1;

        // View-only screenshake magnitude, decays each frame.
        private float _shake;

        public SimRenderer(Transform root, SpriteFactory sprites, ContentRegistry registry)
        {
            _root = root;
            _sprites = sprites;
            _registry = registry;
        }

        public void EnsurePlayer()
        {
            if (_player == null)
            {
                _player = MakeRenderer("Player", 10);
                _player.sprite = _sprites.Get("triangle");
                _player.color = new Color(0.4f, 0.9f, 1f);
                _player.transform.localScale = Vector3.one * 0.7f;
            }
        }

        public void DrawFloor(RoomDef room)
        {
            if (room == null) return;
            if (_floor == null)
            {
                _floor = MakeRenderer("Floor", -100);
                _floor.sprite = _sprites.Get("square");
            }
            _floor.color = ColorUtil.Parse(room.backgroundTint, new Color(0.04f, 0.05f, 0.1f));
            _floor.transform.localScale = new Vector3(room.width, room.height, 1f);
            _floor.transform.position = Vector3.zero;

            DrawObstacles(room);
        }

        /// <summary>Draws the room's solid obstacle blocks so the (now-simulated) cover is visible.</summary>
        private void DrawObstacles(RoomDef room)
        {
            int used = 0;
            if (room.obstacles != null)
            {
                var wall = ColorUtil.Parse(room.backgroundTint, new Color(0.04f, 0.05f, 0.1f));
                // Obstacles are a lighter shade of the floor so they read as raised blocks.
                var obColor = new Color(Mathf.Min(1f, wall.r + 0.10f), Mathf.Min(1f, wall.g + 0.11f),
                                        Mathf.Min(1f, wall.b + 0.16f), 1f);
                for (int i = 0; i < room.obstacles.Count; i++)
                {
                    var o = room.obstacles[i];
                    var sr = GetPooled(_obstaclePool, used, "Obstacle", -50);
                    sr.sprite = _sprites.Get("square");
                    sr.color = obColor;
                    sr.transform.position = new Vector3(o.x, o.y, 0f);
                    sr.transform.localScale = new Vector3(o.width, o.height, 1f);
                    sr.enabled = true;
                    used++;
                }
            }
            HideFrom(_obstaclePool, used);
        }

        public void Render(Simulation sim, float interpolation)
        {
            interpolation = Mathf.Clamp01(interpolation);

            // When the sim advances a tick, roll "current" into "previous" for smooth interpolation.
            bool tickAdvanced = sim.Tick != _lastTick;
            if (tickAdvanced) CapturePrevious(sim);
            _lastTick = sim.Tick;

            EnsurePlayer();

            Vec2 pp = _hasPlayerPrev ? Lerp(_playerPrev, sim.Player.Position, interpolation) : sim.Player.Position;
            _player.transform.position = ToV3(pp);

            // Flash while invulnerable (hit-flash juice), view-only.
            float a = sim.Player.InvulnTimer > 0f ? (Mathf.PingPong(Time.time * 12f, 1f) * 0.6f + 0.4f) : 1f;
            var pc = _player.color; pc.a = a; _player.color = pc;
            if (sim.Player.InvulnTimer > 0f) _shake = Mathf.Max(_shake, 0.18f);

            RenderEnemies(sim, interpolation);
            RenderProjectiles(sim, interpolation);
            RenderPickups(sim);

            _shake *= 0.85f; // decay
        }

        /// <summary>Current screenshake offset for the camera to apply. Non-deterministic, view-only.</summary>
        public Vector3 ShakeOffset()
        {
            if (_shake < 0.002f) return Vector3.zero;
            float t = Time.time * 40f;
            return new Vector3(Mathf.Sin(t * 1.1f) * _shake, Mathf.Cos(t * 1.7f) * _shake, 0f);
        }

        private void CapturePrevious(Simulation sim)
        {
            _playerPrev = sim.Player.Position;
            _hasPlayerPrev = true;
            for (int i = 0; i < Simulation.MaxEnemies; i++)
            {
                if (sim.Enemies[i].Active) { _enemyPrev[i] = sim.Enemies[i].Position; _enemyPrevValid[i] = true; }
                else _enemyPrevValid[i] = false;
            }
            for (int i = 0; i < Simulation.MaxProjectiles; i++)
            {
                if (sim.Projectiles[i].Active) { _projPrev[i] = sim.Projectiles[i].Position; _projPrevValid[i] = true; }
                else _projPrevValid[i] = false;
            }
        }

        private void RenderEnemies(Simulation sim, float interp)
        {
            int used = 0;
            for (int i = 0; i < Simulation.MaxEnemies; i++)
            {
                if (!sim.Enemies[i].Active) continue;
                var sr = GetPooled(_enemyPool, used, "Enemy", 5);
                ref readonly var e = ref sim.Enemies[i];

                string enemyId = e.DefIndex < _registry.EnemyIds.Count ? _registry.EnemyIds[e.DefIndex] : null;
                var def = _registry.GetEnemy(enemyId);
                sr.sprite = _sprites.Get(def?.sprite ?? "circle");
                sr.color = ColorUtil.Parse(def?.tint, Color.white);

                Vec2 p = _enemyPrevValid[i] ? Lerp(_enemyPrev[i], e.Position, interp) : e.Position;
                sr.transform.position = ToV3(p);
                sr.transform.localScale = Vector3.one * (e.Radius * 2f);
                sr.enabled = true;
                used++;
            }
            HideFrom(_enemyPool, used);
        }

        private void RenderProjectiles(Simulation sim, float interp)
        {
            int used = 0;
            for (int i = 0; i < Simulation.MaxProjectiles; i++)
            {
                if (!sim.Projectiles[i].Active) continue;
                var sr = GetPooled(_projPool, used, "Projectile", 8);
                ref readonly var p = ref sim.Projectiles[i];

                string wid = p.DefIndex < _registry.WeaponIds.Count ? _registry.WeaponIds[p.DefIndex] : null;
                var w = _registry.GetWeapon(wid);
                sr.sprite = _sprites.Get(w?.sprite ?? "bolt");
                sr.color = ColorUtil.Parse(w?.tint, new Color(1f, 0.9f, 0.5f));

                Vec2 pos = _projPrevValid[i] ? Lerp(_projPrev[i], p.Position, interp) : p.Position;
                sr.transform.position = ToV3(pos);
                sr.transform.localScale = Vector3.one * Mathf.Max(0.3f, p.Radius * 3f);
                float ang = SimMathUtil.Angle(p.Velocity);
                sr.transform.rotation = Quaternion.Euler(0, 0, ang);
                sr.enabled = true;
                used++;
            }
            HideFrom(_projPool, used);
        }

        private void RenderPickups(Simulation sim)
        {
            int used = 0;
            for (int i = 0; i < Simulation.MaxPickups; i++)
            {
                if (!sim.Pickups[i].Active) continue;
                var sr = GetPooled(_pickupPool, used, "Pickup", 6);
                ref readonly var pk = ref sim.Pickups[i];
                sr.sprite = _sprites.Get(pk.Kind == 0 ? "diamond" : "ring");
                sr.color = pk.Kind == 0 ? new Color(0.4f, 1f, 0.5f) : new Color(1f, 0.85f, 0.3f);
                sr.transform.position = ToV3(pk.Position);
                sr.transform.localScale = Vector3.one * (pk.Radius * 2f);
                sr.enabled = true;
                used++;
            }
            HideFrom(_pickupPool, used);
        }

        /// <summary>Returns the pooled renderer at <paramref name="index"/>, growing the pool as needed.</summary>
        private SpriteRenderer GetPooled(List<SpriteRenderer> pool, int index, string name, int order)
        {
            while (pool.Count <= index)
            {
                pool.Add(MakeRenderer(name, order));
            }
            return pool[index];
        }

        /// <summary>Disables every pooled renderer at or after <paramref name="from"/> (those unused this frame).</summary>
        private static void HideFrom(List<SpriteRenderer> pool, int from)
        {
            for (int i = from; i < pool.Count; i++)
            {
                if (pool[i].enabled) pool[i].enabled = false;
            }
        }

        private SpriteRenderer MakeRenderer(string name, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_root, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sortingOrder = order;
            return sr;
        }

        private static Vec2 Lerp(Vec2 a, Vec2 b, float t) =>
            new Vec2(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);

        private static Vector3 ToV3(Vec2 v) => new Vector3(v.X, v.Y, 0f);
    }
}
