using System.Collections.Generic;

namespace OnlineSeverGame.Shared.Simulation
{
    public readonly struct DashHit
    {
        public readonly int Attacker;
        public readonly int Target;
        /// <summary>The hit flattened the target. False when the target was immune or already flat (pushed only).</summary>
        public readonly bool Stunned;

        public DashHit(int attacker, int target, bool stunned = false)
        {
            Attacker = attacker;
            Target = target;
            Stunned = stunned;
        }
    }

    /// <summary>
    /// Run after every active player has stepped. A dashing player whose box overlaps another player
    /// knocks that player back along the dasher's facing and stops dashing (half as far if the target crouches). Two dashers that meet both
    /// bounce away from each other. A player is knocked at most once per tick (lowest index pair wins).
    /// The knocked player is also flattened for DashStunTicks unless already flat or immune
    /// (PlayerState.CanBeStunned); then the push only takes control away for StunTicks, and an existing flat stun is not extended.
    /// Server-authoritative: clients do not predict it, the next snapshot corrects them.
    /// </summary>
    public static class DashCollision
    {
        public static void Resolve(PlayerState[] states, bool[] active, int count, List<DashHit> hits = null,
            Stage stage = null)
        {
            int firstHit = hits?.Count ?? 0;
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

            var stunned = new bool[count];
            for (int i = 0; i < count; i++)
            {
                if (knock[i] != 0)
                {
                    stunned[i] = states[i].CanBeStunned;
                    // A crouching target is braced and flies half as far.
                    bool braced = states[i].Crouching;
                    // Hit while standing on ice: no lift, the target slides along the ice instead.
                    bool onIce = stage != null && states[i].Grounded &&
                                 stage.IsIceAt(states[i].X - GameConstants.PlayerHalfWidth, states[i].X + GameConstants.PlayerHalfWidth, states[i].Y);
                    states[i].Vx = knock[i] * (braced ? GameConstants.CrouchKnockbackSpeed : GameConstants.KnockbackSpeed);
                    states[i].Vy = onIce ? 0 : braced ? GameConstants.CrouchKnockbackLift : GameConstants.KnockbackLift;
                    if (stunned[i])
                    {
                        states[i].StunTicks = GameConstants.DashStunTicks;
                        states[i].Stomped = true;
                    }
                    else if (states[i].StunTicks < GameConstants.StunTicks)
                    {
                        // Immune or already flat: pushed only. Keep control away long enough for the push to play out.
                        states[i].StunTicks = GameConstants.StunTicks;
                    }
                    states[i].DashTicks = 0;
                    states[i].Grounded = onIce;
                    states[i].Crouching = false;
                }
                else if (stopDash[i])
                {
                    states[i].DashTicks = 0;
                    states[i].Vx = 0;
                }
            }
            if (hits != null) MarkStunned(hits, firstHit, stunned);
        }

        private static void MarkStunned(List<DashHit> hits, int from, bool[] stunned)
        {
            for (int k = from; k < hits.Count; k++)
                hits[k] = new DashHit(hits[k].Attacker, hits[k].Target, stunned[hits[k].Target]);
        }

        private static void SetKnock(sbyte[] knock, int index, sbyte dir)
        {
            if (knock[index] == 0) knock[index] = dir == 0 ? (sbyte)1 : dir;
        }
    }
}
