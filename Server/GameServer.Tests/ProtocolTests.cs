using OnlineSeverGame.Shared.Protocol;
using OnlineSeverGame.Shared.Simulation;

namespace OnlineSeverGame.Server.Tests;

public class ProtocolTests
{
    private static T RoundTrip<T>(T packet) where T : IPacket
    {
        byte[] frame = PacketCodec.EncodeFrame(packet);
        Assert.Equal(frame.Length - 2, frame[0] | (frame[1] << 8));
        var decoder = new FrameDecoder();
        decoder.Push(frame, 0, frame.Length);
        Assert.True(decoder.TryReadFrame(out byte[] payload));
        Assert.False(decoder.TryReadFrame(out _));
        return Assert.IsType<T>(PacketCodec.DecodePayload(payload));
    }

    [Fact]
    public void Hello_round_trips_with_utf8_name()
    {
        var p = RoundTrip(new HelloPacket { ProtocolVersion = 7, Name = "칼피하기왕" });
        Assert.Equal(7, p.ProtocolVersion);
        Assert.Equal("칼피하기왕", p.Name);
    }

    [Fact]
    public void Welcome_reject_joined_left_round_trip()
    {
        var w = RoundTrip(new WelcomePacket { PlayerId = 3, ServerTick = 123456, StageId = 1 });
        Assert.Equal((3, 20, 123456u, 1, 8, ProtocolConstants.Version),
            (w.PlayerId, w.TickRate, w.ServerTick, w.StageId, w.MaxPlayers, w.ProtocolVersion));

        Assert.Equal(RejectReason.RoomFull, RoundTrip(new RejectPacket { Reason = RejectReason.RoomFull }).Reason);

        var j = RoundTrip(new PlayerJoinedPacket { PlayerId = 8, Name = "Bot8" });
        Assert.Equal((8, "Bot8"), (j.PlayerId, j.Name));

        var l = RoundTrip(new PlayerLeftPacket { PlayerId = 2, Reason = LeaveReason.Timeout });
        Assert.Equal((2, LeaveReason.Timeout), (l.PlayerId, l.Reason));
    }

    [Fact]
    public void Input_ping_pong_round_trip()
    {
        var i = RoundTrip(new InputPacket { Seq = uint.MaxValue - 1, Buttons = InputButtons.Left | InputButtons.Dash });
        Assert.Equal(uint.MaxValue - 1, i.Seq);
        Assert.Equal(InputButtons.Left | InputButtons.Dash, i.Buttons);

        Assert.Equal(99u, RoundTrip(new PingPacket { ClientTime = 99 }).ClientTime);
        var pong = RoundTrip(new PongPacket { ClientTime = 5, ServerTick = 6 });
        Assert.Equal((5u, 6u), (pong.ClientTime, pong.ServerTick));
    }

    [Fact]
    public void Snapshot_round_trips_every_field()
    {
        var state = new PlayerState
        {
            X = 12345, Y = -7, Vx = -1200, Vy = 800, Facing = 1, Grounded = true, DashTicks = 3, DashCooldown = 19,
            StunTicks = 2, PrevButtons = InputButtons.Right | InputButtons.Jump, Crouching = true, Stomped = true,
        };
        var snap = new SnapshotPacket
        {
            ServerTick = 1000, RoomState = RoomState.Playing, RoundNumber = 4, RoundTick = 321, StateTimer = 0,
        };
        snap.Players.Add(new PlayerSnapshot { PlayerId = 1, Alive = true, InRound = true, State = state, LastInputSeq = 77 });
        snap.Players.Add(new PlayerSnapshot
        {
            PlayerId = 2, Alive = false, InRound = false, State = new PlayerState { Facing = -1 }, LastInputSeq = 0,
        });
        snap.Knives.Add(new KnifeState { Id = 9, Phase = KnifePhase.Falling, X = 500, Y = 9000, Timer = 0 });
        snap.Knives.Add(new KnifeState { Id = 10, Kind = HazardKind.Icicle, Phase = KnifePhase.Warning, X = 700, Y = 14000, Timer = 20 });
        snap.Pushables.Add(new PushableState { Id = 1, X = 12000, Y = 0, Vx = -650, Vy = 0, Width = 1200, Height = 1000, Grounded = true });
        snap.Pushables.Add(new PushableState { Id = 7, X = 300, Y = 4800, Vx = 0, Vy = -300, Width = 600, Height = 600, Crumble = 12 });

        var r = RoundTrip(snap);
        Assert.Equal((1000u, RoomState.Playing, (ushort)4, 321u), (r.ServerTick, r.RoomState, r.RoundNumber, r.RoundTick));
        Assert.Equal(2, r.Players.Count);
        Assert.Equal(state, r.Players[0].State);
        Assert.True(r.Players[0].Alive && r.Players[0].InRound);
        Assert.Equal(77u, r.Players[0].LastInputSeq);
        Assert.False(r.Players[1].Alive || r.Players[1].InRound);
        Assert.Equal(-1, r.Players[1].State.Facing);
        Assert.Equal(snap.Knives, r.Knives);
        Assert.Equal(snap.Pushables, r.Pushables);
    }

