using OnlineSeverGame.Shared.Protocol;
using OnlineSeverGame.Shared.Simulation;
using static OnlineSeverGame.Shared.Simulation.GameConstants;

namespace OnlineSeverGame.Server.Tests;

/// <summary>
/// docs/work/map-select-hit.md: hazards hit the drawn shapes (HitShapes) instead of the movement box, and the host
/// (earliest player still connected) picks the next map and the rotation (MapRequest / RoomInfo, protocol v4).
/// </summary>
public class MapSelectHitTests
{
    private static List<IPacket> Drain(GameRoom room, byte target = 1)
    {
        var packets = room.Outbox
            .Where(o => o.Target == 0 || o.Target == target)
            .Select(o => PacketCodec.DecodePayload(o.Frame, 2, o.Frame.Length - 2))
            .ToList();
        room.Outbox.Clear();
        return packets;
    }

    private static GameRoom Room(MapRotation rotation = MapRotation.Cycle, int seed = 1, bool legacy = false) =>
        new GameRoom(new RoomConfig
        {
            MinPlayers = 2, AutoSpawnKnives = false, Seed = seed, Rotation = rotation, LegacyHitbox = legacy,
        });

    private static void TickToPlaying(GameRoom room)
    {
        for (int i = 0; i < CountdownTicks + 5 && room.State != RoomState.Playing; i++) room.Tick();
        Assert.Equal(RoomState.Playing, room.State);
    }

    /// <summary>Kills the victim on open floor so the round ends, then waits for the next countdown.</summary>
    private static void FinishRound(GameRoom room, byte victim, byte other)
    {
        int x = 0;
        for (int c = 500; c < room.Stage.Width; c += 250)
        {
            if (room.Stage.HazardLandingY(room.Stage.Hazard, c, room.Stage.KnifeSpawnY) != 0) continue;
            if (room.Stage.Pushables.Any(p => Math.Abs(p.X - c) < p.Width / 2 + 1500)) continue;
            x = c;
            break;
        }
        room.GetPlayer(victim).State = PlayerState.SpawnAt(x, 0, 1);
        room.GetPlayer(other).State = PlayerState.SpawnAt(x < 12000 ? 23000 : 1000, 0, 1);
        room.SpawnKnife(x);
        for (int i = 0; i < 80 && room.State == RoomState.Playing; i++) room.Tick();
        Assert.Equal(RoomState.Ended, room.State);
        for (int i = 0; i < ResultTicks + 2 && room.State == RoomState.Ended; i++) room.Tick();
        Assert.Equal(RoomState.Countdown, room.State);
    }

    /// <summary>Drops one hazard at x on a flat stretch and reports whether the player standing at px was hit.</summary>
    private static bool Dropped(HazardKind kind, int x, PlayerState player)
    {
        var stage = new Stage(9, "Flat", 24000, 13500, 14000, Array.Empty<Platform>(),
            new[] { new SpawnPoint(1000, 0, 1), new SpawnPoint(23000, 0, -1) }, hazard: kind);
        var k = KnifeState.Warn(1, x, stage);
        for (int i = 0; i < 200; i++)
        {
            int from = k.Y;
            if (!KnifePhysics.Step(ref k, stage, out bool dangerous, out _)) break;
            if (dangerous && HitShapes.Hits(player, kind, k.X, from, k.Y)) return true;
            if (k.Phase == KnifePhase.Landed) break;
        }
        return false;
    }

    // ---------------------------------------------------------------- drawn hit shapes

    [Fact]
    public void Knife_hits_where_the_drawn_blade_meets_the_drawn_belly()
    {
        // Drawn: blade 0.12 left / 0.13 right of the tip, belly 0.51 either side of the feet. Touching edges do not hit.
        var standing = PlayerState.SpawnAt(10000, 0, 1);
        Assert.True(Dropped(HazardKind.Knife, 10000 + 620, standing));   // old box (0.2 + 0.4 = 0.6) missed this
        Assert.False(Dropped(HazardKind.Knife, 10000 + 630, standing));
        Assert.True(Dropped(HazardKind.Knife, 10000 - 630, standing));
        Assert.False(Dropped(HazardKind.Knife, 10000 - 640, standing));
        Assert.Equal(130, HitShapes.HazardHalfWidth(HazardKind.Knife));
    }

