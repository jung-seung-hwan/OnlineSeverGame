using OnlineSeverGame.Shared.Simulation;

namespace OnlineSeverGame.BotClient;

/// <summary>
/// Hazard avoidance for bots, as a pure function of what a client can see in a snapshot. Each tick:
///  1. Every warned or falling hazard (knife, icicle, crate, fork: each with its own width and fall speed)
///     that would reach my body becomes a danger interval of x positions (drawn hazard width + drawn body
///     width + a margin). Hazards that land on a platform or box above me are ignored: it is a roof. Icicles go
///     through ice platforms, so those are no roof.
///  2. If I stand inside an interval, walk to the nearest x outside all of them; dash when walking
///     cannot get there before impact; drop through the platform (Down + Jump) when the knife would land on it.
///  3. Otherwise follow the wander input, but do not walk into an interval.
///  4. Side knives (docs/work/dash-side-knife.md 3.6): for each noticed side knife whose lane meets my body,
///     crouch while it passes if crouching gets under it, else jump a few ticks before it arrives. While one is
///     coming at a height a jump would carry me into, do not jump.
/// Bots only notice a warning after their reaction time, so some still get hit.
/// </summary>
public static class DodgeBrain
{
    public const int Margin = 300;
    /// <summary>How far ahead (ticks of walking) a wandering bot checks before stepping.</summary>
    public const int LookAheadTicks = 3;

    public readonly struct Danger
    {
        public readonly int Left, Right, TicksToImpact;
        /// <summary>Where the hazard will stop.</summary>
        public readonly int LandingY;

        public Danger(int left, int right, int ticksToImpact, int landingY = 0)
        {
            Left = left;
            Right = right;
            TicksToImpact = ticksToImpact;
            LandingY = landingY;
        }

        public bool Contains(int x) => x >= Left && x <= Right;
    }

    public static List<Danger> FindDangers(in PlayerState me, IReadOnlyList<KnifeState> knives, Stage stage, int reactionTicks,
        IReadOnlyList<PushableState> boxes = null)
    {
        var dangers = new List<Danger>();
        int bodyTop = me.Y + GameConstants.PlayerHeight;
        foreach (var k in knives)
        {
            HazardSpec spec = k.Spec;
            // Hazards hit the drawn shapes (HitShapes), which are wider than the movement box.
            int halfWidth = HitShapes.HazardHalfWidth(k.Kind) + HitShapes.BodyHalfWidth + Margin;
            int tipY;
            int ticks;
            if (k.Phase == KnifePhase.Warning)
            {
                if (spec.WarningTicks - k.Timer < reactionTicks) continue; // not noticed yet
                tipY = stage.KnifeSpawnY;
                ticks = k.Timer + TicksToFall(spec, 0, tipY - bodyTop);
            }
            else if (k.Phase == KnifePhase.Falling)
            {
                tipY = k.Y;
                if (tipY + spec.Height <= me.Y) continue; // already below my feet
                ticks = TicksToFall(spec, k.Timer, tipY - bodyTop);
            }
            else
            {
                continue; // landed hazards are harmless
            }

            int landing = PushablePhysics.HazardLandingY(stage, boxes, k.Kind, k.X, tipY);
            if (landing >= bodyTop) continue; // stops on a surface above me
            dangers.Add(new Danger(k.X - halfWidth, k.X + halfWidth, ticks, landing));
        }
        return dangers;
    }

    /// <summary>Whole falling ticks that fit in distance for a hazard that has fallen fallenTicks (icicles speed up).</summary>
    public static int TicksToFall(HazardSpec spec, int fallenTicks, int distance)
    {
        int ticks = 0;
        while (ticks < 200 && distance >= spec.SpeedAt(fallenTicks + ticks + 1))
        {
            ticks++;
            distance -= spec.SpeedAt(fallenTicks + ticks);
        }
        return ticks;
    }

    public enum SideAction { None, Duck, Jump, StayDown }

