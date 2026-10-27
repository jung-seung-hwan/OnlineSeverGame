using System;
using System.Collections.Generic;
using OnlineSeverGame.Shared.Simulation;

namespace OnlineSeverGame.Shared.Protocol
{
    /// <summary>A packet body. Byte layouts are listed in docs/protocol.md; keep the two in sync.</summary>
    public interface IPacket
    {
        PacketType Type { get; }
        void Write(PacketWriter w);
    }

    /// <summary>C: first packet on a new connection.</summary>
    public sealed class HelloPacket : IPacket
    {
        public ushort ProtocolVersion = ProtocolConstants.Version;
        public string Name = "";

        public PacketType Type => PacketType.Hello;

        public void Write(PacketWriter w)
        {
            w.WriteUInt16(ProtocolVersion);
            w.WriteString(Name);
        }

        public static HelloPacket Read(PacketReader r) =>
            new HelloPacket { ProtocolVersion = r.ReadUInt16(), Name = r.ReadString() };
    }

    /// <summary>S: reply to Hello when the player got a slot.</summary>
    public sealed class WelcomePacket : IPacket
    {
        public ushort ProtocolVersion = ProtocolConstants.Version;
        public byte PlayerId;
        public byte TickRate = GameConstants.TickRate;
        public uint ServerTick;
        public byte StageId;
        public byte MaxPlayers = ProtocolConstants.MaxPlayers;

        public PacketType Type => PacketType.Welcome;

        public void Write(PacketWriter w)
        {
            w.WriteUInt16(ProtocolVersion);
            w.WriteByte(PlayerId);
            w.WriteByte(TickRate);
            w.WriteUInt32(ServerTick);
            w.WriteByte(StageId);
            w.WriteByte(MaxPlayers);
        }

        public static WelcomePacket Read(PacketReader r) => new WelcomePacket
        {
            ProtocolVersion = r.ReadUInt16(), PlayerId = r.ReadByte(), TickRate = r.ReadByte(),
            ServerTick = r.ReadUInt32(), StageId = r.ReadByte(), MaxPlayers = r.ReadByte(),
        };
    }

    /// <summary>S: reply to Hello when the player cannot join. The server closes the connection after it.</summary>
    public sealed class RejectPacket : IPacket
    {
        public RejectReason Reason;

        public PacketType Type => PacketType.Reject;

        public void Write(PacketWriter w) => w.WriteByte((byte)Reason);

        public static RejectPacket Read(PacketReader r) => new RejectPacket { Reason = (RejectReason)r.ReadByte() };
    }

    /// <summary>S: roster entry. Sent for every existing player (including yourself) after Welcome, then for each newcomer.</summary>
    public sealed class PlayerJoinedPacket : IPacket
    {
        public byte PlayerId;
        public string Name = "";

        public PacketType Type => PacketType.PlayerJoined;

        public void Write(PacketWriter w)
        {
            w.WriteByte(PlayerId);
            w.WriteString(Name);
        }

        public static PlayerJoinedPacket Read(PacketReader r) =>
            new PlayerJoinedPacket { PlayerId = r.ReadByte(), Name = r.ReadString() };
    }

    /// <summary>S: the player is gone (and, if in a round, eliminated). The id may be reused later.</summary>
    public sealed class PlayerLeftPacket : IPacket
    {
        public byte PlayerId;
        public LeaveReason Reason;

        public PacketType Type => PacketType.PlayerLeft;

        public void Write(PacketWriter w)
        {
            w.WriteByte(PlayerId);
            w.WriteByte((byte)Reason);
        }

        public static PlayerLeftPacket Read(PacketReader r) =>
            new PlayerLeftPacket { PlayerId = r.ReadByte(), Reason = (LeaveReason)r.ReadByte() };
    }

    /// <summary>C: buttons for one client tick. Seq starts at 1 and increases by 1 every client tick.</summary>
    public sealed class InputPacket : IPacket
    {
        public uint Seq;
        public InputButtons Buttons;

        public PacketType Type => PacketType.Input;

        public void Write(PacketWriter w)
        {
            w.WriteUInt32(Seq);
            w.WriteByte((byte)Buttons);
        }

        public static InputPacket Read(PacketReader r) =>
            new InputPacket { Seq = r.ReadUInt32(), Buttons = (InputButtons)r.ReadByte() };
    }

    [Flags]
    public enum PlayerFlags : byte
    {
        None = 0,
        Alive = 1 << 0,
        /// <summary>Participant of the current round (false = joined mid-round, spectating).</summary>
        InRound = 1 << 1,
        Grounded = 1 << 2,
        FacingRight = 1 << 3,
        Crouching = 1 << 4,
        /// <summary>The current stun came from a stomp (draw flattened).</summary>
        Stomped = 1 << 5,
    }

    public struct PlayerSnapshot
    {
        public byte PlayerId;
        public bool Alive;
        public bool InRound;
        public PlayerState State;
        /// <summary>Seq of the last input the server applied for this player (0 = none yet).</summary>
        public uint LastInputSeq;

