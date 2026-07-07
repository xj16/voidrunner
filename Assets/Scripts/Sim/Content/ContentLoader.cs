using System.Collections.Generic;
using VoidRunner.Data;

namespace VoidRunner.Content
{
    /// <summary>Guards against non-finite or absurd numbers in untrusted content packs.</summary>
    internal static class NumberGuard
    {
        // A generous world-scale bound. Anything past this in a content pack is either a typo or a
        // hostile value designed to overflow the sim (e.g. a projectile that teleports across the
        // float range). Legit content stays within a few hundred.
        public const float MaxMagnitude = 1_000_000f;

        public static bool IsFinite(float f) => !(float.IsNaN(f) || float.IsInfinity(f));

        /// <summary>
        /// Returns true if the value is finite and within bounds. On failure it appends a precise
        /// error and returns false so the loader can refuse the pack rather than run a broken world.
        /// </summary>
        public static bool Check(float value, string source, string entity, string field, List<string> errors)
        {
            if (!IsFinite(value))
            {
                errors.Add($"[{source}] {entity} field '{field}' is not a finite number (NaN/Infinity)");
                return false;
            }
            if (value > MaxMagnitude || value < -MaxMagnitude)
            {
                errors.Add($"[{source}] {entity} field '{field}' = {value} is out of the allowed range (±{MaxMagnitude})");
                return false;
            }
            return true;
        }
    }

    /// <summary>
    /// Result of loading one or more content files: the populated registry plus any warnings and
    /// hard errors. A pack with errors is refused so a broken mod can never corrupt a run.
    /// </summary>
    public sealed class ContentLoadResult
    {
        public readonly ContentRegistry Registry;
        public readonly List<string> Warnings = new List<string>();
        public readonly List<string> Errors = new List<string>();

        public bool Ok => Errors.Count == 0;

        public ContentLoadResult(ContentRegistry registry) { Registry = registry; }
    }

    /// <summary>
    /// Parses content-pack JSON into strongly-typed defs and validates them. Engine-agnostic: it
    /// takes raw strings so it can be driven by the Unity <see cref="VoidRunner.Modding.PackDiscovery"/>
    /// loader in the editor/build, or directly by unit tests in plain .NET.
    /// </summary>
    public static class ContentLoader
    {
        /// <summary>Loads a batch of (sourceName, jsonText) pairs into a fresh registry, in order.</summary>
        public static ContentLoadResult LoadFiles(IEnumerable<(string source, string json)> files)
        {
            var registry = new ContentRegistry();
            var result = new ContentLoadResult(registry);

            foreach (var (source, json) in files)
            {
                LoadInto(result, source, json);
            }

            ValidateReferences(result);
            return result;
        }

        /// <summary>Loads a single content file into an existing result/registry.</summary>
        public static void LoadInto(ContentLoadResult result, string source, string json)
        {
            JsonValue root;
            try
            {
                root = Json.Parse(json);
            }
            catch (JsonParseException ex)
            {
                result.Errors.Add($"[{source}] {ex.Message}");
                return;
            }

            if (root == null || !root.IsObject)
            {
                result.Errors.Add($"[{source}] top-level JSON must be an object with enemies/weapons/rooms/waves arrays");
                return;
            }

            LoadEnemies(result, source, root["enemies"]);
            LoadWeapons(result, source, root["weapons"]);
            LoadRooms(result, source, root["rooms"]);
            LoadWaves(result, source, root["waves"]);
        }

        private static void LoadEnemies(ContentLoadResult result, string source, JsonValue array)
        {
            if (array == null) return;
            if (!array.IsArray) { result.Errors.Add($"[{source}] 'enemies' must be an array"); return; }

            foreach (var node in array.AsArray)
            {
                if (!node.IsObject) { result.Errors.Add($"[{source}] enemy entries must be objects"); continue; }

                string id = node.GetString("id");
                if (string.IsNullOrEmpty(id)) { result.Errors.Add($"[{source}] an enemy is missing 'id'"); continue; }

                var def = new EnemyDef
                {
                    id = id,
                    displayName = node.GetString("displayName", id),
                    sprite = node.GetString("sprite", "circle"),
                    tint = node.GetString("tint", "#FFFFFF"),
                    maxHealth = node.GetFloat("maxHealth", 10f),
                    moveSpeed = node.GetFloat("moveSpeed", 2f),
                    contactDamage = node.GetFloat("contactDamage", 1f),
                    radius = node.GetFloat("radius", 0.4f),
                    behaviour = node.GetString("behaviour", "chase"),
                    scoreValue = node.GetInt("scoreValue", 10),
                    dropChance = node.GetFloat("dropChance", 0.1f)
                };

                // Reject non-finite / absurd numbers before they reach the deterministic sim, where
                // a NaN would silently corrupt every downstream position and the state hash.
                string ent = $"enemy '{id}'";
                bool numsOk =
                    NumberGuard.Check(def.maxHealth, source, ent, "maxHealth", result.Errors) &
                    NumberGuard.Check(def.moveSpeed, source, ent, "moveSpeed", result.Errors) &
                    NumberGuard.Check(def.contactDamage, source, ent, "contactDamage", result.Errors) &
                    NumberGuard.Check(def.radius, source, ent, "radius", result.Errors) &
                    NumberGuard.Check(def.dropChance, source, ent, "dropChance", result.Errors);
                if (!numsOk) continue;

                if (def.radius <= 0f) { result.Warnings.Add($"[{source}] {ent} radius <= 0; clamped to 0.05"); def.radius = 0.05f; }
                if (def.maxHealth <= 0f) result.Warnings.Add($"[{source}] enemy '{id}' has maxHealth <= 0; clamped to 1");
                if (def.maxHealth <= 0f) def.maxHealth = 1f;
                if (!IsKnownBehaviour(def.behaviour))
                    result.Warnings.Add($"[{source}] enemy '{id}' has unknown behaviour '{def.behaviour}'; defaulting to chase");

                result.Registry.AddEnemy(def);
            }
        }

