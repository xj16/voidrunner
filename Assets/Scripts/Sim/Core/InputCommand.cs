using System;

namespace VoidRunner.Core
{
    /// <summary>
    /// The complete player input for a single simulation tick. The simulation consumes exactly one
    /// of these per fixed step, which is what makes a run reproducible: (seed + ordered list of
    /// InputCommands) fully determines the outcome. A replay is nothing more than the header seed
    /// plus this stream.
    ///
    /// Movement and aim are packed into signed bytes / a compact angle so the on-disk replay stays
    /// tiny (a few KB for a full run) and — crucially — quantised, so tiny float jitter in the live
    /// input can never desync a replay.
    /// </summary>
    [Serializable]
    public struct InputCommand : IEquatable<InputCommand>
    {
        /// <summary>Horizontal move axis, quantised to [-100, 100] then stored as a signed byte-ish int.</summary>
        public sbyte MoveX;

        /// <summary>Vertical move axis, quantised to [-100, 100].</summary>
        public sbyte MoveY;

        /// <summary>Aim direction in whole degrees [0, 359]. 0 = +X (right).</summary>
        public short AimDegrees;

        /// <summary>True when the fire button is held this tick.</summary>
        public bool Firing;

        public static readonly InputCommand None = new InputCommand { MoveX = 0, MoveY = 0, AimDegrees = 0, Firing = false };

        public Vec2 MoveVector => new Vec2(MoveX / 100f, MoveY / 100f).ClampMagnitude(1f);

        public Vec2 AimVector => SimMathUtil.FromAngle(AimDegrees);

        /// <summary>Builds a command from continuous inputs, quantising them deterministically.</summary>
        public static InputCommand From(Vec2 move, Vec2 aim, bool firing)
        {
            move = move.ClampMagnitude(1f);
            int mx = (int)Math.Round(SimMathUtil.Clamp(move.X, -1f, 1f) * 100f);
            int my = (int)Math.Round(SimMathUtil.Clamp(move.Y, -1f, 1f) * 100f);

            float ang = SimMathUtil.Angle(aim.SqrMagnitude > 1e-8f ? aim : new Vec2(1f, 0f));
            int deg = ((int)Math.Round(ang) % 360 + 360) % 360;

            return new InputCommand
            {
                MoveX = (sbyte)SimMathUtil.Clamp(mx, -100, 100),
                MoveY = (sbyte)SimMathUtil.Clamp(my, -100, 100),
                AimDegrees = (short)deg,
                Firing = firing
            };
        }

        public bool Equals(InputCommand other) =>
            MoveX == other.MoveX && MoveY == other.MoveY &&
            AimDegrees == other.AimDegrees && Firing == other.Firing;

        public override bool Equals(object obj) => obj is InputCommand c && Equals(c);

        public override int GetHashCode()
        {
            int h = MoveX;
            h = (h * 397) ^ MoveY;
            h = (h * 397) ^ AimDegrees;
            h = (h * 397) ^ (Firing ? 1 : 0);
            return h;
        }
    }
}