    [Fact]
    public void Each_hazard_kind_uses_its_own_drawn_width()
    {
        var p = PlayerState.SpawnAt(10000, 0, 1);
        // Icicle: the wide top (0.22) only reaches the narrow head when it lands, so the belly meets the 0.15 part: 0.66.
        // Crate 0.72, fork tines 0.19.
        Assert.True(Dropped(HazardKind.Icicle, 10000 + 650, p));
        Assert.False(Dropped(HazardKind.Icicle, 10000 + 660, p));
        Assert.True(Dropped(HazardKind.Crate, 10000 + 1220, p));
        Assert.False(Dropped(HazardKind.Crate, 10000 + 1230, p));
        Assert.True(Dropped(HazardKind.Fork, 10000 + 690, p));
        Assert.False(Dropped(HazardKind.Fork, 10000 + 700, p));
    }

    [Fact]
    public void Crouching_lowers_the_drawn_body_and_widens_it()
    {
        var standing = PlayerState.SpawnAt(10000, 0, 1);
        var crouching = standing;
        crouching.Crouching = true;
        // A knife that stops 0.75 above the feet (say on a box top) misses a crouching jelly (0.72 tall), not a standing one.
        Assert.True(HitShapes.Hits(standing, HazardKind.Knife, 10000, 3000, 750));
        Assert.False(HitShapes.Hits(crouching, HazardKind.Knife, 10000, 3000, 750));
        Assert.True(HitShapes.Hits(crouching, HazardKind.Knife, 10000, 3000, 700));
        // Squashed 1.15 wide: belly 0.59.
        Assert.True(HitShapes.Hits(crouching, HazardKind.Knife, 10000 + 700, 3000, 0));
        Assert.False(HitShapes.Hits(standing, HazardKind.Knife, 10000 + 700, 3000, 0));
        Assert.Equal(720, HitShapes.Crouch.Max(b => b.Top));
    }

    [Fact]
    public void Dash_and_knockback_shapes_follow_the_facing()
    {
        var dashing = PlayerState.SpawnAt(10000, 0, 1);
        dashing.DashTicks = 2;
        Assert.True(HitShapes.Hits(dashing, HazardKind.Knife, 10000 + 900, 3000, 0));   // long nose in front
        dashing.Facing = -1;
        Assert.True(HitShapes.Hits(dashing, HazardKind.Knife, 10000 - 900, 3000, 0));
        Assert.False(HitShapes.Hits(dashing, HazardKind.Knife, 10000 + 960, 3000, 0));

        var stunned = PlayerState.SpawnAt(10000, 0, 1);
        stunned.StunTicks = 5;
        Assert.True(HitShapes.Hits(stunned, HazardKind.Knife, 10000 - 900, 3000, 0));   // falls over backwards
        Assert.False(HitShapes.Hits(stunned, HazardKind.Knife, 10000 + 560, 3000, 0));
        stunned.Stomped = true;                                                          // flattened: 0.44 tall over the feet
        Assert.True(HitShapes.Hits(stunned, HazardKind.Knife, 10000, 3000, 400));
        Assert.True(HitShapes.Hits(stunned, HazardKind.Knife, 10000, 3000, 400));
    }

    [Fact]
    public void Head_top_is_narrow_like_the_drawing()
    {
        var p = PlayerState.SpawnAt(10000, 0, 1);
        // Stopping at 1.1 (only the top 0.1 of the head is in reach): the head is 0.26 wide there, not the box's 0.4.
        Assert.False(HitShapes.Hits(p, HazardKind.Knife, 10000 + 400, 3000, 1100));
        Assert.True(HitShapes.LegacySeparation(p, HazardKind.Knife, 10000 + 400, 3000, 1100) < 0);
        Assert.True(HitShapes.Hits(p, HazardKind.Knife, 10000 + 330, 3000, 1100));
    }

    [Fact]
    public void Room_kills_with_the_drawn_shapes_unless_legacy_is_asked()
    {
        foreach (bool legacy in new[] { false, true })
        {
            var room = Room(MapRotation.Fixed, legacy: legacy);
            byte a = room.AddPlayer("a"), b = room.AddPlayer("b");
            room.Tick();
            TickToPlaying(room);
            room.GetPlayer(a).State = PlayerState.SpawnAt(1500, 0, 1); // open floor left of the low shelf
            room.GetPlayer(b).State = PlayerState.SpawnAt(23000, 0, 1);
            room.SpawnKnife(1500 + 620); // drawn edges overlap, old boxes 0.02 apart
            for (int i = 0; i < KnifeWarningTicks + 25; i++) room.Tick();
            Assert.Equal(!legacy, room.State == RoomState.Ended);
            Assert.Equal(legacy, room.GetPlayer(a).Alive);
        }
    }