        private static void LoadWeapons(ContentLoadResult result, string source, JsonValue array)
        {
            if (array == null) return;
            if (!array.IsArray) { result.Errors.Add($"[{source}] 'weapons' must be an array"); return; }

            foreach (var node in array.AsArray)
            {
                if (!node.IsObject) { result.Errors.Add($"[{source}] weapon entries must be objects"); continue; }

                string id = node.GetString("id");
                if (string.IsNullOrEmpty(id)) { result.Errors.Add($"[{source}] a weapon is missing 'id'"); continue; }

                var def = new WeaponDef
                {
                    id = id,
                    displayName = node.GetString("displayName", id),
                    sprite = node.GetString("sprite", "bolt"),
                    tint = node.GetString("tint", "#FFFFFF"),
                    damage = node.GetFloat("damage", 3f),
                    fireRate = node.GetFloat("fireRate", 3f),
                    projectileSpeed = node.GetFloat("projectileSpeed", 12f),
                    projectileLifetime = node.GetFloat("projectileLifetime", 1.2f),
                    projectilesPerShot = node.GetInt("projectilesPerShot", 1),
                    spreadDegrees = node.GetFloat("spreadDegrees", 0f),
                    pierce = node.GetInt("pierce", 0),
                    projectileRadius = node.GetFloat("projectileRadius", 0.15f),
                    rarityWeight = node.GetFloat("rarityWeight", 1f)
                };

                string went = $"weapon '{id}'";
                bool wOk =
                    NumberGuard.Check(def.damage, source, went, "damage", result.Errors) &
                    NumberGuard.Check(def.fireRate, source, went, "fireRate", result.Errors) &
                    NumberGuard.Check(def.projectileSpeed, source, went, "projectileSpeed", result.Errors) &
                    NumberGuard.Check(def.projectileLifetime, source, went, "projectileLifetime", result.Errors) &
                    NumberGuard.Check(def.spreadDegrees, source, went, "spreadDegrees", result.Errors) &
                    NumberGuard.Check(def.projectileRadius, source, went, "projectileRadius", result.Errors) &
                    NumberGuard.Check(def.rarityWeight, source, went, "rarityWeight", result.Errors);
                if (!wOk) continue;

                // A shot count in the thousands would exhaust the projectile pool every frame; bound
                // it to a sane maximum so a hostile pack can't lock up the sim.
                if (def.projectilesPerShot > 256)
                {
                    result.Warnings.Add($"[{source}] {went} projectilesPerShot {def.projectilesPerShot} capped to 256");
                    def.projectilesPerShot = 256;
                }
                if (def.fireRate <= 0f) { result.Warnings.Add($"[{source}] weapon '{id}' fireRate <= 0; clamped to 0.1"); def.fireRate = 0.1f; }
                if (def.projectilesPerShot < 1) def.projectilesPerShot = 1;

                result.Registry.AddWeapon(def);
            }
        }

