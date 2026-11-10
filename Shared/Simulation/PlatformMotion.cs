namespace OnlineSeverGame.Shared.Simulation
{
    public enum PlatformMotionKind : byte
    {
        /// <summary>Goes back and forth between its defined place and a shifted one, pausing at each end.</summary>
        Shuttle = 1,
        /// <summary>Stays in place but vanishes for a while every period (crumbles, then comes back).</summary>
        Blink = 2,
    }

    /// <summary>
    /// How a platform changes during a round (docs/work/map-layout.md 5.1). Everything is an integer function of
    /// the round tick, so the server, the client's prediction and the bots get the same platform on the same tick
    /// without sending anything. Round tick 0 (countdown, waiting, results) is the defined place, present.
    /// </summary>
    public sealed class PlatformMotion
    {
        public readonly PlatformMotionKind Kind;

        // Shuttle
        public readonly int ShiftX, ShiftY, TravelTicks, PauseTicks;

        // Blink
        public readonly int FirstGoneTick, PeriodTicks, GoneTicks, WarningTicks;

        private PlatformMotion(PlatformMotionKind kind, int shiftX, int shiftY, int travelTicks, int pauseTicks,
            int firstGoneTick, int periodTicks, int goneTicks, int warningTicks)
        {
            Kind = kind;
            ShiftX = shiftX;
            ShiftY = shiftY;
            TravelTicks = travelTicks;
            PauseTicks = pauseTicks;
            FirstGoneTick = firstGoneTick;
            PeriodTicks = periodTicks;
            GoneTicks = goneTicks;
            WarningTicks = warningTicks;
        }

        /// <summary>
        /// Waits pauseTicks at the defined place, moves (shiftX, shiftY) over travelTicks, waits pauseTicks, comes
        /// back over travelTicks; repeats. Cycle = 2 x (travel + pause).
        /// </summary>
        public static PlatformMotion Shuttle(int shiftX, int shiftY, int travelTicks, int pauseTicks)
        {
            return new PlatformMotion(PlatformMotionKind.Shuttle, shiftX, shiftY, travelTicks < 1 ? 1 : travelTicks,
                pauseTicks < 0 ? 0 : pauseTicks, 0, 0, 0, 0);
        }

        /// <summary>
        /// Present until firstGoneTick, then gone for goneTicks out of every periodTicks. For warningTicks before it
        /// goes it is still solid but marked Crumbling (drawn shaking).
        /// </summary>
        public static PlatformMotion Blink(int firstGoneTick, int periodTicks, int goneTicks, int warningTicks)
        {
            return new PlatformMotion(PlatformMotionKind.Blink, 0, 0, 0, 0, firstGoneTick, periodTicks, goneTicks, warningTicks);
        }

        public int CycleTicks => Kind == PlatformMotionKind.Shuttle ? 2 * (TravelTicks + PauseTicks) : PeriodTicks;

        /// <summary>Offset from the defined place on this round tick (0, 0 for a blinking platform).</summary>
        public void OffsetAt(uint roundTick, out int dx, out int dy)
        {
            dx = 0;
            dy = 0;
            if (Kind != PlatformMotionKind.Shuttle) return;
            long u = roundTick % (uint)CycleTicks;
            long f; // progress 0..TravelTicks
            if (u < PauseTicks) f = 0;
            else if (u < PauseTicks + TravelTicks) f = u - PauseTicks;
            else if (u < 2 * PauseTicks + TravelTicks) f = TravelTicks;
            else f = TravelTicks - (u - 2 * PauseTicks - TravelTicks);
            dx = (int)(ShiftX * f / TravelTicks);
            dy = (int)(ShiftY * f / TravelTicks);
        }

        public bool IsGone(uint roundTick)
        {
            if (Kind != PlatformMotionKind.Blink || roundTick < FirstGoneTick) return false;
            return (roundTick - FirstGoneTick) % (uint)PeriodTicks < GoneTicks;
        }

        /// <summary>Solid but about to vanish within WarningTicks.</summary>
        public bool IsCrumbling(uint roundTick)
        {
            if (Kind != PlatformMotionKind.Blink || IsGone(roundTick)) return false;
            long next = roundTick < FirstGoneTick
                ? FirstGoneTick
                : FirstGoneTick + ((roundTick - FirstGoneTick) / (uint)PeriodTicks + 1) * (long)PeriodTicks;
            return next - roundTick <= WarningTicks;
        }
    }
}
