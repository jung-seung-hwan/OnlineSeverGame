using System.Net.Sockets;
using OnlineSeverGame.Shared.Protocol;
using static OnlineSeverGame.Shared.Simulation.GameConstants;

namespace OnlineSeverGame.Server.Tests;

/// <summary>
/// docs/work/player-host.md: a player hosts the room inside the game (protocol v13). The first round waits for the
/// host's StartRequest; after that rounds follow on their own, as in a dedicated-server room.
/// </summary>
public class PlayerHostTests
{
    private static GameRoom Room(int minPlayers = 1) =>
        new GameRoom(new RoomConfig { MinPlayers = minPlayers, HostStarts = true, AutoSpawnKnives = false, AutoSpawnSideKnives = false });

    private static void TickN(GameRoom room, int n)
    {
        for (int i = 0; i < n; i++) room.Tick();
    }

    [Fact]
    public void HostStartRoom_WaitsForTheHost_EvenWithEnoughPlayers()
    {
        var room = Room();
        room.AddPlayer("host");
        room.AddPlayer("guest");
        TickN(room, TickRate * 5);
        Assert.Equal(RoomState.Waiting, room.State);
    }

    [Fact]
    public void HostAlone_CanStart_AndTheCountdownBegins()
    {
        var room = Room();
        byte host = room.AddPlayer("host");
        room.Tick();
        Assert.True(room.RequestStart(host));
        room.Tick();
        Assert.Equal(RoomState.Countdown, room.State);
        TickN(room, CountdownTicks + 2);
        Assert.Equal(RoomState.Playing, room.State);
    }

    [Fact]
    public void NonHost_CannotStart()
    {
        var room = Room();
        room.AddPlayer("host");
        byte guest = room.AddPlayer("guest");
        Assert.False(room.RequestStart(guest));
        TickN(room, 3);
        Assert.Equal(RoomState.Waiting, room.State);
    }

    [Fact]
    public void Start_IsIgnoredOutsideWaiting_AndInRoomsThatStartOnTheirOwn()
    {
        var room = Room();
        byte host = room.AddPlayer("host");
        Assert.True(room.RequestStart(host));
        room.Tick();
        Assert.Equal(RoomState.Countdown, room.State);
        Assert.False(room.RequestStart(host));

        var auto = new GameRoom(new RoomConfig { MinPlayers = 2, AutoSpawnKnives = false });
        byte first = auto.AddPlayer("a");
        Assert.False(auto.RequestStart(first));
    }

    [Fact]
    public void StartBelowMinPlayers_IsDropped_NotRemembered()
    {
        var room = Room(minPlayers: 2);
        byte host = room.AddPlayer("host");
        Assert.True(room.RequestStart(host));
        room.Tick();
        Assert.Equal(RoomState.Waiting, room.State);
        room.AddPlayer("guest");
        TickN(room, 3);
        Assert.Equal(RoomState.Waiting, room.State); // the early press does not start the round later
    }

    [Fact]
    public void RoomInfo_TellsClientsTheRoomWaitsForTheHost()
    {
        var room = Room();
        room.AddPlayer("host");
        room.Tick();
        var info = room.Outbox.Select(o => PacketCodec.DecodePayload(o.Frame, 2, o.Frame.Length - 2))
            .OfType<RoomInfoPacket>().First();
        Assert.True(info.HostStarts);
    }

    [Fact]
    public void RoomInfoFlags_And_StartRequest_RoundTrip()
    {
        var info = new RoomInfoPacket { HostId = 3, NextStageId = 2, Rotation = MapRotation.Random, NextChosen = true, HostStarts = true };
        byte[] frame = PacketCodec.EncodeFrame(info);
        var back = (RoomInfoPacket)PacketCodec.DecodePayload(frame, 2, frame.Length - 2);
        Assert.True(back.SameAs(info));
        Assert.True(back.HostStarts && back.NextChosen);

        info.NextChosen = false;
        frame = PacketCodec.EncodeFrame(info);
        back = (RoomInfoPacket)PacketCodec.DecodePayload(frame, 2, frame.Length - 2);
        Assert.True(back.HostStarts);
        Assert.False(back.NextChosen);

        frame = PacketCodec.EncodeFrame(new StartRequestPacket());
        Assert.Equal(3, frame.Length); // header + type byte, no payload
        Assert.IsType<StartRequestPacket>(PacketCodec.DecodePayload(frame, 2, frame.Length - 2));
    }

    /// <summary>The real server over TCP: the host's StartRequest starts the round, a guest's does not.</summary>
    [Fact]
    public void GameServer_HostStartsOverTcp()
    {
        const int port = 7809;
        var server = new GameServer(new ServerOptions { Port = port, MinPlayers = 1, HostStarts = true, LogAddresses = false, Seed = 1 });
        server.Start();
        try
        {
            using var host = Join(port, "host");
            Thread.Sleep(200); // the host's Hello reaches the game loop first (two receive threads race otherwise)
            using var guest = Join(port, "guest");
            Thread.Sleep(300);
            Send(guest, new StartRequestPacket());
            Thread.Sleep(300);
            Assert.DoesNotContain(ReadAll(host), p => p is RoundStartPacket);

            Send(host, new StartRequestPacket());
            var deadline = DateTime.UtcNow.AddSeconds(3);
            bool started = false;
            while (!started && DateTime.UtcNow < deadline)
            {
                started = ReadAll(host).Any(p => p is RoundStartPacket);
                Thread.Sleep(50);
            }
            Assert.True(started);
        }
        finally
        {
            Assert.True(server.StopAndWait(3000));
        }
    }

    private static TcpClient Join(int port, string name)
    {
        var c = new TcpClient("127.0.0.1", port) { NoDelay = true };
        Send(c, new HelloPacket { ProtocolVersion = ProtocolConstants.Version, Name = name });
        return c;
    }

    private static void Send(TcpClient c, IPacket p)
    {
        byte[] f = PacketCodec.EncodeFrame(p);
        c.GetStream().Write(f, 0, f.Length);
    }

    private static readonly Dictionary<TcpClient, FrameDecoder> Decoders = new();

    private static List<IPacket> ReadAll(TcpClient c)
    {
        var list = new List<IPacket>();
        if (!Decoders.TryGetValue(c, out var dec)) Decoders[c] = dec = new FrameDecoder();
        var buf = new byte[65536];
        while (c.Available > 0)
        {
            int n = c.GetStream().Read(buf, 0, buf.Length);
            dec.Push(buf, 0, n);
        }
        while (dec.TryReadFrame(out byte[] payload)) list.Add(PacketCodec.DecodePayload(payload));
        return list;
    }
}
