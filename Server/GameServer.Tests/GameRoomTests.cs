using OnlineSeverGame.Shared.Protocol;
using OnlineSeverGame.Shared.Simulation;
using static OnlineSeverGame.Shared.Simulation.GameConstants;

namespace OnlineSeverGame.Server.Tests;

public class GameRoomTests
{
    private static GameRoom NewRoom(int minPlayers = 2) =>
        new GameRoom(new RoomConfig { MinPlayers = minPlayers, AutoSpawnKnives = false, AutoSpawnSideKnives = false, Seed = 1 });

    private static List<IPacket> Drain(GameRoom room, byte target = 0)
    {
        var packets = room.Outbox
            .Where(o => o.Target == 0 || o.Target == target)
            .Select(o => PacketCodec.DecodePayload(o.Frame, 2, o.Frame.Length - 2))
            .ToList();
        room.Outbox.Clear();
        return packets;
    }

    /// <summary>Adds players and ticks through the countdown until Playing.</summary>
    private static GameRoom PlayingRoom(int players, out byte[] ids)
    {
        var room = NewRoom();
        ids = Enumerable.Range(0, players).Select(i => room.AddPlayer("P" + i)).ToArray();
        room.Tick(); // Waiting -> Countdown
        Assert.Equal(RoomState.Countdown, room.State);
        for (int i = 0; i < CountdownTicks; i++) room.Tick();
        Assert.Equal(RoomState.Playing, room.State);
        room.Outbox.Clear();
        return room;
    }

    private static void Place(GameRoom room, byte id, int x)
    {
        room.GetPlayer(id).State = PlayerState.SpawnAt(x, 0, 1);
    }

    /// <summary>Ticks until a knife dropped now reaches the floor (warning + fall).</summary>
    private static void TickUntilKnivesLand(GameRoom room)
    {
        for (int i = 0; i < KnifeWarningTicks + 25 && room.State == RoomState.Playing; i++) room.Tick();
    }

    [Fact]
    public void Join_sends_welcome_and_roster_and_ids_fill_the_lowest_gap()
    {
        var room = NewRoom();
        Assert.Equal(1, room.AddPlayer("a"));
        Assert.Equal(2, room.AddPlayer("b"));
        room.Outbox.Clear();
        room.RemovePlayer(1, LeaveReason.Disconnected);
        room.Outbox.Clear();

        Assert.Equal(1, room.AddPlayer("  "));
        var toNew = Drain(room, 1);
        var welcome = Assert.IsType<WelcomePacket>(toNew[0]);
        Assert.Equal(1, welcome.PlayerId);
        var roster = toNew.OfType<PlayerJoinedPacket>().Select(p => (p.PlayerId, p.Name)).ToList();
        Assert.Equal(new[] { ((byte)1, "Player1"), ((byte)2, "b") }, roster);
    }

    [Fact]
    public void Ninth_player_is_refused()
    {
        var room = NewRoom();
        for (int i = 0; i < 8; i++) Assert.NotEqual(0, room.AddPlayer("p"));
        Assert.Equal(0, room.AddPlayer("late"));
    }

    [Fact]
    public void Round_starts_with_min_players_and_spawns_everyone()
    {
        var room = NewRoom();
        room.AddPlayer("a");
        room.Tick();
        Assert.Equal(RoomState.Waiting, room.State);

        room.AddPlayer("b");
        room.Outbox.Clear();
        room.Tick();
        Assert.Equal(RoomState.Countdown, room.State);
        var start = Drain(room).OfType<RoundStartPacket>().Single();
        Assert.Equal(1, start.RoundNumber);
        Assert.Equal(new byte[] { 1, 2 }, start.Participants);
        Assert.Equal(room.ServerTick + CountdownTicks, start.PlayStartTick);
        Assert.Equal(Stage.Default.SpawnPoints[1].X, room.GetPlayer(2).State.X);
    }

    [Fact]
    public void Countdown_falls_back_to_waiting_when_someone_leaves()
    {
        var room = NewRoom();
        room.AddPlayer("a");
        room.AddPlayer("b");
        room.Tick();
        room.RemovePlayer(2, LeaveReason.Disconnected);
        room.Tick();
        Assert.Equal(RoomState.Waiting, room.State);
    }

    [Fact]
    public void Knife_kills_and_last_survivor_wins()
    {
        var room = PlayingRoom(3, out var ids);
        Place(room, ids[0], 1500);
        Place(room, ids[1], 22500);
        Place(room, ids[2], 12000); // under the middle platform: shielded
        room.SpawnKnife(1500);
        room.SpawnKnife(22500);
        room.SpawnKnife(12000);
        TickUntilKnivesLand(room);

        Assert.Equal(RoomState.Ended, room.State);
        var end = room.LastRoundEnd;
        Assert.Equal(new[] { ids[2] }, end.Winners);
        Assert.Equal(ids[2], end.Results[0].PlayerId);
        Assert.Equal(1, end.Results[0].Place);
        Assert.Equal(EliminationCause.None, end.Results[0].Cause);
        // The other two died on the same tick: shared 2nd place.
        Assert.All(end.Results.Skip(1), r => Assert.Equal((2, EliminationCause.Knife), (r.Place, r.Cause)));
        Assert.False(room.GetPlayer(ids[0]).Alive);
        Assert.True(room.GetPlayer(ids[2]).Alive);
        Assert.Empty(room.Knives);
    }

