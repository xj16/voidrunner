# Modding VoidRunner

Everything that makes VoidRunner a game — its enemies, weapons, rooms and enemy waves — lives in
**content packs**: folders of JSON under `Assets/StreamingAssets/ContentPacks/`. This document is the
complete field reference and the rules for how packs combine.

No programming and no rebuild are required. Edit a `.json`, re-enter play mode (or run the validator),
done.

---

## Anatomy of a pack

```
ContentPacks/
└─ my-mod/
   ├─ pack.json        (required — the manifest)
   ├─ content.json     (your content; name it whatever, list it in pack.json)
   └─ drifter.png      (optional art referenced by "sprite")
```

### `pack.json` (manifest)

| Field | Type | Required | Meaning |
| --- | --- | --- | --- |
| `id` | string | ✅ | Unique, lowercase, hyphenated id (`my-mod`). Used for load-order and overrides. |
| `name` | string |  | Display name. |
| `author` | string |  | Your name/handle. |
| `version` | string |  | Semver-ish, informational. |
| `description` | string |  | Shown in tooling. |
| `format` | int |  | Content format version. Current = `1`. |
| `dependencies` | string[] |  | Ids of packs that must load **before** this one. |
| `files` | string[] | ✅ | Content JSON files to load, **in order**, relative to the pack folder. |

The loader topologically sorts packs by their dependencies, so a pack listing `"dependencies": ["base"]`
is always loaded after `base`. A missing dependency or a dependency cycle is reported as an error and
the offending pack is skipped rather than crashing the game.

---

## Content files

A content file is a JSON object with any of these top-level arrays: `enemies`, `weapons`, `rooms`,
`waves`. You can split content across multiple files or put it all in one — order within a pack is the
order in `files`.

The parser is lenient in author-friendly ways: it accepts `// line` and `/* block */` comments and
top-level formatting you'd expect, and it reports the **exact line and column** of a syntax error.

### Enemies

```json
{
  "enemies": [
    {
      "id": "drifter",
      "displayName": "Drifter",
      "sprite": "circle",
      "tint": "#7FB4FF",
      "maxHealth": 8,
      "moveSpeed": 2.6,
      "contactDamage": 6,
      "radius": 0.42,
      "behaviour": "chase",
      "scoreValue": 10,
      "dropChance": 0.12
    }
  ]
}
```

| Field | Type | Default | Notes |
| --- | --- | --- | --- |
| `id` | string | — (required) | Unique enemy id. Re-using a base id **overrides** it. |
| `displayName` | string | = id | Cosmetic. |
| `sprite` | string | `circle` | Procedural shape (`circle`, `square`, `triangle`, `diamond`, `ring`) or a PNG name in the pack. |
| `tint` | hex | `#FFFFFF` | `#RRGGBB` or `#RRGGBBAA`. |
| `maxHealth` | float | 10 | Clamped to ≥ 1 (with a warning) if ≤ 0. |
| `moveSpeed` | float | 2 | World units/second. |
| `contactDamage` | float | 1 | Damage to the player on touch. |
| `radius` | float | 0.4 | Collision radius. |
| `behaviour` | string | `chase` | One of `chase`, `kite`, `charger`, `wander`. Unknown → `chase` (warning). |
| `scoreValue` | int | 10 | Score on kill. |
| `dropChance` | float | 0.1 | Probability [0..1] of a pickup drop. |

**Behaviours**

- `chase` — walks straight at the player.
- `kite` — holds a preferred distance and strafes (ranged/annoying).
- `charger` — dashes in bursts toward the player's last locked position.
- `wander` — ignores the player, drifts on a randomly re-rolled heading.

### Weapons

```json
{
  "weapons": [
    {
      "id": "scatter", "displayName": "Scattergun", "sprite": "bolt", "tint": "#FFC46B",
      "damage": 2.5, "fireRate": 2.2, "projectileSpeed": 13, "projectileLifetime": 0.55,
      "projectilesPerShot": 6, "spreadDegrees": 42, "pierce": 0, "projectileRadius": 0.11,
      "rarityWeight": 3
    }
  ]
}
```

| Field | Type | Default | Notes |
| --- | --- | --- | --- |
| `id` | string | — (required) | Unique weapon id. |
| `damage` | float | 3 | Per projectile hit. |
| `fireRate` | float | 3 | Shots/second. Clamped to ≥ 0.1. |
| `projectileSpeed` | float | 12 | World units/second. |
| `projectileLifetime` | float | 1.2 | Seconds before a projectile despawns. |
| `projectilesPerShot` | int | 1 | > 1 makes a spread/shotgun. |
| `spreadDegrees` | float | 0 | Total cone across all projectiles. `360` = nova ring. |
| `pierce` | int | 0 | Enemies a projectile passes through before dying. |
| `projectileRadius` | float | 0.15 | Projectile collision radius. |
| `rarityWeight` | float | 1 | Higher = drops more often. The **highest**-weight weapon is the starting weapon. |

### Waves & rooms

Waves group enemy spawns; rooms bundle a play area with the waves that can spawn in it.

```json
{
  "waves": [
    { "id": "w_intro", "groups": [
      { "enemyId": "drifter", "count": 4, "delay": 0.0 },
      { "enemyId": "mote",    "count": 2, "delay": 1.5 }
    ]}
  ],
  "rooms": [
    { "id": "hangar", "displayName": "Derelict Hangar", "width": 26, "height": 15,
      "backgroundTint": "#0B0F1E", "weight": 3,
      "waveIds": ["w_intro"],
      "obstacles": [ { "x": -6, "y": 3, "width": 2, "height": 2 } ] }
  ]
}
```

- A wave `group` = `enemyId` + `count` + `delay` (seconds after the wave starts).
- A room's `weight` biases how often the run picks it. `width`/`height` are the interior in world units
  (clamped to sensible minimums). `obstacles` are decorative rectangles.
- `waveIds` must reference waves that exist **somewhere across all loaded packs** — a dangling
  reference is a hard error, so a run can never try to spawn something that isn't defined.

Difficulty scales automatically with depth: deeper rooms repeat waves more and buff enemy health/speed.

---

## Overrides & the content fingerprint

- **Last write wins.** If two packs declare the same `id`, the one loaded later replaces it. This is
  how a mod re-balances base content — just re-declare the id with new numbers.
- **Fingerprint.** The game computes a 64-bit fingerprint of all *gameplay-affecting* fields (ids and
  numbers, **not** cosmetic `displayName`/`sprite`/`tint`). Replays embed it, so a replay recorded with
  your mod won't be falsely "verified" against vanilla — and a pure texture/name swap won't invalidate
  existing replays. See [`REPLAY_FORMAT.md`](REPLAY_FORMAT.md).

## Validating your pack

- **In Unity:** menu **VoidRunner ▸ Validate Content Packs**. Runs the real loader and prints the load
  order, totals, the fingerprint, and every error/warning.
- **Command line:** `dotnet run --project tools/VrVerify -- info Assets/StreamingAssets/ContentPacks`
  (or against `vrverify.dll` after a build).

## Distributing a pack

Zip your pack folder and share it. A player drops it into their
`…/VoidRunner_Data/StreamingAssets/ContentPacks/` (or the project's `Assets/StreamingAssets/ContentPacks/`
in the editor) and it appears next launch. To play vanilla, they delete or move the folder.
