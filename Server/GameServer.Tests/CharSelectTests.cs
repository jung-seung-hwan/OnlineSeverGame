using System.Reflection;
using OnlineSeverGame.BotClient;
using OnlineSeverGame.Shared.Protocol;
using OnlineSeverGame.Shared.Simulation;
using static OnlineSeverGame.Shared.Simulation.GameConstants;

namespace OnlineSeverGame.Server.Tests;

/// <summary>
/// docs/work/char-select.md: character kinds (protocol v11). Hello carries the wanted kind, PlayerJoined the current
/// one (so late joiners learn everyone's), CharacterRequest / PlayerCharacter change it outside a round, and hazards
/// judge every kind the same.
/// </summary>
public class CharSelectTests
{
    private static GameRoom NewRoom(int minPlayers = 2) =>
        new(new RoomConfig { MinPlayers = minPlayers, AutoSpawnKnives = false, AutoSpawnSideKnives = false, Seed = 1, Stage = TestStages.Arena });

    private static List<(byte Target, IPacket Packet)> Take(GameRoom room)
    {
        var list = room.Outbox.Select(o => (o.Target, PacketCodec.DecodePayload(o.Frame, 2, o.Frame.Length - 2))).ToList();
        room.Outbox.Clear();
        return list;
    }

    private static List<PlayerCharacterPacket> CharacterChanges(GameRoom room) =>
        Take(room).Where(x => x.Packet is PlayerCharacterPacket).Select(x =>
        {
            Assert.Equal(0, x.Target); // everyone, the chooser too
            return (PlayerCharacterPacket)x.Packet;
        }).ToList();

    private static T RoundTrip<T>(T packet) where T : IPacket
    {
        byte[] frame = PacketCodec.EncodeFrame(packet);
        return Assert.IsType<T>(PacketCodec.DecodePayload(frame, 2, frame.Length - 2));
    }

    /// <summary>Two players in Waiting, ticked into Countdown and then Playing.</summary>
    private static GameRoom PlayingRoom(out byte a, out byte b)
    {
        var room = NewRoom();
        a = room.AddPlayer("a", CharacterKind.Jelly);
        b = room.AddPlayer("b", CharacterKind.Bear);
        room.Tick();
        for (int i = 0; i < CountdownTicks; i++) room.Tick();
        Assert.Equal(RoomState.Playing, room.State);
        room.Outbox.Clear();
        return room;
    }

    // ---------------------------------------------------------------- packets

    [Fact]
    public void Packets_round_trip_and_version_is_11()
    {
        Assert.True(ProtocolConstants.Version >= 11);
        Assert.Equal(3, ProtocolConstants.MaxCharacter);

        var hello = RoundTrip(new HelloPacket { Name = "곰", Character = CharacterKind.Bear });
        Assert.Equal(("곰", CharacterKind.Bear), (hello.Name, hello.Character));

        var joined = RoundTrip(new PlayerJoinedPacket { PlayerId = 4, Name = "p", Character = CharacterKind.Pudding });
        Assert.Equal((4, "p", CharacterKind.Pudding), (joined.PlayerId, joined.Name, joined.Character));

        byte[] frame = PacketCodec.EncodeFrame(new CharacterRequestPacket { Character = CharacterKind.Pudding });
        Assert.Equal(2 + 1 + 1, frame.Length);
        Assert.Equal(CharacterKind.Pudding, RoundTrip(new CharacterRequestPacket { Character = CharacterKind.Pudding }).Character);

        frame = PacketCodec.EncodeFrame(new PlayerCharacterPacket { PlayerId = 7, Character = CharacterKind.Bear });
        Assert.Equal(2 + 1 + 2, frame.Length);
        var pc = RoundTrip(new PlayerCharacterPacket { PlayerId = 7, Character = CharacterKind.Bear });
        Assert.Equal((7, CharacterKind.Bear), (pc.PlayerId, pc.Character));
    }

