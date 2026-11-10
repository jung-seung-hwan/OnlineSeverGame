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
    /// What flies in from the side on a map (v6, docs/work/side-hazards-i18n.md). The snapshot carries it per
    /// entry; 0 (old data) reads as Knife.
    /// </summary>
    public enum SideHazardKind : byte
    {
        /// <summary>Kitchen: the kitchen knife lying flat, long and thin, 8 u/s.</summary>
        Knife = 1,
        /// <summary>Winter eaves: a small rolling snowball, short and fast (11 u/s).</summary>
        Snowball = 2,
        /// <summary>Warehouse: a small wooden crate, tall and slow (6 u/s).</summary>
        Crate = 3,
        /// <summary>Dessert shop: a macaron, 9 u/s.</summary>
        Macaron = 4,
    }

    /// <summary>
    /// Size, motion and lanes of one side hazard kind. Position is the leading edge (the "tip") at the lane's
    /// centre line. Shape is the drawn sprite measured into bands for one flying right: Bottom/Top relative to
    /// the lane, Left/Right relative to the tip (so &lt;= 0). Flying left the shape is mirrored in x, and the
    /// knife (turned a quarter the other way) also in y.
    /// </summary>
    public sealed class SideHazardSpec
    {
        public readonly SideHazardKind Kind;
        /// <summary>Tip to tail.</summary>
        public readonly int Length;
        /// <summary>Distance per flying tick (constant).</summary>
        public readonly int Speed;
        public readonly int WarningTicks;
        /// <summary>Lane centre above a hiding surface: low lane (jump over) and head lane (duck under).</summary>
        public readonly int LowOffset;
        public readonly int HeadOffset;
        public readonly HitBand[] Shape;
        public readonly bool FlipYWhenLeft;
        /// <summary>Lowest and highest extent of the shape around the lane centre (either direction).</summary>
        public readonly int HalfThickness;

        public SideHazardSpec(SideHazardKind kind, int length, int speed, int warningTicks, int lowOffset, int headOffset,
            HitBand[] shape, bool flipYWhenLeft)
        {
            Kind = kind;
            Length = length;
            Speed = speed;
            WarningTicks = warningTicks;
            LowOffset = lowOffset;
            HeadOffset = headOffset;
            Shape = shape;
            FlipYWhenLeft = flipYWhenLeft;
            int half = 0;
            foreach (var b in shape)
            {
                if (-b.Bottom > half) half = -b.Bottom;
                if (b.Top > half) half = b.Top;
            }
            HalfThickness = half;
        }

        /// <summary>The kitchen knife: the falling knife's bands turned on their side (same numbers as v5).</summary>
        public static readonly SideHazardSpec Knife = new SideHazardSpec(SideHazardKind.Knife, GameConstants.SideKnifeLength,
            GameConstants.SideKnifeSpeed, GameConstants.SideKnifeWarningTicks, GameConstants.SideKnifeLowOffset,
            GameConstants.SideKnifeHeadOffset, HitShapes.SideKnife, true);
        /// <summary>Snowball sprite at half size (0.52 across); it spins, so the shape is the circle around it.</summary>
        public static readonly SideHazardSpec Snowball = new SideHazardSpec(SideHazardKind.Snowball, 520, 550, 25, 300, 1060,
            HitShapes.Snowball, false);
        /// <summary>Warehouse crate sprite at half size (0.72 x 0.62).</summary>
        public static readonly SideHazardSpec Crate = new SideHazardSpec(SideHazardKind.Crate, 720, 300, 20, 330, 1110,
            HitShapes.SideCrate, false);
        /// <summary>Macaron sprite at 0.6 (0.6 x 0.44).</summary>
        public static readonly SideHazardSpec Macaron = new SideHazardSpec(SideHazardKind.Macaron, 600, 450, 20, 300, 1020,
            HitShapes.Macaron, false);

        public static readonly SideHazardSpec[] All = { Knife, Snowball, Crate, Macaron };

        public static SideHazardSpec Of(SideHazardKind kind)
        {
            switch (kind)
            {
                case SideHazardKind.Snowball: return Snowball;
                case SideHazardKind.Crate: return Crate;
                case SideHazardKind.Macaron: return Macaron;
                default: return Knife;
            }
        }

        /// <summary>Lowest y the shape covers on the lane at centre y (either direction).</summary>
        public int BottomAt(int laneY) => laneY - HalfThickness;
        public int TopAt(int laneY) => laneY + HalfThickness;
    }

    /// <summary>
    /// One side hazard (knife, snowball, crate or macaron; the name stays from the first one) flying across the
    /// whole map at one height. It ignores platforms, boxes and walls, so where it is follows from its start
    /// alone and a client can draw it at any tick.
    /// </summary>
    public struct SideKnifeState
    {
        public ushort Id;
        public SideHazardKind Kind;
        /// <summary>+1 flies right (comes from the left wall), -1 flies left.</summary>
        public sbyte Dir;
        public SideKnifePhase Phase;
        /// <summary>Tip (leading edge) position, milli-units. Starts one length beyond the wall it comes from.</summary>
        public int X;
        /// <summary>Lane: centre line of the hazard.</summary>
        public int Y;
        public ushort Timer;

        public SideHazardSpec Spec => SideHazardSpec.Of(Kind);

        /// <summary>A warning for the stage's own side hazard kind.</summary>
        public static SideKnifeState Warn(ushort id, sbyte dir, int laneY, Stage stage)
        {
            SideHazardSpec spec = SideHazardSpec.Of(stage.SideHazard);
            return new SideKnifeState
            {
                Id = id, Kind = spec.Kind, Dir = dir < 0 ? (sbyte)-1 : (sbyte)1, Phase = SideKnifePhase.Warning,
                X = SideKnifePhysics.StartX(stage.Width, dir, spec.Length), Y = laneY, Timer = (ushort)spec.WarningTicks,
            };
        }

        /// <summary>The trailing end (behind the tip).</summary>
        public int TailX => X - Dir * Spec.Length;

        public override string ToString() => $"side {Kind}#{Id} {(Dir > 0 ? "->" : "<-")} {Phase} ({X},{Y}) t={Timer}";
    }

    public static class SideKnifePhysics
    {
        /// <summary>Tip x when it starts flying: one length beyond the wall it comes from.</summary>
        public static int StartX(int stageWidth, int dir, int length = GameConstants.SideKnifeLength) =>
            dir > 0 ? -length : stageWidth + length;

        /// <summary>
        /// Advances one tick. Returns false when the hazard has left the map (tail one length beyond the far
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
            SideHazardSpec spec = k.Spec;
            if (k.Timer < ushort.MaxValue) k.Timer++;
            k.X += k.Dir * spec.Speed;
            dangerous = true;
            int tail = k.TailX;
            return k.Dir > 0 ? tail <= stageWidth + spec.Length : tail >= -spec.Length;
        }

        /// <summary>
        /// Lanes of a map: for every surface in Stage.SideKnifeSurfaces a low lane (jump over) and a head lane
        /// (duck under), low first. The offsets depend on the map's side hazard (its thickness).
        /// </summary>
        public static int[] Lanes(Stage stage)
        {
            SideHazardSpec spec = SideHazardSpec.Of(stage.SideHazard);
            var lanes = new int[stage.SideKnifeSurfaces.Count * 2];
            for (int i = 0; i < stage.SideKnifeSurfaces.Count; i++)
            {
                lanes[i * 2] = stage.SideKnifeSurfaces[i] + spec.LowOffset;
                lanes[i * 2 + 1] = stage.SideKnifeSurfaces[i] + spec.HeadOffset;
            }
            return lanes;
        }

        /// <summary>True when laneY is one of the map's low lanes (jump over).</summary>
        public static bool IsLowLane(Stage stage, int laneY)
        {
            SideHazardSpec spec = SideHazardSpec.Of(stage.SideHazard);
            foreach (int surface in stage.SideKnifeSurfaces)
                if (laneY == surface + spec.LowOffset) return true;
            return false;
        }

        /// <summary>
        /// Ticks from the start of Playing between side hazard warnings, or -1 before the first one is due.
        /// interval = max(Min, Start - (roundTick - StartTick) / Step). The same on every map.
        /// </summary>
        public static int IntervalAt(uint roundTick)
        {
            if (roundTick < GameConstants.SideKnifeStartTick) return -1;
            long v = GameConstants.SideKnifeIntervalStart -
                     ((long)roundTick - GameConstants.SideKnifeStartTick) / GameConstants.SideKnifeIntervalStepTicks;
            return v < GameConstants.SideKnifeIntervalMin ? GameConstants.SideKnifeIntervalMin : (int)v;
        }

        /// <summary>
        /// Direction for a new side hazard: the same as any still warning or flying (so two never meet head on and
        /// you never have to dodge two at once), else the random pick.
        /// </summary>
        public static sbyte PickDir(IReadOnlyList<SideKnifeState> alive, sbyte randomDir)
        {
            if (alive.Count > 0) return alive[0].Dir;
            return randomDir < 0 ? (sbyte)-1 : (sbyte)1;
        }

        /// <summary>The hazard as it will be after the given number of ticks (it does not depend on anything else).</summary>
        public static SideKnifeState Advance(SideKnifeState k, int ticks, int stageWidth)
        {
            for (int i = 0; i < ticks; i++) Step(ref k, stageWidth, out _, out _);
            return k;
        }
    }
}
