using System.Collections.Generic;

namespace OnlineSeverGame.Shared.Simulation
{
    public readonly struct StompHit
    {
        public readonly int Stomper;
        public readonly int Target;
        /// <summary>False when the target was already flat or immune: the stomper bounces but nothing else happens.</summary>
        public readonly bool Stunned;

        public StompHit(int stomper, int target, bool stunned)
        {
            Stomper = stomper;
            Target = target;
            Stunned = stunned;
        }
    }

    /// <summary>
    /// Run after every player has stepped (and after DashCollision). A player whose feet were at or above
    /// another player's head before the tick and at or below it after, overlapping horizontally, stomped
    /// that player: the stomper is put on the head and bounces up, the target is stunned for
    /// StompStunTicks and drawn flattened. A target that is already flat or still stun-immune after a flat stun
    /// (PlayerState.CanBeStunned) is not stunned again, so nobody can be kept down forever. Server-authoritative like dash pushes; a client may predict its own
    /// bounce with PredictBounce so the stomper does not sink into the target before the snapshot arrives.
    /// </summary>
    public static class StompCollision
    {
        /// <summary>
        /// True when a, moving from aBefore to aAfter under its own control, lands on b's head (b moved
        /// from bBefore to bAfter). headY is b's head height after the tick.
        /// </summary>
        public static bool Lands(in PlayerState aBefore, in PlayerState aAfter, in PlayerState bBefore, in PlayerState bAfter,
            out int headY)
        {
            headY = bAfter.Y + bAfter.Height;
            if (aAfter.Y >= aBefore.Y || aAfter.StunTicks > 0) return false;
            if (aBefore.Y < bBefore.Y + bBefore.Height || aAfter.Y > headY) return false;
            return aAfter.X - GameConstants.PlayerHalfWidth < bAfter.X + GameConstants.PlayerHalfWidth &&
                   bAfter.X - GameConstants.PlayerHalfWidth < aAfter.X + GameConstants.PlayerHalfWidth;
        }

        /// <summary>
        /// Client prediction of the stomper's side only: bounce off the highest head among the others,
        /// given where they were before and after the same tick (a client gets them by stepping the
        /// latest snapshot states with their last held buttons). The target's stun stays server-only.
        /// </summary>
        public static bool PredictBounce(in PlayerState before, ref PlayerState after,
            IReadOnlyList<PlayerState> othersBefore, IReadOnlyList<PlayerState> othersAfter)
        {
            if (othersBefore == null || othersAfter == null) return false;
            int best = int.MinValue;
            for (int i = 0; i < othersAfter.Count; i++)
            {
                if (Lands(before, after, othersBefore[i], othersAfter[i], out int head) && head > best) best = head;
            }
            if (best == int.MinValue) return false;
            Bounce(ref after, best);
            return true;
        }

        private static void Bounce(ref PlayerState s, int headY)
        {
            s.Y = headY;
            s.Vy = GameConstants.StompBounce;
            s.Grounded = false;
            s.DashTicks = 0;
            s.Crouching = false;
        }

        public static void Resolve(PlayerState[] before, PlayerState[] states, bool[] active, int count, List<StompHit> hits = null)
        {
            var target = new int[count];
            var landY = new int[count];
            for (int a = 0; a < count; a++)
            {
                target[a] = -1;
                if (!active[a]) continue;
                int bestTop = int.MinValue;
                for (int b = 0; b < count; b++)
                {
                    if (b == a || !active[b]) continue;
                    if (!Lands(before[a], states[a], before[b], states[b], out int head) || head <= bestTop) continue;
                    bestTop = head;
                    target[a] = b;
                    landY[a] = head;
                }
            }

            // Apply after the search so the order of players does not matter.
            var stunnedNow = new bool[count];
            for (int a = 0; a < count; a++)
            {
                int b = target[a];
                if (b < 0) continue;
                bool stun = states[b].CanBeStunned || stunnedNow[b];
                if (stun && !stunnedNow[b])
                {
                    stunnedNow[b] = true;
                    states[b].StunTicks = GameConstants.StompStunTicks;
                    states[b].Stomped = true;
                    states[b].Vx = 0;
                    states[b].DashTicks = 0;
                    states[b].Crouching = false;
                }
                Bounce(ref states[a], landY[a]);
                hits?.Add(new StompHit(a, b, stun));
            }
        }
    }
}
