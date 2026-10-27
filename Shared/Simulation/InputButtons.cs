using System;

namespace OnlineSeverGame.Shared.Simulation
{
    /// <summary>Held buttons for one tick. Jump and Dash trigger on the press edge.</summary>
    [Flags]
    public enum InputButtons : byte
    {
        None = 0,
        Left = 1 << 0,
        Right = 1 << 1,
        Jump = 1 << 2,
        Dash = 1 << 3,
        /// <summary>Held on the ground: crouch. Down + Jump press on a platform: drop below it.</summary>
        Down = 1 << 4,
    }
}
