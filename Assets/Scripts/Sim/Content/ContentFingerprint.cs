using VoidRunner.Data;

namespace VoidRunner.Content
{
    /// <summary>
    /// Computes a stable 64-bit fingerprint of a loaded <see cref="ContentRegistry"/>. Two players
    /// with the same packs get the same fingerprint; a mod that changes any balance value changes
    /// it. Replays embed this so a run recorded with mods can't be "verified" against vanilla and
    /// falsely flagged as a desync (or vice-versa).
    ///
    /// The fingerprint hashes the ids and the numeric fields that affect the simulation. It does
    /// NOT hash purely cosmetic fields (displayName, sprite, tint) — a texture swap should not
    /// invalidate a replay.
    /// </summary>
    public static class ContentFingerprint
    {
        public static ulong Compute(ContentRegistry registry)
        {
            unchecked
            {
                ulong h = 1469598103934665603UL;
                const ulong prime = 1099511628211UL;

                // unchecked applied per-function: the outer 'unchecked' block does not reach into
                // nested local function bodies, so FNV multiplication must be marked here too.
                void MixStr(string s)
                {
                    unchecked
                    {
                        if (s == null) { h ^= 0xFF; h *= prime; return; }
                        foreach (char c in s) { h ^= c; h *= prime; }
                        h ^= 0x1D; h *= prime; // field separator
                    }
                }
                void MixF(float f)
                {
                    unchecked
                    {
                        h ^= (uint)System.BitConverter.SingleToInt32Bits(f);
                        h *= prime;
                    }
                }
                void MixI(int i) { unchecked { h ^= (ulong)(uint)i; h *= prime; } }

                // Enemies (order is the registry's stable insertion order).
                foreach (var id in registry.EnemyIds)
                {
                    var e = registry.GetEnemy(id);
                    MixStr(e.id);
                    MixF(e.maxHealth); MixF(e.moveSpeed); MixF(e.contactDamage);
                    MixF(e.radius); MixI(e.scoreValue); MixF(e.dropChance);
                    MixStr((e.behaviour ?? "").ToLowerInvariant());
                }

                foreach (var id in registry.WeaponIds)
                {
                    var w = registry.GetWeapon(id);
                    MixStr(w.id);
                    MixF(w.damage); MixF(w.fireRate); MixF(w.projectileSpeed);
                    MixF(w.projectileLifetime); MixI(w.projectilesPerShot);
                    MixF(w.spreadDegrees); MixI(w.pierce); MixF(w.projectileRadius);
                    MixF(w.rarityWeight);
                }

                foreach (var id in registry.RoomIds)
                {
                    var r = registry.GetRoom(id);
                    MixStr(r.id);
                    MixF(r.width); MixF(r.height); MixF(r.weight);
                    // Obstacle GEOMETRY is now simulated (entities collide with it, projectiles die
                    // on it), so it affects the run and must be part of the fingerprint — hashing
                    // only the count would let a mod slide a wall without invalidating replays.
                    MixI(r.obstacles.Count);
                    foreach (var o in r.obstacles)
                    {
                        MixF(o.x); MixF(o.y); MixF(o.width); MixF(o.height);
                    }
                    foreach (var wid in r.waveIds) MixStr(wid);
                }

                return h;
            }
        }
    }
}
