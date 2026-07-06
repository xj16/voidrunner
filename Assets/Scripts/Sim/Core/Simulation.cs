using System;
using System.Collections.Generic;
using VoidRunner.Content;
using VoidRunner.Data;
using VoidRunner.Rng;

namespace VoidRunner.Core
{
    /// <summary>
    /// The deterministic heart of VoidRunner.
    ///
    /// Everything about a run — player, enemies, projectiles, spawning, RNG, room progression — is
    /// advanced here on a FIXED timestep by feeding one <see cref="InputCommand"/> per tick. Given
    /// the same seed, the same content registry and the same input stream, the simulation produces
    /// byte-identical state every time, on every platform. That property is what powers:
    ///   * daily/shared seeds (type a seed, get the exact same run),
    ///   * replays (record the input stream, replay it later),
    ///   * headless verification in CI (no rendering, no Unity engine needed).
    ///
    /// The class intentionally references NO UnityEngine API. The Unity layer
    /// (<see cref="VoidRunner.Gameplay.GameController"/>) owns a Simulation, pushes input into it,
    /// and reads its public state to draw sprites.
    /// </summary>
    public sealed class Simulation
    {
        public const float FixedDeltaTime = 1f / 60f;

        public const int MaxEnemies = 512;
        public const int MaxProjectiles = 1024;
        public const int MaxPickups = 64;

        private readonly ContentRegistry _registry;
        private readonly DeterministicRandom _rng;

        // --- Public, read-only-ish state for the view layer ---
        public readonly PlayerState Player = new PlayerState();
        public readonly EnemyInstance[] Enemies = new EnemyInstance[MaxEnemies];
        public readonly ProjectileInstance[] Projectiles = new ProjectileInstance[MaxProjectiles];
        public readonly PickupInstance[] Pickups = new PickupInstance[MaxPickups];

        public int Tick { get; private set; }
        public int Score { get; private set; }
        public int RoomNumber { get; private set; }
        public int EnemiesAlive { get; private set; }
        public bool RunOver { get; private set; }
        public RoomDef CurrentRoom { get; private set; }

        public ulong Seed => _rng.Seed;

        // Pending spawn queue for the active room's waves: (enemyId, spawnAtTick).
        private struct PendingSpawn { public string EnemyId; public int AtTick; }
        private readonly List<PendingSpawn> _pending = new List<PendingSpawn>();

        // Cached enemy-id -> index lookups for DefIndex (order matches registry.EnemyIds).
        private readonly Dictionary<string, int> _enemyIndex = new Dictionary<string, int>();
        private readonly Dictionary<string, int> _weaponIndex = new Dictionary<string, int>();

        private int _roomClearGraceTicks;

        public Simulation(ContentRegistry registry, ulong seed)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _rng = new DeterministicRandom(seed);

            for (int i = 0; i < registry.EnemyIds.Count; i++) _enemyIndex[registry.EnemyIds[i]] = i;
            for (int i = 0; i < registry.WeaponIds.Count; i++) _weaponIndex[registry.WeaponIds[i]] = i;

            InitPlayer();
            EnterRoom(1);
        }

        private void InitPlayer()
        {
            Player.MaxHealth = 100f;
            Player.Health = 100f;
            Player.MoveSpeed = 6.5f;
            Player.Position = Vec2.Zero;
            Player.Velocity = Vec2.Zero;

            // Starting weapon: the highest-rarity-weight (most common) weapon, chosen deterministically.
            string startWeapon = PickStartingWeapon();
            Player.WeaponId = startWeapon;
        }

        private string PickStartingWeapon()
        {
            string best = null;
            float bestWeight = float.NegativeInfinity;
            foreach (var w in _registry.AllWeapons())
            {
                if (w.rarityWeight > bestWeight)
                {
                    bestWeight = w.rarityWeight;
                    best = w.id;
                }
            }
            return best ?? (_registry.WeaponIds.Count > 0 ? _registry.WeaponIds[0] : null);
        }

        // -----------------------------------------------------------------------------------------
        // Room / wave management
        // -----------------------------------------------------------------------------------------

