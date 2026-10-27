using System.Collections.Generic;

namespace OnlineSeverGame.Shared.Simulation
{
    /// <summary>
    /// A box players can push, stand on and dash into. The server simulates it and sends it in every
    /// snapshot; clients interpolate it and use the latest snapshot copy as a solid for prediction.
    /// Position is the bottom-center, like players. Width and Height travel with the state so a map can
    /// also create boxes at run time (for example a falling crate that turns into a box).
    /// </summary>
    public struct PushableState
    {
        public ushort Id;
        public int X;
        public int Y;
        public int Vx;
        public int Vy;
        public ushort Width;
        public ushort Height;
        public bool Grounded;
        /// <summary>&gt; 0: a crate about to vanish (warehouse limit); ticks left. Still solid meanwhile.</summary>
        public byte Crumble;

        public int Left => X - Width / 2;
        public int Right => X + Width / 2;
        public int Top => Y + Height;
        public Aabb Bounds => new Aabb(Left, Y, Right, Top);

        public static PushableState Spawn(ushort id, PushableSpawn sp)
        {
            return new PushableState
            {
                Id = id, X = sp.X, Y = sp.Y, Width = (ushort)sp.Width, Height = (ushort)sp.Height, Grounded = sp.Y == 0,
            };
        }

        public override string ToString() => $"box#{Id} ({X},{Y}) v=({Vx},{Vy}) {Width}x{Height}";
    }

    /// <summary>What one player step did to the boxes: walked or dashed into a side, or stood on a top.</summary>
    public struct BoxContact
    {
        /// <summary>Index of the box whose side the player ran into this tick, -1 if none.</summary>
        public int PushBox;
        public sbyte PushDir;
        /// <summary>The player was dashing when it hit the box (the dash ends there).</summary>
        public bool PushDashing;
        /// <summary>Index of the box the player is standing on after the step, -1 if none.</summary>
        public int GroundBox;

        public static readonly BoxContact None = new BoxContact { PushBox = -1, GroundBox = -1 };
    }

    public static class PushablePhysics
    {
        /// <summary>
        /// A player pushed this box. A dash hit launches it; walking sets it to walking-push speed unless it
        /// already slides faster the same way.
        /// </summary>
        public static void ApplyPush(ref PushableState box, sbyte dir, bool dashing)
        {
            if (dir == 0) return;
            int speed = dashing ? GameConstants.PushableDashSpeed : GameConstants.PushableWalkSpeed;
            if (box.Vx * dir < speed) box.Vx = dir * speed;
        }