    // ---------------------------------------------------------------- host and next map

    [Fact]
    public void Host_is_the_earliest_player_still_here_not_the_lowest_id()
    {
        var room = Room();
        Assert.Equal(0, room.HostId);
        byte a = room.AddPlayer("a"), b = room.AddPlayer("b"), c = room.AddPlayer("c");
        Assert.Equal(a, room.HostId);
        room.RemovePlayer(a, LeaveReason.Disconnected);
        Assert.Equal(b, room.HostId);
        byte again = room.AddPlayer("late"); // reuses id 1 but joined last
        Assert.Equal(a, again);
        Assert.Equal(b, room.HostId);
        room.RemovePlayer(b, LeaveReason.Disconnected);
        Assert.Equal(c, room.HostId);
        room.Outbox.Clear();
        room.Tick();
        var info = Drain(room).OfType<RoomInfoPacket>().Single();
        Assert.Equal(c, info.HostId);
    }

    [Fact]
    public void Newcomer_gets_room_info_and_changes_are_broadcast_once()
    {
        var room = Room();
        byte a = room.AddPlayer("a");
        var first = Drain(room, a);
        Assert.IsType<WelcomePacket>(first[0]);
        var info = first.OfType<RoomInfoPacket>().Single();
        Assert.Equal((a, (byte)1, MapRotation.Cycle, false), (info.HostId, info.NextStageId, info.Rotation, info.NextChosen));

        room.Tick();
        Assert.Single(Drain(room).OfType<RoomInfoPacket>()); // first broadcast of the state
        room.Tick();
        Assert.Empty(Drain(room).OfType<RoomInfoPacket>()); // nothing changed

        Assert.True(room.RequestMap(a, 3, 0));
        room.Tick();
        info = Drain(room).OfType<RoomInfoPacket>().Single();
        Assert.Equal((a, (byte)3, MapRotation.Cycle, true), (info.HostId, info.NextStageId, info.Rotation, info.NextChosen));
    }

    [Fact]
    public void Only_the_host_may_change_the_map_and_bad_requests_change_nothing()
    {
        var room = Room();
        byte a = room.AddPlayer("a"), b = room.AddPlayer("b");
        Assert.False(room.RequestMap(b, 4, MapRotation.Random));
        Assert.False(room.RequestMap(a, 9, 0));
        Assert.False(room.RequestMap(a, 0, (MapRotation)7));
        Assert.False(room.RequestMap(42, 2, 0));
        Assert.Equal((1, MapRotation.Cycle, false), (room.NextStage.Id, room.Rotation, room.NextChosen));

        room.RemovePlayer(a, LeaveReason.Disconnected);
        Assert.True(room.RequestMap(b, 4, MapRotation.Random)); // the new host
        Assert.Equal((4, MapRotation.Random, true), (room.NextStage.Id, room.Rotation, room.NextChosen));
    }

    [Fact]
    public void Map_picked_during_a_round_applies_from_the_next_round_then_the_rotation_goes_on_from_there()
    {
        var room = Room(MapRotation.Cycle);
        byte a = room.AddPlayer("a"), b = room.AddPlayer("b");
        room.Tick();
        TickToPlaying(room);
        Assert.Equal(1, room.Stage.Id);
        Assert.Equal(2, room.NextStage.Id); // cycle shows the coming map while playing

        Assert.True(room.RequestMap(a, 4, 0));
        Assert.Equal(1, room.Stage.Id);    // this round stays on the kitchen
        FinishRound(room, a, b);
        Assert.Equal(4, room.Stage.Id);
        Assert.Equal(4, Drain(room).OfType<RoundStartPacket>().Last().StageId);
        Assert.False(room.NextChosen);

        TickToPlaying(room);
        Assert.Equal(1, room.NextStage.Id); // 4 -> 1 in cycle order
        FinishRound(room, a, b);
        Assert.Equal(1, room.Stage.Id);
    }