    [Fact]
    public void Simultaneous_last_deaths_share_the_win()
    {
        // Open floor (no platform overhead) only near the walls: x < ~2600 and x > ~21400.
        var room = PlayingRoom(3, out var ids);
        Place(room, ids[0], 600);
        Place(room, ids[1], 23400);
        Place(room, ids[2], 2200);
        room.SpawnKnife(2200); // ids[2] dies first
        for (int i = 0; i < 10; i++) room.Tick();
        room.SpawnKnife(600);
        room.SpawnKnife(23400);
        TickUntilKnivesLand(room);

        Assert.Equal(RoomState.Ended, room.State);
        var end = room.LastRoundEnd;
        Assert.Equal(new[] { ids[0], ids[1] }, end.Winners.OrderBy(x => x));
        Assert.All(end.Results.Where(r => r.PlayerId != ids[2]), r => Assert.Equal((1, EliminationCause.Knife), (r.Place, r.Cause)));
        var first = end.Results.Single(r => r.PlayerId == ids[2]);
        Assert.Equal((3, EliminationCause.Knife), (first.Place, first.Cause));
        Assert.Equal(first, end.Results[2]);
    }

    [Fact]
    public void Disconnect_eliminates_immediately_and_the_remaining_player_wins()
    {
        var room = PlayingRoom(3, out var ids);
        room.RemovePlayer(ids[0], LeaveReason.Disconnected);
        room.Tick();
        Assert.Equal(RoomState.Playing, room.State);
        Assert.Null(room.GetPlayer(ids[0]));
        var snap = Drain(room).OfType<SnapshotPacket>().Last();
        Assert.DoesNotContain(snap.Players, p => p.PlayerId == ids[0]);

        room.RemovePlayer(ids[1], LeaveReason.Timeout);
        room.Tick();
        Assert.Equal(RoomState.Ended, room.State);
        var end = room.LastRoundEnd;
        Assert.Equal(new[] { ids[2] }, end.Winners);
        Assert.Equal(EliminationCause.Disconnected, end.Results.Single(r => r.PlayerId == ids[0]).Cause);
        Assert.Equal(EliminationCause.Disconnected, end.Results.Single(r => r.PlayerId == ids[1]).Cause);
        Assert.True(end.Results.Single(r => r.PlayerId == ids[1]).Place < end.Results.Single(r => r.PlayerId == ids[0]).Place);
    }

    [Fact]
    public void Leaving_just_before_a_knife_kill_does_not_take_the_win()
    {
        var room = PlayingRoom(2, out var ids);
        Place(room, ids[0], 1500);
        room.SpawnKnife(1500);
        for (int i = 0; i < 100 && room.GetPlayer(ids[0]).Alive && room.State == RoomState.Playing; i++)
        {
            // Leave on the very tick the knife lands.
            var k = room.Knives[0];
            if (k.Phase == KnifePhase.Falling && k.Y <= KnifeFallSpeed + PlayerHeight)
                room.RemovePlayer(ids[1], LeaveReason.Disconnected);
            room.Tick();
        }
        var end = room.LastRoundEnd;
        Assert.NotNull(end);
        Assert.Equal(new[] { ids[0] }, end.Winners);
    }

    [Fact]
    public void Everyone_leaving_ends_with_no_winner()
    {
        var room = PlayingRoom(2, out var ids);
        room.RemovePlayer(ids[0], LeaveReason.Disconnected);
        room.RemovePlayer(ids[1], LeaveReason.Disconnected);
        room.Tick();
        Assert.Equal(RoomState.Ended, room.State);
        Assert.Empty(room.LastRoundEnd.Winners);
    }

    [Fact]
    public void Time_limit_makes_all_survivors_winners()
    {
        var room = PlayingRoom(2, out _);
        for (int i = 0; i < RoundTimeLimitTicks; i++) room.Tick();
        Assert.Equal(RoomState.Ended, room.State);
        Assert.Equal(2, room.LastRoundEnd.Winners.Count);
        Assert.All(room.LastRoundEnd.Results, r => Assert.Equal((uint)RoundTimeLimitTicks, r.SurvivedTicks));
    }