    /// <summary>A side action plus the knife direction not to run along with (0 = none).</summary>
    public readonly struct SidePlan
    {
        public readonly SideAction Action;
        /// <summary>Running the same way as a knife at body height keeps it from passing (and a jump lands on it).</summary>
        public readonly int HoldDir;

        public SidePlan(SideAction action, int holdDir)
        {
            Action = action;
            HoldDir = holdDir;
        }
    }

    /// <summary>Clearance kept around the body when judging side knife timing and heights.</summary>
    public const int SideMargin = 90;
    /// <summary>Jump this many ticks (or fewer) before a low side knife reaches the body.</summary>
    public const int JumpLeadTicks = 5;
    /// <summary>Start crouching this many ticks (or fewer) before a head-height side knife reaches the body.</summary>
    public const int DuckLeadTicks = 4;
    /// <summary>Highest the body reaches above take-off in a jump (2.8 jump + 1.2 body).</summary>
    private const int JumpReach = 2800 + GameConstants.PlayerHeight;

    /// <summary>
    /// What to do about side knives this tick, standing where I am. Duck: hold Down (no jump, no dash) because a
    /// head-height knife is about to pass. Jump: a low knife arrives within JumpLeadTicks. StayDown: something
    /// is flying at a height a jump would reach, so do not jump. Only on the ground. lagTicks = how many ticks
    /// after the state seen an input sent now gets applied (inputs waiting in the server queue), so the timing
    /// is judged for when the input lands.
    /// </summary>
    public static SideAction SideKnifeAction(in PlayerState me, IReadOnlyList<SideKnifeState> knives, int reactionTicks,
        int lagTicks = 0) => PlanSide(me, knives, reactionTicks, lagTicks).Action;

    public static SidePlan PlanSide(in PlayerState me, IReadOnlyList<SideKnifeState> knives, int reactionTicks, int lagTicks = 0)
    {
        if (knives == null || !me.Grounded) return new SidePlan(SideAction.None, 0);
        var action = SideAction.None;
        int holdDir = 0;
        int soonest = int.MaxValue;
        int front = HitShapes.BodyHalfWidth + SideMargin;
        foreach (var k in knives)
        {
            if (k.Phase == SideKnifePhase.Warning && GameConstants.SideKnifeWarningTicks - k.Timer < reactionTicks) continue;
            int bottom = HitShapes.SideKnifeBottom(k.Y), top = HitShapes.SideKnifeTop(k.Y);
            if (top < me.Y || bottom > me.Y + JumpReach) continue;
            // Ticks until the tip reaches my near edge, and until the tail clears my far edge.
            int near = k.Dir > 0 ? (me.X - front) - k.X : k.X - (me.X + front);
            int far = k.Dir > 0 ? (me.X + front) - k.TailX : k.TailX - (me.X - front);
            if (far < 0) continue; // already past
            int wait = k.Phase == SideKnifePhase.Warning ? k.Timer : 0;
            // Walking toward it closes the gap faster; walking away is stopped below.
            int closing = GameConstants.SideKnifeSpeed + Math.Max(0, -k.Dir * me.Vx);
            int arrive = wait + Math.Max(0, (near + closing - 1) / closing) - lagTicks;
            bool atBodyHeight = bottom < me.Y + GameConstants.PlayerHeight + SideMargin;
            if (atBodyHeight && arrive <= 20) holdDir = k.Dir;

            SideAction need;
            if (bottom >= me.Y + GameConstants.PlayerHeight + SideMargin) need = SideAction.StayDown;
            else if (bottom >= me.Y + GameConstants.CrouchHeight + SideMargin) need = arrive <= DuckLeadTicks ? SideAction.Duck : SideAction.StayDown;
            else need = arrive <= JumpLeadTicks ? SideAction.Jump : SideAction.StayDown;
            if (need == SideAction.StayDown && arrive > 20) continue;
            if (need != SideAction.StayDown && arrive < soonest)
            {
                soonest = arrive;
                action = need;
            }
            else if (action == SideAction.None) action = SideAction.StayDown;
        }
        return new SidePlan(action, holdDir);
    }

