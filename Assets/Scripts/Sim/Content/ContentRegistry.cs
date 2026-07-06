using System.Collections.Generic;
using VoidRunner.Data;

namespace VoidRunner.Content
{
    /// <summary>
    /// The in-memory database of all content merged from every loaded pack. Systems look content
    /// up by id here; nothing in the gameplay simulation hard-codes an enemy or weapon.
    ///
    /// Later packs override earlier ones on id collision (last-write-wins), which is what lets a
    /// mod re-balance the base game by shipping an enemy with the same id.
    /// </summary>
    public sealed class ContentRegistry
    {
        private readonly Dictionary<string, EnemyDef> _enemies = new Dictionary<string, EnemyDef>();
        private readonly Dictionary<string, WeaponDef> _weapons = new Dictionary<string, WeaponDef>();
        private readonly Dictionary<string, RoomDef> _rooms = new Dictionary<string, RoomDef>();
        private readonly Dictionary<string, WaveDef> _waves = new Dictionary<string, WaveDef>();

        // Ordered id lists so run generation is deterministic (dictionary order is not guaranteed).
        private readonly List<string> _enemyOrder = new List<string>();
        private readonly List<string> _weaponOrder = new List<string>();
        private readonly List<string> _roomOrder = new List<string>();

        public IReadOnlyList<string> EnemyIds => _enemyOrder;
        public IReadOnlyList<string> WeaponIds => _weaponOrder;
        public IReadOnlyList<string> RoomIds => _roomOrder;

        public int EnemyCount => _enemies.Count;
        public int WeaponCount => _weapons.Count;
        public int RoomCount => _rooms.Count;
        public int WaveCount => _waves.Count;

        public void AddEnemy(EnemyDef def)
        {
            if (!_enemies.ContainsKey(def.id)) _enemyOrder.Add(def.id);
            _enemies[def.id] = def;
        }

        public void AddWeapon(WeaponDef def)
        {
            if (!_weapons.ContainsKey(def.id)) _weaponOrder.Add(def.id);
            _weapons[def.id] = def;
        }

        public void AddRoom(RoomDef def)
        {
            if (!_rooms.ContainsKey(def.id)) _roomOrder.Add(def.id);
            _rooms[def.id] = def;
        }

        public void AddWave(WaveDef def) => _waves[def.id] = def;

        public EnemyDef GetEnemy(string id) => id != null && _enemies.TryGetValue(id, out var d) ? d : null;
        public WeaponDef GetWeapon(string id) => id != null && _weapons.TryGetValue(id, out var d) ? d : null;
        public RoomDef GetRoom(string id) => id != null && _rooms.TryGetValue(id, out var d) ? d : null;
        public WaveDef GetWave(string id) => id != null && _waves.TryGetValue(id, out var d) ? d : null;

        public bool HasEnemy(string id) => id != null && _enemies.ContainsKey(id);
        public bool HasWeapon(string id) => id != null && _weapons.ContainsKey(id);
        public bool HasWave(string id) => id != null && _waves.ContainsKey(id);

        public IEnumerable<WeaponDef> AllWeapons()
        {
            foreach (var id in _weaponOrder) yield return _weapons[id];
        }

        public IEnumerable<RoomDef> AllRooms()
        {
            foreach (var id in _roomOrder) yield return _rooms[id];
        }
    }
}
