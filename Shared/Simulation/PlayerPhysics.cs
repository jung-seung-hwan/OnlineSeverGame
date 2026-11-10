using System.Collections.Generic;

namespace OnlineSeverGame.Shared.Simulation
{
    /// <summary>
    /// One fixed tick of player movement. Pure integer math with no hidden state, so the server and the
    /// client's prediction produce identical results for the same (state, buttons, stage, boxes).
    /// </summary>
    public static class PlayerPhysics
    {
        public static void Step(ref PlayerState s, InputButtons buttons, Stage stage)
        {
            Step(ref s, buttons, stage, null, out _);
        }

        /// <summary>
        /// boxes are solid: their sides block, their tops can be stood on (they are not one-way). They do
        /// not move here; contact tells the caller which box was pushed or stood on.
        /// </summary>
        public static void Step(ref PlayerState s, InputButtons buttons, Stage stage, IReadOnlyList<PushableState> boxes,
            out BoxContact contact)
        {
            contact = BoxContact.None;
            InputButtons pressed = buttons & ~s.PrevButtons;
            bool down = (buttons & InputButtons.Down) != 0;
            bool dropThrough = false;
            // A platform that moved since the last tick carries whoever stood on it (docs/work/map-layout.md 5.1).
            if (s.Grounded) CarryOnPlatform(ref s, stage);
            // Map event at the start of the tick: wind / conveyor push, syrup slows walking.
            bool groundedAtStart = s.Grounded;
            int eventPush = MapEventPhysics.PlayerPush(stage, s, groundedAtStart, groundedAtStart && down);
            int walkPercent = MapEventPhysics.WalkPercent(stage, s, groundedAtStart);
            bool onIce = s.Grounded && stage.IsIceAt(s.X - GameConstants.PlayerHalfWidth, s.X + GameConstants.PlayerHalfWidth, s.Y);

            // DashTicks counts the dash ticks still owed, including the one performed in this step.
            if (s.DashTicks > 0) s.DashTicks--;
            if (s.DashCooldown > 0) s.DashCooldown--;
            if (s.ImmuneTicks > 0) s.ImmuneTicks--;
            if (s.PadCooldown > 0) s.PadCooldown--;

            if (s.StunTicks > 0)
            {
                // Knocked back or flattened: no control, horizontal speed bleeds off. A flat stun ending
                // starts the stun immunity. A flat stun waits while the player is still in the air (flying back
                // from a dash hit), so it always lies flat on the ground for the full stun.
                if (!(s.Stomped && !s.Grounded)) s.StunTicks--;
                if (s.StunTicks == 0 && s.Stomped)
                {
                    s.Stomped = false;
                    s.ImmuneTicks = GameConstants.StunImmunityTicks;
                }
                s.DashTicks = 0;
                s.Vx -= s.Vx / (onIce ? GameConstants.IceStunDecayDivisor : 4);
            }
            else
            {
                int dir = 0;
                if ((buttons & InputButtons.Left) != 0) dir--;
                if ((buttons & InputButtons.Right) != 0) dir++;
                if (dir != 0 && s.DashTicks == 0) s.Facing = (sbyte)dir;

                if ((pressed & InputButtons.Dash) != 0 && s.DashTicks == 0 && s.DashCooldown == 0)
                {
                    s.DashTicks = GameConstants.DashTicks;
                    s.DashCooldown = GameConstants.DashCooldownTicks;
                }

                if (s.DashTicks > 0)
                {
                    s.Vx = s.Facing * GameConstants.DashSpeed;
                    s.Vy = 0;
                }
                else
                {
                    bool crouch = s.Grounded && down;
                    int want = dir * (crouch ? GameConstants.CrouchMoveSpeed : GameConstants.MoveSpeed) * walkPercent / 100;
                    s.Vx = onIce ? Slide(s.Vx, want) : want;
                    if ((pressed & InputButtons.Jump) != 0 && s.Grounded)
                    {
                        // Down + Jump on a platform drops below it; anywhere else it is a normal jump.
                        if (down && OnPlatform(s, stage, boxes)) dropThrough = true;
                        else
                        {
                            s.Vy = GameConstants.JumpVelocity;
                            s.Grounded = false;
                        }
                    }
                }
            }

            if (s.DashTicks == 0)
            {
                s.Vy -= GameConstants.Gravity;
                if (s.Vy < -GameConstants.MaxFallSpeed) s.Vy = -GameConstants.MaxFallSpeed;
            }

            // Horizontal: walls at both stage edges, then box sides.
            int oldX = s.X;
            s.X += s.Vx + eventPush;
            int minX = GameConstants.PlayerHalfWidth;
            int maxX = stage.Width - GameConstants.PlayerHalfWidth;
            if (s.X < minX || s.X > maxX)
            {
                s.X = s.X < minX ? minX : maxX;
                if (s.StunTicks > 0) s.Vx = 0;
            }
            if (boxes != null && s.X != oldX) BlockAtBoxSides(ref s, oldX, boxes, ref contact);

            // Vertical: land on the floor, on a platform when coming from above (unless dropping through
            // it this tick), or on a box top.
            int prevY = s.Y;
            s.Y += s.Vy;
            s.Grounded = false;
            if (s.Vy <= 0)
            {
                int left = s.X - GameConstants.PlayerHalfWidth, right = s.X + GameConstants.PlayerHalfWidth;
                bool found = stage.TryFindLanding(left, right, prevY, s.Y, !dropThrough, out int top);
                if (PushablePhysics.TryFindBoxLanding(boxes, left, right, prevY, s.Y, out int boxTop, out int boxIndex) &&
                    (!found || boxTop >= top))
                {
                    top = boxTop;
                    found = true;
                    contact.GroundBox = boxIndex;
                }
                if (found)
                {
                    s.Y = top;
                    s.Vy = 0;
                    s.Grounded = true;
                    // A jump pad launches whoever lands on it from the air (stunned, flat or holding Down too),
                    // unless it launched them less than JumpPadCooldownTicks ago. A launch ends a dash.
                    if (contact.GroundBox >= 0 && !groundedAtStart && s.PadCooldown == 0 && boxes[contact.GroundBox].IsJumpPad)
                    {
                        s.Vy = boxes[contact.GroundBox].Launch;
                        s.Grounded = false;
                        s.DashTicks = 0;
                        s.PadCooldown = GameConstants.JumpPadCooldownTicks;
                        contact.LaunchBox = contact.GroundBox;
                        contact.GroundBox = -1;
                    }
                }
            }

            s.Crouching = s.Grounded && down && s.DashTicks == 0 && s.StunTicks == 0;
            s.PrevButtons = buttons;
        }