        private void EnterRoom(int number)
        {
            RoomNumber = number;
            Player.Position = Vec2.Zero;

            // Deterministically pick a room by weight from the registry.
            CurrentRoom = PickRoom();
            _pending.Clear();
            _roomClearGraceTicks = 0;

            if (CurrentRoom == null) return;

            // Difficulty scales with room number: more copies of each wave, tougher enemies.
            int waveRepeat = 1 + (number - 1) / 2;

            for (int rep = 0; rep < waveRepeat; rep++)
            {
                foreach (var waveId in CurrentRoom.waveIds)
                {
                    var wave = _registry.GetWave(waveId);
                    if (wave == null) continue;
                    foreach (var group in wave.groups)
                    {
                        int baseTick = Tick + (int)(group.delay * 60f) + rep * 90;
                        for (int c = 0; c < group.count; c++)
                        {
                            _pending.Add(new PendingSpawn
                            {
                                EnemyId = group.enemyId,
                                AtTick = baseTick + c * 6
                            });
                        }
                    }
                }
            }
        }

        private RoomDef PickRoom()
        {
            var rooms = new List<RoomDef>();
            var weights = new List<float>();
            foreach (var r in _registry.AllRooms())
            {
                rooms.Add(r);
                weights.Add(r.weight);
            }
            if (rooms.Count == 0) return null;
            int idx = _rng.WeightedIndex(weights);
            if (idx < 0) idx = 0;
            return rooms[idx];
        }

        // -----------------------------------------------------------------------------------------
        // Main fixed-step advance
        // -----------------------------------------------------------------------------------------

        /// <summary>Advances the whole simulation by one fixed tick with the given input.</summary>
        public void Step(InputCommand input)
        {
            if (RunOver) return;

            float dt = FixedDeltaTime;

            ProcessSpawns();
            UpdatePlayer(input, dt);
            UpdateEnemies(dt);
            UpdateProjectiles(dt);
            UpdatePickups(dt);
            ResolveCombat(dt);
            CheckRoomProgress();

            if (!Player.Alive)
            {
                RunOver = true;
            }

            Tick++;
        }

