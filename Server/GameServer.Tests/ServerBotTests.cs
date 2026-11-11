using System.Net.Sockets;
using OnlineSeverGame.Shared.Protocol;
using OnlineSeverGame.Shared.Simulation;
using Xunit.Abstractions;
using static OnlineSeverGame.Shared.Simulation.GameConstants;

namespace OnlineSeverGame.Server.Tests;

/// <summary>
/// docs/work/bots.md: bots the room runs itself (protocol v14). The game's offline mode is a loopback room with seven
/// of them; a host adds or removes them in the waiting room and picks one level for all.
/// </summary>
public class ServerBotTests
{
    private readonly ITestOutputHelper _out;

    public ServerBotTests(ITestOutputHelper output) => _out = output;

    private static GameRoom Room(BotLevel level = BotLevel.Normal, int seed = 1, bool hostStarts = true) =>
        new GameRoom(new RoomConfig { MinPlayers = 1, HostStarts = hostStarts, BotLevel = level, Seed = seed, BotNamePrefix = "봇" });

    private static RoomInfoPacket LastRoomInfo(GameRoom room) =>
        room.Outbox.Select(o => PacketCodec.DecodePayload(o.Frame, 2, o.Frame.Length - 2)).OfType<RoomInfoPacket>().LastOrDefault();

    [Fact]
    public void Bots_JoinWithNames_AreMarkedInRoomInfo_AndAreNeverHost()
    {
        var room = Room();
        byte b1 = room.AddBot();
        byte host = room.AddPlayer("me");
        byte b2 = room.AddBot();
        room.Tick();
        Assert.Equal(host, room.HostId);
        Assert.Equal("봇 1", room.GetPlayer(b1).Name);
        Assert.Equal("봇 2", room.GetPlayer(b2).Name);
        var info = LastRoomInfo(room);
        Assert.True(info.IsBot(b1) && info.IsBot(b2));
        Assert.False(info.IsBot(host));
        Assert.Equal(BotLevel.Normal, info.BotLevel);
        // Nothing is queued for a bot to send: it has no socket.
        Assert.DoesNotContain(room.Outbox, o => o.Target == b1 || o.Target == b2);
    }

    [Fact]
    public void BotsAlone_NeverStartARound()
    {
        var room = Room(hostStarts: false);
        for (int i = 0; i < 3; i++) room.AddBot();
        for (int i = 0; i < TickRate * 3; i++) room.Tick();
        Assert.Equal(RoomState.Waiting, room.State);
        Assert.Equal(0, room.HostId);
    }

    [Fact]
    public void OnlyTheHost_AddsRemovesAndSetsTheLevel()
    {
        var room = Room();
        byte host = room.AddPlayer("host");
        byte guest = room.AddPlayer("guest");
        Assert.False(room.RequestBot(guest, BotAction.Add, 0));
        Assert.Equal(0, room.BotCount);
        Assert.True(room.RequestBot(host, BotAction.Add, 0));
        Assert.True(room.RequestBot(host, BotAction.Add, 0));
        Assert.Equal(2, room.BotCount);
        Assert.True(room.RequestBot(host, BotAction.SetLevel, BotLevel.Hard));
        Assert.Equal(BotLevel.Hard, room.BotLevel);
        Assert.All(room.Players.Where(p => p.IsBot), p => Assert.Equal(BotLevel.Hard, p.Bot.Level));
        Assert.False(room.RequestBot(host, BotAction.SetLevel, (BotLevel)9));
        Assert.True(room.RequestBot(host, BotAction.Remove, 0));
        Assert.Equal(1, room.BotCount);
        Assert.True(room.RequestBot(host, BotAction.Remove, 0));
        Assert.False(room.RequestBot(host, BotAction.Remove, 0));
    }

