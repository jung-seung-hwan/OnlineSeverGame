namespace OnlineSeverGame.Shared.Simulation
{
    /// <summary>
    /// One fixed tick of player movement. Pure integer math with no hidden state, so the server and the
    /// client's prediction produce identical results for the same (state, buttons, stage).
    /// </summary>
    public static class PlayerPhysics
    {
        public static void Step(ref PlayerState s, InputButtons buttons, Stage stage)
        {
            InputButtons pressed = buttons & ~s.PrevButtons;

            // DashTicks counts the dash ticks still owed, including the one performed in this step.
            if (s.DashTicks > 0) s.DashTicks--;
            if (s.DashCooldown > 0) s.DashCooldown--;

            if (s.StunTicks > 0)
            {
                // Knocked back: no control, horizontal speed bleeds off.
                s.StunTicks--;
                s.DashTicks = 0;
                s.Vx -= s.Vx / 4;
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
                    s.Vx = dir * GameConstants.MoveSpeed;
                    if ((pressed & InputButtons.Jump) != 0 && s.Grounded)
                    {
                        s.Vy = GameConstants.JumpVelocity;
                        s.Grounded = false;
                    }
                }
            }

            if (s.DashTicks == 0)
            {
                s.Vy -= GameConstants.Gravity;
                if (s.Vy < -GameConstants.MaxFallSpeed) s.Vy = -GameConstants.MaxFallSpeed;
            }

            // Horizontal: walls at both stage edges.
            s.X += s.Vx;
            int minX = GameConstants.PlayerHalfWidth;
            int maxX = stage.Width - GameConstants.PlayerHalfWidth;
            if (s.X < minX || s.X > maxX)
            {
                s.X = s.X < minX ? minX : maxX;
                if (s.StunTicks > 0) s.Vx = 0;
            }

            // Vertical: land on the floor, or on a platform when coming from above (unless Down is held).
            int prevY = s.Y;
            s.Y += s.Vy;
            s.Grounded = false;
            if (s.Vy <= 0)
            {
                bool includePlatforms = (buttons & InputButtons.Down) == 0;
                if (stage.TryFindLanding(s.X - GameConstants.PlayerHalfWidth, s.X + GameConstants.PlayerHalfWidth,
                        prevY, s.Y, includePlatforms, out int top))
                {
                    s.Y = top;
                    s.Vy = 0;
                    s.Grounded = true;
                }
            }

            s.PrevButtons = buttons;
        }
    }
}
