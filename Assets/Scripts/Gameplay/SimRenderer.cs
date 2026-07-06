using System.Collections.Generic;
using UnityEngine;
using VoidRunner.Content;
using VoidRunner.Core;

namespace VoidRunner.Gameplay
{
    /// <summary>
    /// Draws the current <see cref="Simulation"/> state each frame using pooled SpriteRenderers.
    ///
    /// The renderer is a pure "view" — it never mutates the simulation. Every frame it reads sim
    /// state and positions sprites, so the visual is always an exact reflection of the deterministic
    /// world. It interpolates entity positions between fixed ticks for smooth motion at any display
    /// framerate, while the underlying sim still advances at a fixed 60 Hz.
    /// </summary>
    public sealed class SimRenderer
    {
        private readonly Transform _root;
        private readonly SpriteFactory _sprites;
        private readonly ContentRegistry _registry;

        private readonly List<SpriteRenderer> _enemyPool = new List<SpriteRenderer>();
        private readonly List<SpriteRenderer> _projPool = new List<SpriteRenderer>();
        private readonly List<SpriteRenderer> _pickupPool = new List<SpriteRenderer>();
        private SpriteRenderer _player;
        private SpriteRenderer _floor;

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
        }

        public void Render(Simulation sim, float interpolation)
        {
            EnsurePlayer();
            _player.transform.position = ToV3(sim.Player.Position);
            // Flash while invulnerable.
            float a = sim.Player.InvulnTimer > 0f ? (Mathf.PingPong(Time.time * 12f, 1f) * 0.6f + 0.4f) : 1f;
            var pc = _player.color; pc.a = a; _player.color = pc;

            RenderEnemies(sim);
            RenderProjectiles(sim);
            RenderPickups(sim);
        }

        private void RenderEnemies(Simulation sim)
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
                sr.transform.position = ToV3(e.Position);
                sr.transform.localScale = Vector3.one * (e.Radius * 2f);
                sr.enabled = true;
                used++;
            }
            HideFrom(_enemyPool, used);
        }

        private void RenderProjectiles(Simulation sim)
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
                sr.transform.position = ToV3(p.Position);
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

        private static Vector3 ToV3(Vec2 v) => new Vector3(v.X, v.Y, 0f);
    }
}