    [Fact]
    public void APersonJoiningAFullRoom_TakesABotsPlace()
    {
        var room = Room();
        room.AddPlayer("host");
        for (int i = 0; i < 7; i++) Assert.NotEqual(0, room.AddBot());
        Assert.Equal(0, room.AddBot()); // full
        byte friend = room.AddPlayer("friend");
        Assert.NotEqual(0, friend);
        Assert.Equal(8, room.PlayerCount);
        Assert.Equal(6, room.BotCount);
    }

    [Fact]
    public void BotsMove_InTheWaitingRoom_AndAfterTheHostStarts_PlayWholeRounds()
    {
        var room = Room(seed: 7);
        byte host = room.AddPlayer("host");
        for (int i = 0; i < 7; i++) room.AddBot();
        var spawn = room.Players.Where(p => p.IsBot).ToDictionary(p => p.Id, p => p.State.X);
        for (int i = 0; i < TickRate * 2; i++) room.Tick();
        Assert.Contains(room.Players.Where(p => p.IsBot), p => p.State.X != spawn[p.Id]);
        Assert.Equal(RoomState.Waiting, room.State);

        room.RequestStart(host);
        int guard = 0;
        while (room.CompletedRounds < 2 && guard++ < TickRate * 400)
        {
            // The host stands still (and is soon gone); the bots carry on to the end of the round.
            room.QueueInput(host, (uint)guard, InputButtons.None);
            room.Tick();
        }
        Assert.Equal(2, room.CompletedRounds);
        Assert.NotNull(room.LastRoundEnd);
    }

    [Fact]
    public void HardBots_DashIntoPeople_AndTheTargetIsFlattened()
    {
        var room = new GameRoom(new RoomConfig
        {
            MinPlayers = 1, HostStarts = true, BotLevel = BotLevel.Hard, Seed = 3,
            AutoSpawnKnives = false, AutoSpawnSideKnives = false, Stage = Stage.Default, Rotation = MapRotation.Fixed,
        });
        byte host = room.AddPlayer("host");
        byte bot = room.AddBot();
        room.RequestStart(host);
        for (int i = 0; i < CountdownTicks + 2; i++) room.Tick();
        Assert.Equal(RoomState.Playing, room.State);

        // Stand the bot next to me on the floor, both grounded.
        var me = room.GetPlayer(host);
        var b = room.GetPlayer(bot);
        me.State = PlayerState.SpawnAt(8000, 0, 1);
        b.State = PlayerState.SpawnAt(10500, 0, -1);
        bool flattened = false;
        for (int i = 0; i < TickRate * 5 && !flattened; i++)
        {
            room.QueueInput(host, (uint)(i + 1), InputButtons.None);
            room.Tick();
            flattened = me.State.Stomped && me.State.StunTicks > 0;
        }
        Assert.True(flattened, "a hard bot next to a standing player dashes into them");
        Assert.True(room.RoundDashStuns > 0);
    }

    /// <summary>
    /// Over the same rounds (seeds 1-3, two rounds each, seven bots and an idle person), easy bots die soonest and hard
    /// bots last longest: they notice warnings sooner, miss none, and keep on the ground while side knives fly.
    /// </summary>
    [Fact]
    public void HarderBots_SurviveLonger()
    {
        lock (Causes) Causes.Clear();
        double easy = AverageSurvivedTicks(BotLevel.Easy);
        double normal = AverageSurvivedTicks(BotLevel.Normal);
        double hard = AverageSurvivedTicks(BotLevel.Hard);
        _out.WriteLine($"average survived ticks: easy {easy:0}, normal {normal:0}, hard {hard:0}");
        foreach (var kv in Causes.OrderBy(k => k.Key)) _out.WriteLine($"{kv.Key} {kv.Value}");
        Assert.True(normal > easy * 1.15, $"easy {easy:0}, normal {normal:0}");
        Assert.True(hard > normal * 1.1, $"normal {normal:0}, hard {hard:0}");
    }

    /// <summary>Deaths by level and cause, written to the test output.</summary>
    private static readonly Dictionary<string, int> Causes = new();

