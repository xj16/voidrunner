using System;
using System.Collections.Generic;

namespace VoidRunner.Data
{
    /// <summary>
    /// Plain-old-data schema for VoidRunner content packs. These types are the on-disk contract:
    /// a modder writes JSON that deserializes straight into them, no C# compilation required.
    ///
    /// Everything here is engine-agnostic (no UnityEngine references) so the schema can be
    /// validated, unit-tested and reasoned about outside the editor. Vector-like values are kept
    /// as plain floats/ints for stable, human-editable JSON.
    ///
    /// All classes are marked [Serializable] so Unity's JsonUtility can also read them, but the
    /// canonical loader (<see cref="VoidRunner.Content.ContentLoader"/>) uses a hand-rolled reader
    /// that reports precise validation errors, which JsonUtility cannot.
    /// </summary>
    [Serializable]
    public sealed class ContentPackManifest
    {
        /// <summary>Unique, lowercase, hyphenated id, e.g. "base" or "cosmic-horrors".</summary>
        public string id;

        /// <summary>Human-readable name shown in the mod list.</summary>
        public string name;

        public string author;
        public string version;
        public string description;

        /// <summary>Content-pack format version this pack targets. Current = 1.</summary>
        public int format = 1;

        /// <summary>Other pack ids this one depends on. Loaded first if present.</summary>
        public List<string> dependencies = new List<string>();

        /// <summary>Relative file names (from the pack folder) to load, in order.</summary>
        public List<string> files = new List<string>();
    }

    /// <summary>Top-level container a single content JSON file deserializes into.</summary>
    [Serializable]
    public sealed class ContentFile
    {
        public List<EnemyDef> enemies = new List<EnemyDef>();
        public List<WeaponDef> weapons = new List<WeaponDef>();
        public List<RoomDef> rooms = new List<RoomDef>();
        public List<WaveDef> waves = new List<WaveDef>();
    }

    // ---------------------------------------------------------------------------------------------
    // Enemies
    // ---------------------------------------------------------------------------------------------

    [Serializable]
    public sealed class EnemyDef
    {
        public string id;
        public string displayName;

        /// <summary>Sprite key resolved against the pack's sprite table (or a fallback shape).</summary>
        public string sprite;

        /// <summary>Tint applied to the sprite, hex "#RRGGBB" or "#RRGGBBAA".</summary>
        public string tint = "#FFFFFF";

        public float maxHealth = 10f;
        public float moveSpeed = 2f;

        /// <summary>Contact damage dealt to the player per hit.</summary>
        public float contactDamage = 1f;

        /// <summary>Radius used for collision against player/projectiles, in world units.</summary>
        public float radius = 0.4f;

        /// <summary>How the enemy moves. See <see cref="AiBehaviour"/>.</summary>
        public string behaviour = "chase";

        /// <summary>Score / currency awarded on kill.</summary>
        public int scoreValue = 10;

        /// <summary>Chance in [0,1] to drop a pickup on death.</summary>
        public float dropChance = 0.1f;
    }

    public enum AiBehaviour
    {
        /// <summary>Walks straight toward the player.</summary>
        Chase,

        /// <summary>Keeps distance and drifts sideways (glass-cannon shooters).</summary>
        Kite,

        /// <summary>Ignores the player and wanders on a fixed heading.</summary>
        Wander,

        /// <summary>Charges in bursts toward the player's last position.</summary>
        Charger
    }

    // ---------------------------------------------------------------------------------------------
    // Weapons
    // ---------------------------------------------------------------------------------------------

    [Serializable]
    public sealed class WeaponDef
    {
        public string id;
        public string displayName;
        public string sprite;
        public string tint = "#FFFFFF";

        public float damage = 3f;

        /// <summary>Shots per second.</summary>
        public float fireRate = 3f;

        /// <summary>Projectile speed in world units per second.</summary>
        public float projectileSpeed = 12f;

        /// <summary>Projectile lifetime in seconds before it despawns.</summary>
        public float projectileLifetime = 1.2f;

        /// <summary>Number of projectiles fired per shot (shotgun style).</summary>
        public int projectilesPerShot = 1;

        /// <summary>Total spread cone in degrees across all projectiles in a shot.</summary>
        public float spreadDegrees = 0f;

        /// <summary>How many enemies a projectile can pass through before despawning.</summary>
        public int pierce = 0;

        /// <summary>Collision radius of a projectile.</summary>
        public float projectileRadius = 0.15f;

        /// <summary>Rarity weight used when this weapon appears as a drop / choice.</summary>
        public float rarityWeight = 1f;
    }

    // ---------------------------------------------------------------------------------------------
    // Rooms
    // ---------------------------------------------------------------------------------------------

    [Serializable]
    public sealed class RoomDef
    {
        public string id;
        public string displayName;

        /// <summary>Interior play area in world units.</summary>
        public float width = 24f;
        public float height = 14f;

        /// <summary>Background tint hex.</summary>
        public string backgroundTint = "#0B0E1A";

        /// <summary>Fixed obstacle rectangles placed inside the room.</summary>
        public List<ObstacleDef> obstacles = new List<ObstacleDef>();

        /// <summary>Ids of waves (from any loaded pack) that can spawn in this room.</summary>
        public List<string> waveIds = new List<string>();

        /// <summary>Selection weight when the run picks the next room.</summary>
        public float weight = 1f;
    }

    [Serializable]
    public sealed class ObstacleDef
    {
        public float x;
        public float y;
        public float width = 1f;
        public float height = 1f;
    }

    // ---------------------------------------------------------------------------------------------
    // Waves
    // ---------------------------------------------------------------------------------------------

    [Serializable]
    public sealed class WaveDef
    {
        public string id;

        /// <summary>Groups of enemies spawned together, resolved by the spawner.</summary>
        public List<SpawnGroup> groups = new List<SpawnGroup>();
    }

    [Serializable]
    public sealed class SpawnGroup
    {
        /// <summary>Enemy id to spawn.</summary>
        public string enemyId;

        /// <summary>How many to spawn.</summary>
        public int count = 1;

        /// <summary>Delay in seconds before this group appears after the wave starts.</summary>
        public float delay = 0f;
    }
}
