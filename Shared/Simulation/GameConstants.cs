namespace OnlineSeverGame.Shared.Simulation
{
    /// <summary>
    /// All gameplay numbers. Positions are integer milli-units (1 world unit = 1000), velocities are
    /// milli-units per tick, so the server and Unity run the exact same integer math.
    /// Y points up, the floor top is y = 0.
    /// </summary>
    public static class GameConstants
    {
        // Time
        public const int TickRate = 20;
        public const int TickMilliseconds = 1000 / TickRate;

        /// <summary>Divide positions by this to get Unity world units.</summary>
        public const int UnitsPerWorldUnit = 1000;

        // Player body: position is the bottom-center of the box.
        public const int PlayerWidth = 800;
        public const int PlayerHalfWidth = PlayerWidth / 2;
        public const int PlayerHeight = 1200;

        // Movement (per tick)
        public const int MoveSpeed = 400;          // 8 u/s
        public const int Gravity = 100;            // 40 u/s^2, applied to vy every tick
        public const int MaxFallSpeed = 1000;      // 20 u/s
        public const int JumpVelocity = 800;       // apex ~2.8 u above take-off

        // Dash: fixed speed along facing, no gravity while dashing.
        public const int DashSpeed = 1200;         // 24 u/s
        public const int DashTicks = 4;            // 0.2 s, ~4.8 u
        public const int DashCooldownTicks = 20;   // 1 s, counted from dash start

        // Dash hit: the target is knocked back and loses control for a moment.
        public const int KnockbackSpeed = 900;
        public const int KnockbackLift = 500;
        public const int StunTicks = 8;

        // Knife
        public const int KnifeWidth = 400;
        public const int KnifeHalfWidth = KnifeWidth / 2;
        public const int KnifeHeight = 1000;       // position is the tip (bottom-center)
        public const int KnifeWarningTicks = 20;   // 1 s marker before it drops
        public const int KnifeFallSpeed = 700;     // 14 u/s, constant
        public const int KnifeLandedTicks = 10;    // stuck in the ground, harmless, then removed

        // Round flow (server)
        public const int CountdownTicks = 3 * TickRate;
        public const int ResultTicks = 5 * TickRate;
        public const int RoundTimeLimitTicks = 180 * TickRate;

        // Knife spawn ramp (server): interval = max(Min, Start - roundTick / IntervalStepTicks),
        // knives per spawn = min(Max, 1 + roundTick / CountStepTicks).
        public const int KnifeSpawnIntervalStart = 30;
        public const int KnifeSpawnIntervalMin = 4;
        public const int KnifeSpawnIntervalStepTicks = 5 * TickRate;
        public const int KnivesPerSpawnMax = 4;
        public const int KnivesPerSpawnStepTicks = 30 * TickRate;
    }
}