    [Fact]
    public void An_old_hello_without_the_character_byte_still_parses_so_the_server_can_reject_its_version()
    {
        // v9 layout: u16 version, string name. No trailing character byte.
        var w = new PacketWriter();
        w.WriteByte((byte)PacketType.Hello);
        w.WriteUInt16(9);
        w.WriteString("old");
        byte[] payload = w.ToArray();
        var hello = Assert.IsType<HelloPacket>(PacketCodec.DecodePayload(payload));
        Assert.Equal((9, "old", (CharacterKind)0), (hello.ProtocolVersion, hello.Name, hello.Character));
    }

    // ---------------------------------------------------------------- joining

    [Fact]
    public void Join_validates_the_hello_character_and_everyone_learns_it()
    {
        var room = NewRoom(minPlayers: 8);
        byte a = room.AddPlayer("a", CharacterKind.Pudding);
        byte b = room.AddPlayer("b", 0);                       // not given
        byte c = room.AddPlayer("c", (CharacterKind)9);       // unknown
        Assert.Equal(CharacterKind.Pudding, room.GetPlayer(a).Character);
        Assert.Equal(CharacterKind.Jelly, room.GetPlayer(b).Character);
        Assert.Equal(CharacterKind.Jelly, room.GetPlayer(c).Character);
        room.Outbox.Clear();

        byte d = room.AddPlayer("d", CharacterKind.Bear);
        var sent = Take(room);
        // The newcomer gets the whole roster with each player's character.
        var roster = sent.Where(x => x.Target == d && x.Packet is PlayerJoinedPacket)
            .Select(x => (PlayerJoinedPacket)x.Packet).ToDictionary(p => p.PlayerId, p => p.Character);
        Assert.Equal(new Dictionary<byte, CharacterKind>
        {
            [a] = CharacterKind.Pudding, [b] = CharacterKind.Jelly, [c] = CharacterKind.Jelly, [d] = CharacterKind.Bear,
        }, roster);
        // Everyone already here gets the newcomer with its character.
        foreach (byte old in new[] { a, b, c })
        {
            var p = (PlayerJoinedPacket)Assert.Single(sent, x => x.Target == old && x.Packet is PlayerJoinedPacket).Packet;
            Assert.Equal((d, CharacterKind.Bear), (p.PlayerId, p.Character));
        }
    }

    [Fact]
    public void A_late_joiner_learns_characters_changed_before_it_came()
    {
        var room = NewRoom(minPlayers: 8);
        byte a = room.AddPlayer("a", CharacterKind.Jelly);
        Assert.True(room.RequestCharacter(a, CharacterKind.Pudding));
        room.Tick();
        room.Outbox.Clear();

        byte late = room.AddPlayer("late", CharacterKind.Jelly);
        var joined = Take(room).Where(x => x.Target == late).Select(x => x.Packet).OfType<PlayerJoinedPacket>().Single(p => p.PlayerId == a);
        Assert.Equal(CharacterKind.Pudding, joined.Character);
    }

    [Fact]
    public void A_mid_round_joiner_keeps_its_hello_character_while_spectating()
    {
        var room = PlayingRoom(out _, out _);
        byte late = room.AddPlayer("late", CharacterKind.Pudding);
        Assert.False(room.GetPlayer(late).InRound);
        Assert.Equal(CharacterKind.Pudding, room.GetPlayer(late).Character);
    }

    // ---------------------------------------------------------------- changing

    [Fact]
    public void In_waiting_a_change_applies_at_once_and_is_broadcast()
    {
        var room = NewRoom(minPlayers: 8);
        byte a = room.AddPlayer("a", CharacterKind.Jelly);
        room.Outbox.Clear();

        Assert.True(room.RequestCharacter(a, CharacterKind.Bear));
        Assert.Equal(CharacterKind.Bear, room.GetPlayer(a).Character);
        var change = Assert.Single(CharacterChanges(room));
        Assert.Equal((a, CharacterKind.Bear), (change.PlayerId, change.Character));

        // Same kind again: accepted, nothing to tell.
        Assert.True(room.RequestCharacter(a, CharacterKind.Bear));
        Assert.Empty(CharacterChanges(room));
    }

