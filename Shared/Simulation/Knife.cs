namespace OnlineSeverGame.Shared.Simulation
{
    public enum KnifePhase : byte
    {
        /// <summary>Marker shown at X; Timer = ticks until it starts falling.</summary>
        Warning = 1,
        /// <summary>Dropping from Stage.KnifeSpawnY at KnifeFallSpeed. Kills on touch.</summary>
        Falling = 2,
        /// <summary>Stuck in a surface, harmless; Timer = ticks until removal.</summary>
        Landed = 3,
    }

    public struct KnifeState
    {
        public ushort Id;
        public KnifePhase Phase;
        public int X;       // center
        public int Y;       // tip (bottom); Stage.KnifeSpawnY while warning
        public ushort Timer;

        public static KnifeState Warn(ushort id, int x, Stage stage)
        {
            return new KnifeState
            {
                Id = id, Phase = KnifePhase.Warning, X = x, Y = stage.KnifeSpawnY, Timer = GameConstants.KnifeWarningTicks,
            };
        }

        public override string ToString() => $"knife#{Id} {Phase} ({X},{Y}) t={Timer}";
    }

    public static class KnifePhysics
    {
        /// <summary>
        /// Advances one tick. Returns false when the knife should be removed. When the knife was falling
        /// during this tick, dangerous is true and swept is the whole area it passed through (no tunneling).
        /// </summary>
        public static bool Step(ref KnifeState k, Stage stage, out bool dangerous, out Aabb swept)
        {
            dangerous = false;
            swept = default;
            switch (k.Phase)
            {
                case KnifePhase.Warning:
                    if (k.Timer > 0) k.Timer--;
                    if (k.Timer == 0)
                    {
                        k.Phase = KnifePhase.Falling;
                        k.Y = stage.KnifeSpawnY;
                    }
                    return true;

                case KnifePhase.Falling:
                {
                    int prevY = k.Y;
                    int newY = k.Y - GameConstants.KnifeFallSpeed;
                    int left = k.X - GameConstants.KnifeHalfWidth;
                    int right = k.X + GameConstants.KnifeHalfWidth;
                    if (stage.TryFindLanding(left, right, prevY, newY, true, out int top))
                    {
                        k.Y = top;
                        k.Phase = KnifePhase.Landed;
                        k.Timer = GameConstants.KnifeLandedTicks;
                    }
                    else
                    {
                        k.Y = newY;
                    }
                    dangerous = true;
                    swept = new Aabb(left, k.Y, right, prevY + GameConstants.KnifeHeight);
                    return true;
                }

                case KnifePhase.Landed:
                    if (k.Timer > 0) k.Timer--;
                    return k.Timer > 0;

                default:
                    return false;
            }
        }
    }
}