    [Fact]
    public void Mid_round_joiner_spectates_then_plays_the_next_round()
    {
        var room = PlayingRoom(2, out var ids);
        byte late = room.AddPlayer("late");
        Assert.False(room.GetPlayer(late).Alive);
        Assert.False(room.GetPlayer(late).InRound);

        room.RemovePlayer(ids[0], LeaveReason.Disconnected);
        room.Tick();
        Assert.Equal(RoomState.Ended, room.State);
        Assert.DoesNotContain(room.LastRoundEnd.Results, r => r.PlayerId == late);

        for (int i = 0; i < ResultTicks; i++) room.Tick();
        Assert.Equal(RoomState.Countdown, room.State);
        Assert.Equal(2, room.RoundNumber);
        Assert.True(room.GetPlayer(late).Alive && room.GetPlayer(late).InRound);
    }

    [Fact]
    public void Dead_players_spectate_and_their_input_is_consumed_not_applied()
    {
        var room = PlayingRoom(3, out var ids);
        Place(room, ids[0], 1500);
        room.SpawnKnife(1500);
        TickUntilKnivesLand(room);
        var dead = room.GetPlayer(ids[0]);
        Assert.False(dead.Alive);
        int x = dead.State.X;

        room.QueueInput(ids[0], 1, InputButtons.Right);
        room.Tick();
        Assert.Equal(x, dead.State.X);
        Assert.Equal(1u, dead.LastInputSeq);
    }

    [Fact]
    public void One_input_per_tick_in_order_with_held_buttons_repeating()
    {
        var room = PlayingRoom(2, out var ids);
        var p = room.GetPlayer(ids[0]);
        p.State = PlayerState.SpawnAt(12000, 0, 1);
        room.QueueInput(ids[0], 1, InputButtons.Right);
        room.QueueInput(ids[0], 2, InputButtons.Right);
        room.QueueInput(ids[0], 2, InputButtons.Left); // duplicate seq: ignored
        room.Tick();
        Assert.Equal(1u, p.LastInputSeq);
        room.Tick();
        Assert.Equal(2u, p.LastInputSeq);
        room.Tick(); // queue empty: Right is held
        Assert.Equal(2u, p.LastInputSeq);
        Assert.Equal(12000 + 3 * MoveSpeed, p.State.X);
    }

    [Fact]
    public void Input_backlog_drops_the_oldest()
    {
        var room = PlayingRoom(2, out var ids);
        for (uint s = 1; s <= 12; s++) room.QueueInput(ids[0], s, InputButtons.None);
        Assert.Equal(4u, room.GetPlayer(ids[0]).LastInputSeq); // 1..4 dropped, 5..12 kept
        room.Tick();
        Assert.Equal(5u, room.GetPlayer(ids[0]).LastInputSeq);
    }

    [Fact]
    public void Dash_input_pushes_another_player_in_the_room()
    {
        var room = PlayingRoom(2, out var ids);
        room.GetPlayer(ids[0]).State = PlayerState.SpawnAt(12000, 0, 1);
        room.GetPlayer(ids[1]).State = PlayerState.SpawnAt(13500, 0, -1);
        room.QueueInput(ids[0], 1, InputButtons.Dash);
        room.Tick();
        var target = room.GetPlayer(ids[1]);
        Assert.Equal(DashStunTicks, target.State.StunTicks);
        Assert.Equal(KnockbackSpeed, target.State.Vx);
    }

    [Fact]
    public void Snapshot_is_broadcast_every_tick()
    {
        var room = PlayingRoom(2, out var ids);
        room.SpawnKnife(5000);
        room.Tick();
        var snap = Drain(room).OfType<SnapshotPacket>().Single();
        Assert.Equal(room.ServerTick, snap.ServerTick);
        Assert.Equal(RoomState.Playing, snap.RoomState);
        Assert.Equal(1u, snap.RoundTick);
        Assert.Equal(ids, snap.Players.Select(p => p.PlayerId));
        Assert.All(snap.Players, p => Assert.True(p.Alive && p.InRound));
        Assert.Equal(KnifePhase.Warning, Assert.Single(snap.Knives).Phase);
    }

    [Fact]
    public void Automatic_knives_get_more_frequent()
    {
        var room = new GameRoom(new RoomConfig { MinPlayers = 1, Seed = 3, AutoSpawnSideKnives = false });
        byte id = room.AddPlayer("solo");
        room.Tick();
        for (int i = 0; i < CountdownTicks; i++) room.Tick();
        var p = room.GetPlayer(id);

        int SpawnsDuring(int ticks)
        {
            var seen = new HashSet<ushort>();
            for (int i = 0; i < ticks; i++)
            {
                p.Alive = true; // keep the solo player alive for the measurement
                p.State = PlayerState.SpawnAt(12000, 0, 1); // shielded under the middle platform
                room.Tick();
                foreach (var k in room.Knives) seen.Add(k.Id);
            }
            return seen.Count;
        }

        int early = SpawnsDuring(20 * TickRate);
        for (int i = 0; i < 60 * TickRate; i++) { p.Alive = true; p.State = PlayerState.SpawnAt(12000, 0, 1); room.Tick(); }
        int late = SpawnsDuring(20 * TickRate);
        Assert.Equal(RoomState.Playing, room.State);
        Assert.True(late > early * 2, $"early {early}, late {late}");
    }
}
