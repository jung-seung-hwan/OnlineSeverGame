using System;
using System.Collections.Generic;
using OnlineSeverGame.Shared.Protocol;
using OnlineSeverGame.Shared.Simulation;

namespace OnlineSeverGame.Server
{
    /// <summary>
    /// A bot the room runs itself (v14, docs/work/bots.md): no socket, the room asks it for one input every tick and
    /// applies it like a player's. It sees what a client sees (players, hazards, boxes) and plays with the same
    /// DodgeBrain as the console bots (Tools/BotClient). The level sets how fast it notices warnings, whether it dashes
    /// to get away, and how often it dashes into someone close by.
    /// </summary>
    public sealed class ServerBot
    {
        private readonly Random _random;
        private BotLevel _level;
        private int _reactionTicks;
        private int _dir;
        private int _dirTicksLeft;
        private int _crouchTicksLeft;
        private int _padTripTicks;
        private InputButtons _lastSent;
        private readonly int _salt;
        private readonly List<KnifeState> _seenKnives = new List<KnifeState>();
        private readonly List<SideKnifeState> _seenSides = new List<SideKnifeState>();

        public ServerBot(int seed, BotLevel level)
        {
            _random = new Random(seed);
            _salt = _random.Next();
            Level = level;
        }

        public uint Seq { get; private set; }

        public BotLevel Level
        {
            get => _level;
            set
            {
                _level = ProtocolConstants.IsValidBotLevel((byte)value) ? value : BotLevel.Normal;
                RollReaction();
            }
        }

        /// <summary>Ticks after a warning appears before this bot notices it (warnings last 20 ticks).</summary>
        public int ReactionTicks => _reactionTicks;

        /// <summary>A new reaction time, rolled per round so a bot is not always equally quick.</summary>
        public void RollReaction()
        {
            switch (_level)
            {
                case BotLevel.Easy: _reactionTicks = _random.Next(12, 19); break;  // 0.6 - 0.9 s of a 1 s warning
                case BotLevel.Hard: _reactionTicks = _random.Next(1, 3); break;    // 0.05 - 0.1 s
                default: _reactionTicks = _random.Next(3, 8); break;               // 0.15 - 0.35 s
            }
        }

        /// <summary>Percent of hazards this bot never notices at all (it is looking elsewhere).</summary>
        public int MissPercent => _level == BotLevel.Easy ? 35 : _level == BotLevel.Normal ? 10 : 0;

        /// <summary>Whether this bot notices hazard id (fixed per bot and hazard, so it does not flicker tick to tick).</summary>
        private bool Notices(int id, int kind)
        {
            int miss = MissPercent;
            if (miss == 0) return true;
            uint h = (uint)(id * 73856093) ^ (uint)(kind * 19349663) ^ (uint)_salt;
            h ^= h >> 13;
            h *= 0x5bd1e995;
            h ^= h >> 15;
            return h % 100 >= miss;
        }

        /// <summary>One chance in 300 per tick to play an emote while alive: about one every 15 s.</summary>
        public bool WantsEmote() => _random.Next(300) == 0;

        public EmoteKind PickEmote() => (EmoteKind)(1 + _random.Next(ProtocolConstants.MaxEmote));

