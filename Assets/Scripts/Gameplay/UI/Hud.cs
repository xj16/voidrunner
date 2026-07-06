using UnityEngine;
using VoidRunner.Core;

namespace VoidRunner.Gameplay.UI
{
    /// <summary>
    /// A lightweight IMGUI overlay: health bar, score, room, seed, current weapon, and the
    /// title/game-over/replay panels. Using IMGUI (OnGUI) keeps the whole UI in code, so the
    /// repository needs no Canvas prefabs or UI binary assets to be fully playable.
    /// </summary>
    public sealed class Hud
    {
        private GUIStyle _label;
        private GUIStyle _big;
        private GUIStyle _panel;
        private Texture2D _white;
        private bool _init;

        private void Init()
        {
            _white = new Texture2D(1, 1);
            _white.SetPixel(0, 0, Color.white);
            _white.Apply();

            _label = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold };
            _label.normal.textColor = Color.white;

            _big = new GUIStyle(GUI.skin.label)
            { fontSize = 42, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            _big.normal.textColor = Color.white;

            _panel = new GUIStyle(GUI.skin.box);
            _init = true;
        }

        public void DrawInGame(Simulation sim, string weaponName, string seedText)
        {
            if (!_init) Init();

            // Health bar
            float hpFrac = sim.Player.MaxHealth > 0 ? Mathf.Clamp01(sim.Player.Health / sim.Player.MaxHealth) : 0f;
            var barRect = new Rect(16, 16, 260, 22);
            DrawBar(barRect, hpFrac, new Color(0.15f, 0.15f, 0.2f), new Color(0.9f, 0.25f, 0.3f));
            GUI.Label(new Rect(barRect.x + 6, barRect.y + 1, 260, 22),
                $"HP {Mathf.CeilToInt(sim.Player.Health)}/{Mathf.CeilToInt(sim.Player.MaxHealth)}", _label);

            GUI.Label(new Rect(16, 44, 400, 24), $"Score {sim.Score}    Room {sim.RoomNumber}", _label);
            GUI.Label(new Rect(16, 68, 500, 24), $"Weapon: {weaponName}", _label);
            GUI.Label(new Rect(16, 92, 700, 24), $"Seed: {seedText}   Enemies: {sim.EnemiesAlive}", _label);

            GUI.Label(new Rect(16, Screen.height - 30, 900, 24),
                "WASD/Arrows move · Mouse aim · Hold LMB/Space fire · R restart · Esc menu", _label);
        }

        public void DrawGameOver(Simulation sim)
        {
            if (!_init) Init();
            var r = new Rect(Screen.width / 2f - 220, Screen.height / 2f - 110, 440, 220);
            GUI.Box(r, GUIContent.none, _panel);
            GUI.Label(new Rect(r.x, r.y + 20, r.width, 60), "RUN OVER", _big);
            GUI.Label(new Rect(r.x, r.y + 90, r.width, 30),
                $"Score {sim.Score}   ·   Room {sim.RoomNumber}", CenteredLabel());
            GUI.Label(new Rect(r.x, r.y + 130, r.width, 30),
                "Press R to run again · S to save replay", CenteredLabel());
        }

        public void DrawReplayBanner(int tick, int totalTicks)
        {
            if (!_init) Init();
            float frac = totalTicks > 0 ? (float)tick / totalTicks : 0f;
            var r = new Rect(Screen.width / 2f - 200, 16, 400, 40);
            GUI.Box(r, GUIContent.none, _panel);
            GUI.Label(new Rect(r.x, r.y + 8, r.width, 24), $"▶ REPLAY  {(int)(frac * 100)}%", CenteredLabel());
        }

        private GUIStyle CenteredLabel()
        {
            var s = new GUIStyle(_label) { alignment = TextAnchor.MiddleCenter };
            return s;
        }

        private void DrawBar(Rect rect, float frac, Color bg, Color fg)
        {
            var prev = GUI.color;
            GUI.color = bg; GUI.DrawTexture(rect, _white);
            GUI.color = fg; GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width * frac, rect.height), _white);
            GUI.color = prev;
        }
    }
}
