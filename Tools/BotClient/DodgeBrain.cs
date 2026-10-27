using OnlineSeverGame.Shared.Simulation;

namespace OnlineSeverGame.BotClient;

/// <summary>
/// Knife-avoidance for bots, as a pure function of what a client can see in a snapshot. Each tick:
///  1. Every warned or falling knife that would reach my body becomes a danger interval of x positions
///     (knife width + my width + a margin). Knives that land on a platform above me are ignored: the
///     platform is a roof.
///  2. If I stand inside an interval, walk to the nearest x outside all of them; dash when walking
///     cannot get there before impact; drop through the platform (Down + Jump) when the knife would land on it.
///  3. Otherwise follow the wander input, but do not walk into an interval.
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

        public Danger(int left, int right, int ticksToImpact)
        {
            Left = left;
            Right = right;
            TicksToImpact = ticksToImpact;
        }

        public bool Contains(int x) => x >= Left && x <= Right;
    }

    public static List<Danger> FindDangers(in PlayerState me, IReadOnlyList<KnifeState> knives, Stage stage, int reactionTicks)
    {
        var dangers = new List<Danger>();
        int bodyTop = me.Y + GameConstants.PlayerHeight;
        int halfWidth = GameConstants.KnifeHalfWidth + GameConstants.PlayerHalfWidth + Margin;
        foreach (var k in knives)
        {
            int tipY;
            int ticks;
            if (k.Phase == KnifePhase.Warning)
            {
                if (GameConstants.KnifeWarningTicks - k.Timer < reactionTicks) continue; // not noticed yet
                tipY = stage.KnifeSpawnY;
                ticks = k.Timer + Math.Max(0, tipY - bodyTop) / GameConstants.KnifeFallSpeed;
            }
            else if (k.Phase == KnifePhase.Falling)
            {
                tipY = k.Y;
                if (tipY + GameConstants.KnifeHeight <= me.Y) continue; // already below my feet
                ticks = Math.Max(0, tipY - bodyTop) / GameConstants.KnifeFallSpeed;
            }
            else
            {
                continue; // landed knives are harmless
            }

            if (stage.KnifeLandingY(k.X, tipY) >= bodyTop) continue; // stops on a surface above me
            dangers.Add(new Danger(k.X - halfWidth, k.X + halfWidth, ticks));
        }
        return dangers;
    }

    public static InputButtons Decide(in PlayerState me, IReadOnlyList<KnifeState> knives, Stage stage, int reactionTicks,
        InputButtons wander)
    {
        var dangers = FindDangers(me, knives, stage, reactionTicks);
        int minX = GameConstants.PlayerHalfWidth;
        int maxX = stage.Width - GameConstants.PlayerHalfWidth;

        int soonest = int.MaxValue;
        foreach (var d in dangers)
            if (d.Contains(me.X)) soonest = Math.Min(soonest, d.TicksToImpact);

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

        // Standing on a platform the knife would land on: dropping below (Down + Jump) turns the platform into a roof.
        if (me.Grounded && me.Y > 0 && soonest > 6) buttons |= InputButtons.Down | InputButtons.Jump;
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