        public void Write(PacketWriter w)
        {
            PlayerFlags flags = PlayerFlags.None;
            if (Alive) flags |= PlayerFlags.Alive;
            if (InRound) flags |= PlayerFlags.InRound;
            if (State.Grounded) flags |= PlayerFlags.Grounded;
            if (State.Facing > 0) flags |= PlayerFlags.FacingRight;
            if (State.Crouching) flags |= PlayerFlags.Crouching;
            if (State.Stomped) flags |= PlayerFlags.Stomped;
            w.WriteByte(PlayerId);
            w.WriteByte((byte)flags);
            w.WriteInt32(State.X);
            w.WriteInt32(State.Y);
            w.WriteInt32(State.Vx);
            w.WriteInt32(State.Vy);
            w.WriteByte(State.DashTicks);
            w.WriteByte(State.DashCooldown);
            w.WriteByte(State.StunTicks);
            w.WriteByte((byte)State.PrevButtons);
            w.WriteUInt32(LastInputSeq);
        }

        public static PlayerSnapshot Read(PacketReader r)
        {
            var p = new PlayerSnapshot { PlayerId = r.ReadByte() };
            var flags = (PlayerFlags)r.ReadByte();
            p.Alive = (flags & PlayerFlags.Alive) != 0;
            p.InRound = (flags & PlayerFlags.InRound) != 0;
            p.State.Grounded = (flags & PlayerFlags.Grounded) != 0;
            p.State.Facing = (flags & PlayerFlags.FacingRight) != 0 ? (sbyte)1 : (sbyte)-1;
            p.State.Crouching = (flags & PlayerFlags.Crouching) != 0;
            p.State.Stomped = (flags & PlayerFlags.Stomped) != 0;
            p.State.X = r.ReadInt32();
            p.State.Y = r.ReadInt32();
            p.State.Vx = r.ReadInt32();
            p.State.Vy = r.ReadInt32();
            p.State.DashTicks = r.ReadByte();
            p.State.DashCooldown = r.ReadByte();
            p.State.StunTicks = r.ReadByte();
            p.State.PrevButtons = (InputButtons)r.ReadByte();
            p.LastInputSeq = r.ReadUInt32();
            return p;
        }
    }

    /// <summary>S: full world state, broadcast every tick. Same bytes for every client.</summary>
    public sealed class SnapshotPacket : IPacket
    {
        public uint ServerTick;
        public RoomState RoomState;
        public ushort RoundNumber;
        /// <summary>Ticks since Playing began (0 outside Playing).</summary>
        public uint RoundTick;
        /// <summary>Ticks left in Countdown / Ended (0 otherwise).</summary>
        public ushort StateTimer;
        public List<PlayerSnapshot> Players = new List<PlayerSnapshot>();
        public List<KnifeState> Knives = new List<KnifeState>();
        public List<PushableState> Pushables = new List<PushableState>();

        public PacketType Type => PacketType.Snapshot;

        public void Write(PacketWriter w)
        {
            if (Players.Count > 255 || Knives.Count > 255 || Pushables.Count > 255) throw new InvalidOperationException("Too many entries");
            w.WriteUInt32(ServerTick);
            w.WriteByte((byte)RoomState);
            w.WriteUInt16(RoundNumber);
            w.WriteUInt32(RoundTick);
            w.WriteUInt16(StateTimer);
            w.WriteByte((byte)Players.Count);
            for (int i = 0; i < Players.Count; i++) Players[i].Write(w);
            w.WriteByte((byte)Knives.Count);
            for (int i = 0; i < Knives.Count; i++)
            {
                KnifeState k = Knives[i];
                w.WriteUInt16(k.Id);
                w.WriteByte((byte)k.Kind);
                w.WriteByte((byte)k.Phase);
                w.WriteInt32(k.X);
                w.WriteInt32(k.Y);
                w.WriteUInt16(k.Timer);
            }
            w.WriteByte((byte)Pushables.Count);
            for (int i = 0; i < Pushables.Count; i++)
            {
                PushableState b = Pushables[i];
                w.WriteUInt16(b.Id);
                w.WriteInt32(b.X);
                w.WriteInt32(b.Y);
                w.WriteInt32(b.Vx);
                w.WriteInt32(b.Vy);
                w.WriteUInt16(b.Width);
                w.WriteUInt16(b.Height);
                w.WriteByte(b.Crumble);
            }
        }

