using System;
using System.Collections.Generic;

namespace OnlineSeverGame.Shared.Simulation
{
    /// <summary>A map's mid-round event (docs/work/map-layout.md 4).</summary>
    public enum MapEventKind : byte
    {
        None = 0,
        /// <summary>Kitchen: two of the four burners catch fire; touching the flames eliminates (cause 4).</summary>
        Fire = 1,
        /// <summary>Winter eaves: everyone is blown sideways (half as much crouching on the ground).</summary>
        Wind = 2,
        /// <summary>Warehouse: the middle of the floor moves one way, carrying players and boxes on it.</summary>
        Conveyor = 3,
        /// <summary>Dessert shop: half the floor turns to syrup; walking on it is half speed.</summary>
        Syrup = 4,
    }

    public enum MapEventPhase : byte
    {
        None = 0,
        /// <summary>Shown on screen, harmless.</summary>
        Warning = 1,
        Active = 2,
    }

    /// <summary>
    /// When and how strongly a map's event happens. The n-th event (from 0) warns at FirstTick - WarningTicks +
    /// n x PeriodTicks, is active for ActiveTicks after that, alternates direction (+1 for even n) and uses span set
    /// n mod SpanSets.Length (burners, belt, syrup halves). Nothing is random, so the round tick alone gives the
    /// state on every machine.
    /// </summary>
    public sealed class MapEventSpec
    {
        public readonly MapEventKind Kind;
        /// <summary>Round tick the first event becomes active.</summary>
        public readonly int FirstTick;
        public readonly int PeriodTicks;
        public readonly int WarningTicks;
        public readonly int ActiveTicks;
        /// <summary>Fire: flame height. Wind/Conveyor: milli-units per tick. Syrup: walking speed percent.</summary>
        public readonly int Strength;
        /// <summary>Floor stretches the event uses, one set per event in turn (empty for wind).</summary>
        public readonly FloorSpan[][] SpanSets;

        public MapEventSpec(MapEventKind kind, int firstTick, int periodTicks, int warningTicks, int activeTicks,
            int strength, FloorSpan[][] spanSets)
        {
            Kind = kind;
            FirstTick = firstTick;
            PeriodTicks = periodTicks;
            WarningTicks = warningTicks;
            ActiveTicks = activeTicks;
            Strength = strength;
            SpanSets = spanSets ?? new FloorSpan[0][];
        }

        // Proposed values (docs/work/map-layout.md 4.0): first event 30 s into the round, then every 40 s, 2 s warning.
        public const int DefaultFirstTick = 30 * GameConstants.TickRate;
        public const int DefaultPeriodTicks = 40 * GameConstants.TickRate;
        public const int DefaultWarningTicks = 2 * GameConstants.TickRate;

        public static MapEventSpec Fire(int activeTicks, int flameHeight, params FloorSpan[][] burnerSets) =>
            new MapEventSpec(MapEventKind.Fire, DefaultFirstTick, DefaultPeriodTicks, DefaultWarningTicks, activeTicks,
                flameHeight, burnerSets);

        public static MapEventSpec Wind(int activeTicks, int pushPerTick) =>
            new MapEventSpec(MapEventKind.Wind, DefaultFirstTick, DefaultPeriodTicks, DefaultWarningTicks, activeTicks,
                pushPerTick, null);

        public static MapEventSpec Conveyor(int activeTicks, int movePerTick, params FloorSpan[] belt) =>
            new MapEventSpec(MapEventKind.Conveyor, DefaultFirstTick, DefaultPeriodTicks, DefaultWarningTicks, activeTicks,
                movePerTick, new[] { belt });

        public static MapEventSpec Syrup(int activeTicks, int speedPercent, params FloorSpan[][] syrupSets) =>
            new MapEventSpec(MapEventKind.Syrup, DefaultFirstTick, DefaultPeriodTicks, DefaultWarningTicks, activeTicks,
                speedPercent, syrupSets);
    }

