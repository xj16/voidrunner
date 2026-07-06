# VoidRunner replay format (`.vrplay`)

A VoidRunner replay is a small text file that reproduces an entire run. Because the simulation is
deterministic, a replay does **not** store positions, health, or any per-frame state — only the
information needed to re-derive the run:

```
run outcome = f(seed, contentFingerprint, inputStream)
```

The player (or a server, or CI) re-runs the simulation from the seed, feeds it the recorded inputs one
per fixed tick, and gets back a bit-identical run. A final state hash is stored so reproduction can be
*verified*.

## File shape

A `.vrplay` file is a single JSON object:

```json
{
  "magic": "VRPLAY",
  "version": 1,
  "seed": "10140842863430120355",
  "contentFingerprint": "3197613932402756164",
  "label": "COSMIC-DRIFT",
  "recordedAt": 1783376104,
  "tickCount": 755,
  "finalScore": 58,
  "finalRoom": 1,
  "finalStateHash": "10363620492745782005",
  "inputs": "AQAUMiUAAAEAF6oq...=="
}
```

| Field | Type | Meaning |
| --- | --- | --- |
| `magic` | string | Always `"VRPLAY"`. Lets tools sniff the format. |
| `version` | int | Format version. Current = `1`. Mismatches are refused. |
| `seed` | string | The 64-bit run seed. **Stored as a string** (see "Why strings" below). |
| `contentFingerprint` | string | 64-bit fingerprint of the content used (see [MODDING.md](MODDING.md)). |
| `label` | string | Human label — usually the seed text the player typed. |
| `recordedAt` | int | Unix seconds when recorded. Informational; not part of determinism. |
| `tickCount` | int | Number of simulation ticks (60 = 1 second). |
| `finalScore` / `finalRoom` | int | Recorded outcome, verified on replay. |
| `finalStateHash` | string | 64-bit hash of the full sim state at the end. The verification anchor. |
| `inputs` | string | Base64 of the run-length-encoded input stream (below). |

### Why 64-bit values are strings

JSON numbers are IEEE-754 doubles, which only hold 53 bits of integer precision. A raw numeric `seed`
or `finalStateHash` would silently lose its low bits on a round-trip and every replay would fail to
verify. Storing them as strings preserves all 64 bits exactly. (This project's own parser learned that
the hard way — there's a regression test for it.)

## Input stream encoding

The simulation consumes one `InputCommand` per tick. Each command is small and **quantised** so live
float jitter can't diverge a replay:

| Part | Range | Storage |
| --- | --- | --- |
| `MoveX`, `MoveY` | −100..100 (hundredths of an axis) | 1 signed byte each |
| `AimDegrees` | 0..359 whole degrees | 2 bytes (little-endian) |
| `Firing` | bool | 1 byte |

The stream is then **run-length encoded**: consecutive identical commands (very common — a player holds
a direction for many ticks) become a single `(count, command)` pair. Each pair is 7 bytes:

```
[count_lo][count_hi] [moveX][moveY] [aim_lo][aim_hi] [firing]
```

The whole byte array is Base64-encoded into the `inputs` string. In practice a multi-minute run is a
few kilobytes (the committed sample is a 755-tick run in ~7 KB).

## Verifying a replay

```bash
dotnet run --project tools/VrVerify -- verify Assets/StreamingAssets/ContentPacks docs/samples/cosmic-drift.vrplay
```

`vrverify`:

1. Loads the content packs and computes their fingerprint.
2. **Refuses** the replay if the fingerprint doesn't match (recorded with different mods) — no false
   desync.
3. Constructs a fresh `Simulation` from the seed and steps it through every recorded input.
4. Compares the reproduced score, room and state hash against the file.
5. Prints `PASS`/`FAIL` and exits `0`/`1` accordingly.

The same `ReplayVerifier` runs inside the game (to sanity-check a replay before playback) and inside the
CI, which records a run and verifies it end-to-end on every push.

## Stability guarantees

- A given `(version, seed, contentFingerprint, inputStream)` reproduces forever, across machines and OSes,
  as long as the `Sim` core is unchanged.
- **Changing simulation logic** (physics, AI, spawn timing) is a breaking change for old replays. If that
  happens, bump `version` and, if you care about old replays, keep the old sim behind the old version.
- **Cosmetic content changes** (renames, sprites, tints) do **not** change the fingerprint, so they don't
  break replays.
