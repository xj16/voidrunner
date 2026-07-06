# Building & CI

VoidRunner has two independently buildable layers, and the CI reflects that:

1. **The deterministic core** (`Assets/Scripts/Sim`) — engine-agnostic C#. Builds and tests on a plain
   **.NET 8 SDK**, no Unity required. This is the always-on CI job.
2. **The Unity player** — the view/input layer plus the core. Needs the Unity Editor. Built in CI via
   **GameCI**, gated behind a free Unity licence secret so the pipeline is green without it.

## Local: core only (no Unity)

Requires the free [.NET 8 SDK](https://dotnet.microsoft.com/download).

```bash
# Unit tests (RNG, JSON, content loader, determinism, replay round-trip, shipped-pack smoke tests):
dotnet test ci/HeadlessTests/HeadlessTests.csproj -c Release

# CLI: record and verify a replay against the shipped content:
dotnet build tools/VrVerify/VrVerify.csproj -c Release
BIN=tools/VrVerify/bin/Release/net8.0/vrverify.dll
dotnet "$BIN" record Assets/StreamingAssets/ContentPacks COSMIC-DRIFT 1800 run.vrplay
dotnet "$BIN" verify Assets/StreamingAssets/ContentPacks run.vrplay
```

Both the test project and the CLI compile the *same* `Assets/Scripts/Sim/**/*.cs` sources the Unity
game uses (via a glob in their `.csproj`), so passing here means the real game logic is correct.

## Local: full game (Unity)

1. Install **Unity 2022.3 LTS** through Unity Hub (Personal licence is free).
2. Open the repo folder as a project. Unity imports assets and generates `Library/`, `.sln`, `.csproj`
   (all git-ignored) and the `.meta` files for anything missing.
3. Open `Assets/Scenes/Main.unity` → **Play**.
4. Build a player from **VoidRunner ▸ Build ▸ Standalone (current platform)**, or headless:

   ```
   Unity -batchmode -quit -projectPath . -executeMethod VoidRunner.EditorTools.BuildScript.BuildLinux
   ```

## CI

[`.github/workflows/ci.yml`](../.github/workflows/ci.yml) has two jobs.

### `sim-tests` (always runs, no secrets)

- Sets up the .NET 8 SDK.
- Builds and runs the NUnit tests.
- Builds `vrverify`, then does an **end-to-end** check: loads the shipped packs, records a 2400-tick bot
  run, and verifies it reproduces — plus verifies the committed sample replay.

This job needs nothing but the public repo, so forks and PRs get real signal for free.

### `unity-build` (optional, gated)

Runs only when a `UNITY_LICENSE` secret is present. It uses GameCI to:

- run the EditMode tests inside a real Unity install, and
- build a Linux standalone player and upload it as an artifact.

When the secret is absent the job logs that it's skipping and exits green — **no failure**, so the
default repo stays healthy without any licence.

#### Enabling the Unity job

1. Get a **free Unity Personal** licence file:
   - Locally, activate Unity once, or use GameCI's activation flow:
     <https://game.ci/docs/github/activation>.
2. Add three repository secrets (Settings → Secrets and variables → Actions):
   - `UNITY_LICENSE` — the contents of your `.ulf` licence file.
   - `UNITY_EMAIL` — your Unity account email.
   - `UNITY_PASSWORD` — your Unity account password.
3. Push. The `unity-build` job now activates, tests and builds a player.

Everything here is free: Unity Personal, GitHub Actions minutes on a public repo, and the .NET SDK.

## Notes on `.meta` files

Unity generates a `.meta` for every asset. This repo commits the load-bearing ones (the boot scene and
the `GameController` script, so the scene→script link survives a fresh clone) and lets Unity regenerate
the rest on first import. That's normal and safe for a source-only project.
