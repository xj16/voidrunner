# VoidRunner

**A data-driven Unity 2D roguelite with a byte-for-byte deterministic core — enemies, weapons and rooms are pure JSON, every run is reproducible from a seed, and every run records to a tiny replay you can verify without trusting the client. Play it in your browser right now.**

[![CI](https://github.com/xj16/voidrunner/actions/workflows/ci.yml/badge.svg)](https://github.com/xj16/voidrunner/actions/workflows/ci.yml)
[![Coverage](https://img.shields.io/badge/sim%20core%20coverage-~95%25-2ea44f.svg)](#how-its-tested)
[![Determinism](https://img.shields.io/badge/determinism-ubuntu%20%C2%B7%20windows%20%C2%B7%20macos-5ad1ff.svg)](#how-determinism-is-guaranteed-the-short-version)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![Unity 2022.3 LTS](https://img.shields.io/badge/Unity-2022.3%20LTS-black.svg)](https://unity.com/releases/lts)

> **▶ Play in the browser:** [xj16.github.io/voidrunner](https://xj16.github.io/voidrunner/) — the entire game runs client-side via a
> pure-JS port of the engine that reproduces the C# simulation *to the bit*. No install, no engine, no server.
> Deep-link a run with `?seed=COSMIC-DRIFT`. (Published by CI once GitHub Pages is enabled; the bundle also lives in [`web/dist/`](web/dist).)

---

## What it is

VoidRunner is a top-down twin-stick roguelite: dive through procedurally-chosen rooms, clear waves of enemies, grab weapon and heal drops, and go as deep as you can before you die. It is built around two ideas that most small game projects skip:

1. **Everything is data.** Enemies, weapons, rooms and enemy waves are defined in plain JSON files inside *content packs*. There is no enemy class per enemy, no weapon prefab per weapon. Want a faster hot-pink swarmer or a piercing beam rifle? Edit a number in a `.json` file. Want a whole new monster? Add an object to an array. **No C# is recompiled, no editor is required** — drop a folder in and it shows up in the game.

2. **Runs are deterministic and replayable.** The entire game simulation is a pure, engine-agnostic C# core driven by a custom seeded RNG on a fixed 60 Hz timestep. Given the same seed, the same content and the same inputs, the run unfolds **identically on every machine**. That means: type a seed and share the exact same run with a friend; record a run as a tiny (~KB) replay file and play it back or hand it to a tournament to verify a score without trusting the client.

> The determinism isn't a claim — it's tested. The CI records a run, serialises it, and replays it headlessly, asserting the final state hash matches to the bit. See [`ci.yml`](.github/workflows/ci.yml) and the [tests](Assets/Scripts/Tests).

## Why this design

Small roguelites live or die on *content velocity* and *fairness*.

- **Content velocity:** by making content data instead of code, a designer (or a modder who can't program) iterates in seconds and the game never needs a rebuild. The same loader that ships the base game loads community packs, so mods are first-class, not bolted on.
- **Fairness & shareability:** a deterministic core turns "I got a great run" into a shareable artifact. Seeds make runs reproducible; replays make scores verifiable. This is the same architecture used by competitive roguelikes and fighting games, applied to a small, readable codebase you can actually learn from.

It's also just a *clean architecture* to study: the game logic has **zero UnityEngine dependencies**, so it compiles and runs — and is unit-tested — in plain .NET. Unity is only the view/input skin on top.

## Features

- **Play in the browser, no install** — a dependency-free JS port of the core (`web/voidrunner-core.js`) runs the game client-side and reproduces the C# simulation *bit-for-bit*. The static demo lets you play a seed, watch a **verified** replay, or live-edit a content pack and see the validator + a bot preview instantly.
- **Data-driven content packs** — enemies, weapons, rooms, waves in JSON. Hot-swappable folders under `StreamingAssets/ContentPacks/`.
- **Modding with dependencies & overrides** — a pack can depend on another (topologically load-ordered), and re-declaring an id *overrides* base content, so mods re-balance the base game or add brand-new content. Ships with a working example mod (`neon-swarm`).
- **Deterministic seed-based runs, proven across OSes** — custom xoshiro256\*\* RNG + software-defined float transcendentals (no platform `libm`), fixed timestep, no `UnityEngine.Random`. CI records the same run on ubuntu/windows/macos and fails unless all three hashes match to the bit.
- **Replay recording, sharing & verification** — compact, human-inspectable `.vrplay` files (JSON header + run-length-encoded input stream). A content **fingerprint** stops a replay from a different mod set silently desyncing. A C#-recorded replay even re-verifies in the browser JS core.
- **Seed-of-the-day + self-verifying leaderboard** — a UTC daily seed everyone shares, and a local high-score table where each entry carries its `.vrplay` and is only accepted if it actually reproduces — so a score can't be faked.
- **Solid rooms** — obstacles are real cover: entities collide with and steer around them, projectiles die on them.
- **Four AI behaviours** — chase, kite, charger, wander — all selected by a string in the enemy data.
- **Juice** — interpolated motion between fixed ticks, screen-scaling camera, i-frames with hit-flash, screenshake, knockback, weapon variety (single-shot, shotgun spread, piercing lance, 360° nova), between-room heals, difficulty scaling with depth.
- **Hardened against hostile input** — the JSON parser, content loader and replay decoder treat shared packs/replays as untrusted: nesting-depth caps, NaN/Inf rejection, and bounded allocation so a malicious file can't OOM or hang the verifier.
- **No binary assets required** — fully playable with runtime-generated procedural sprites. An optional Blender script renders nicer emissive art if you want it.
- **In-editor content validator** — `VoidRunner ▸ Validate Content Packs` runs the exact game loader and prints precise errors/warnings for modders.
- **`vrverify` CLI** — load packs, record bot runs, and verify replays from the command line.
- **Real CI without a paid licence** — the deterministic core is built and tested on a plain .NET runner (with coverage); the optional Unity player + WebGL build is gated behind a free licence secret and skipped otherwise.

## How a run plays

```
WASD / Arrows ....... move
Mouse ............... aim
Hold LMB / Space .... fire
R ................... restart (new run from the current seed field)
S (on game over) .... save the replay
Esc ................. back to menu
```

From the menu, type a seed (or leave it blank for random), press **Start Run**, or **Watch last replay** to re-run your most recent saved run.

---

## Repository layout

```
voidrunner/
├─ Assets/
│  ├─ Scripts/
│  │  ├─ Sim/                     ← engine-agnostic core (NO UnityEngine; unit-tested in plain .NET)
│  │  │  ├─ Rng/                  ← DeterministicRandom (xoshiro256** + SplitMix64)
│  │  │  ├─ Data/                 ← content schema (POCOs the JSON deserializes into)
│  │  │  ├─ Content/              ← JSON parser, loader/validator, registry, fingerprint
│  │  │  ├─ Core/                 ← Vec2 math, DetMath (deterministic trig/sqrt), InputCommand, Simulation
│  │  │  ├─ Replay/               ← replay data, codec (serialize/deserialize), verifier
│  │  │  ├─ Meta/                 ← seed-of-the-day + self-verifying local leaderboard
│  │  │  └─ Modding/              ← pack discovery + dependency-ordered loading
│  │  ├─ Gameplay/                ← Unity view layer (MonoBehaviours, renderer, input, HUD)
│  │  ├─ Editor/                  ← content validator window + CI build script
│  │  └─ Tests/                   ← NUnit tests (run in Unity AND in the headless harness)
│  ├─ StreamingAssets/
│  │  └─ ContentPacks/
│  │     ├─ base/                 ← the base game content (enemies/weapons/rooms/waves)
│  │     └─ example-mod/          ← "neon-swarm" — depends on base, overrides + adds content
│  └─ Scenes/Main.unity           ← the single boot scene
├─ Packages/manifest.json         ← Unity package manifest
├─ ProjectSettings/               ← Unity project settings
├─ ci/HeadlessTests/              ← .NET test project compiling the Sim sources + tests
├─ tools/VrVerify/                ← `vrverify` CLI (record/verify replays headlessly)
├─ web/                           ← pure-JS core port + browser demo (voidrunner-core.js, parity.test.mjs, dist/)
├─ art/blender/generate_sprites.py← optional Blender sprite renderer
├─ docs/                          ← modding guide, replay format, build guide, sample replay
├─ CHANGELOG.md                   ← Keep-a-Changelog history
└─ .github/workflows/ci.yml       ← CI
```

## Tech stack

| Area | Tech |
| --- | --- |
| Engine | **Unity** 2022.3 LTS (2D, URP-agnostic, legacy Input Manager, IMGUI for UI) |
| Language | **C#** 9 / **.NET** Standard 2.1 (Unity) & .NET 8 (headless tests/tools) |
| Simulation | Custom deterministic core, xoshiro256\*\* RNG, fixed-timestep |
| Data | Hand-rolled dependency-free JSON parser (top-level arrays, comments, precise errors) |
| Art (optional) | **Blender** 3.x/4.x Python (`bpy`) sprite renderer; procedural sprites at runtime otherwise |
| Tooling | `vrverify` .NET CLI |
| CI | **GitHub Actions** — .NET build+test always; GameCI Unity build when a free licence secret is present |

---

## Getting started

### Play it in the Unity Editor

1. Install **Unity 2022.3 LTS** (any 2022.3.x patch) via Unity Hub. The free Personal licence is fine.
2. Open this folder as a project in Unity Hub. Unity will import assets and generate the `Library/` and `.csproj` files (both git-ignored).
3. Open `Assets/Scenes/Main.unity` and press **Play**.

The `Game` GameObject carries a single `GameController` component that self-assembles the camera, renderer, input and content at runtime, so there's nothing else to wire up.

### Build a standalone player

- In-editor: menu **VoidRunner ▸ Build ▸ Standalone (current platform)**.
- Headless / CI: `Unity -batchmode -quit -projectPath . -executeMethod VoidRunner.EditorTools.BuildScript.BuildLinux`

See [`docs/BUILDING.md`](docs/BUILDING.md) for the full CI/GameCI setup (including the free Unity licence).

### Run the deterministic core WITHOUT Unity

You don't need Unity to build or test the game logic — the simulation is engine-agnostic.

```bash
# Requires the .NET 8 SDK (free).
dotnet test ci/HeadlessTests/HeadlessTests.csproj -c Release

# Build the CLI and drive a full record → verify loop against the shipped packs:
dotnet build tools/VrVerify/VrVerify.csproj -c Release
BIN=tools/VrVerify/bin/Release/net8.0/vrverify.dll
dotnet "$BIN" info   Assets/StreamingAssets/ContentPacks
dotnet "$BIN" record Assets/StreamingAssets/ContentPacks COSMIC-DRIFT 1800 run.vrplay
dotnet "$BIN" verify Assets/StreamingAssets/ContentPacks run.vrplay
```

A committed sample replay lives at [`docs/samples/cosmic-drift.vrplay`](docs/samples/cosmic-drift.vrplay); CI verifies it on every push.

### Play / build the browser demo (no Unity, no engine)

```bash
# Prove the JS core reproduces the C# core bit-for-bit (and re-verifies a C#-recorded replay in JS):
node web/parity.test.mjs

# Build the self-contained static bundle, then open it — works from file:// too:
node web/build.mjs && open web/dist/index.html   # or just double-click web/dist/index.html
```

The bundle is a single ~68 KB HTML file with the core, the content packs and the sample replay
inlined. It is the same simulation as the game — see [Architecture](#architecture).

---

## Architecture

```
                    ┌──────────────────────────────────────────────┐
                    │  Sim core  (Assets/Scripts/Sim, NO UnityEngine)│
                    │  RNG · DetMath · Simulation · Content · Replay │
                    └──────────────────────────────────────────────┘
                        ▲              ▲               ▲            ▲
        ┌───────────────┘              │               │            └────────────────┐
   Unity view layer            .NET headless          vrverify CLI            Pure-JS port
   (MonoBehaviours,            tests (NUnit,           (record / verify        (web/voidrunner-core.js
    renderer, HUD)             plain .NET 8)            replays)                → browser demo)
```

The entire game *logic* lives in one engine-agnostic C# module with **zero UnityEngine references**.
Four independent front-ends drive that same code:

- **Unity** is only the view/input skin — it owns a `Simulation`, pushes quantised input in, and reads
  public state to draw sprites.
- **The .NET test harness** compiles the *exact same source files* and unit-tests them on any runner —
  no licence, no editor, no GPU.
- **`vrverify`** shares those sources to record and verify replays from the command line.
- **The pure-JS port** re-implements the core in dependency-free JavaScript that reproduces it
  bit-for-bit (float32-exact via `Math.fround`; the 64-bit RNG uses `BigInt`), so the game runs and a
  C#-recorded replay verifies **in a browser**. A CI job asserts this parity against committed golden
  vectors on every push.

Determinism is the load-bearing invariant everywhere: `run outcome = f(seed, contentFingerprint, inputStream)`.

## How it's tested

- **85 unit tests** over the hardest, most-advertised subsystems — the deterministic simulation,
  obstacle collision, the RNG, the JSON/loader/fingerprint pipeline, replay round-trip + verification,
  the leaderboard, and adversarial/hostile inputs. Run: `dotnet test ci/HeadlessTests/HeadlessTests.csproj`.
- **Golden-vector math tests** pin the exact 32-bit bits of `DetMath.Sin/Cos/Sqrt/Atan2`, so a platform
  that computes any of them differently fails CI.
- **A determinism soak** records dozens of seeds over thousands of ticks and asserts every replay
  re-verifies; a **performance test** holds a heavy modded wave under a per-tick budget.
- **Cross-OS consensus** in CI (ubuntu/windows/macos) fails unless all three agree on the run's final
  hash to the bit.
- **JS↔C# parity** (`web/parity.test.mjs`) asserts the browser core reproduces committed C# golden
  hashes and re-verifies a C#-recorded replay.
- **Coverage** (~95% of the engine-agnostic core) is collected on every CI run.

---

## Making a content pack (modding)

A pack is a folder with a `pack.json` manifest and one or more content JSON files. Minimal example:

```
Assets/StreamingAssets/ContentPacks/my-mod/
├─ pack.json
└─ content.json
```

`pack.json`:

```json
{
  "id": "my-mod",
  "name": "My Mod",
  "author": "you",
  "version": "1.0.0",
  "dependencies": ["base"],
  "files": ["content.json"]
}
```

`content.json` (override a base enemy AND add a new one):

```json
{
  "enemies": [
    { "id": "drifter", "maxHealth": 20, "tint": "#FF00AA" },
    { "id": "brute", "displayName": "Brute", "sprite": "square",
      "maxHealth": 40, "moveSpeed": 1.4, "contactDamage": 12,
      "behaviour": "charger", "scoreValue": 60, "dropChance": 0.5 }
  ]
}
```

Because `my-mod` depends on `base`, the loader loads base first, then applies your file on top. Re-declaring `drifter` **overrides** it; `brute` is **new**. Run **VoidRunner ▸ Validate Content Packs** (or `vrverify info`) to check it before playing.

Full field reference and the override/fingerprint rules: [`docs/MODDING.md`](docs/MODDING.md).
Replay file format: [`docs/REPLAY_FORMAT.md`](docs/REPLAY_FORMAT.md).

## Optional: nicer art from Blender

The game ships with no binary art and generates crisp procedural sprites at runtime. To replace them with rendered emissive shapes:

```bash
blender --background --python art/blender/generate_sprites.py -- --out Assets/StreamingAssets/ContentPacks/base
```

Then reference a PNG by name in your content (`"sprite": "drifter"`). Blender uses its bundled Python — no `pip` installs needed.

---

## How determinism is guaranteed (the short version)

- The RNG is a self-contained xoshiro256\*\* implementation seeded via SplitMix64 — **not** `UnityEngine.Random` (which is global and version-dependent).
- The simulation never calls the platform `Math.Sin/Cos/Atan2` (whose last bits vary across OS/CPU/runtime). It uses `DetMath`: fixed-coefficient float32 polynomials and a hardware-independent IEEE float `Sqrt`, built from only the correctly-rounded IEEE-754 operations — so the transcendentals are identical everywhere. Golden-vector tests pin the exact bits and a cross-OS CI matrix re-checks it.
- The simulation advances only in fixed `1/60 s` steps, consuming exactly one quantised `InputCommand` per step. Rendering interpolates between steps but never mutates state.
- Inputs are quantised (movement to hundredths, aim to whole degrees) so tiny float jitter in live input can't diverge a replay.
- A run's outcome is `f(seed, contentFingerprint, inputStream)`. A replay stores exactly those, plus a final state hash used to verify reproduction.

Read the annotated core in [`Assets/Scripts/Sim/Core/Simulation.cs`](Assets/Scripts/Sim/Core/Simulation.cs).

## Contributing

Issues and PRs welcome — especially content packs. The `Sim` core has no Unity dependency, so please keep it that way (the `VoidRunner.Sim.asmdef` enforces `noEngineReferences`), and add a test in `Assets/Scripts/Tests` for any core change.

## License

MIT — see [LICENSE](LICENSE). Copyright (c) 2026 xj16.
