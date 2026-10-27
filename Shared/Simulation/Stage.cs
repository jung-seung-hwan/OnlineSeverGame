using System.Collections.Generic;

namespace OnlineSeverGame.Shared.Simulation
{
    /// <summary>
    /// One-way platform: land on it from above, pass through from below or with Down + Jump.
    /// Ice platforms are slippery and icicles fall through them. Melting platforms (MeltPerTick &gt; 0)
    /// shrink toward their center during a round; see Stage.AtRoundTick.
    /// </summary>
    public readonly struct Platform
    {
        public readonly int Left, Right, Top;
        public readonly bool Ice;
        /// <summary>Milli-units each end moves inward per tick once melting starts (0 = never melts).</summary>
        public readonly int MeltPerTick;
        /// <summary>Melting stops at this width.</summary>
        public readonly int MinWidth;

        public Platform(int left, int right, int top) : this(left, right, top, false, 0, 0)
        {
        }

        public Platform(int left, int right, int top, bool ice, int meltPerTick = 0, int minWidth = 0)
        {
            Left = left;
            Right = right;
            Top = top;
            Ice = ice;
            MeltPerTick = meltPerTick;
            MinWidth = minWidth;
        }

        /// <summary>A melting platform centered on centerX, full width at the start of every round.</summary>
        public static Platform Melting(int centerX, int width, int minWidth, int top, int meltPerTick)
        {
            return new Platform(centerX - width / 2, centerX + width / 2, top, false, meltPerTick, minWidth);
        }

        public int CenterX => (Left + Right) / 2;
        public int Width => Right - Left;

        /// <summary>This platform after meltTicks of melting.</summary>
        public Platform MeltedBy(int meltTicks)
        {
            if (MeltPerTick <= 0 || meltTicks <= 0) return this;
            int half = Width / 2;
            long shrunk = half - (long)meltTicks * MeltPerTick;
            int minHalf = MinWidth / 2;
            int newHalf = shrunk < minHalf ? minHalf : (int)shrunk;
            int c = CenterX;
            return new Platform(c - newHalf, c + newHalf, Top, Ice, MeltPerTick, MinWidth);
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

    /// <summary>Where a pushable box starts each round (bottom-center) and its size, milli-units.</summary>
    public readonly struct PushableSpawn
    {
        public readonly int X, Y, Width, Height;

        public PushableSpawn(int x, int y, int width, int height)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }
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
    /// at x = 0 and x = Width stop players. Everything is in milli-units. Stages are immutable; a map
    /// with melting platforms hands out its shape at a given round tick through AtRoundTick.
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

        private readonly Stage _base;          // set on the melted copies AtRoundTick makes
        private readonly bool _melts;
        private MeltCache _meltCache;

        private sealed class MeltCache
        {
            public int MeltTicks;
            public Stage Stage;
        }

        public Stage(byte id, string name, int width, int viewHeight, int knifeSpawnY,
            Platform[] platforms, SpawnPoint[] spawnPoints, PushableSpawn[] pushables = null,
            Decoration[] decorations = null, HazardKind hazard = HazardKind.Knife, HazardSchedule? schedule = null,
            int[] hazardSlots = null, FloorSpan[] floorIce = null)
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
            for (int i = 0; i < platforms.Length; i++)
                if (platforms[i].MeltPerTick > 0) _melts = true;
        }

        private Stage(Stage baseStage, Platform[] platforms)
            : this(baseStage.Id, baseStage.Name, baseStage.Width, baseStage.ViewHeight, baseStage.KnifeSpawnY,
                platforms, ToArray(baseStage.SpawnPoints), ToArray(baseStage.Pushables), ToArray(baseStage.Decorations),
                baseStage.Hazard, baseStage.Schedule, ToArray(baseStage.HazardSlots), ToArray(baseStage.FloorIce))
        {
            _base = baseStage;
        }

        private static T[] ToArray<T>(IReadOnlyList<T> list)
        {
            var a = new T[list.Count];
            for (int i = 0; i < a.Length; i++) a[i] = list[i];
            return a;
        }

        /// <summary>The map as defined, before any melting.</summary>
        public Stage Base => _base ?? this;

        public bool HasMeltingPlatforms => Base._melts;

        /// <summary>
        /// The shape of this map on the given round tick (ticks since Playing began, 0 outside Playing).
        /// Melting platforms start shrinking after GameConstants.MeltDelayTicks. Maps without them return
        /// themselves.
        /// </summary>
        public Stage AtRoundTick(uint roundTick)
        {
            Stage b = Base;
            if (!b._melts) return b;
            int meltTicks = roundTick > GameConstants.MeltDelayTicks ? (int)(roundTick - GameConstants.MeltDelayTicks) : 0;
            if (meltTicks == 0) return b;
            // Past the point where every platform reached its minimum the shape no longer changes.
            int full = 0;
            for (int i = 0; i < b.Platforms.Count; i++)
            {
                Platform p = b.Platforms[i];
                if (p.MeltPerTick <= 0) continue;
                int need = ((p.Width - p.MinWidth) / 2 + p.MeltPerTick - 1) / p.MeltPerTick;
                if (need > full) full = need;
            }
            if (meltTicks > full) meltTicks = full;

            MeltCache cache = b._meltCache; // one reference read, so sharing between threads is safe
            if (cache != null && cache.MeltTicks == meltTicks) return cache.Stage;
            var platforms = new Platform[b.Platforms.Count];
            for (int i = 0; i < platforms.Length; i++) platforms[i] = b.Platforms[i].MeltedBy(meltTicks);
            var melted = new Stage(b, platforms);
            b._meltCache = new MeltCache { MeltTicks = meltTicks, Stage = melted };
            return melted;
        }

        /// <summary>
        /// Map 1, kitchen: 24 x 13.5 units. Floor plus three platforms: two low ones (top 2.4) on the sides and a
        /// high one (top 4.8) in the middle that can only be reached from a low one. One butter block to
        /// push under the high platform; a towel, a ladle and a garlic bulb hang from the shelves and the
        /// lid, a herb sprig stands on the board. Knives.
        /// </summary>
        public static readonly Stage Default = new Stage(
            id: 1,
            name: "Kitchen",
            width: 24000,
            viewHeight: 13500,
            knifeSpawnY: 14000,
            platforms: new[]
            {
                new Platform(3000, 9000, 2400),
                new Platform(15000, 21000, 2400),
                new Platform(9000, 15000, 4800),
            },
            spawnPoints: new[]
            {
                new SpawnPoint(1500, 0, 1),
                new SpawnPoint(22500, 0, -1),
                new SpawnPoint(4500, 0, 1),
                new SpawnPoint(19500, 0, -1),
                new SpawnPoint(7500, 0, 1),
                new SpawnPoint(16500, 0, -1),
                new SpawnPoint(10500, 0, 1),
                new SpawnPoint(13500, 0, -1),
            },
            pushables: new[]
            {
                new PushableSpawn(12000, 0, 1200, 1000),
            },
            decorations: new[]
            {
                new Decoration("towel", 4300, 2050, 700, 950, true),
                new Decoration("ladle", 19700, 2050, 360, 1050, true),
                new Decoration("garlic", 12000, 4450, 500, 750, true),
                new Decoration("herb", 8300, 0, 500, 750, false),
            });

        public static Stage Kitchen => Default;

        /// <summary>
        /// Map 2, winter eaves (docs/work/maps.md 2.2). Icicles hang from the eave in 24 slots and fall through
        /// the two icy window sills; the wooden beams and the bench stop them. The sills and a frozen puddle in
        /// the middle of the floor are slippery. A snowball to push.
        /// </summary>
        public static readonly Stage Winter = new Stage(
            id: 2,
            name: "Winter Eaves",
            width: 24000,
            viewHeight: 13500,
            knifeSpawnY: 11800,
            platforms: new[]
            {
                new Platform(1500, 6500, 2400, true),
                new Platform(17500, 22500, 2400, true),
                new Platform(10000, 14000, 1600),
                new Platform(5000, 9500, 4800),
                new Platform(14500, 19000, 4800),
            },
            spawnPoints: new[]
            {
                new SpawnPoint(1000, 0, 1),
                new SpawnPoint(23000, 0, -1),
                new SpawnPoint(4500, 0, 1),
                new SpawnPoint(20500, 0, -1),
                new SpawnPoint(7000, 0, 1),
                new SpawnPoint(17000, 0, -1),
                new SpawnPoint(10500, 0, 1),
                new SpawnPoint(13500, 0, -1),
            },
            pushables: new[]
            {
                new PushableSpawn(3000, 0, 1000, 1000),
            },
            decorations: new[]
            {
                new Decoration("lantern", 3000, 13000, 600, 1200, true),
                new Decoration("lantern", 21000, 13000, 600, 1200, true),
            },
            hazard: HazardKind.Icicle,
            schedule: new HazardSchedule(30, 5, 5 * GameConstants.TickRate, 4, 30 * GameConstants.TickRate),
            hazardSlots: Slots(500, 1000, 24),
            floorIce: new[] { new FloorSpan(8000, 16000) });

        /// <summary>
        /// Map 3, warehouse (docs/work/maps.md 2.3). Two-level steel racks on both walls and a pallet hanging in
        /// the middle. Wide crates fall and stay as pushable boxes (two of them are there from the start).
        /// </summary>
        public static readonly Stage Warehouse = new Stage(
            id: 3,
            name: "Warehouse",
            width: 24000,
            viewHeight: 13500,
            knifeSpawnY: 14000,
            platforms: new[]
            {
                new Platform(1000, 5000, 2400),
                new Platform(1000, 5000, 4800),
                new Platform(19000, 23000, 2400),
                new Platform(19000, 23000, 4800),
                new Platform(9500, 14500, 3600),
            },
            spawnPoints: new[]
            {
                new SpawnPoint(1500, 0, 1),
                new SpawnPoint(22500, 0, -1),
                new SpawnPoint(4000, 0, 1),
                new SpawnPoint(20000, 0, -1),
                new SpawnPoint(6000, 0, 1),
                new SpawnPoint(18000, 0, -1),
                new SpawnPoint(10500, 0, 1),
                new SpawnPoint(13500, 0, -1),
            },
            pushables: new[]
            {
                new PushableSpawn(8000, 0, 1400, 1200),
                new PushableSpawn(16000, 0, 1400, 1200),
            },
            decorations: new[]
            {
                new Decoration("hook", 6000, 13200, 500, 2000, true),
                new Decoration("hook", 18000, 13200, 500, 2000, true),
            },
            hazard: HazardKind.Crate,
            schedule: new HazardSchedule(40, 10, 3 * GameConstants.TickRate, 2, 45 * GameConstants.TickRate));

        /// <summary>
        /// Map 4, dessert shop (docs/work/maps.md 2.4). Four ice-cream platforms melt toward their centers from
        /// 10 s into the round. Forks fall. A macaron to push.
        /// </summary>
        public static readonly Stage Dessert = new Stage(
            id: 4,
            name: "Dessert Shop",
            width: 24000,
            viewHeight: 13500,
            knifeSpawnY: 14000,
            platforms: new[]
            {
                Platform.Melting(4000, 5000, 1600, 2000, 2),   // strawberry
                Platform.Melting(20000, 5000, 1600, 2000, 2),  // mint
                Platform.Melting(12000, 7000, 2000, 3600, 2),  // vanilla
                Platform.Melting(12000, 3000, 1000, 6000, 3),  // chocolate
            },
            spawnPoints: new[]
            {
                new SpawnPoint(1000, 0, 1),
                new SpawnPoint(23000, 0, -1),
                new SpawnPoint(3500, 0, 1),
                new SpawnPoint(20500, 0, -1),
                new SpawnPoint(7500, 0, 1),
                new SpawnPoint(16500, 0, -1),
                new SpawnPoint(10500, 0, 1),
                new SpawnPoint(13500, 0, -1),
            },
            pushables: new[]
            {
                new PushableSpawn(12000, 0, 1000, 700),
            },
            decorations: new[]
            {
                new Decoration("lollipop", 4000, 13500, 1500, 1500, true),
                new Decoration("lamp", 12000, 13500, 600, 1600, true),
                new Decoration("lamp", 20000, 13500, 600, 1600, true),
            },
            hazard: HazardKind.Fork,
            schedule: new HazardSchedule(30, 5, 5 * GameConstants.TickRate, 4, 30 * GameConstants.TickRate));

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
                if (p.Top == y && left < p.Right && p.Left < right) return true;
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
                if (p.Ice && p.Top == y && left < p.Right && p.Left < right) return true;
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
                    if (skipIce && p.Ice) continue;
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