        /// <summary>
        /// Standing on a platform that moved by (Dx, Dy) since the previous tick (its old top and span still under the
        /// feet): move along with it, inside the walls.
        /// </summary>
        public static void CarryOnPlatform(ref PlayerState s, Stage stage)
        {
            int half = GameConstants.PlayerHalfWidth;
            for (int i = 0; i < stage.Platforms.Count; i++)
            {
                Platform p = stage.Platforms[i];
                if ((p.Dx == 0 && p.Dy == 0) || p.Gone) continue;
                if (s.Y != p.Top - p.Dy) continue;
                if (!(s.X - half < p.Right - p.Dx && p.Left - p.Dx < s.X + half)) continue;
                int x = s.X + p.Dx;
                int maxX = stage.Width - half;
                s.X = x < half ? half : x > maxX ? maxX : x;
                s.Y += p.Dy;
                return;
            }
        }

        /// <summary>On ice: drop any dash or knockback speed to walking speed, then ease toward want.</summary>
        private static int Slide(int vx, int want)
        {
            if (vx > GameConstants.MoveSpeed) vx = GameConstants.MoveSpeed;
            else if (vx < -GameConstants.MoveSpeed) vx = -GameConstants.MoveSpeed;
            if (vx < want) return vx + GameConstants.IceAccel < want ? vx + GameConstants.IceAccel : want;
            if (vx > want) return vx - GameConstants.IceAccel > want ? vx - GameConstants.IceAccel : want;
            return vx;
        }

        private static bool OnPlatform(in PlayerState s, Stage stage, IReadOnlyList<PushableState> boxes)
        {
            int left = s.X - GameConstants.PlayerHalfWidth, right = s.X + GameConstants.PlayerHalfWidth;
            if (!stage.IsOnPlatform(left, right, s.Y)) return false;
            if (boxes != null)
            {
                // Standing on a box whose top is level with the platform: the box holds us up.
                for (int i = 0; i < boxes.Count; i++)
                    if (boxes[i].Top == s.Y && PushablePhysics.Overlaps1D(left, right, boxes[i].Left, boxes[i].Right))
                        return false;
            }
            return true;
        }

        private static void BlockAtBoxSides(ref PlayerState s, int oldX, IReadOnlyList<PushableState> boxes, ref BoxContact contact)
        {
            int half = GameConstants.PlayerHalfWidth;
            int bottom = s.Y, top = s.Y + s.Height;
            for (int i = 0; i < boxes.Count; i++)
            {
                PushableState b = boxes[i];
                if (!(bottom < b.Top && b.Y < top)) continue;                            // not level with it
                if (PushablePhysics.Overlaps1D(oldX - half, oldX + half, b.Left, b.Right)) continue; // already inside: let it walk out
                if (!PushablePhysics.Overlaps1D(s.X - half, s.X + half, b.Left, b.Right)) continue;
                sbyte dir = s.X > oldX ? (sbyte)1 : (sbyte)-1;
                s.X = dir > 0 ? b.Left - half : b.Right + half;
                contact.PushBox = i;
                contact.PushDir = dir;
                contact.PushDashing = s.DashTicks > 0;
                if (s.DashTicks > 0)
                {
                    // A dash spends itself on the box.
                    s.DashTicks = 0;
                    s.Vx = 0;
                }
                if (s.StunTicks > 0) s.Vx = 0;
            }
        }
    }
}
