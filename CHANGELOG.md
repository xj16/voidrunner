# Changelog

All notable changes to VoidRunner are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project aims to follow
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.1.0] — 2026-07-07

A determinism-and-showcase pass: the boldest README claims are now enforced by CI, the "parsed but
unused" features are finished, and the game is playable and verifiable in a browser with zero install.

### Added

- **Deterministic transcendentals (`DetMath`).** The simulation no longer calls the platform
  `Math.Sin/Cos/Atan2` (whose last bits differ across OS/CPU/runtime). It now uses fixed-coefficient
  float32 polynomials plus a hardware-independent IEEE float `Sqrt`, so a run is bit-identical on
  every machine — turning the README's "byte-identical on every machine" from an assertion into a
  property. Golden-vector tests pin the exact bits.
- **Cross-OS determinism matrix in CI.** `sim-tests` now runs on ubuntu, windows **and** macos; a
  new `cross-os-consensus` job fails the build unless all three produce the identical final-state
  hash for the same recorded run.
- **Obstacle collision (finishing a shipped-but-unused feature).** Room `obstacles` were parsed and
  fingerprinted but ignored by the sim. They are now solid: entities are pushed out / steer around
  them and projectiles die on impact. Fully deterministic; covered by tests.
- **Renderer interpolation, screenshake and hit-flash.** The view layer now uses the fractional-tick
  interpolation factor it previously ignored (smooth motion at any display rate), plus view-only
  screenshake and a hit-flash — none of which touch the deterministic sim.
- **Seed-of-the-day + verified local leaderboard.** A UTC-derived daily seed and a self-verifying
  high-score table keyed by `(seed, contentFingerprint)`: every entry stores its `.vrplay` and is
  only admitted if the replay reproduces the score under `ReplayVerifier`.
- **Pure-JavaScript core port + live browser demo.** `web/voidrunner-core.js` re-implements the RNG,
  `DetMath`, simulation, content loader, fingerprint and replay verifier in dependency-free JS that
  reproduces the C# core **bit-for-bit** (float32-exact via `Math.fround`). `web/parity.test.mjs`
  proves it against committed golden vectors and re-verifies a C#-recorded replay in JS. A
  self-contained static demo (`web/dist/index.html`, built by `web/build.mjs`) lets anyone play a
  seed, watch a verified replay, or live-edit a content pack in the browser.
- **WebGL build + GitHub Pages deploy.** `BuildScript.BuildWebGL` and a GameCI WebGL target; the
  static JS demo is built in CI and published to GitHub Pages on every push to `main`. `GameController`
  honours `?seed=…` / `?daily=1` deep links.
- **Code coverage.** `dotnet test` now collects Cobertura coverage (coverlet); CI reports the line
  rate (~95% of the engine-agnostic core) in the job summary. Coverage badge added to the README.
- **Flagship tests.** New suites for obstacle collision, `DetMath` golden vectors + accuracy, a
  multi-seed determinism soak, a heavy-wave per-tick performance budget, the leaderboard/daily seed,
  and adversarial hardening (nesting bombs, fuzzed JSON, hostile replays). Test count 54 → 85.

### Changed

- **Content fingerprint now hashes obstacle geometry.** Because obstacles are simulated, moving a
  wall changes the run and must invalidate a cross-fingerprint replay. (The committed sample replay
  was regenerated accordingly.)
- `ReplayCodec.Deserialize` now presents a single `FormatException` for any malformed replay input
  (previously it could surface a raw `JsonParseException`).

### Security

- **Untrusted-input hardening.** The JSON parser caps nesting depth (rejecting stack-blowing bombs);
  the replay decoder bounds the expanded tick count and record count before allocating (rejecting
  a tiny RLE blob that claims billions of ticks); the content loader rejects non-finite/out-of-range
  numbers and caps `projectilesPerShot`. All covered by hostile-input tests.

### Notes

- No Unity licence is required for CI: the deterministic core is built and tested on a plain .NET 8
  runner, and the browser demo is pure JS. The Unity player/WebGL build remains optional and gated
  behind a free licence secret.

## [1.0.0] — 2026-07-07

- Initial public release: data-driven, moddable Unity 2D roguelite with an engine-agnostic
  deterministic core, xoshiro256\*\* RNG, content packs with dependencies/overrides, `.vrplay`
  record/replay/verify, the `vrverify` CLI, and CI that builds+tests the core on plain .NET.

[1.1.0]: https://github.com/xj16/voidrunner/releases/tag/v1.1.0
[1.0.0]: https://github.com/xj16/voidrunner/releases/tag/v1.0.0