        private static void LoadRooms(ContentLoadResult result, string source, JsonValue array)
        {
            if (array == null) return;
            if (!array.IsArray) { result.Errors.Add($"[{source}] 'rooms' must be an array"); return; }

            foreach (var node in array.AsArray)
            {
                if (!node.IsObject) { result.Errors.Add($"[{source}] room entries must be objects"); continue; }

                string id = node.GetString("id");
                if (string.IsNullOrEmpty(id)) { result.Errors.Add($"[{source}] a room is missing 'id'"); continue; }

                var def = new RoomDef
                {
                    id = id,
                    displayName = node.GetString("displayName", id),
                    width = node.GetFloat("width", 24f),
                    height = node.GetFloat("height", 14f),
                    backgroundTint = node.GetString("backgroundTint", "#0B0E1A"),
                    weight = node.GetFloat("weight", 1f)
                };

                string rent = $"room '{id}'";
                bool rOk =
                    NumberGuard.Check(def.width, source, rent, "width", result.Errors) &
                    NumberGuard.Check(def.height, source, rent, "height", result.Errors) &
                    NumberGuard.Check(def.weight, source, rent, "weight", result.Errors);
                if (!rOk) continue;

                var obstacles = node["obstacles"];
                if (obstacles != null && obstacles.IsArray)
                {
                    bool obstaclesOk = true;
                    foreach (var o in obstacles.AsArray)
                    {
                        if (!o.IsObject) continue;
                        var ob = new ObstacleDef
                        {
                            x = o.GetFloat("x"),
                            y = o.GetFloat("y"),
                            width = o.GetFloat("width", 1f),
                            height = o.GetFloat("height", 1f)
                        };
                        // Obstacles are simulated now, so their geometry must be finite & sane.
                        obstaclesOk &=
                            NumberGuard.Check(ob.x, source, rent, "obstacle.x", result.Errors) &
                            NumberGuard.Check(ob.y, source, rent, "obstacle.y", result.Errors) &
                            NumberGuard.Check(ob.width, source, rent, "obstacle.width", result.Errors) &
                            NumberGuard.Check(ob.height, source, rent, "obstacle.height", result.Errors);
                        if (ob.width < 0f) ob.width = -ob.width;
                        if (ob.height < 0f) ob.height = -ob.height;
                        def.obstacles.Add(ob);
                    }
                    if (!obstaclesOk) continue;
                }

                var waveIds = node["waveIds"];
                if (waveIds != null && waveIds.IsArray)
                {
                    foreach (var w in waveIds.AsArray)
                    {
                        if (w.Type == JsonType.String) def.waveIds.Add(w.AsString);
                    }
                }

                if (def.width < 8f) { result.Warnings.Add($"[{source}] room '{id}' width < 8; clamped"); def.width = 8f; }
                if (def.height < 6f) { result.Warnings.Add($"[{source}] room '{id}' height < 6; clamped"); def.height = 6f; }

                result.Registry.AddRoom(def);
            }
        }

        private static void LoadWaves(ContentLoadResult result, string source, JsonValue array)
        {
            if (array == null) return;
            if (!array.IsArray) { result.Errors.Add($"[{source}] 'waves' must be an array"); return; }

            foreach (var node in array.AsArray)
            {
                if (!node.IsObject) { result.Errors.Add($"[{source}] wave entries must be objects"); continue; }

                string id = node.GetString("id");
                if (string.IsNullOrEmpty(id)) { result.Errors.Add($"[{source}] a wave is missing 'id'"); continue; }

                var def = new WaveDef { id = id };
                var groups = node["groups"];
                if (groups != null && groups.IsArray)
                {
                    foreach (var g in groups.AsArray)
                    {
                        if (!g.IsObject) continue;
                        def.groups.Add(new SpawnGroup
                        {
                            enemyId = g.GetString("enemyId"),
                            count = g.GetInt("count", 1),
                            delay = g.GetFloat("delay", 0f)
                        });
                    }
                }

                if (def.groups.Count == 0)
                    result.Warnings.Add($"[{source}] wave '{id}' has no spawn groups");

                result.Registry.AddWave(def);
            }
        }

        /// <summary>
        /// Cross-checks references after all files are loaded: rooms must reference known waves,
        /// waves must reference known enemies. Broken references are hard errors so a run never
        /// tries to spawn something that does not exist.
        /// </summary>
        private static void ValidateReferences(ContentLoadResult result)
        {
            var reg = result.Registry;

            foreach (var room in reg.AllRooms())
            {
                foreach (var waveId in room.waveIds)
                {
                    if (!reg.HasWave(waveId))
                        result.Errors.Add($"room '{room.id}' references unknown wave '{waveId}'");
                }
            }

            foreach (var waveId in reg.RoomIds) { /* iterated above via rooms */ }

            // Validate each wave's enemy references.
            foreach (var roomId in reg.RoomIds)
            {
                var room = reg.GetRoom(roomId);
                foreach (var waveId in room.waveIds)
                {
                    var wave = reg.GetWave(waveId);
                    if (wave == null) continue;
                    foreach (var group in wave.groups)
                    {
                        if (!reg.HasEnemy(group.enemyId))
                            result.Errors.Add($"wave '{wave.id}' references unknown enemy '{group.enemyId}'");
                    }
                }
            }

            if (reg.EnemyCount == 0) result.Errors.Add("no enemies defined across all packs");
            if (reg.WeaponCount == 0) result.Errors.Add("no weapons defined across all packs");
            if (reg.RoomCount == 0) result.Errors.Add("no rooms defined across all packs");
        }

        private static bool IsKnownBehaviour(string b)
        {
            switch ((b ?? "").ToLowerInvariant())
            {
                case "chase":
                case "kite":
                case "wander":
                case "charger":
                    return true;
                default:
                    return false;
            }
        }

        public static AiBehaviour ParseBehaviour(string b)
        {
            switch ((b ?? "").ToLowerInvariant())
            {
                case "kite": return AiBehaviour.Kite;
                case "wander": return AiBehaviour.Wander;
                case "charger": return AiBehaviour.Charger;
                default: return AiBehaviour.Chase;
            }
        }
    }
}