    [Fact]
    public void Picking_during_the_countdown_keeps_the_countdown_map_and_survives_the_play_start()
    {
        var room = Room(MapRotation.Cycle);
        byte a = room.AddPlayer("a"), b = room.AddPlayer("b");
        room.Tick();
        Assert.Equal(RoomState.Countdown, room.State);
        Assert.True(room.RequestMap(a, 3, 0));
        Assert.Equal(1, room.Stage.Id);
        TickToPlaying(room);
        Assert.Equal(3, room.NextStage.Id); // the rotation did not overwrite the host's pick
        FinishRound(room, a, b);
        Assert.Equal(3, room.Stage.Id);
    }

    [Fact]
    public void Picked_map_survives_a_cancelled_countdown()
    {
        var room = Room(MapRotation.Cycle);
        byte a = room.AddPlayer("a"), b = room.AddPlayer("b");
        room.Tick();
        TickToPlaying(room);
        FinishRound(room, a, b);           // now counting down on map 2
        room.RemovePlayer(b, LeaveReason.Disconnected);
        room.Tick();
        Assert.Equal(RoomState.Waiting, room.State);
        Assert.True(room.RequestMap(a, 4, 0));
        room.AddPlayer("b2");
        room.Outbox.Clear();
        room.Tick();
        Assert.Equal(RoomState.Countdown, room.State);
        Assert.Equal(4, Drain(room).OfType<RoundStartPacket>().Single().StageId);
    }

    [Fact]
    public void Fixed_repeats_the_map_and_random_never_repeats()
    {
        var room = Room(MapRotation.Cycle, seed: 3);
        byte a = room.AddPlayer("a"), b = room.AddPlayer("b");
        Assert.True(room.RequestMap(a, 2, MapRotation.Fixed));
        room.Tick();
        for (int round = 0; round < 3; round++)
        {
            TickToPlaying(room);
            Assert.Equal(2, room.Stage.Id);
            Assert.Equal(2, room.NextStage.Id);
            FinishRound(room, a, b);
        }
        Assert.Equal(2, room.Stage.Id);

        Assert.True(room.RequestMap(a, 0, MapRotation.Random)); // in the countdown: no pick, so random decides at play start
        var seen = new List<byte> { room.Stage.Id };
        for (int round = 0; round < 12; round++)
        {
            TickToPlaying(room);
            Assert.NotEqual(room.Stage.Id, room.NextStage.Id);
            FinishRound(room, a, b);
            seen.Add(room.Stage.Id);
        }
        for (int i = 1; i < seen.Count; i++) Assert.NotEqual(seen[i - 1], seen[i]);
    }

    [Fact]
    public void Rotation_change_after_a_round_updates_the_shown_next_map()
    {
        var room = Room(MapRotation.Cycle);
        byte a = room.AddPlayer("a"), b = room.AddPlayer("b");
        room.Tick();
        TickToPlaying(room);
        Assert.Equal(2, room.NextStage.Id);
        Assert.True(room.RequestMap(a, 0, MapRotation.Fixed));
        Assert.Equal(1, room.NextStage.Id);
        Assert.True(room.RequestMap(a, 0, MapRotation.Cycle));
        Assert.Equal(2, room.NextStage.Id);
    }

    [Fact]
    public void Room_info_and_map_request_round_trip()
    {
        var info = new RoomInfoPacket { HostId = 5, NextStageId = 3, Rotation = MapRotation.Random, NextChosen = true };
        var frame = PacketCodec.EncodeFrame(info);
        Assert.Equal(2 + 1 + 4, frame.Length);
        var back = Assert.IsType<RoomInfoPacket>(PacketCodec.DecodePayload(frame, 2, frame.Length - 2));
        Assert.True(info.SameAs(back));

        var req = new MapRequestPacket { StageId = 2, Rotation = MapRotation.Fixed };
        frame = PacketCodec.EncodeFrame(req);
        Assert.Equal(2 + 1 + 2, frame.Length);
        var r = Assert.IsType<MapRequestPacket>(PacketCodec.DecodePayload(frame, 2, frame.Length - 2));
        Assert.Equal((2, MapRotation.Fixed), (r.StageId, r.Rotation));
        Assert.Equal(5, ProtocolConstants.Version);
    }
}