    private static double AverageSurvivedTicks(BotLevel level)
    {
        long total = 0;
        int count = 0;
        for (int seed = 1; seed <= 3; seed++)
        {
            var room = Room(level, seed);
            byte host = room.AddPlayer("host");
            for (int i = 0; i < 7; i++) room.AddBot();
            room.RemovePlayer(host, LeaveReason.Disconnected); // leave the bots to it
            byte watcher = room.AddPlayer("watcher"); // a person has to be in the room for rounds to start
            room.RequestStart(watcher);
            int rounds = 0, guard = 0;
            while (rounds < 2 && guard++ < TickRate * 600)
            {
                room.Tick();
                if (room.CompletedRounds > rounds)
                {
                    rounds = room.CompletedRounds;
                    foreach (var r in room.LastRoundEnd.Results)
                    {
                        if (r.PlayerId == watcher) continue;
                        total += r.SurvivedTicks;
                        count++;
                        Causes[level + ":" + r.Cause] = Causes.GetValueOrDefault(level + ":" + r.Cause) + 1;
                    }
                }
            }
        }
        return (double)total / Math.Max(1, count);
    }

    [Fact]
    public void BotRequest_And_RoomInfoBots_RoundTrip()
    {
        var req = new BotRequestPacket { Action = BotAction.SetLevel, Level = BotLevel.Easy };
        byte[] frame = PacketCodec.EncodeFrame(req);
        var back = (BotRequestPacket)PacketCodec.DecodePayload(frame, 2, frame.Length - 2);
        Assert.Equal(BotAction.SetLevel, back.Action);
        Assert.Equal(BotLevel.Easy, back.Level);

        var info = new RoomInfoPacket { HostId = 1, NextStageId = 1, BotLevel = BotLevel.Hard, BotMask = 0b1000_0110 };
        frame = PacketCodec.EncodeFrame(info);
        var infoBack = (RoomInfoPacket)PacketCodec.DecodePayload(frame, 2, frame.Length - 2);
        Assert.True(infoBack.SameAs(info));
        Assert.True(infoBack.IsBot(2) && infoBack.IsBot(3) && infoBack.IsBot(8));
        Assert.False(infoBack.IsBot(1));
    }

    /// <summary>The offline mode's server: loopback only, any free port, seven bots waiting for the one player.</summary>
    [Fact]
    public void OfflineServer_ListensOnLoopback_WithItsBots()
    {
        var server = new GameServer(new ServerOptions
        {
            Port = 0, LoopbackOnly = true, MinPlayers = 1, HostStarts = true, Bots = 7, BotLevel = BotLevel.Easy,
            LogAddresses = false, Seed = 5,
        });
        server.Start();
        try
        {
            Assert.NotEqual(0, server.Port);
            using var me = new TcpClient("127.0.0.1", server.Port) { NoDelay = true };
            byte[] hello = PacketCodec.EncodeFrame(new HelloPacket { ProtocolVersion = ProtocolConstants.Version, Name = "me" });
            me.GetStream().Write(hello, 0, hello.Length);
            var dec = new FrameDecoder();
            var buf = new byte[65536];
            var deadline = DateTime.UtcNow.AddSeconds(3);
            int joined = 0;
            RoomInfoPacket info = null;
            while (DateTime.UtcNow < deadline && (joined < 8 || info == null))
            {
                me.ReceiveTimeout = 500;
                int n;
                try { n = me.GetStream().Read(buf, 0, buf.Length); } catch (IOException) { continue; }
                dec.Push(buf, 0, n);
                while (dec.TryReadFrame(out byte[] payload))
                {
                    var p = PacketCodec.DecodePayload(payload);
                    if (p is PlayerJoinedPacket) joined++;
                    if (p is RoomInfoPacket ri) info = ri;
                }
            }
            Assert.Equal(8, joined);
            Assert.NotNull(info);
            Assert.Equal(BotLevel.Easy, info.BotLevel);
            Assert.Equal(7, Enumerable.Range(1, 8).Count(id => info.IsBot((byte)id)));
        }
        finally
        {
            Assert.True(server.StopAndWait(3000));
        }
    }
}
