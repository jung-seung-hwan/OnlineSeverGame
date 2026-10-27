namespace OnlineSeverGame.Shared.Simulation
{
    /// <summary>
    /// Everything PlayerPhysics.Step reads and writes. The snapshot carries all of it, so a client can
    /// reset to the server state and replay its unacknowledged inputs bit-for-bit.
    /// </summary>
    public struct PlayerState
    {
        public int X;              // bottom-center, milli-units
        public int Y;
        public int Vx;             // milli-units per tick
        public int Vy;
        public sbyte Facing;       // -1 left, +1 right
        public bool Grounded;
        public byte DashTicks;     // > 0 while dashing
        public byte DashCooldown;  // > 0 until the next dash is allowed
        public byte StunTicks;     // > 0 while knocked back or stomped (input ignored)
        public InputButtons PrevButtons; // for press-edge detection
        public bool Crouching;     // holding Down on the ground: lower body, slower, half knockback
        public bool Stomped;       // flat stun (stomp or dash hit): drawn flattened once on the ground
        public byte ImmuneTicks;   // > 0 after a flat stun ended: cannot be stunned again (drawn blinking)

        public bool IsDashing => DashTicks > 0;

        /// <summary>A dash hit or stomp may flatten this player now (not already flat, not immune).</summary>
        public bool CanBeStunned => !Stomped && ImmuneTicks == 0;

        public int Height => Crouching ? GameConstants.CrouchHeight : GameConstants.PlayerHeight;

        public static PlayerState SpawnAt(int x, int y, sbyte facing)
        {
            return new PlayerState { X = x, Y = y, Facing = facing == 0 ? (sbyte)1 : facing, Grounded = y == 0 };
        }

        public Aabb Bounds => new Aabb(X - GameConstants.PlayerHalfWidth, Y,
            X + GameConstants.PlayerHalfWidth, Y + Height);

        public override string ToString() =>
            $"({X},{Y}) v=({Vx},{Vy}) f={Facing} g={Grounded} dash={DashTicks} cd={DashCooldown} stun={StunTicks}" +
            (Crouching ? " crouch" : "") + (Stomped ? " flat" : "") + (ImmuneTicks > 0 ? $" immune={ImmuneTicks}" : "");
    }
}
