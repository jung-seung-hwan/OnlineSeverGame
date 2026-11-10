using System.Collections.Generic;

namespace OnlineSeverGame.Shared.Simulation
{
    /// <summary>
    /// One-way platform: land on it from above, pass through from below or with Down + Jump.
    /// Ice platforms are slippery and icicles fall through them. Melting platforms (MeltPerTick &gt; 0)
    /// shrink toward their center during a round; platforms with a Motion move or vanish
    /// (docs/work/map-layout.md 5.1). Stage.AtRoundTick hands out the shape on a given round tick.
    /// </summary>
    public readonly struct Platform
    {
        public readonly int Left, Right, Top;
        public readonly bool Ice;
        /// <summary>Milli-units each end moves inward per tick once melting starts (0 = never melts).</summary>
        public readonly int MeltPerTick;
        /// <summary>Melting stops at this width.</summary>
        public readonly int MinWidth;
        /// <summary>How it moves or vanishes during a round; null = it stays put.</summary>
        public readonly PlatformMotion Motion;
        /// <summary>How far it moved since the previous round tick (riders are carried by this much).</summary>
        public readonly int Dx, Dy;
        /// <summary>Vanished on this tick: nothing lands on it.</summary>
        public readonly bool Gone;
        /// <summary>Still solid but about to vanish (drawn shaking).</summary>
        public readonly bool Crumbling;

        public Platform(int left, int right, int top) : this(left, right, top, false, 0, 0)
        {
        }

        public Platform(int left, int right, int top, bool ice, int meltPerTick = 0, int minWidth = 0)
            : this(left, right, top, ice, meltPerTick, minWidth, null, 0, 0, false, false)
        {
        }

        private Platform(int left, int right, int top, bool ice, int meltPerTick, int minWidth, PlatformMotion motion,
            int dx, int dy, bool gone, bool crumbling)
        {
            Left = left;
            Right = right;
            Top = top;
            Ice = ice;
            MeltPerTick = meltPerTick;
            MinWidth = minWidth;
            Motion = motion;
            Dx = dx;
            Dy = dy;
            Gone = gone;
            Crumbling = crumbling;
        }

        /// <summary>A melting platform centered on centerX, full width at the start of every round.</summary>
        public static Platform Melting(int centerX, int width, int minWidth, int top, int meltPerTick)
        {
            return new Platform(centerX - width / 2, centerX + width / 2, top, false, meltPerTick, minWidth);
        }

        /// <summary>A platform that moves or vanishes during a round; (left, right, top) is where it is at round tick 0.</summary>
        public static Platform WithMotion(int left, int right, int top, PlatformMotion motion, bool ice = false)
        {
            return new Platform(left, right, top, ice, 0, 0, motion, 0, 0, false, false);
        }

        public int CenterX => (Left + Right) / 2;
        public int Width => Right - Left;
        public bool Moves => Motion != null && Motion.Kind == PlatformMotionKind.Shuttle;
        public bool Blinks => Motion != null && Motion.Kind == PlatformMotionKind.Blink;

        /// <summary>This platform after meltTicks of melting.</summary>
        public Platform MeltedBy(int meltTicks)
        {
            if (MeltPerTick <= 0 || meltTicks <= 0) return this;
            int half = Width / 2;
            long shrunk = half - (long)meltTicks * MeltPerTick;
            int minHalf = MinWidth / 2;
            int newHalf = shrunk < minHalf ? minHalf : (int)shrunk;
            int c = CenterX;
            return new Platform(c - newHalf, c + newHalf, Top, Ice, MeltPerTick, MinWidth, Motion, Dx, Dy, Gone, Crumbling);
        }

        /// <summary>
        /// This (as-defined) platform on a round tick: melted, moved along its motion, vanished or not, with the move
        /// since the previous tick in Dx/Dy.
        /// </summary>
        public Platform AtRoundTick(uint roundTick)
        {
            int meltTicks = roundTick > GameConstants.MeltDelayTicks ? (int)(roundTick - GameConstants.MeltDelayTicks) : 0;
            Platform p = MeltedBy(meltTicks);
            if (Motion == null) return p;
            Motion.OffsetAt(roundTick, out int ox, out int oy);
            int dx = 0, dy = 0;
            if (roundTick > 0)
            {
                Motion.OffsetAt(roundTick - 1, out int px, out int py);
                dx = ox - px;
                dy = oy - py;
            }
            return new Platform(p.Left + ox, p.Right + ox, p.Top + oy, Ice, MeltPerTick, MinWidth, Motion, dx, dy,
                Motion.IsGone(roundTick), Motion.IsCrumbling(roundTick));
        }
    }

