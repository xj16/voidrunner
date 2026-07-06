using System;
using VoidRunner.Data;

namespace VoidRunner.Core
{
    /// <summary>The player's simulated state. One per run.</summary>
    public sealed class PlayerState
    {
        public Vec2 Position;
        public Vec2 Velocity;
        public float Health;
        public float MaxHealth;
        public float MoveSpeed;
        public float Radius = 0.35f;

        /// <summary>Currently equipped weapon id (resolved against the registry each shot).</summary>
        public string WeaponId;

        /// <summary>Seconds until the weapon can fire again.</summary>
        public float FireCooldown;

        /// <summary>Seconds of post-hit invulnerability remaining.</summary>
        public float InvulnTimer;

        public bool Alive => Health > 0f;
    }

    /// <summary>A live enemy in the simulation. Pooled by index inside <see cref="Simulation"/>.</summary>
    public struct EnemyInstance
    {
        public bool Active;
        public Vec2 Position;
        public Vec2 Velocity;
        public float Health;

        /// <summary>Cached def fields (copied so AI never dictionary-looks-up in the hot loop).</summary>
        public float MaxHealth;
        public float MoveSpeed;
        public float ContactDamage;
        public float Radius;
        public int ScoreValue;
        public float DropChance;
        public AiBehaviour Behaviour;

        /// <summary>Index into the registry's enemy id list, for the view layer to pick a sprite.</summary>
        public int DefIndex;

        /// <summary>Per-enemy AI scratch: heading for wander/charger, timer for charge bursts.</summary>
        public float AiTimer;
        public Vec2 AiHeading;
    }

    /// <summary>A live projectile. Pooled by index.</summary>
    public struct ProjectileInstance
    {
        public bool Active;
        public Vec2 Position;
        public Vec2 Velocity;
        public float Damage;
        public float Radius;
        public float Lifetime;
        public int PierceRemaining;
        public int DefIndex;
    }

    /// <summary>A dropped pickup that heals or swaps a weapon when the player touches it.</summary>
    public struct PickupInstance
    {
        public bool Active;
        public Vec2 Position;
        public float Radius;

        /// <summary>0 = heal, 1 = weapon swap.</summary>
        public int Kind;

        /// <summary>For weapon pickups, the registry index of the offered weapon.</summary>
        public int WeaponDefIndex;

        public float HealAmount;
    }
}