        public static SnapshotPacket Read(PacketReader r)
        {
            var s = new SnapshotPacket
            {
                ServerTick = r.ReadUInt32(), RoomState = (RoomState)r.ReadByte(), RoundNumber = r.ReadUInt16(),
                RoundTick = r.ReadUInt32(), StateTimer = r.ReadUInt16(),
            };
            int players = r.ReadByte();
            for (int i = 0; i < players; i++) s.Players.Add(PlayerSnapshot.Read(r));
            int knives = r.ReadByte();
            for (int i = 0; i < knives; i++)
            {
                s.Knives.Add(new KnifeState
                {
                    Id = r.ReadUInt16(), Kind = (HazardKind)r.ReadByte(), Phase = (KnifePhase)r.ReadByte(),
                    X = r.ReadInt32(), Y = r.ReadInt32(),
                    Timer = r.ReadUInt16(),
                });
            }
            int boxes = r.ReadByte();
            for (int i = 0; i < boxes; i++)
            {
                var b = new PushableState
                {
                    Id = r.ReadUInt16(), X = r.ReadInt32(), Y = r.ReadInt32(), Vx = r.ReadInt32(), Vy = r.ReadInt32(),
                    Width = r.ReadUInt16(), Height = r.ReadUInt16(), Crumble = r.ReadByte(),
                };
                b.Grounded = b.Vy == 0;
                s.Pushables.Add(b);
            }
            return s;
        }
    }

    /// <summary>S: a round's countdown began. Participants are at spawn points; Playing starts at PlayStartTick.</summary>
    public sealed class RoundStartPacket : IPacket
    {
        public ushort RoundNumber;
        public ushort CountdownTicks;
        public uint PlayStartTick;
        /// <summary>Map of this round (Stage.ById). The map changes only here.</summary>
        public byte StageId;
        public List<byte> Participants = new List<byte>();

        public PacketType Type => PacketType.RoundStart;

        public void Write(PacketWriter w)
        {
            w.WriteUInt16(RoundNumber);
            w.WriteUInt16(CountdownTicks);
            w.WriteUInt32(PlayStartTick);
            w.WriteByte(StageId);
            w.WriteByte((byte)Participants.Count);
            for (int i = 0; i < Participants.Count; i++) w.WriteByte(Participants[i]);
        }

        public static RoundStartPacket Read(PacketReader r)
        {
            var p = new RoundStartPacket
            {
                RoundNumber = r.ReadUInt16(), CountdownTicks = r.ReadUInt16(), PlayStartTick = r.ReadUInt32(),
                StageId = r.ReadByte(),
            };
            int n = r.ReadByte();
            for (int i = 0; i < n; i++) p.Participants.Add(r.ReadByte());
            return p;
        }
    }

    public struct RoundResult
    {
        public byte PlayerId;
        /// <summary>1 = winner. Players eliminated on the same tick share a place.</summary>
        public byte Place;
        /// <summary>Ticks survived since Playing began.</summary>
        public uint SurvivedTicks;
        public EliminationCause Cause;
    }

    /// <summary>S: the round is over. Results are ordered by place.</summary>
    public sealed class RoundEndPacket : IPacket
    {
        public ushort RoundNumber;
        public uint EndTick;
        public List<byte> Winners = new List<byte>();
        public List<RoundResult> Results = new List<RoundResult>();

        public PacketType Type => PacketType.RoundEnd;

        public void Write(PacketWriter w)
        {
            w.WriteUInt16(RoundNumber);
            w.WriteUInt32(EndTick);
            w.WriteByte((byte)Winners.Count);
            for (int i = 0; i < Winners.Count; i++) w.WriteByte(Winners[i]);
            w.WriteByte((byte)Results.Count);
            for (int i = 0; i < Results.Count; i++)
            {
                RoundResult res = Results[i];
                w.WriteByte(res.PlayerId);
                w.WriteByte(res.Place);
                w.WriteUInt32(res.SurvivedTicks);
                w.WriteByte((byte)res.Cause);
            }
        }

        public static RoundEndPacket Read(PacketReader r)
        {
            var p = new RoundEndPacket { RoundNumber = r.ReadUInt16(), EndTick = r.ReadUInt32() };
            int winners = r.ReadByte();
            for (int i = 0; i < winners; i++) p.Winners.Add(r.ReadByte());
            int results = r.ReadByte();
            for (int i = 0; i < results; i++)
            {
                p.Results.Add(new RoundResult
                {
                    PlayerId = r.ReadByte(), Place = r.ReadByte(), SurvivedTicks = r.ReadUInt32(),
                    Cause = (EliminationCause)r.ReadByte(),
                });
            }
            return p;
        }
    }

    /// <summary>C: RTT probe; ClientTime is any client clock value, echoed back.</summary>
    public sealed class PingPacket : IPacket
    {
        public uint ClientTime;

        public PacketType Type => PacketType.Ping;

        public void Write(PacketWriter w) => w.WriteUInt32(ClientTime);

        public static PingPacket Read(PacketReader r) => new PingPacket { ClientTime = r.ReadUInt32() };
    }

    /// <summary>S: answer to Ping.</summary>
    public sealed class PongPacket : IPacket
    {
        public uint ClientTime;
        public uint ServerTick;

        public PacketType Type => PacketType.Pong;

        public void Write(PacketWriter w)
        {
            w.WriteUInt32(ClientTime);
            w.WriteUInt32(ServerTick);
        }

        public static PongPacket Read(PacketReader r) =>
            new PongPacket { ClientTime = r.ReadUInt32(), ServerTick = r.ReadUInt32() };
    }
}
