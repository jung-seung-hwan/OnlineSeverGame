using OnlineSeverGame.Server;
using OnlineSeverGame.Shared.Protocol;
using OnlineSeverGame.Shared.Simulation;

namespace OnlineSeverGame.Server.Tests;

/// <summary>docs/work/char-motion.md: emotes (EmoteRequest / Emote, protocol v7) are relayed to everyone, rate-limited.</summary>
public class EmoteTests
{
    private static GameRoom Room() => new(new RoomConfig { MinPlayers = 3, AutoSpawnKnives = false, Seed = 1 });

    private static List<EmotePacket> Emotes(GameRoom room)
    {
        var list = room.Outbox
            .Select(o => (o.Target, Packet: PacketCodec.DecodePayload(o.Frame, 2, o.Frame.Length - 2)))
            .Where(x => x.Packet is EmotePacket)
            .Select(x =>
            {
                Assert.Equal(0, x.Target); // to everyone, the sender included
                return (EmotePacket)x.Packet;
            })
            .ToList();
        room.Outbox.Clear();
        return list;
    }

    [Fact]
    public void Packets_round_trip()
    {
        var frame = PacketCodec.EncodeFrame(new EmoteRequestPacket { Emote = EmoteKind.Taunt });
        Assert.Equal(2 + 1 + 1, frame.Length);
        var req = Assert.IsType<EmoteRequestPacket>(PacketCodec.DecodePayload(frame, 2, frame.Length - 2));
        Assert.Equal(EmoteKind.Taunt, req.Emote);

        frame = PacketCodec.EncodeFrame(new EmotePacket { PlayerId = 5, Emote = EmoteKind.Sad });
        Assert.Equal(2 + 1 + 2, frame.Length);
        var e = Assert.IsType<EmotePacket>(PacketCodec.DecodePayload(frame, 2, frame.Length - 2));
        Assert.Equal((5, EmoteKind.Sad), (e.PlayerId, e.Emote));
        Assert.True(ProtocolConstants.Version >= 7); // v7 added emotes (v8: map event cause, same layout)
    }

    [Fact]
    public void Emote_is_relayed_to_everyone_and_changes_nothing_else()
    {
        var room = Room();
        byte a = room.AddPlayer("A");
        room.AddPlayer("B");
        room.Tick();
        room.Outbox.Clear();
        var before = room.GetPlayer(a).State;

        Assert.True(room.RequestEmote(a, EmoteKind.Wave));
        var e = Assert.Single(Emotes(room));
        Assert.Equal((a, EmoteKind.Wave), (e.PlayerId, e.Emote));
        Assert.Equal(before, room.GetPlayer(a).State);
    }

    [Fact]
    public void Second_emote_within_a_second_is_dropped()
    {
        var room = Room();
        byte a = room.AddPlayer("A");
        byte b = room.AddPlayer("B");
        room.Outbox.Clear();

        Assert.True(room.RequestEmote(a, EmoteKind.Wave));
        Assert.False(room.RequestEmote(a, EmoteKind.Taunt));
        Assert.True(room.RequestEmote(b, EmoteKind.Taunt)); // the cooldown is per player
        for (int i = 0; i < GameConstants.EmoteCooldownTicks - 1; i++) room.Tick();
        Assert.False(room.RequestEmote(a, EmoteKind.Cheer));
        room.Tick();
        Assert.True(room.RequestEmote(a, EmoteKind.Cheer));
        Assert.Equal(new[] { EmoteKind.Wave, EmoteKind.Taunt, EmoteKind.Cheer }, Emotes(room).Select(x => x.Emote));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(255)]
    public void Unknown_kind_is_dropped(byte kind)
    {
        var room = Room();
        byte a = room.AddPlayer("A");
        room.Outbox.Clear();
        Assert.False(room.RequestEmote(a, (EmoteKind)kind));
        Assert.False(room.RequestEmote(9, EmoteKind.Wave)); // nobody
        Assert.Empty(Emotes(room));
    }

    [Fact]
    public void Dead_players_and_spectators_cannot_emote()
    {
        var room = Room();
        byte a = room.AddPlayer("A");
        room.AddPlayer("B");
        room.AddPlayer("C");
        for (int i = 0; i < 200 && room.State != RoomState.Playing; i++) room.Tick();
        Assert.Equal(RoomState.Playing, room.State);
        byte late = room.AddPlayer("Late"); // spectates until the next round
        room.GetPlayer(a).Alive = false;
        room.Outbox.Clear();

        Assert.False(room.RequestEmote(a, EmoteKind.Sad));
        Assert.False(room.RequestEmote(late, EmoteKind.Sad));
        Assert.Empty(Emotes(room));
    }
}