        /// <summary>
        /// The buttons for this tick. me = this bot's index in states; isBot marks the other bots. Outside Playing it
        /// only wanders (the waiting room and the countdown let everyone run around).
        /// </summary>
        public InputButtons Think(int me, PlayerState[] states, bool[] alive, bool[] isBot, int count, bool playing, Stage stage,
            IReadOnlyList<KnifeState> knives, IReadOnlyList<SideKnifeState> sideKnives, IReadOnlyList<PushableState> boxes)
        {
            Seq++;
            PlayerState self = states[me];
            InputButtons buttons = Wander();
            if (playing && alive[me])
            {
                // Now and then take a ride on the jump pad (like the console bots).
                if (_padTripTicks == 0 && self.Grounded && _random.Next(160) == 0) _padTripTicks = 120;
                if (_padTripTicks > 0)
                {
                    _padTripTicks--;
                    InputButtons seek = DodgeBrain.SeekPad(self, boxes, stage);
                    if (seek != InputButtons.None) buttons = seek;
                    else if (self.PadCooldown > 0 || self.Grounded) _padTripTicks = 0;
                }
                InputButtons attack = Attack(me, states, alive, isBot, count);
                if (attack != InputButtons.None) buttons = attack;
                _seenKnives.Clear();
                if (knives != null)
                    foreach (var k in knives) if (Notices(k.Id, 1)) _seenKnives.Add(k);
                _seenSides.Clear();
                if (sideKnives != null)
                    foreach (var k in sideKnives) if (Notices(k.Id, 2)) _seenSides.Add(k);
                // While a side knife it has seen is out, a normal or hard bot stays on the ground and keeps its dash
                // (a jump or dash at the wrong moment is what catches it); DodgeBrain jumps or ducks when it is time.
                if (_level != BotLevel.Easy && _seenSides.Count > 0 && attack == InputButtons.None)
                    buttons &= ~(InputButtons.Jump | InputButtons.Dash);
                buttons = DodgeBrain.Decide(self, _seenKnives, stage, _reactionTicks, buttons, boxes, _seenSides);
                // An easy bot walks out from under a knife but does not dash away: a late one still catches it.
                if (_level == BotLevel.Easy && attack == InputButtons.None) buttons &= ~InputButtons.Dash;
            }
            // Jump acts on the press edge only: a held jump is released every other tick (as the console bots do).
            if ((buttons & _lastSent & InputButtons.Jump) != 0) buttons &= ~InputButtons.Jump;
            _lastSent = buttons;
            return buttons;
        }

        /// <summary>
        /// Dash into a player standing within dash reach on my level: it knocks them back and flattens them, which can
        /// leave them under a knife. Bots go for people far more than for each other (a hard room is hard for the
        /// person, not a brawl the bots lose among themselves). Hard bots also walk up to a person nearby.
        /// Easy bots never do it.
        /// </summary>
        private InputButtons Attack(int me, PlayerState[] states, bool[] alive, bool[] isBot, int count)
        {
            PlayerState self = states[me];
            if (_level == BotLevel.Easy || !self.Grounded || self.StunTicks > 0 || self.Crouching) return InputButtons.None;
            bool hard = _level == BotLevel.Hard;
            int best = -1, bestDist = int.MaxValue;
            for (int i = 0; i < count; i++)
            {
                if (i == me || !alive[i]) continue;
                PlayerState o = states[i];
                if (!o.CanBeStunned) continue; // flat or blinking: a dash would only push
                int dist = Math.Abs(o.X - self.X);
                int reach = hard && !isBot[i] ? ChaseReach : DashReach;
                if (dist < GameConstants.PlayerHalfWidth * 2 || dist > reach) continue;
                if (Math.Abs(o.Y - self.Y) > 600) continue;
                if (isBot[i]) dist += ChaseReach; // a person first
                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = i;
                }
            }
            if (best < 0) return InputButtons.None;
            InputButtons toward = states[best].X > self.X ? InputButtons.Right : InputButtons.Left;
            bool person = !isBot[best];
            if (self.DashCooldown == 0 && Math.Abs(states[best].X - self.X) <= DashReach)
            {
                // percent per tick
                int chance = person ? (hard ? 35 : 6) : (hard ? 3 : 1);
                // The dash goes the way I face; facing turns with the held direction on this same tick.
                if (_random.Next(100) < chance) return toward | InputButtons.Dash;
            }
            // A hard bot closes in on a person (and waits for its dash there).
            return hard && person ? toward : InputButtons.None;
        }

        /// <summary>How far ahead a dash carries (4 ticks at 1.2 u) less a little, in milli-units.</summary>
        private const int DashReach = 4000;
        /// <summary>How far a hard bot walks to get a person within dash reach.</summary>
        private const int ChaseReach = 7000;

        private InputButtons Wander()
        {
            if (--_dirTicksLeft <= 0)
            {
                _dir = _random.Next(-1, 2);
                _dirTicksLeft = _random.Next(5, 25);
            }
            var b = InputButtons.None;
            if (_dir < 0) b |= InputButtons.Left;
            if (_dir > 0) b |= InputButtons.Right;
            if (_crouchTicksLeft > 0) _crouchTicksLeft--;
            else if (_random.Next(100) < 2) _crouchTicksLeft = _random.Next(10, 41);
            if (_crouchTicksLeft > 0) return b | InputButtons.Down;
            if (_random.Next(100) < 10) b |= InputButtons.Jump;
            if (_random.Next(100) < (_level == BotLevel.Easy ? 2 : 5)) b |= InputButtons.Dash;
            if (_random.Next(100) < 2) b |= InputButtons.Down | InputButtons.Jump;
            return b;
        }
    }
}