    public static InputButtons Decide(in PlayerState me, IReadOnlyList<KnifeState> knives, Stage stage, int reactionTicks,
        InputButtons wander, IReadOnlyList<PushableState> boxes = null, IReadOnlyList<SideKnifeState> sideKnives = null,
        int lagTicks = 0)
    {
        return ApplySide(DecideFalling(me, knives, stage, reactionTicks, wander, boxes),
            PlanSide(me, sideKnives, reactionTicks, lagTicks));
    }

    private static InputButtons ApplySide(InputButtons b, SidePlan plan)
    {
        if (plan.HoldDir > 0) b &= ~InputButtons.Right;
        else if (plan.HoldDir < 0) b &= ~InputButtons.Left;
        switch (plan.Action)
        {
            case SideAction.Duck:
                return (b & ~(InputButtons.Jump | InputButtons.Dash)) | InputButtons.Down;
            case SideAction.Jump:
                return (b & ~(InputButtons.Down | InputButtons.Dash)) | InputButtons.Jump;
            case SideAction.StayDown:
                return b & ~InputButtons.Jump;
            default:
                return b;
        }
    }

    private static InputButtons DecideFalling(in PlayerState me, IReadOnlyList<KnifeState> knives, Stage stage, int reactionTicks,
        InputButtons wander, IReadOnlyList<PushableState> boxes)
    {
        var dangers = FindDangers(me, knives, stage, reactionTicks, boxes);
        int minX = GameConstants.PlayerHalfWidth;
        int maxX = stage.Width - GameConstants.PlayerHalfWidth;

        int soonest = int.MaxValue;
        bool landsOnMySurface = true;
        foreach (var d in dangers)
        {
            if (!d.Contains(me.X)) continue;
            soonest = Math.Min(soonest, d.TicksToImpact);
            if (d.LandingY != me.Y) landsOnMySurface = false;
        }

        if (soonest == int.MaxValue)
        {
            // Safe here: wander, but never step into a danger interval.
            int dir = Dir(wander);
            if (dir != 0)
            {
                int ahead = me.X + dir * GameConstants.MoveSpeed * LookAheadTicks;
                foreach (var d in dangers)
                    if (d.Contains(ahead)) return wander & ~(InputButtons.Left | InputButtons.Right | InputButtons.Dash);
            }
            return wander;
        }

        // In danger: the nearest x that is outside every interval and inside the walls.
        int best = int.MinValue;
        foreach (var d in dangers)
        {
            foreach (int candidate in new[] { d.Left - 1, d.Right + 1 })
            {
                if (candidate < minX || candidate > maxX) continue;
                bool covered = false;
                foreach (var other in dangers)
                    if (other.Contains(candidate)) { covered = true; break; }
                if (covered) continue;
                if (best == int.MinValue || Math.Abs(candidate - me.X) < Math.Abs(best - me.X)) best = candidate;
            }
        }

        var buttons = InputButtons.None;
        if (best != int.MinValue)
        {
            int dist = Math.Abs(best - me.X);
            buttons |= best < me.X ? InputButtons.Left : InputButtons.Right;
            if (dist > soonest * GameConstants.MoveSpeed) buttons |= InputButtons.Dash;
        }

        // Standing on a platform the hazard would land on: dropping below (Down + Jump) turns the platform into a
        // roof. Not when it would follow me down (an icicle over an ice sill).
        if (me.Grounded && me.Y > 0 && soonest > 6 && landsOnMySurface) buttons |= InputButtons.Down | InputButtons.Jump;
        return buttons;
    }

    private static int Dir(InputButtons b)
    {
        int dir = 0;
        if ((b & InputButtons.Left) != 0) dir--;
        if ((b & InputButtons.Right) != 0) dir++;
        return dir;
    }
}
