using System;
using System.IO;
using UnityEngine;
using VoidRunner.Content;
using VoidRunner.Core;
using VoidRunner.Gameplay.UI;
using VoidRunner.Replay;
using VoidRunner.Rng;

namespace VoidRunner.Gameplay
{
    /// <summary>
    /// The single MonoBehaviour that runs VoidRunner. Attach it to one GameObject in the boot scene
    /// (see <c>Assets/Scenes/Main.unity</c>) and it self-assembles everything else at runtime:
    /// loads content packs, builds the sprite factory + renderer, reads input, advances the
    /// deterministic simulation on a fixed accumulator, draws the HUD, and records/plays replays.
    ///
    /// State machine: Menu → Playing → GameOver, plus a Replaying state entered from the menu.
    ///
    /// Determinism is preserved by advancing the sim only in fixed 1/60 s steps (accumulator
    /// pattern), feeding exactly one recorded/live input per step. Rendering interpolates between
    /// steps for smoothness but never touches sim state.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GameController : MonoBehaviour
    {
        private enum State { Loading, Menu, Playing, GameOver, Replaying }

        [Header("Seed")]
        [Tooltip("Optional starting seed text. Leave blank for a random seed each run.")]
        public string startSeed = "";

        private State _state = State.Loading;

        private ContentRegistry _registry;
        private ulong _fingerprint;
        private bool _contentOk;
        private string _contentError = "";

        private SpriteFactory _sprites;
        private SimRenderer _renderer;
        private InputReader _input;
        private Hud _hud;
        private Camera _camera;
        private Transform _entityRoot;

        private Simulation _sim;
        private ReplayRecorder _recorder;
        private ReplayData _playbackReplay;
        private int _playbackTick;

        private string _seedText = "";
        private double _accumulator;

        // Menu text-entry buffer for the seed.
        private string _menuSeedField = "";

        private void Awake()
        {
            Application.targetFrameRate = 120;
            _camera = Camera.main;
            if (_camera == null)
            {
                var camGo = new GameObject("Main Camera");
                _camera = camGo.AddComponent<Camera>();
                _camera.tag = "MainCamera";
            }
            _camera.orthographic = true;
            _camera.orthographicSize = 8f;
            _camera.backgroundColor = new Color(0.02f, 0.02f, 0.04f);
            _camera.transform.position = new Vector3(0, 0, -10);

            _entityRoot = new GameObject("Entities").transform;
            _sprites = new SpriteFactory();
            _hud = new Hud();
            _input = new InputReader(_camera);

            LoadContent();
        }

        private void LoadContent()
        {
            var outcome = RuntimeContent.Load();
            _contentOk = outcome.Ok;
            _registry = outcome.Registry;
            _fingerprint = outcome.Fingerprint;

            if (!_contentOk)
            {
                _contentError = outcome.Errors.Count > 0 ? outcome.Errors[0] : "unknown content error";
                _state = State.Loading; // stay on the error screen
                return;
            }

            _renderer = new SimRenderer(_entityRoot, _sprites, _registry);
            _menuSeedField = startSeed;
            _state = State.Menu;

            // Deep-link support for the embeddable WebGL demo: a URL like
            //   index.html?seed=COSMIC-DRIFT      → boots straight into that seed
            //   index.html?daily=1                → boots the seed-of-the-day
            // lets a portfolio link a visitor into an exact run with zero clicks. Harmless off the web.
            TryHandleUrlParams();
        }

        /// <summary>Parses the launch URL (WebGL) for a seed/daily deep-link and auto-starts a run.</summary>
        private void TryHandleUrlParams()
        {
            string url = Application.absoluteURL;
            if (string.IsNullOrEmpty(url) || url.IndexOf('?') < 0) return;

            string query = url.Substring(url.IndexOf('?') + 1);
            string seedParam = null;
            bool daily = false;
            foreach (var pair in query.Split('&'))
            {
                int eq = pair.IndexOf('=');
                string key = eq >= 0 ? pair.Substring(0, eq) : pair;
                string val = eq >= 0 ? Uri.UnescapeDataString(pair.Substring(eq + 1)) : "";
                if (key == "seed") seedParam = val;
                else if (key == "daily" && (val == "1" || val == "true")) daily = true;
            }

            if (daily)
            {
                var now = DateTime.UtcNow;
                _menuSeedField = VoidRunner.Meta.DailySeed.LabelFor(now.Year, now.Month, now.Day);
                StartRunFromField();
            }
            else if (!string.IsNullOrEmpty(seedParam))
            {
                _menuSeedField = seedParam;
                StartRunFromField();
            }
        }

        // -----------------------------------------------------------------------------------------
        // Run lifecycle
        // -----------------------------------------------------------------------------------------

        private void StartRun(ulong seed, string seedLabel)
        {
            _seedText = seedLabel;
            _sim = new Simulation(_registry, seed);
            _recorder = new ReplayRecorder(seed, _fingerprint, seedLabel);
            _accumulator = 0;
            _renderer.DrawFloor(_sim.CurrentRoom);
            _state = State.Playing;
        }

        private void StartRunFromField()
        {
            ulong seed;
            string label;
            if (string.IsNullOrWhiteSpace(_menuSeedField))
            {
                seed = (ulong)Guid.NewGuid().GetHashCode() ^ ((ulong)DateTime.UtcNow.Ticks << 16);
                label = "random:" + (seed & 0xFFFFFF).ToString("X6");
            }
            else
            {
                var r = DeterministicRandom.FromString(_menuSeedField.Trim());
                seed = r.Seed;
                label = _menuSeedField.Trim();
            }
            StartRun(seed, label);
        }

        private void StartReplay(ReplayData replay)
        {
            var verify = ReplayVerifier.Verify(replay, _registry, _fingerprint);
            if (!verify.Reproduced)
            {
                Debug.LogWarning($"[VoidRunner] replay may desync: {verify.Message}");
            }

            _playbackReplay = replay;
            _playbackTick = 0;
            _sim = new Simulation(_registry, replay.Seed);
            _seedText = string.IsNullOrEmpty(replay.Label) ? replay.Seed.ToString() : replay.Label;
            _accumulator = 0;
            _renderer.DrawFloor(_sim.CurrentRoom);
            _state = State.Replaying;
        }

        // -----------------------------------------------------------------------------------------
        // Update loop (fixed-step accumulator)
        // -----------------------------------------------------------------------------------------

        private void Update()
        {
            switch (_state)
            {
                case State.Playing: TickPlaying(); break;
                case State.Replaying: TickReplaying(); break;
                case State.GameOver: HandleGameOverInput(); break;
                case State.Menu: HandleMenuInput(); break;
            }

            if (_state == State.Playing || State.Replaying == _state || _state == State.GameOver)
            {
                if (_sim != null && _renderer != null)
                {
                    _renderer.DrawFloor(_sim.CurrentRoom);
                    _renderer.Render(_sim, (float)(_accumulator / Simulation.FixedDeltaTime));
                    FollowCamera();
                }
            }
        }

        private void TickPlaying()
        {
            _accumulator += Time.deltaTime;
            int guard = 0;
            while (_accumulator >= Simulation.FixedDeltaTime && guard++ < 8)
            {
                _accumulator -= Simulation.FixedDeltaTime;
                var cmd = _input.Read(new Vector2(_sim.Player.Position.X, _sim.Player.Position.Y));
                _recorder.Record(cmd);
                _sim.Step(cmd);
                if (_sim.RunOver)
                {
                    _recorder.Finish(_sim);
                    _state = State.GameOver;
                    break;
                }
            }

            if (Input.GetKeyDown(KeyCode.R)) StartRunFromField();
            if (Input.GetKeyDown(KeyCode.Escape)) _state = State.Menu;
        }

        private void TickReplaying()
        {
            _accumulator += Time.deltaTime;
            int guard = 0;
            while (_accumulator >= Simulation.FixedDeltaTime && guard++ < 8)
            {
                _accumulator -= Simulation.FixedDeltaTime;
                if (_playbackTick >= _playbackReplay.Inputs.Count || _sim.RunOver)
                {
                    _state = State.GameOver;
                    break;
                }
                _sim.Step(_playbackReplay.Inputs[_playbackTick]);
                _playbackTick++;
            }

            if (Input.GetKeyDown(KeyCode.Escape)) _state = State.Menu;
        }

        private void HandleGameOverInput()
        {
            if (Input.GetKeyDown(KeyCode.R)) StartRunFromField();
            if (Input.GetKeyDown(KeyCode.Escape)) _state = State.Menu;
            if (Input.GetKeyDown(KeyCode.S) && _recorder != null) SaveReplay(_recorder.Data);
        }

        private void HandleMenuInput()
        {
            // Menu interactions are handled in OnGUI; nothing per-frame here.
        }

        private void FollowCamera()
        {
            if (_sim == null || _sim.CurrentRoom == null) return;
            // Keep the room centred; clamp camera so the whole room stays framed. A view-only
            // screenshake offset (from the renderer) is layered on top for hit feedback — it never
            // affects the simulation.
            Vector3 shake = _renderer != null ? _renderer.ShakeOffset() : Vector3.zero;
            _camera.transform.position = new Vector3(shake.x, shake.y, -10);
            float targetSize = Mathf.Max(_sim.CurrentRoom.height * 0.5f + 1f,
                                          (_sim.CurrentRoom.width * 0.5f + 1f) / _camera.aspect);
            _camera.orthographicSize = Mathf.Lerp(_camera.orthographicSize, targetSize, Time.deltaTime * 4f);
        }

        // -----------------------------------------------------------------------------------------
        // Replay persistence
        // -----------------------------------------------------------------------------------------

        private static string ReplayDir => Path.Combine(Application.persistentDataPath, "replays");

        private void SaveReplay(ReplayData data)
        {
            try
            {
                Directory.CreateDirectory(ReplayDir);
                string file = Path.Combine(ReplayDir, $"run_{data.RecordedAtUnix}_{data.FinalScore}.vrplay");
                File.WriteAllText(file, ReplayCodec.Serialize(data));
                Debug.Log($"[VoidRunner] replay saved: {file}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[VoidRunner] failed to save replay: {ex.Message}");
            }
        }

        private ReplayData LoadMostRecentReplay()
        {
            try
            {
                if (!Directory.Exists(ReplayDir)) return null;
                string newest = null;
                DateTime newestTime = DateTime.MinValue;
                foreach (var f in Directory.GetFiles(ReplayDir, "*.vrplay"))
                {
                    var t = File.GetLastWriteTimeUtc(f);
                    if (t > newestTime) { newestTime = t; newest = f; }
                }
                if (newest == null) return null;
                return ReplayCodec.Deserialize(File.ReadAllText(newest));
            }
            catch (Exception ex)
            {
                Debug.LogError($"[VoidRunner] failed to load replay: {ex.Message}");
                return null;
            }
        }

        // -----------------------------------------------------------------------------------------
        // UI
        // -----------------------------------------------------------------------------------------

        private void OnGUI()
        {
            switch (_state)
            {
                case State.Loading: DrawLoadingOrError(); break;
                case State.Menu: DrawMenu(); break;
                case State.Playing:
                    _hud.DrawInGame(_sim, WeaponName(), _seedText);
                    break;
                case State.Replaying:
                    _hud.DrawInGame(_sim, WeaponName(), _seedText);
                    _hud.DrawReplayBanner(_playbackTick, _playbackReplay?.Inputs.Count ?? 0);
                    break;
                case State.GameOver:
                    _hud.DrawInGame(_sim, WeaponName(), _seedText);
                    _hud.DrawGameOver(_sim);
                    break;
            }
        }

        private string WeaponName()
        {
            var w = _sim != null ? _registry.GetWeapon(_sim.Player.WeaponId) : null;
            return w != null ? w.displayName : "-";
        }

        private void DrawLoadingOrError()
        {
            var r = new Rect(Screen.width / 2f - 300, Screen.height / 2f - 60, 600, 120);
            GUI.Box(r, GUIContent.none);
            if (_contentOk)
            {
                GUI.Label(new Rect(r.x, r.y + 40, r.width, 40), "Loading VoidRunner…");
            }
            else
            {
                GUI.Label(new Rect(r.x + 12, r.y + 20, r.width - 24, 80),
                    "Content failed to load:\n" + _contentError);
            }
        }

        private void DrawMenu()
        {
            float cx = Screen.width / 2f;
            var title = new GUIStyle(GUI.skin.label)
            { fontSize = 54, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            title.normal.textColor = new Color(0.5f, 0.9f, 1f);
            GUI.Label(new Rect(cx - 300, 60, 600, 70), "VOIDRUNNER", title);

            var sub = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 15 };
            sub.normal.textColor = new Color(0.8f, 0.8f, 0.85f);
            GUI.Label(new Rect(cx - 300, 130, 600, 24),
                "data-driven roguelite · deterministic seeds · shareable replays", sub);

            var box = new Rect(cx - 200, 190, 400, 250);
            GUI.Box(box, GUIContent.none);

            GUI.Label(new Rect(box.x + 20, box.y + 16, 360, 24), "Seed (blank = random):");
            _menuSeedField = GUI.TextField(new Rect(box.x + 20, box.y + 42, 360, 26), _menuSeedField ?? "");

            if (GUI.Button(new Rect(box.x + 20, box.y + 82, 360, 40), "▶  Start Run"))
            {
                StartRunFromField();
            }

            if (GUI.Button(new Rect(box.x + 20, box.y + 130, 360, 34), "↻  Watch last replay"))
            {
                var replay = LoadMostRecentReplay();
                if (replay != null) StartReplay(replay);
                else Debug.Log("[VoidRunner] no saved replay found yet.");
            }

            GUI.Label(new Rect(box.x + 20, box.y + 178, 360, 60),
                $"Content: {_registry.EnemyCount} enemies · {_registry.WeaponCount} weapons · {_registry.RoomCount} rooms\nFingerprint: {_fingerprint}");
        }
    }
}
