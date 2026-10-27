using System.Collections.Generic;

namespace OnlineSeverGame.Shared.Simulation
{
    public enum SideKnifePhase : byte
    {
        /// <summary>A line across the lane; X is the start position off the wall, Timer = ticks until it flies.</summary>
        Warning = 1,
        /// <summary>Flying straight across. Kills on touch. Timer = ticks flown.</summary>
        Flying = 2,
    }

    /// <summary>
    /// A knife lying flat that flies across the whole map at one height (every map, docs/work/dash-side-knife.md).
    /// It ignores platforms, boxes and walls, so where it is follows from its start alone and a client can
    /// draw it at any tick.
    /// </summary>
    public struct SideKnifeState
    {
        public ushort Id;
        /// <summary>+1 flies right (comes from the left wall), -1 flies left.</summary>
        public sbyte Dir;
        public SideKnifePhase Phase;
        /// <summary>Tip position, milli-units. Starts SideKnifeLength beyond the wall it comes from.</summary>
        public int X;
        /// <summary>Lane: centre line of the blade.</summary>
        public int Y;
        public ushort Timer;

        public static SideKnifeState Warn(ushort id, sbyte dir, int laneY, Stage stage)
        {
            return new SideKnifeState
            {
                Id = id, Dir = dir < 0 ? (sbyte)-1 : (sbyte)1, Phase = SideKnifePhase.Warning,
                X = SideKnifePhysics.StartX(stage.Width, dir), Y = laneY, Timer = GameConstants.SideKnifeWarningTicks,
            };
        }

        /// <summary>The handle end (the tail, behind the tip).</summary>
        public int TailX => X - Dir * GameConstants.SideKnifeLength;

        public override string ToString() => $"side#{Id} {(Dir > 0 ? "->" : "<-")} {Phase} ({X},{Y}) t={Timer}";
    }

    public static class SideKnifePhysics
    {
        /// <summary>Tip x when it starts flying: one knife length beyond the wall it comes from.</summary>
        public static int StartX(int stageWidth, int dir) =>
            dir > 0 ? -GameConstants.SideKnifeLength : stageWidth + GameConstants.SideKnifeLength;

        /// <summary>
        /// Advances one tick. Returns false when the knife has left the map (tail one length beyond the far
        /// wall). While flying, fromX is the tip before this tick's move, so fromX..k.X is the swept stretch.
        /// </summary>
        public static bool Step(ref SideKnifeState k, int stageWidth, out bool dangerous, out int fromX)
        {
            dangerous = false;
            fromX = k.X;
            if (k.Phase == SideKnifePhase.Warning)
            {
                if (k.Timer > 0) k.Timer--;
                if (k.Timer == 0) k.Phase = SideKnifePhase.Flying;
                return true;
            }
            if (k.Timer < ushort.MaxValue) k.Timer++;
            k.X += k.Dir * GameConstants.SideKnifeSpeed;
            dangerous = true;
            int tail = k.TailX;
            return k.Dir > 0 ? tail <= stageWidth + GameConstants.SideKnifeLength : tail >= -GameConstants.SideKnifeLength;
        }

        /// <summary>
        /// Lanes of a map: for every surface in Stage.SideKnifeSurfaces a low lane (jump over) and a head lane
        /// (duck under), low first.
        /// </summary>
        public static int[] Lanes(Stage stage)
        {
            var lanes = new int[stage.SideKnifeSurfaces.Count * 2];
            for (int i = 0; i < stage.SideKnifeSurfaces.Count; i++)
            {
                lanes[i * 2] = stage.SideKnifeSurfaces[i] + GameConstants.SideKnifeLowOffset;
                lanes[i * 2 + 1] = stage.SideKnifeSurfaces[i] + GameConstants.SideKnifeHeadOffset;
            }
            return lanes;
        }

        /// <summary>
        /// Ticks from the start of Playing between side knife warnings, or -1 before the first one is due.
        /// interval = max(Min, Start - (roundTick - StartTick) / Step).
        /// </summary>
        public static int IntervalAt(uint roundTick)
        {
            if (roundTick < GameConstants.SideKnifeStartTick) return -1;
            long v = GameConstants.SideKnifeIntervalStart -
                     ((long)roundTick - GameConstants.SideKnifeStartTick) / GameConstants.SideKnifeIntervalStepTicks;
            return v < GameConstants.SideKnifeIntervalMin ? GameConstants.SideKnifeIntervalMin : (int)v;
        }

        /// <summary>
        /// Direction for a new side knife: the same as any knife still warning or flying (so two never meet head
        /// on and you never have to dodge two at once), else the random pick.
        /// </summary>
        public static sbyte PickDir(IReadOnlyList<SideKnifeState> alive, sbyte randomDir)
        {
            if (alive.Count > 0) return alive[0].Dir;
            return randomDir < 0 ? (sbyte)-1 : (sbyte)1;
        }

        /// <summary>The knife as it will be after the given number of ticks (it does not depend on anything else).</summary>
        public static SideKnifeState Advance(SideKnifeState k, int ticks, int stageWidth)
        {
            for (int i = 0; i < ticks; i++) Step(ref k, stageWidth, out _, out _);
            return k;
        }
    }
}