    [Fact]
    public void Snapshot_entry_sizes_match_the_doc()
    {
        int Size(SnapshotPacket s) => PacketCodec.EncodeFrame(s).Length;
        var empty = new SnapshotPacket();
        var onePlayer = new SnapshotPacket();
        onePlayer.Players.Add(new PlayerSnapshot());
        var oneKnife = new SnapshotPacket();
        oneKnife.Knives.Add(new KnifeState());
        var oneBox = new SnapshotPacket();
        oneBox.Pushables.Add(new PushableState());
        Assert.Equal(2 + 1 + 4 + 1 + 2 + 4 + 2 + 1 + 1 + 1, Size(empty));
        Assert.Equal(26, Size(onePlayer) - Size(empty));
        Assert.Equal(14, Size(oneKnife) - Size(empty));
        Assert.Equal(23, Size(oneBox) - Size(empty));
    }

    [Fact]
    public void Round_start_and_end_round_trip()
    {
        var rs = new RoundStartPacket { RoundNumber = 2, CountdownTicks = 60, PlayStartTick = 5000, StageId = 3 };
        rs.Participants.AddRange(new byte[] { 1, 2, 5 });
        var rs2 = RoundTrip(rs);
        Assert.Equal((2, 60, 5000u, (byte)3), (rs2.RoundNumber, rs2.CountdownTicks, rs2.PlayStartTick, rs2.StageId));
        Assert.Equal(rs.Participants, rs2.Participants);

        var re = new RoundEndPacket { RoundNumber = 2, EndTick = 9000 };
        re.Winners.Add(5);
        re.Results.Add(new RoundResult { PlayerId = 5, Place = 1, SurvivedTicks = 800, Cause = EliminationCause.None });
        re.Results.Add(new RoundResult { PlayerId = 1, Place = 2, SurvivedTicks = 700, Cause = EliminationCause.Disconnected });
        var re2 = RoundTrip(re);
        Assert.Equal(re.Winners, re2.Winners);
        Assert.Equal(re.Results, re2.Results);
    }

    [Fact]
    public void Frames_split_into_single_bytes_reassemble()
    {
        byte[] a = PacketCodec.EncodeFrame(new InputPacket { Seq = 1, Buttons = InputButtons.Jump });
        byte[] b = PacketCodec.EncodeFrame(new HelloPacket { Name = "x" });
        byte[] stream = a.Concat(b).ToArray();

        var decoder = new FrameDecoder();
        var got = new List<IPacket>();
        foreach (byte value in stream)
        {
            decoder.Push(new[] { value }, 0, 1);
            while (decoder.TryReadFrame(out byte[] payload)) got.Add(PacketCodec.DecodePayload(payload));
        }
        Assert.Collection(got,
            p => Assert.Equal(1u, Assert.IsType<InputPacket>(p).Seq),
            p => Assert.Equal("x", Assert.IsType<HelloPacket>(p).Name));
        Assert.Equal(0, decoder.BufferedBytes);
    }

    [Fact]
    public void Merged_frames_and_a_partial_tail_in_one_read()
    {
        var frames = Enumerable.Range(1, 50)
            .Select(i => PacketCodec.EncodeFrame(new InputPacket { Seq = (uint)i })).ToList();
        byte[] all = frames.SelectMany(f => f).ToArray();
        int cut = all.Length - 3; // last frame missing 3 bytes

        var decoder = new FrameDecoder();
        decoder.Push(all, 0, cut);
        var seqs = new List<uint>();
        while (decoder.TryReadFrame(out byte[] payload)) seqs.Add(((InputPacket)PacketCodec.DecodePayload(payload)).Seq);
        Assert.Equal(Enumerable.Range(1, 49).Select(i => (uint)i), seqs);

        decoder.Push(all, cut, 3);
        Assert.True(decoder.TryReadFrame(out byte[] last));
        Assert.Equal(50u, ((InputPacket)PacketCodec.DecodePayload(last)).Seq);
    }

    [Fact]
    public void Large_snapshot_spanning_buffer_growth_reassembles()
    {
        var snap = new SnapshotPacket();
        for (int i = 0; i < 200; i++) snap.Knives.Add(new KnifeState { Id = (ushort)i, Phase = KnifePhase.Falling, X = i });
        byte[] frame = PacketCodec.EncodeFrame(snap);
        Assert.True(frame.Length > 2600);

        var decoder = new FrameDecoder();
        for (int off = 0; off < frame.Length; off += 1000)
            decoder.Push(frame, off, Math.Min(1000, frame.Length - off));
        Assert.True(decoder.TryReadFrame(out byte[] payload));
        Assert.Equal(200, ((SnapshotPacket)PacketCodec.DecodePayload(payload)).Knives.Count);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(ProtocolConstants.MaxPayloadSize + 1)]
    public void Bad_frame_length_throws(int length)
    {
        var decoder = new FrameDecoder();
        decoder.Push(new[] { (byte)(length & 0xFF), (byte)(length >> 8) }, 0, 2);
        Assert.Throws<PacketFormatException>(() => decoder.TryReadFrame(out _));
    }

    [Fact]
    public void Unknown_type_short_body_and_trailing_bytes_throw()
    {
        Assert.Throws<PacketFormatException>(() => PacketCodec.DecodePayload(new byte[] { 200 }));
        Assert.Throws<PacketFormatException>(() => PacketCodec.DecodePayload(new byte[] { (byte)PacketType.Input, 1, 0 }));
        Assert.Throws<PacketFormatException>(() =>
            PacketCodec.DecodePayload(new byte[] { (byte)PacketType.Ping, 1, 0, 0, 0, 9 }));
    }

    [Fact]
    public void Long_names_are_truncated_on_a_character_boundary()
    {
        string name = new string('가', 20); // 3 bytes each = 60 bytes
        var p = RoundTrip(new HelloPacket { Name = name });
        Assert.Equal(new string('가', 10), p.Name); // 30 bytes <= 32
    }
}
