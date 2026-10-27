using System.Collections.Generic;

namespace OnlineSeverGame.Shared.Simulation
{
    public readonly struct DashHit
    {
        public readonly int Attacker;
        public readonly int Target;

        public DashHit(int attacker, int target)
        {
            Attacker = attacker;
            Target = target;
        }
    }

    /// <summary>
    /// Run after every active player has stepped. A dashing player whose box overlaps another player
    /// knocks that player back along the dasher's facing and stops dashing. Two dashers that meet both
    /// bounce away from each other. A player is knocked at most once per tick (lowest index pair wins).
    /// Server-authoritative: clients do not predict it, the next snapshot corrects them.
    /// </summary>
    public static class DashCollision
    {
        public static void Resolve(PlayerState[] states, bool[] active, int count, List<DashHit> hits = null)
        {
            var knock = new sbyte[count];
            var stopDash = new bool[count];

            for (int i = 0; i < count; i++)
            {
                if (!active[i]) continue;
                for (int j = i + 1; j < count; j++)
                {
                    if (!active[j]) continue;
                    bool di = states[i].IsDashing;
                    bool dj = states[j].IsDashing;
                    if (!di && !dj) continue;
                    if (!states[i].Bounds.Overlaps(states[j].Bounds)) continue;

                    if (di && dj)
                    {
                        sbyte away = states[i].X < states[j].X ? (sbyte)-1
                            : states[i].X > states[j].X ? (sbyte)1
                            : (sbyte)-states[i].Facing;
                        SetKnock(knock, i, away);
                        SetKnock(knock, j, (sbyte)-away);
                        stopDash[i] = stopDash[j] = true;
                        hits?.Add(new DashHit(i, j));
                        hits?.Add(new DashHit(j, i));
                    }
                    else if (di)
                    {
                        SetKnock(knock, j, states[i].Facing);
                        stopDash[i] = true;
                        hits?.Add(new DashHit(i, j));
                    }
                    else
                    {
                        SetKnock(knock, i, states[j].Facing);
                        stopDash[j] = true;
                        hits?.Add(new DashHit(j, i));
                    }
                }
            }

            for (int i = 0; i < count; i++)
            {
                if (knock[i] != 0)
                {
                    states[i].Vx = knock[i] * GameConstants.KnockbackSpeed;
                    states[i].Vy = GameConstants.KnockbackLift;
                    states[i].StunTicks = GameConstants.StunTicks;
                    states[i].DashTicks = 0;
                    states[i].Grounded = false;
                }
                else if (stopDash[i])
                {
                    states[i].DashTicks = 0;
                    states[i].Vx = 0;
                }
            }
        }

        private static void SetKnock(sbyte[] knock, int index, sbyte dir)
        {
            if (knock[index] == 0) knock[index] = dir == 0 ? (sbyte)1 : dir;
        }
    }
}