    /// <summary>A stretch of the floor [Left, Right) with a different surface (ice).</summary>
    public readonly struct FloorSpan
    {
        public readonly int Left, Right;

        public FloorSpan(int left, int right)
        {
            Left = left;
            Right = right;
        }
    }

    /// <summary>
    /// Hazard spawn ramp: interval = max(IntervalMin, IntervalStart - roundTick / IntervalStepTicks),
    /// hazards per spawn = min(CountMax, 1 + roundTick / CountStepTicks).
    /// </summary>
    public readonly struct HazardSchedule
    {
        public readonly int IntervalStart, IntervalMin, IntervalStepTicks, CountMax, CountStepTicks;

        public HazardSchedule(int intervalStart, int intervalMin, int intervalStepTicks, int countMax, int countStepTicks)
        {
            IntervalStart = intervalStart;
            IntervalMin = intervalMin;
            IntervalStepTicks = intervalStepTicks;
            CountMax = countMax;
            CountStepTicks = countStepTicks;
        }

        public int IntervalAt(uint roundTick)
        {
            long v = IntervalStart - roundTick / (uint)IntervalStepTicks;
            return v < IntervalMin ? IntervalMin : (int)v;
        }

        public int CountAt(uint roundTick)
        {
            long v = 1 + roundTick / (uint)CountStepTicks;
            return v > CountMax ? CountMax : (int)v;
        }

        /// <summary>The kitchen's knife ramp.</summary>
        public static readonly HazardSchedule Kitchen = new HazardSchedule(GameConstants.KnifeSpawnIntervalStart,
            GameConstants.KnifeSpawnIntervalMin, GameConstants.KnifeSpawnIntervalStepTicks,
            GameConstants.KnivesPerSpawnMax, GameConstants.KnivesPerSpawnStepTicks);
    }

    public readonly struct SpawnPoint
    {
        public readonly int X, Y;
        public readonly sbyte Facing;

        public SpawnPoint(int x, int y, sbyte facing)
        {
            X = x;
            Y = y;
            Facing = facing;
        }
    }

    /// <summary>
    /// Where a pushable box starts each round (bottom-center) and its size, milli-units. Launch &gt; 0 makes it a
    /// jump pad: landing on its top from the air sets vy to Launch (docs/work/jump-pad.md).
    /// </summary>
    public readonly struct PushableSpawn
    {
        public readonly int X, Y, Width, Height;
        public readonly int Launch;

        public PushableSpawn(int x, int y, int width, int height, int launch = 0)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
            Launch = launch;
        }

        /// <summary>A jump pad on the floor at x (GameConstants.JumpPadWidth x JumpPadHeight).</summary>
        public static PushableSpawn JumpPad(int x, int launch) =>
            new PushableSpawn(x, 0, GameConstants.JumpPadWidth, GameConstants.JumpPadHeight, launch);

