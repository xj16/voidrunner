using UnityEngine;
using VoidRunner.Core;

namespace VoidRunner.Gameplay
{
    /// <summary>
    /// Translates live Unity input into a quantised <see cref="InputCommand"/> for the current tick.
    ///
    /// Uses the legacy Input Manager (axes "Horizontal"/"Vertical", Fire1, mouse position) so the
    /// project has no extra package dependency and runs on a default Unity install. Aim is toward
    /// the mouse in world space; movement is WASD/arrows/left-stick.
    /// </summary>
    public sealed class InputReader
    {
        private readonly Camera _camera;

        public InputReader(Camera camera) { _camera = camera; }

        public InputCommand Read(Vector2 playerWorldPos)
        {
            float mx = Input.GetAxisRaw("Horizontal");
            float my = Input.GetAxisRaw("Vertical");
            var move = new Vec2(mx, my);

            Vector3 mouseWorld = _camera != null
                ? _camera.ScreenToWorldPoint(new Vector3(Input.mousePosition.x, Input.mousePosition.y, 10f))
                : new Vector3(playerWorldPos.x + 1f, playerWorldPos.y, 0f);

            var aim = new Vec2(mouseWorld.x - playerWorldPos.x, mouseWorld.y - playerWorldPos.y);
            bool firing = Input.GetButton("Fire1") || Input.GetKey(KeyCode.Space);

            return InputCommand.From(move, aim, firing);
        }
    }
}
