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

        public Stage(byte id, string name, int width, int viewHeight, int knifeSpawnY,
            Platform[] platforms, SpawnPoint[] spawnPoints)
        {
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
        /// high one (top 4.8) in the middle that can only be reached from a low one.
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
            });

        public static Stage ById(byte id) => id == Default.Id ? Default : null;

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