        public bool IsJumpPad => Launch > 0;
    }

    /// <summary>
    /// Client-only decoration that sways when a player brushes past it. The server ignores it and it
    /// never collides. Kind picks the picture on the client (unknown kinds get a plain shape).
    /// Hanging ones pivot at (X, Y) = their top center; standing ones at (X, Y) = their bottom center.
    /// </summary>
    public readonly struct Decoration
    {
        public readonly string Kind;
        public readonly int X, Y, Width, Height;
        public readonly bool Hanging;

        public Decoration(string kind, int x, int y, int width, int height, bool hanging)
        {
            Kind = kind;
            X = x;
            Y = y;
            Width = width;
            Height = height;
            Hanging = hanging;
        }

        /// <summary>Box covered at rest, for touch checks.</summary>
        public Aabb Bounds => Hanging
            ? new Aabb(X - Width / 2, Y - Height, X + Width / 2, Y)
            : new Aabb(X - Width / 2, Y, X + Width / 2, Y + Height);
    }

    /// <summary>
    /// Terrain and hazards of one map. The floor spans [0, Width] with its top at y = 0 and is solid. Walls
    /// at x = 0 and x = Width stop players. Everything is in milli-units. Stages are immutable; a map whose
    /// platforms melt, move or vanish, or that has a mid-round event, hands out its state on a given round tick
    /// through AtRoundTick (docs/work/map-layout.md).
    /// </summary>
    public sealed class Stage
    {
        public readonly byte Id;
        public readonly string Name;
        public readonly int Width;
        /// <summary>Visible height for the camera; nothing collides with it.</summary>
        public readonly int ViewHeight;
        /// <summary>Hazards start falling from this height (tip position). The name stays from the knife map.</summary>
        public readonly int KnifeSpawnY;
        public readonly IReadOnlyList<Platform> Platforms;
        public readonly IReadOnlyList<SpawnPoint> SpawnPoints;
        /// <summary>Boxes the server simulates (reset every round). Box ids are index + 1.</summary>
        public readonly IReadOnlyList<PushableSpawn> Pushables;
        /// <summary>Swaying decorations, drawn and animated by the client only.</summary>
        public readonly IReadOnlyList<Decoration> Decorations;
        /// <summary>What falls on this map.</summary>
        public readonly HazardKind Hazard;
        public readonly HazardSchedule Schedule;
        /// <summary>Fixed x positions hazards are picked from (icicles under the eave); empty = anywhere.</summary>
        public readonly IReadOnlyList<int> HazardSlots;
        /// <summary>Icy stretches of the floor.</summary>
        public readonly IReadOnlyList<FloorSpan> FloorIce;
        /// <summary>
        /// Surface heights side knives aim at: the floor and platforms with a roof above them (places that are
        /// safe from falling hazards). Each gives a low and a head-height lane (SideKnifePhysics.Lanes).
        /// </summary>
        public readonly IReadOnlyList<int> SideKnifeSurfaces;
        /// <summary>What flies in from the sides on this map (v6): knife, snowball, crate or macaron.</summary>
        public readonly SideHazardKind SideHazard;
        /// <summary>The map's mid-round event (null = none).</summary>
        public readonly MapEventSpec EventSpec;
        /// <summary>The round tick this copy describes (0 for the map as defined).</summary>
        public readonly uint RoundTick;
        /// <summary>The event on RoundTick (None on the map as defined).</summary>
        public readonly MapEventState Event;

        private readonly Stage _base;          // set on the copies AtRoundTick makes
        private readonly bool _dynamic;
        private TickCache _cache;

        private sealed class TickCache
        {
            public uint RoundTick;
            public Stage Stage;
        }

        public Stage(byte id, string name, int width, int viewHeight, int knifeSpawnY,
            Platform[] platforms, SpawnPoint[] spawnPoints, PushableSpawn[] pushables = null,
            Decoration[] decorations = null, HazardKind hazard = HazardKind.Knife, HazardSchedule? schedule = null,
            int[] hazardSlots = null, FloorSpan[] floorIce = null, int[] sideKnifeSurfaces = null,
            SideHazardKind sideHazard = SideHazardKind.Knife, MapEventSpec mapEvent = null)
        {
            Pushables = pushables ?? new PushableSpawn[0];
            Decorations = decorations ?? new Decoration[0];
            Id = id;
            Name = name;
            Width = width;
            ViewHeight = viewHeight;
            KnifeSpawnY = knifeSpawnY;
            Platforms = platforms;
            SpawnPoints = spawnPoints;
            Hazard = hazard;
            Schedule = schedule ?? HazardSchedule.Kitchen;
            HazardSlots = hazardSlots ?? new int[0];
            FloorIce = floorIce ?? new FloorSpan[0];
            SideKnifeSurfaces = sideKnifeSurfaces ?? new[] { 0 };
            SideHazard = sideHazard;
            EventSpec = mapEvent != null && mapEvent.Kind != MapEventKind.None ? mapEvent : null;
            _dynamic = EventSpec != null;
            for (int i = 0; i < platforms.Length; i++)
                if (platforms[i].MeltPerTick > 0 || platforms[i].Motion != null) _dynamic = true;
        }

        /// <summary>A copy on a round tick: shares everything with the map as defined except the platforms and the event.</summary>
        private Stage(Stage b, Platform[] platforms, uint roundTick, MapEventState mapEvent)
        {
            Id = b.Id;
            Name = b.Name;
            Width = b.Width;
            ViewHeight = b.ViewHeight;
            KnifeSpawnY = b.KnifeSpawnY;
            Platforms = platforms;
            SpawnPoints = b.SpawnPoints;
            Pushables = b.Pushables;
            Decorations = b.Decorations;
            Hazard = b.Hazard;
            Schedule = b.Schedule;
            HazardSlots = b.HazardSlots;
            FloorIce = b.FloorIce;
            SideKnifeSurfaces = b.SideKnifeSurfaces;
            SideHazard = b.SideHazard;
            EventSpec = b.EventSpec;
            RoundTick = roundTick;
            Event = mapEvent;
            _base = b;
            _dynamic = b._dynamic;
        }

        /// <summary>The map as defined, before any melting, moving or event.</summary>
        public Stage Base => _base ?? this;

        public bool HasMeltingPlatforms
        {
            get
            {
                foreach (var p in Base.Platforms)
                    if (p.MeltPerTick > 0) return true;
                return false;
            }
        }

        /// <summary>True when platforms melt, move or vanish, or the map has an event (AtRoundTick makes copies).</summary>
        public bool ChangesDuringRound => Base._dynamic;

        /// <summary>
        /// The map on the given round tick (ticks since Playing began, 0 outside Playing): melting platforms shrink
        /// after GameConstants.MeltDelayTicks, moving ones are along their path (Dx/Dy = move since the previous
        /// tick), blinking ones may be Gone, and Event is the map event's state. Round tick 0 is the map as defined.
        /// </summary>
        public Stage AtRoundTick(uint roundTick)
        {
            Stage b = Base;
            if (!b._dynamic || roundTick == 0) return b;
            TickCache cache = b._cache; // one reference read, so sharing between threads is safe
            if (cache != null && cache.RoundTick == roundTick) return cache.Stage;
            var platforms = new Platform[b.Platforms.Count];
            for (int i = 0; i < platforms.Length; i++) platforms[i] = b.Platforms[i].AtRoundTick(roundTick);
            var copy = new Stage(b, platforms, roundTick, MapEventPhysics.At(b.EventSpec, roundTick));
            b._cache = new TickCache { RoundTick = roundTick, Stage = copy };
            return copy;
        }

        /// <summary>
        /// Map 1, kitchen (docs/work/map-layout.md 2.1): a three-shelf spice rack tower on the left wall topped by a
        /// ladle rail at 9.6, a wall cutting board and a tray shelf in the middle, the range hood over the stove on
        /// the right. Event: two of the four burners catch fire. Knives.
        /// </summary>
        public static readonly Stage Default = new Stage(
            id: 1,
            name: "Kitchen",
            width: 24000,
            viewHeight: 13500,
            knifeSpawnY: 14000,
            platforms: new[]
            {
                new Platform(1000, 4000, 2400),     // spice rack shelf 1
                new Platform(1000, 4000, 4800),     // spice rack shelf 2
                new Platform(1000, 4000, 7200),     // spice rack shelf 3 (top of the tower)
                new Platform(5000, 9000, 9600),     // ladle rail
                new Platform(9500, 13500, 4800),    // wall cutting board
                new Platform(12500, 15500, 2400),   // tray shelf
                new Platform(16000, 20500, 7200),   // range hood
            },
            spawnPoints: new[]
            {
                new SpawnPoint(2500, 0, 1),
                new SpawnPoint(22000, 0, -1),
                new SpawnPoint(10500, 0, 1),     // 11.0 before the jump pad (docs/work/jump-pad.md)
                new SpawnPoint(18500, 0, -1),
                new SpawnPoint(5500, 0, 1),
                new SpawnPoint(14500, 0, -1),
                new SpawnPoint(8500, 0, 1),
                new SpawnPoint(20500, 0, -1),
            },
            pushables: new[]
            {
                new PushableSpawn(7000, 0, 1200, 1000),   // butter block under the ladle rail
                PushableSpawn.JumpPad(12000, KitchenPadLaunch),   // pot-trivet spring: reaches the ladle rail (9.6)
            },
            decorations: new[]
            {
                new Decoration("garlic", 2500, 4750, 500, 750, true),
                new Decoration("towel", 6000, 9550, 700, 950, true),
                new Decoration("ladle", 8000, 9550, 360, 1050, true),
                new Decoration("herb", 4500, 0, 500, 750, false),
            },
            sideKnifeSurfaces: new[] { 0, 2400, 4800 },   // floor, shelves 1 and 2 (roofed by the shelves above)
            mapEvent: MapEventSpec.Fire(8 * GameConstants.TickRate, 800,
                new[] { new FloorSpan(10000, 13000), new FloorSpan(17000, 20000) },
                new[] { new FloorSpan(13500, 16500), new FloorSpan(20500, 23500) }));

        public static Stage Kitchen => Default;

        /// <summary>The kitchen's four burners (x 10~13, 13.5~16.5, 17~20, 20.5~23.5); the fire lights 1+3 or 2+4.</summary>
        public static readonly FloorSpan[] KitchenBurners =
        {
            new FloorSpan(10000, 13000), new FloorSpan(13500, 16500), new FloorSpan(17000, 20000), new FloorSpan(20500, 23500),
        };

        /// <summary>
        /// Map 2, winter eaves (docs/work/map-layout.md 2.2). Two routes up to a stone ledge at 9.6: icy sill, wooden
        /// beam and a snow awning on the left; icy sill, a second snow awning and an icy gutter on the right. The
        /// awnings collapse in turn (gone 4 s of every 12 s). Icicles from 24 eave slots fall through ice.
        /// Event: wind.
        /// </summary>
        public static readonly Stage Winter = new Stage(
            id: 2,
            name: "Winter Eaves",
            width: 24000,
            viewHeight: 13500,
            knifeSpawnY: 11800,
            platforms: new[]
            {
                new Platform(1000, 5000, 2400, true),       // left icy sill
                new Platform(6500, 10500, 4800),            // wooden beam
                Platform.WithMotion(11000, 14000, 7200, AwningBlink(200)),  // snow awning 1
                new Platform(15000, 18000, 9600),           // stone ledge (top): kept off both awnings' ends (icicle slots, MapLayoutTests)
                new Platform(14500, 18500, 2400, true),     // right icy sill
                Platform.WithMotion(18500, 21500, 4800, AwningBlink(320)),  // snow awning 2
                new Platform(19000, 23500, 7200, true),     // icy gutter
            },
            spawnPoints: new[]
            {
                new SpawnPoint(1500, 0, 1),
                new SpawnPoint(22500, 0, -1),
                new SpawnPoint(9000, 0, 1),
                new SpawnPoint(16500, 0, -1),
                new SpawnPoint(5500, 0, 1),
                new SpawnPoint(19500, 0, -1),
                new SpawnPoint(10500, 0, 1),     // 12.0 / 14.0 before the jump pad
                new SpawnPoint(13500, 0, -1),
            },
            pushables: new[]
            {
                new PushableSpawn(3000, 0, 1000, 1000),
                PushableSpawn.JumpPad(12000, WinterPadLaunch),    // sled spring on the ice: reaches the stone ledge (9.6)
            },
            decorations: new[]
            {
                new Decoration("lantern", 3000, 13000, 600, 1200, true),
                new Decoration("lantern", 21000, 13000, 600, 1200, true),
            },
            hazard: HazardKind.Icicle,
            schedule: new HazardSchedule(30, 5, 5 * GameConstants.TickRate, 4, 30 * GameConstants.TickRate),
            hazardSlots: Slots(500, 1000, 24),
            floorIce: new[] { new FloorSpan(6500, 14000) },
            sideKnifeSurfaces: new[] { 0, 2400 },    // the right sill is roofed by the stone ledge
            sideHazard: SideHazardKind.Snowball,
            mapEvent: MapEventSpec.Wind(10 * GameConstants.TickRate, 120));

        /// <summary>Snow awnings: gone 80 ticks of every 240 from firstGoneTick, shaking for 40 ticks before.</summary>
        private static PlatformMotion AwningBlink(int firstGoneTick) =>
            PlatformMotion.Blink(firstGoneTick, 12 * GameConstants.TickRate, 4 * GameConstants.TickRate, 2 * GameConstants.TickRate);

        /// <summary>
        /// Map 3, warehouse (docs/work/map-layout.md 2.3). Left wall: low workbench, wall shelf and catwalk; right wall:
        /// a three-level rack. A pallet hanging from the crane shuttles across the top (8.4) between them. Wide crates
        /// fall and stay as pushable boxes (two are there from the start). Event: the conveyor belt in the middle.
        /// </summary>
        public static readonly Stage Warehouse = new Stage(
            id: 3,
            name: "Warehouse",
            width: 24000,
            viewHeight: 13500,
            knifeSpawnY: 14000,
            platforms: new[]
            {
                new Platform(0, 6000, 1200),        // workbench
                new Platform(500, 3500, 3600),      // wall shelf
                new Platform(2500, 6000, 6000),     // catwalk
                Platform.WithMotion(6500, 10500, 8400,
                    PlatformMotion.Shuttle(7000, 0, 5 * GameConstants.TickRate, 2 * GameConstants.TickRate)), // crane pallet
                new Platform(19000, 23500, 2400),   // rack level 1
                new Platform(19000, 23500, 4800),   // rack level 2
                new Platform(19000, 23500, 7200),   // rack level 3
            },
            spawnPoints: new[]
            {
                new SpawnPoint(3000, 0, 1),
                new SpawnPoint(21000, 0, -1),
                new SpawnPoint(10500, 0, 1),     // 11.0 / 13.0 before the jump pad
                new SpawnPoint(13500, 0, -1),
                new SpawnPoint(5000, 0, 1),
                new SpawnPoint(17500, 0, -1),
                new SpawnPoint(7000, 0, 1),
                new SpawnPoint(19500, 0, -1),
            },
            pushables: new[]
            {
                new PushableSpawn(9000, 0, 1400, 1200),
                new PushableSpawn(15000, 0, 1400, 1200),
                PushableSpawn.JumpPad(12000, WarehousePadLaunch), // pallet spring on the belt: reaches the crane pallet (8.4)
            },
            decorations: new[]
            {
                new Decoration("hook", 3000, 13200, 500, 2000, true),
                new Decoration("hook", 21000, 13200, 500, 2000, true),
            },
            hazard: HazardKind.Crate,
            schedule: new HazardSchedule(40, 10, 3 * GameConstants.TickRate, 2, 45 * GameConstants.TickRate),
            sideKnifeSurfaces: new[] { 0, 2400, 4800 },    // rack levels 1 and 2 are roofed by the ones above
            sideHazard: SideHazardKind.Crate,
            mapEvent: MapEventSpec.Conveyor(12 * GameConstants.TickRate, 150, new FloorSpan(6000, 19000)));

        /// <summary>The warehouse crane pallet's index in Warehouse.Platforms.</summary>
        public const int WarehousePalletIndex = 3;

        /// <summary>
        /// Map 4, dessert shop (docs/work/map-layout.md 2.4). Three melting ice creams climb to a cake top at 9.6 on the
        /// left; on the right a cake-stand lift goes up and down between 1.2 and 7.2 next to a wafer shelf with a
        /// melting mint scoop under it. Forks fall. A macaron to push. Event: syrup on half the floor.
        /// </summary>
        public static readonly Stage Dessert = new Stage(
            id: 4,
            name: "Dessert Shop",
            width: 24000,
            viewHeight: 13500,
            knifeSpawnY: 14000,
            platforms: new[]
            {
                Platform.Melting(4000, 5000, 1600, 2400, 2),   // strawberry
                Platform.Melting(21500, 4000, 1600, 2400, 2),  // mint (under the wafer shelf)
                Platform.Melting(8500, 6000, 2000, 4800, 2),   // vanilla
                Platform.Melting(13000, 3000, 1000, 7200, 3),  // chocolate: starts where vanilla ends (no roof over it)
                new Platform(14500, 17000, 9600),              // cherry cake top
                Platform.WithMotion(17000, 19500, 1200,
                    PlatformMotion.Shuttle(0, 6000, 5 * GameConstants.TickRate, 2 * GameConstants.TickRate)), // cake lift
                new Platform(19500, 23500, 7200),              // wafer shelf
            },
            spawnPoints: new[]
            {
                new SpawnPoint(1000, 0, 1),
                new SpawnPoint(23000, 0, -1),
                new SpawnPoint(8500, 0, 1),
                new SpawnPoint(14500, 0, -1),
                new SpawnPoint(5000, 0, 1),
                new SpawnPoint(19000, 0, -1),
                new SpawnPoint(10500, 0, 1),
                new SpawnPoint(16000, 0, -1),
            },
            pushables: new[]
            {
                new PushableSpawn(7000, 0, 1000, 700),       // macaron, 12.0 before the jump pad
                PushableSpawn.JumpPad(12000, DessertPadLaunch),   // jelly spring: reaches the cherry cake top (9.6)
            },
            decorations: new[]
            {
                new Decoration("lollipop", 4000, 13500, 1500, 1500, true),
                new Decoration("lamp", 12000, 13500, 600, 1600, true),
                new Decoration("lamp", 20000, 13500, 600, 1600, true),
            },
            hazard: HazardKind.Fork,
            schedule: new HazardSchedule(30, 5, 5 * GameConstants.TickRate, 4, 30 * GameConstants.TickRate),
            sideKnifeSurfaces: new[] { 0, 2400 },    // mint is roofed by the wafer shelf
            sideHazard: SideHazardKind.Macaron,
            mapEvent: MapEventSpec.Syrup(12 * GameConstants.TickRate, 50,
                new[] { new FloorSpan(500, 11500) },
                new[] { new FloorSpan(12500, 23500) }));

        /// <summary>
        /// Jump pad launch speeds (vy, milli-units per tick). Apex of the feet above the pad top (0.5):
        /// 1500 -> 10.5 (feet at 11.0, over the 9.6 tops), 1400 -> 9.1 (feet at 9.6, over the 8.4 pallet).
        /// JumpPadTests checks each map's top surface can be reached from the pad.
        /// </summary>
        public const int KitchenPadLaunch = 1500, WinterPadLaunch = 1500, WarehousePadLaunch = 1400, DessertPadLaunch = 1500;

        /// <summary>The dessert shop cake lift's index in Dessert.Platforms.</summary>
        public const int DessertLiftIndex = 5;

        /// <summary>Every map, in the default rotation order.</summary>
        public static readonly IReadOnlyList<Stage> All = new[] { Default, Winter, Warehouse, Dessert };

        public static Stage ById(byte id)
        {
            for (int i = 0; i < All.Count; i++)
                if (All[i].Id == id) return All[i];
            return null;
        }

        private static int[] Slots(int first, int step, int count)
        {
            var a = new int[count];
            for (int i = 0; i < count; i++) a[i] = first + i * step;
            return a;
        }

        /// <summary>True when a box with horizontal extent (left, right) stands on a platform top at y.</summary>
        public bool IsOnPlatform(int left, int right, int y)
        {
            for (int i = 0; i < Platforms.Count; i++)
            {
                Platform p = Platforms[i];
                if (!p.Gone && p.Top == y && left < p.Right && p.Left < right) return true;
            }
            return false;
        }

        /// <summary>
        /// True when something with horizontal extent (left, right) resting at height y stands on ice: an ice
        /// platform it overlaps, or an icy floor span under its center.
        /// </summary>
        public bool IsIceAt(int left, int right, int y)
        {
            if (y == 0)
            {
                int c = (left + right) / 2;
                for (int i = 0; i < FloorIce.Count; i++)
                    if (FloorIce[i].Left <= c && c < FloorIce[i].Right) return true;
                return false;
            }
            for (int i = 0; i < Platforms.Count; i++)
            {
                Platform p = Platforms[i];
                if (p.Ice && !p.Gone && p.Top == y && left < p.Right && p.Left < right) return true;
            }
            return false;
        }

        /// <summary>
        /// Finds the highest surface a box with horizontal extent (left, right) lands on while its bottom
        /// moves down from prevBottom to newBottom. The floor always counts; platforms only when
        /// includePlatforms is set, and ice platforms not when skipIce is set (icicles).
        /// </summary>
        public bool TryFindLanding(int left, int right, int prevBottom, int newBottom, bool includePlatforms, out int top,
            bool skipIce = false)
        {
            bool found = false;
            top = int.MinValue;
            if (includePlatforms)
            {
                for (int i = 0; i < Platforms.Count; i++)
                {
                    Platform p = Platforms[i];
                    if (p.Gone || (skipIce && p.Ice)) continue;
                    if (left < p.Right && p.Left < right && prevBottom >= p.Top && newBottom <= p.Top && p.Top > top)
                    {
                        top = p.Top;
                        found = true;
                    }
                }
            }
            if (!found && newBottom <= 0)
            {
                top = 0;
                found = true;
            }
            return found;
        }

        /// <summary>Where this map's hazard dropped at x from fromY will stop. Lets a client draw the landing spot.</summary>
        public int KnifeLandingY(int x, int fromY) => HazardLandingY(Hazard, x, fromY);

        public int HazardLandingY(HazardKind kind, int x, int fromY)
        {
            HazardSpec spec = HazardSpec.Of(kind);
            TryFindLanding(x - spec.HalfWidth, x + spec.HalfWidth, fromY, int.MinValue / 2, true, out int top,
                spec.PassesIce);
            return top;
        }
    }
}
