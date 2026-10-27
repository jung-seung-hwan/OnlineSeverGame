using System.Collections.Generic;

namespace OnlineSeverGame.Shared.Simulation
{
    public enum KnifePhase : byte
    {
        /// <summary>Marker shown at X; Timer = ticks until it starts falling.</summary>
        Warning = 1,
        /// <summary>Dropping from Stage.KnifeSpawnY. Kills on touch. Timer = ticks spent falling.</summary>
        Falling = 2,
        /// <summary>Stuck in (or shattered on) a surface, harmless; Timer = ticks until removal.</summary>
        Landed = 3,
    }

    /// <summary>What falls on a map. The snapshot carries it per hazard; 0 (old data) reads as Knife.</summary>
    public enum HazardKind : byte
    {
        Knife = 1,
        /// <summary>Winter eaves: hangs from the eave, falls faster and faster, goes through ice platforms.</summary>
        Icicle = 2,
        /// <summary>Warehouse: wide and slow; when it lands it becomes a pushable box.</summary>
        Crate = 3,
        /// <summary>Dessert shop: a slightly wider knife.</summary>
        Fork = 4,
    }

    /// <summary>Size and motion of one hazard kind. Position is always the bottom-center (tip).</summary>
    public sealed class HazardSpec
    {
        public readonly HazardKind Kind;
        public readonly int Width;
        public readonly int Height;
        public readonly int WarningTicks;
        /// <summary>Constant fall speed per tick, or the top speed when FallAccel &gt; 0.</summary>
        public readonly int FallSpeed;
        /// <summary>&gt; 0: speed starts at 0 and grows by this every falling tick.</summary>
        public readonly int FallAccel;
        public readonly int LandedTicks;
        /// <summary>Falls through ice platforms and stops on the next surface below.</summary>
        public readonly bool PassesIce;

        public HazardSpec(HazardKind kind, int width, int height, int warningTicks, int fallSpeed, int fallAccel,
            int landedTicks, bool passesIce)
        {
            Kind = kind;
            Width = width;
            Height = height;
            WarningTicks = warningTicks;
            FallSpeed = fallSpeed;
            FallAccel = fallAccel;
            LandedTicks = landedTicks;
            PassesIce = passesIce;
        }

        public int HalfWidth => Width / 2;

        /// <summary>Distance fallen on the given falling tick (1 = first).</summary>
        public int SpeedAt(int fallingTick)
        {
            if (FallAccel <= 0) return FallSpeed;
            long v = (long)FallAccel * fallingTick;
            return v > FallSpeed ? FallSpeed : (int)v;
        }

        public static readonly HazardSpec Knife = new HazardSpec(HazardKind.Knife, GameConstants.KnifeWidth,
            GameConstants.KnifeHeight, GameConstants.KnifeWarningTicks, GameConstants.KnifeFallSpeed, 0,
            GameConstants.KnifeLandedTicks, false);
        public static readonly HazardSpec Icicle = new HazardSpec(HazardKind.Icicle, 400, 1200, 25, 1000, 70, 6, true);
        /// <summary>LandedTicks is only used when a crate shatters on a stack that is already two high.</summary>
        public static readonly HazardSpec Crate = new HazardSpec(HazardKind.Crate, 1400, 1200, 25, 600, 0, 6, false);
        public static readonly HazardSpec Fork = new HazardSpec(HazardKind.Fork, 500, 1200, 20, 700, 0, 10, false);

        public static HazardSpec Of(HazardKind kind)
        {
            switch (kind)
            {
                case HazardKind.Icicle: return Icicle;
                case HazardKind.Crate: return Crate;
                case HazardKind.Fork: return Fork;
                default: return Knife;
            }
        }
    }

    /// <summary>One falling hazard (knife, icicle, crate or fork; the name stays from the first map).</summary>
    public struct KnifeState
    {
        public ushort Id;
        public HazardKind Kind;
        public KnifePhase Phase;
        public int X;       // center
        public int Y;       // tip (bottom); Stage.KnifeSpawnY while warning
        public ushort Timer;

        public HazardSpec Spec => HazardSpec.Of(Kind);

        /// <summary>A warning for the stage's own hazard kind.</summary>
        public static KnifeState Warn(ushort id, int x, Stage stage)
        {
            HazardSpec spec = HazardSpec.Of(stage.Hazard);
            return new KnifeState
            {
                Id = id, Kind = spec.Kind, Phase = KnifePhase.Warning, X = x, Y = stage.KnifeSpawnY,
                Timer = (ushort)spec.WarningTicks,
            };
        }

        public override string ToString() => $"{Kind}#{Id} {Phase} ({X},{Y}) t={Timer}";
    }

    public static class KnifePhysics
    {
        /// <summary>
        /// Advances one tick. Returns false when the hazard should be removed. When it was falling during
        /// this tick, dangerous is true and swept is the whole area it passed through (no tunneling).
        /// Hazards also stop on the top of a pushable box. Icicles pass through ice platforms.
        /// </summary>
        public static bool Step(ref KnifeState k, Stage stage, out bool dangerous, out Aabb swept)
        {
            return Step(ref k, stage, null, out dangerous, out swept);
        }

        public static bool Step(ref KnifeState k, Stage stage, IReadOnlyList<PushableState> boxes, out bool dangerous,
            out Aabb swept)
        {
            dangerous = false;
            swept = default;
            HazardSpec spec = k.Spec;
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
                    if (k.Timer < ushort.MaxValue) k.Timer++;
                    int prevY = k.Y;
                    int newY = k.Y - spec.SpeedAt(k.Timer);
                    int left = k.X - spec.HalfWidth;
                    int right = k.X + spec.HalfWidth;
                    bool landed = stage.TryFindLanding(left, right, prevY, newY, true, out int top, spec.PassesIce);
                    if (PushablePhysics.TryFindBoxLanding(boxes, left, right, prevY, newY, out int boxTop, out _) &&
                        (!landed || boxTop > top))
                    {
                        top = boxTop;
                        landed = true;
                    }
                    if (landed)
                    {
                        k.Y = top;
                        k.Phase = KnifePhase.Landed;
                        k.Timer = (ushort)spec.LandedTicks;
                    }
                    else
                    {
                        k.Y = newY;
                    }
                    dangerous = true;
                    swept = new Aabb(left, k.Y, right, prevY + spec.Height);
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