    /// <summary>A map event on one round tick.</summary>
    public readonly struct MapEventState
    {
        public readonly MapEventKind Kind;
        public readonly MapEventPhase Phase;
        /// <summary>Which event of the round (0 = first). Meaningful while Phase != None.</summary>
        public readonly int Index;
        /// <summary>+1 (to the right) for even Index, -1 for odd.</summary>
        public readonly sbyte Dir;
        /// <summary>Ticks left in the current phase (warning or active), 0 when None.</summary>
        public readonly int TicksLeft;
        public readonly int Strength;
        private readonly FloorSpan[] _spans;

        public MapEventState(MapEventKind kind, MapEventPhase phase, int index, sbyte dir, int ticksLeft, int strength,
            FloorSpan[] spans)
        {
            Kind = kind;
            Phase = phase;
            Index = index;
            Dir = dir;
            TicksLeft = ticksLeft;
            Strength = strength;
            _spans = spans;
        }

        public bool IsActive => Phase == MapEventPhase.Active;
        public bool IsWarning => Phase == MapEventPhase.Warning;

        /// <summary>Floor stretches of this event (burners on fire, the belt, the syrup), empty for wind.</summary>
        public IReadOnlyList<FloorSpan> Spans => _spans ?? Empty;

        private static readonly FloorSpan[] Empty = new FloorSpan[0];

        public bool SpanContains(int x)
        {
            if (_spans == null) return false;
            for (int i = 0; i < _spans.Length; i++)
                if (_spans[i].Left <= x && x < _spans[i].Right) return true;
            return false;
        }

        public override string ToString() => Phase == MapEventPhase.None ? "no event" : $"{Kind}#{Index + 1} {Phase} dir {Dir} left {TicksLeft}";
    }

    public static class MapEventPhysics
    {
        public static MapEventState At(MapEventSpec spec, uint roundTick)
        {
            if (spec == null || spec.Kind == MapEventKind.None) return default;
            long warnStart = (long)spec.FirstTick - spec.WarningTicks;
            if (roundTick < warnStart) return default;
            long rel = roundTick - warnStart;
            int index = (int)(rel / spec.PeriodTicks);
            int u = (int)(rel % spec.PeriodTicks);
            MapEventPhase phase;
            int left;
            if (u < spec.WarningTicks)
            {
                phase = MapEventPhase.Warning;
                left = spec.WarningTicks - u;
            }
            else if (u < spec.WarningTicks + spec.ActiveTicks)
            {
                phase = MapEventPhase.Active;
                left = spec.WarningTicks + spec.ActiveTicks - u;
            }
            else return default;
            FloorSpan[] spans = spec.SpanSets.Length > 0 ? spec.SpanSets[index % spec.SpanSets.Length] : null;
            return new MapEventState(spec.Kind, phase, index, index % 2 == 0 ? (sbyte)1 : (sbyte)-1, left, spec.Strength, spans);
        }

        /// <summary>
        /// Extra horizontal move this tick from the stage's active event: wind for everyone (half when crouching on
        /// the ground), the conveyor for someone standing on the floor over the belt at the start of the tick.
        /// </summary>
        public static int PlayerPush(Stage stage, in PlayerState s, bool groundedAtStart, bool crouching)
        {
            MapEventState e = stage.Event;
            if (!e.IsActive) return 0;
            if (e.Kind == MapEventKind.Wind) return e.Dir * (crouching ? e.Strength / 2 : e.Strength);
            if (e.Kind == MapEventKind.Conveyor && groundedAtStart && s.Y == 0 && e.SpanContains(s.X)) return e.Dir * e.Strength;
            return 0;
        }

        /// <summary>The conveyor's move for a box resting on the floor with its centre over the belt.</summary>
        public static int BoxPush(Stage stage, in PushableState b)
        {
            MapEventState e = stage.Event;
            if (!e.IsActive || e.Kind != MapEventKind.Conveyor) return 0;
            return b.Grounded && b.Y == 0 && e.SpanContains(b.X) ? e.Dir * e.Strength : 0;
        }

        /// <summary>Walking speed percent for someone standing on the floor (100 unless on active syrup).</summary>
        public static int WalkPercent(Stage stage, in PlayerState s, bool groundedAtStart)
        {
            MapEventState e = stage.Event;
            if (!e.IsActive || e.Kind != MapEventKind.Syrup) return 100;
            return groundedAtStart && s.Y == 0 && e.SpanContains(s.X) ? e.Strength : 100;
        }

        /// <summary>
        /// How far the player's drawn body is from the nearest flame (negative = touching it, eliminated). int.MaxValue
        /// when no fire is burning.
        /// </summary>
        public static int FireSeparation(Stage stage, in PlayerState p)
        {
            MapEventState e = stage.Event;
            if (!e.IsActive || e.Kind != MapEventKind.Fire) return int.MaxValue;
            int best = int.MaxValue;
            foreach (FloorSpan f in e.Spans)
                best = Math.Min(best, HitShapes.RectSeparation(p, f.Left, f.Right, 0, e.Strength));
            return best;
        }

        public static bool FireHits(Stage stage, in PlayerState p) => FireSeparation(stage, p) < 0;
    }
}