        /// <summary>
        /// One tick for every box: friction, gravity, walls, other boxes, players in the way, landing.
        /// A box that slides into a player shoves that player along (it never traps anyone: things that
        /// already overlap are left alone). movedX receives each box's horizontal move, to carry riders.
        /// </summary>
        public static void Step(PushableState[] boxes, int count, Stage stage, PlayerState[] players, bool[] active,
            int playerCount, int[] movedX)
        {
            for (int i = 0; i < count; i++)
            {
                ref PushableState b = ref boxes[i];
                int oldX = b.X;

                if (b.Grounded)
                {
                    int friction = stage.IsIceAt(b.Left, b.Right, b.Y) ? GameConstants.IceBoxFriction : GameConstants.PushableFriction;
                    if (b.Vx > 0) b.Vx = b.Vx > friction ? b.Vx - friction : 0;
                    else if (b.Vx < 0) b.Vx = -b.Vx > friction ? b.Vx + friction : 0;
                }
                b.Vy -= GameConstants.Gravity;
                if (b.Vy < -GameConstants.MaxFallSpeed) b.Vy = -GameConstants.MaxFallSpeed;

                // Horizontal: walls, then other boxes, then players.
                b.X += b.Vx;
                int half = b.Width / 2;
                if (b.X < half || b.X > stage.Width - half)
                {
                    b.X = b.X < half ? half : stage.Width - half;
                    b.Vx = 0;
                }
                for (int j = 0; j < count; j++)
                {
                    if (j == i) continue;
                    PushableState o = boxes[j];
                    if (!(b.Y < o.Top && o.Y < b.Top)) continue;
                    if (Overlaps1D(oldX - half, oldX + half, o.Left, o.Right)) continue;
                    if (!Overlaps1D(b.X - half, b.X + half, o.Left, o.Right)) continue;
                    b.X = b.X > oldX ? o.Left - half : o.Right + half;
                    b.Vx = 0;
                }
                if (b.X != oldX && players != null)
                {
                    int dir = b.X > oldX ? 1 : -1;
                    for (int p = 0; p < playerCount; p++)
                    {
                        if (!active[p]) continue;
                        ref PlayerState pl = ref players[p];
                        if (!(pl.Y < b.Top && b.Y < pl.Y + pl.Height)) continue;
                        int pl0 = pl.X - GameConstants.PlayerHalfWidth, pr0 = pl.X + GameConstants.PlayerHalfWidth;
                        if (Overlaps1D(oldX - half, oldX + half, pl0, pr0)) continue;
                        if (!Overlaps1D(b.X - half, b.X + half, pl0, pr0)) continue;
                        // Shove the player to the box's leading face, inside the walls.
                        int want = dir > 0 ? b.X + half + GameConstants.PlayerHalfWidth : b.X - half - GameConstants.PlayerHalfWidth;
                        int minX = GameConstants.PlayerHalfWidth, maxX = stage.Width - GameConstants.PlayerHalfWidth;
                        pl.X = want < minX ? minX : want > maxX ? maxX : want;
                        if (pl.X != want)
                        {
                            // Pinned against a wall: the box stops at the player instead.
                            b.X = dir > 0 ? pl.X - GameConstants.PlayerHalfWidth - half : pl.X + GameConstants.PlayerHalfWidth + half;
                            b.Vx = 0;
                        }
                    }
                }

                // Vertical: land on the floor, platforms (from above) or another box's top.
                int prevY = b.Y;
                b.Y += b.Vy;
                b.Grounded = false;
                if (b.Vy <= 0)
                {
                    bool found = stage.TryFindLanding(b.X - half, b.X + half, prevY, b.Y, true, out int top);
                    for (int j = 0; j < count; j++)
                    {
                        if (j == i) continue;
                        PushableState o = boxes[j];
                        if (Overlaps1D(b.X - half, b.X + half, o.Left, o.Right) && prevY >= o.Top && b.Y <= o.Top &&
                            (!found || o.Top > top))
                        {
                            top = o.Top;
                            found = true;
                        }
                    }
                    if (found)
                    {
                        b.Y = top;
                        b.Vy = 0;
                        b.Grounded = true;
                    }
                }

                if (movedX != null) movedX[i] = b.X - oldX;
            }
        }

        /// <summary>Highest box top that a box spanning (left, right) meets moving down from prevBottom to newBottom.</summary>
        public static bool TryFindBoxLanding(IReadOnlyList<PushableState> boxes, int left, int right, int prevBottom,
            int newBottom, out int top, out int index)
        {
            top = int.MinValue;
            index = -1;
            if (boxes == null) return false;
            for (int i = 0; i < boxes.Count; i++)
            {
                PushableState o = boxes[i];
                if (Overlaps1D(left, right, o.Left, o.Right) && prevBottom >= o.Top && newBottom <= o.Top && o.Top > top)
                {
                    top = o.Top;
                    index = i;
                }
            }
            return index >= 0;
        }

        /// <summary>Where the stage's hazard dropped at x from fromY stops: the stage surface or a box top, whichever is higher.</summary>
        public static int KnifeLandingY(Stage stage, IReadOnlyList<PushableState> boxes, int x, int fromY)
        {
            return HazardLandingY(stage, boxes, stage.Hazard, x, fromY);
        }

        public static int HazardLandingY(Stage stage, IReadOnlyList<PushableState> boxes, HazardKind kind, int x, int fromY)
        {
            int half = HazardSpec.Of(kind).HalfWidth;
            int top = stage.HazardLandingY(kind, x, fromY);
            if (TryFindBoxLanding(boxes, x - half, x + half, fromY, int.MinValue / 2, out int boxTop, out _) && boxTop > top)
                top = boxTop;
            return top;
        }

        internal static bool Overlaps1D(int aLeft, int aRight, int bLeft, int bRight) => aLeft < bRight && bLeft < aRight;
    }
}
