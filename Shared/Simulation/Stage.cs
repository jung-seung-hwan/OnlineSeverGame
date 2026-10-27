using System.Collections.Generic;

namespace OnlineSeverGame.Shared.Simulation
{
    /// <summary>One-way platform: land on it from above, pass through from below or while holding Down.</summary>
    public readonly struct Platform
    {
        public readonly int Left, Right, Top;

        public Platform(int left, int right, int top)
        {
            Left = left;
            Right = right;
            Top = top;
        }
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
    /// Static terrain. The floor spans [0, Width] with its top at y = 0 and is solid. Walls at x = 0 and
    /// x = Width stop players. Everything is in milli-units.
    /// </summary>
    public sealed class Stage
    {
        public readonly byte Id;
        public readonly string Name;
        public readonly int Width;
        /// <summary>Visible height for the camera; nothing collides with it.</summary>
        public readonly int ViewHeight;
        /// <summary>Knives start falling from this height (tip position).</summary>
        public readonly int KnifeSpawnY;
        public readonly IReadOnlyList<Platform> Platforms;
        public readonly IReadOnlyList<SpawnPoint> SpawnPoints;
        /// <summary>Boxes the server simulates (reset every round). Box ids are index + 1.</summary>
        public readonly IReadOnlyList<PushableSpawn> Pushables;
        /// <summary>Swaying decorations, drawn and animated by the client only.</summary>
        public readonly IReadOnlyList<Decoration> Decorations;

        public Stage(byte id, string name, int width, int viewHeight, int knifeSpawnY,
            Platform[] platforms, SpawnPoint[] spawnPoints, PushableSpawn[] pushables = null,
            Decoration[] decorations = null)
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
        }

        /// <summary>
        /// Stage 1: 24 x 13.5 units. Floor plus three platforms: two low ones (top 2.4) on the sides and a
        /// high one (top 4.8) in the middle that can only be reached from a low one. One butter block to
        /// push under the high platform; a towel, a ladle and a garlic bulb hang from the shelves and the
        /// lid, a herb sprig stands on the board.
        /// </summary>
        public static readonly Stage Default = new Stage(
            id: 1,
            name: "Arena",
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

        public static Stage ById(byte id) => id == Default.Id ? Default : null;

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
        /// Finds the highest surface a box with horizontal extent (left, right) lands on while its bottom
        /// moves down from prevBottom to newBottom. The floor always counts; platforms only when
        /// includePlatforms is set.
        /// </summary>
        public bool TryFindLanding(int left, int right, int prevBottom, int newBottom, bool includePlatforms, out int top)
        {
            bool found = false;
            top = int.MinValue;
            if (includePlatforms)
            {
                for (int i = 0; i < Platforms.Count; i++)
                {
                    Platform p = Platforms[i];
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

        /// <summary>Where a knife dropped at x from fromY will stop. Lets a client draw the landing spot.</summary>
        public int KnifeLandingY(int x, int fromY)
        {
            TryFindLanding(x - GameConstants.KnifeHalfWidth, x + GameConstants.KnifeHalfWidth, fromY, int.MinValue / 2,
                true, out int top);
            return top;
        }
    }
}