    [Fact]
    public void Unknown_kinds_and_players_are_ignored()
    {
        var room = NewRoom(minPlayers: 8);
        byte a = room.AddPlayer("a", CharacterKind.Bear);
        room.Outbox.Clear();

        Assert.False(room.RequestCharacter(a, 0));
        Assert.False(room.RequestCharacter(a, (CharacterKind)4));
        Assert.False(room.RequestCharacter(a, (CharacterKind)255));
        Assert.False(room.RequestCharacter(7, CharacterKind.Pudding));
        Assert.Equal(CharacterKind.Bear, room.GetPlayer(a).Character);
        Assert.Equal(CharacterKind.Bear, room.GetPlayer(a).WantedCharacter);
        Assert.Empty(CharacterChanges(room));
    }

    [Fact]
    public void In_countdown_a_change_applies_at_once()
    {
        var room = NewRoom();
        byte a = room.AddPlayer("a", CharacterKind.Jelly);
        room.AddPlayer("b", CharacterKind.Jelly);
        room.Tick();
        Assert.Equal(RoomState.Countdown, room.State);
        room.Outbox.Clear();

        Assert.True(room.RequestCharacter(a, CharacterKind.Pudding));
        Assert.Equal(CharacterKind.Pudding, room.GetPlayer(a).Character);
        Assert.Single(CharacterChanges(room));
    }

    [Fact]
    public void While_playing_and_on_the_results_a_change_waits_for_the_next_countdown()
    {
        var room = PlayingRoom(out byte a, out byte b);

        Assert.True(room.RequestCharacter(a, CharacterKind.Pudding));
        Assert.Equal(CharacterKind.Jelly, room.GetPlayer(a).Character);
        Assert.Equal(CharacterKind.Pudding, room.GetPlayer(a).WantedCharacter);
        room.Tick();
        Assert.Empty(CharacterChanges(room));

        // b dies: the round ends and the results show the characters that played.
        room.SpawnKnife(room.GetPlayer(b).State.X);
        room.GetPlayer(a).State = PlayerState.SpawnAt(room.Stage.Width - 1000, 0, 1);
        for (int i = 0; i < 200 && room.State == RoomState.Playing; i++) room.Tick();
        Assert.Equal(RoomState.Ended, room.State);
        Assert.Empty(CharacterChanges(room));

        // A change on the result screen also waits; the latest request wins.
        Assert.True(room.RequestCharacter(b, CharacterKind.Pudding));
        Assert.True(room.RequestCharacter(b, CharacterKind.Jelly));
        Assert.Equal(CharacterKind.Bear, room.GetPlayer(b).Character);

        for (int i = 0; i < ResultTicks && room.State == RoomState.Ended; i++) room.Tick();
        Assert.Equal(RoomState.Countdown, room.State);
        var sent = Take(room).Select(x => x.Packet).ToList();
        var changes = sent.OfType<PlayerCharacterPacket>().Select(p => (p.PlayerId, p.Character)).ToList();
        Assert.Equal(new[] { (a, CharacterKind.Pudding), (b, CharacterKind.Jelly) }, changes);
        // Told before the RoundStart, so the countdown already draws the new kinds.
        Assert.True(sent.FindIndex(p => p is PlayerCharacterPacket) < sent.FindIndex(p => p is RoundStartPacket));
        Assert.Equal(CharacterKind.Pudding, room.GetPlayer(a).Character);
        Assert.Equal(CharacterKind.Jelly, room.GetPlayer(b).Character);
    }

    [Fact]
    public void A_kept_change_applies_when_the_room_falls_back_to_waiting()
    {
        var room = PlayingRoom(out byte a, out byte b);
        Assert.True(room.RequestCharacter(a, CharacterKind.Bear));
        room.RemovePlayer(b, LeaveReason.Disconnected); // a wins alone
        for (int i = 0; i < 5 && room.State == RoomState.Playing; i++) room.Tick();
        Assert.Equal(RoomState.Ended, room.State);
        Assert.Equal(CharacterKind.Jelly, room.GetPlayer(a).Character);
        for (int i = 0; i < ResultTicks && room.State == RoomState.Ended; i++) room.Tick();
        Assert.Equal(RoomState.Waiting, room.State);
        Assert.Equal(CharacterKind.Bear, room.GetPlayer(a).Character);
        Assert.Contains(CharacterChanges(room), p => p.PlayerId == a && p.Character == CharacterKind.Bear);
    }