        private void ProcessSpawns()
        {
            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                if (_pending[i].AtTick <= Tick)
                {
                    SpawnEnemy(_pending[i].EnemyId);
                    _pending.RemoveAt(i);
                }
            }
        }

        private void SpawnEnemy(string enemyId)
        {
            var def = _registry.GetEnemy(enemyId);
            if (def == null || CurrentRoom == null) return;

            int slot = -1;
            for (int i = 0; i < MaxEnemies; i++)
            {
                if (!Enemies[i].Active) { slot = i; break; }
            }
            if (slot < 0) return;

            // Spawn along a room edge, away from the player. Deterministic via _rng.
            float halfW = CurrentRoom.width * 0.5f - 1f;
            float halfH = CurrentRoom.height * 0.5f - 1f;

            Vec2 pos;
            int edge = _rng.Range(0, 4);
            float t = _rng.NextFloat();
            switch (edge)
            {
                case 0: pos = new Vec2(-halfW + t * (halfW * 2f), halfH); break;
                case 1: pos = new Vec2(-halfW + t * (halfW * 2f), -halfH); break;
                case 2: pos = new Vec2(-halfW, -halfH + t * (halfH * 2f)); break;
                default: pos = new Vec2(halfW, -halfH + t * (halfH * 2f)); break;
            }

            // Difficulty scaling: +12% health and +4% speed per room after the first.
            float diff = 1f + (RoomNumber - 1) * 0.12f;
            float speedDiff = 1f + (RoomNumber - 1) * 0.04f;

            ref var e = ref Enemies[slot];
            e.Active = true;
            e.Position = pos;
            e.Velocity = Vec2.Zero;
            e.MaxHealth = def.maxHealth * diff;
            e.Health = e.MaxHealth;
            e.MoveSpeed = def.moveSpeed * speedDiff;
            e.ContactDamage = def.contactDamage;
            e.Radius = def.radius;
            e.ScoreValue = def.scoreValue;
            e.DropChance = def.dropChance;
            e.Behaviour = ContentLoader.ParseBehaviour(def.behaviour);
            e.DefIndex = _enemyIndex.TryGetValue(enemyId, out int di) ? di : 0;
            e.AiTimer = _rng.RangeFloat(0.5f, 1.5f);
            e.AiHeading = SimMathUtil.FromAngle(_rng.RangeFloat(0f, 360f));

            EnemiesAlive++;
        }

        private void UpdatePlayer(InputCommand input, float dt)
        {
            Vec2 move = input.MoveVector;
            Player.Velocity = move * Player.MoveSpeed;
            Player.Position += Player.Velocity * dt;

            ClampToRoom(ref Player.Position, Player.Radius);

            if (Player.InvulnTimer > 0f) Player.InvulnTimer -= dt;
            if (Player.FireCooldown > 0f) Player.FireCooldown -= dt;

            if (input.Firing && Player.FireCooldown <= 0f)
            {
                Fire(input.AimVector);
            }
        }

        private void Fire(Vec2 aim)
        {
            var weapon = _registry.GetWeapon(Player.WeaponId);
            if (weapon == null) return;

            Player.FireCooldown = 1f / weapon.fireRate;

            int count = Math.Max(1, weapon.projectilesPerShot);
            float baseAngle = SimMathUtil.Angle(aim.SqrMagnitude > 1e-8f ? aim : new Vec2(1f, 0f));
            float spread = weapon.spreadDegrees;
            int wIdx = _weaponIndex.TryGetValue(weapon.id, out int wi) ? wi : 0;

            for (int i = 0; i < count; i++)
            {
                float angle;
                if (count == 1)
                {
                    angle = baseAngle;
                }
                else
                {
                    float frac = (float)i / (count - 1); // 0..1
                    angle = baseAngle - spread * 0.5f + spread * frac;
                }

                SpawnProjectile(angle, weapon, wIdx);
            }
        }

        private void SpawnProjectile(float angleDeg, WeaponDef weapon, int defIndex)
        {
            int slot = -1;
            for (int i = 0; i < MaxProjectiles; i++)
            {
                if (!Projectiles[i].Active) { slot = i; break; }
            }
            if (slot < 0) return;

            Vec2 dir = SimMathUtil.FromAngle(angleDeg);
            ref var p = ref Projectiles[slot];
            p.Active = true;
            p.Position = Player.Position + dir * (Player.Radius + 0.2f);
            p.Velocity = dir * weapon.projectileSpeed;
            p.Damage = weapon.damage;
            p.Radius = weapon.projectileRadius;
            p.Lifetime = weapon.projectileLifetime;
            p.PierceRemaining = weapon.pierce;
            p.DefIndex = defIndex;
        }

        private void UpdateEnemies(float dt)
        {
            Vec2 playerPos = Player.Position;
            for (int i = 0; i < MaxEnemies; i++)
            {
                if (!Enemies[i].Active) continue;
                ref var e = ref Enemies[i];

                Vec2 toPlayer = playerPos - e.Position;
                float dist = toPlayer.Magnitude;
                Vec2 dir = dist > 1e-4f ? toPlayer * (1f / dist) : Vec2.Zero;

                switch (e.Behaviour)
                {
                    case AiBehaviour.Chase:
                        e.Velocity = dir * e.MoveSpeed;
                        break;

                    case AiBehaviour.Kite:
                    {
                        // Maintain a preferred distance of ~6 units; strafe otherwise.
                        const float preferred = 6f;
                        Vec2 radial = dist < preferred ? -dir : dir;
                        Vec2 tangent = new Vec2(-dir.Y, dir.X);
                        e.Velocity = (radial * 0.6f + tangent * 0.8f).Normalized * e.MoveSpeed;
                        break;
                    }

                    case AiBehaviour.Wander:
                        e.AiTimer -= dt;
                        if (e.AiTimer <= 0f)
                        {
                            e.AiHeading = SimMathUtil.FromAngle(_rng.RangeFloat(0f, 360f));
                            e.AiTimer = _rng.RangeFloat(1f, 2.5f);
                        }
                        e.Velocity = e.AiHeading * (e.MoveSpeed * 0.6f);
                        break;

                    case AiBehaviour.Charger:
                        e.AiTimer -= dt;
                        if (e.AiTimer <= 0f)
                        {
                            // Lock onto the player and dash for a short window.
                            e.AiHeading = dir;
                            e.AiTimer = _rng.RangeFloat(1.2f, 2.2f);
                        }
                        e.Velocity = e.AiHeading * (e.MoveSpeed * 1.6f);
                        break;
                }

                e.Position += e.Velocity * dt;
                ClampToRoom(ref e.Position, e.Radius);
            }
        }

        private void UpdateProjectiles(float dt)
        {
            for (int i = 0; i < MaxProjectiles; i++)
            {
                if (!Projectiles[i].Active) continue;
                ref var p = ref Projectiles[i];
                p.Position += p.Velocity * dt;
                p.Lifetime -= dt;

                if (p.Lifetime <= 0f || OutOfRoom(p.Position, p.Radius))
                {
                    p.Active = false;
                }
            }
        }

        private void UpdatePickups(float dt)
        {
            for (int i = 0; i < MaxPickups; i++)
            {
                if (!Pickups[i].Active) continue;
                ref var pk = ref Pickups[i];
                float sumR = pk.Radius + Player.Radius;
                if (Vec2.SqrDistance(pk.Position, Player.Position) <= sumR * sumR)
                {
                    ApplyPickup(ref pk);
                    pk.Active = false;
                }
            }
        }

        private void ApplyPickup(ref PickupInstance pk)
        {
            if (pk.Kind == 0)
            {
                Player.Health = Math.Min(Player.MaxHealth, Player.Health + pk.HealAmount);
            }
            else
            {
                if (pk.WeaponDefIndex >= 0 && pk.WeaponDefIndex < _registry.WeaponIds.Count)
                {
                    Player.WeaponId = _registry.WeaponIds[pk.WeaponDefIndex];
                    Player.FireCooldown = 0f;
                }
            }
        }

        private void ResolveCombat(float dt)
        {
            // Projectiles vs enemies.
            for (int pi = 0; pi < MaxProjectiles; pi++)
            {
                if (!Projectiles[pi].Active) continue;
                ref var p = ref Projectiles[pi];

                for (int ei = 0; ei < MaxEnemies; ei++)
                {
                    if (!Enemies[ei].Active) continue;
                    ref var e = ref Enemies[ei];

                    float sumR = p.Radius + e.Radius;
                    if (Vec2.SqrDistance(p.Position, e.Position) <= sumR * sumR)
                    {
                        e.Health -= p.Damage;
                        if (e.Health <= 0f)
                        {
                            KillEnemy(ref e);
                        }

                        if (p.PierceRemaining > 0)
                        {
                            p.PierceRemaining--;
                        }
                        else
                        {
                            p.Active = false;
                            break;
                        }
                    }
                }
            }

            // Enemies vs player (contact damage), respecting i-frames.
            if (Player.InvulnTimer <= 0f)
            {
                for (int ei = 0; ei < MaxEnemies; ei++)
                {
                    if (!Enemies[ei].Active) continue;
                    ref var e = ref Enemies[ei];
                    float sumR = e.Radius + Player.Radius;
                    if (Vec2.SqrDistance(e.Position, Player.Position) <= sumR * sumR)
                    {
                        Player.Health -= e.ContactDamage;
                        Player.InvulnTimer = 0.6f;
                        // Small knockback away from the enemy for game feel.
                        Vec2 push = (Player.Position - e.Position).Normalized * 0.8f;
                        Player.Position += push;
                        ClampToRoom(ref Player.Position, Player.Radius);
                        break;
                    }
                }
            }
        }

        private void KillEnemy(ref EnemyInstance e)
        {
            e.Active = false;
            EnemiesAlive--;
            Score += e.ScoreValue;

            // Deterministic drop roll.
            if (_rng.Chance(e.DropChance))
            {
                SpawnDrop(e.Position);
            }
        }

        private void SpawnDrop(Vec2 pos)
        {
            int slot = -1;
            for (int i = 0; i < MaxPickups; i++)
            {
                if (!Pickups[i].Active) { slot = i; break; }
            }
            if (slot < 0) return;

            ref var pk = ref Pickups[slot];
            pk.Active = true;
            pk.Position = pos;
            pk.Radius = 0.45f;

            // 60% heal, 40% weapon swap — chosen deterministically.
            if (_rng.Chance(0.6f) || _registry.WeaponIds.Count == 0)
            {
                pk.Kind = 0;
                pk.HealAmount = 20f;
            }
            else
            {
                pk.Kind = 1;
                // Weighted by rarity so rarer weapons drop less.
                var weights = new List<float>();
                foreach (var w in _registry.AllWeapons()) weights.Add(w.rarityWeight);
                int idx = _rng.WeightedIndex(weights);
                pk.WeaponDefIndex = idx < 0 ? 0 : idx;
            }
        }

        private void CheckRoomProgress()
        {
            // Room is cleared when there are no living enemies AND no pending spawns.
            if (EnemiesAlive <= 0 && _pending.Count == 0 && CurrentRoom != null)
            {
                _roomClearGraceTicks++;
                // Small grace so the player sees the room empty before advancing (~0.75s).
                if (_roomClearGraceTicks >= 45)
                {
                    AdvanceRoom();
                }
            }
            else
            {
                _roomClearGraceTicks = 0;
            }
        }

        private void AdvanceRoom()
        {
            // Clear leftover projectiles/pickups and heal a little between rooms.
            for (int i = 0; i < MaxProjectiles; i++) Projectiles[i].Active = false;
            for (int i = 0; i < MaxPickups; i++) Pickups[i].Active = false;
            Player.Health = Math.Min(Player.MaxHealth, Player.Health + 10f);
            EnterRoom(RoomNumber + 1);
        }

        // -----------------------------------------------------------------------------------------
        // Helpers
        // -----------------------------------------------------------------------------------------

        private void ClampToRoom(ref Vec2 pos, float radius)
        {
            if (CurrentRoom == null) return;
            float halfW = CurrentRoom.width * 0.5f - radius;
            float halfH = CurrentRoom.height * 0.5f - radius;
            pos.X = SimMathUtil.Clamp(pos.X, -halfW, halfW);
            pos.Y = SimMathUtil.Clamp(pos.Y, -halfH, halfH);
        }

        private bool OutOfRoom(Vec2 pos, float radius)
        {
            if (CurrentRoom == null) return true;
            float halfW = CurrentRoom.width * 0.5f + radius;
            float halfH = CurrentRoom.height * 0.5f + radius;
            return pos.X < -halfW || pos.X > halfW || pos.Y < -halfH || pos.Y > halfH;
        }

        /// <summary>
        /// A cheap 64-bit checksum of the entire simulation state. Two runs that diverge produce
        /// different checksums at the divergence tick, which is exactly how the replay verifier and
        /// the desync test detect a broken determinism guarantee.
        /// </summary>
        public ulong StateHash()
        {
            unchecked
            {
                ulong h = 1469598103934665603UL;
                // The unchecked block does not extend into nested local functions, so mark the
                // wrapping arithmetic explicitly to keep FNV overflow-wrapping under any build flags.
                void Mix(float f) { unchecked { h ^= (uint)BitConverter.SingleToInt32Bits(f); h *= 1099511628211UL; } }
                void MixI(long i) { unchecked { h ^= (ulong)i; h *= 1099511628211UL; } }

                MixI(Tick);
                MixI(Score);
                MixI(RoomNumber);
                Mix(Player.Position.X); Mix(Player.Position.Y);
                Mix(Player.Health);

                for (int i = 0; i < MaxEnemies; i++)
                {
                    if (!Enemies[i].Active) continue;
                    MixI(i);
                    Mix(Enemies[i].Position.X); Mix(Enemies[i].Position.Y);
                    Mix(Enemies[i].Health);
                }
                for (int i = 0; i < MaxProjectiles; i++)
                {
                    if (!Projectiles[i].Active) continue;
                    MixI(i);
                    Mix(Projectiles[i].Position.X); Mix(Projectiles[i].Position.Y);
                }
                return h;
            }
        }
    }
}