    // ---------------------------------------------------------------- judging is the same for every kind

    /// <summary>
    /// The same seed and inputs give byte-identical snapshots and round results whatever characters the players
    /// picked, also when they keep changing them: hazards, dashes and stomps never look at the kind.
    /// </summary>
    [Fact]
    public void Characters_never_change_the_simulation()
    {
        static List<byte[]> Play(Func<int, CharacterKind> kindOf, bool keepChanging)
        {
            var room = new GameRoom(new RoomConfig { MinPlayers = 6, Seed = 77, Rotation = MapRotation.Cycle, FirstKnifeDelayTicks = 5 });
            var ids = Enumerable.Range(0, 6).Select(i => room.AddPlayer("p" + i, kindOf(i))).ToArray();
            var inputs = new Random(5);
            var kinds = new Random(9);
            var frames = new List<byte[]>();
            var seq = new uint[ids.Length];
            for (int tick = 0; tick < 3000; tick++)
            {
                for (int i = 0; i < ids.Length; i++)
                    room.QueueInput(ids[i], ++seq[i], (InputButtons)inputs.Next(0, 64));
                if (keepChanging && tick % 37 == 0)
                    room.RequestCharacter(ids[kinds.Next(ids.Length)], (CharacterKind)kinds.Next(1, 4));
                room.Tick();
                foreach (var o in room.Outbox)
                {
                    var type = (PacketType)o.Frame[2];
                    if (type == PacketType.PlayerCharacter || type == PacketType.PlayerJoined) continue;
                    frames.Add(o.Frame);
                }
                room.Outbox.Clear();
            }
            Assert.True(room.CompletedRounds >= 2, $"only {room.CompletedRounds} rounds");
            return frames;
        }

        var allJelly = Play(_ => CharacterKind.Jelly, false);
        var mixed = Play(i => (CharacterKind)(i % 3 + 1), false);
        var changing = Play(i => (CharacterKind)(2 - i % 2 + 1), true);
        Assert.Equal(allJelly.Count, mixed.Count);
        Assert.Equal(allJelly.Count, changing.Count);
        for (int i = 0; i < allJelly.Count; i++)
        {
            Assert.Equal(allJelly[i], mixed[i]);
            Assert.Equal(allJelly[i], changing[i]);
        }
    }

    [Fact]
    public void Nothing_in_the_simulation_takes_a_character()
    {
        // HitShapes, PlayerPhysics, PlayerState, ... (Shared/Simulation) have no field, property or parameter of the
        // kind, so a hit shape cannot depend on it. The shapes are measured on the narrowest kind (docs/work/char-motion.md).
        var asm = typeof(HitShapes).Assembly;
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;
        foreach (var t in asm.GetTypes().Where(t => t.Namespace == typeof(HitShapes).Namespace))
        {
            foreach (var f in t.GetFields(all)) Assert.NotEqual(typeof(CharacterKind), f.FieldType);
            foreach (var m in t.GetMethods(all))
                Assert.DoesNotContain(m.GetParameters(), p => p.ParameterType == typeof(CharacterKind) ||
                                                              p.ParameterType.GetElementType() == typeof(CharacterKind));
        }
    }

    // ---------------------------------------------------------------- bots

    [Fact]
    public void Bots_pick_a_valid_random_character()
    {
        var kinds = Enumerable.Range(1, 30).Select(seed => new Bot(0, "b", seed, false, true).Character).ToList();
        Assert.All(kinds, k => Assert.True(ProtocolConstants.IsValidCharacter((byte)k)));
        Assert.Equal(3, kinds.Distinct().Count());
    }
}
