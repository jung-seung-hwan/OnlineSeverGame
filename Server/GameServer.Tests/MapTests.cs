using OnlineSeverGame.BotClient;
using OnlineSeverGame.Shared.Protocol;
using OnlineSeverGame.Shared.Simulation;
using static OnlineSeverGame.Shared.Simulation.GameConstants;

namespace OnlineSeverGame.Server.Tests;

/// <summary>The four maps (docs/work/maps.md): rotation, hazard kinds, ice, crates, melting ice cream.</summary>
public class MapTests
{
    private static List<IPacket> Drain(GameRoom room)
    {
        var packets = room.Outbox
            .Where(o => o.Target == 0 || o.Target == 1)
            .Select(o => PacketCodec.DecodePayload(o.Frame, 2, o.Frame.Length - 2))
            .ToList();
        room.Outbox.Clear();
        return packets;
    }

    private static GameRoom Room(Stage stage = null, MapRotation rotation = MapRotation.Fixed, int seed = 1) =>
        new GameRoom(new RoomConfig
        {
            MinPlayers = 2, AutoSpawnKnives = false, Seed = seed, Stage = stage ?? Stage.Default, Rotation = rotation,
        });

    private static void TickToPlaying(GameRoom room)
    {
        for (int i = 0; i < CountdownTicks + 5 && room.State != RoomState.Playing; i++) room.Tick();
        Assert.Equal(RoomState.Playing, room.State);
    }

    private static GameRoom PlayingRoom(Stage stage, out byte a, out byte b)
    {
        var room = Room(stage);
        a = room.AddPlayer("a");
        b = room.AddPlayer("b");
        TickToPlaying(room);
        room.Outbox.Clear();
        return room;
    }

    private static void Place(GameRoom room, byte id, int x, int y = 0) =>
        room.GetPlayer(id).State = PlayerState.SpawnAt(x, y, 1);

    /// <summary>An x on the floor where the map's hazard falls straight to the floor and no box is near.</summary>
    private static int OpenFloorX(Stage stage)
    {
        int half = HazardSpec.Of(stage.Hazard).HalfWidth + PlayerHalfWidth;
        for (int x = 500; x < stage.Width; x += 250)
        {
            if (stage.HazardLandingY(stage.Hazard, x, stage.KnifeSpawnY) != 0) continue;
            if (stage.Pushables.Any(p => Math.Abs(p.X - x) < p.Width / 2 + half)) continue;
            return x;
        }
        throw new InvalidOperationException("no open floor");
    }

    /// <summary>Kills the first player with the map's own hazard so the round ends, then waits for the next countdown.</summary>
    private static void FinishRound(GameRoom room, byte victim, byte other)
    {
        int x = OpenFloorX(room.Stage);
        Place(room, victim, x);
        Place(room, other, x < 12000 ? 23000 : 1000);
        room.SpawnKnife(x);
        for (int i = 0; i < 80 && room.State == RoomState.Playing; i++) room.Tick();
        Assert.Equal(RoomState.Ended, room.State);
        for (int i = 0; i < ResultTicks + 2 && room.State == RoomState.Ended; i++) room.Tick();
        Assert.Equal(RoomState.Countdown, room.State);
    }

    // ---------------------------------------------------------------- map list and rotation

    [Fact]
    public void Four_maps_with_eight_floor_spawns_clear_of_boxes()
    {
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, Stage.All.Select(s => s.Id).ToArray());
        Assert.Equal(new[] { HazardKind.Knife, HazardKind.Icicle, HazardKind.Crate, HazardKind.Fork },
            Stage.All.Select(s => s.Hazard).ToArray());
        foreach (var stage in Stage.All)
        {
            Assert.Same(stage, Stage.ById(stage.Id));
            Assert.Equal((24000, 13500), (stage.Width, stage.ViewHeight));
            Assert.Equal(8, stage.SpawnPoints.Count);
            foreach (var sp in stage.SpawnPoints)
            {
                Assert.Equal(0, sp.Y);
                Assert.InRange(sp.X, PlayerHalfWidth, stage.Width - PlayerHalfWidth);
                foreach (var box in stage.Pushables)
                    Assert.True(Math.Abs(box.X - sp.X) >= box.Width / 2 + PlayerHalfWidth, $"{stage.Name} spawn {sp.X} inside a box");
            }
        }
        Assert.Null(Stage.ById(9));
    }

    [Fact]
    public void Cycle_rotation_plays_kitchen_winter_warehouse_dessert_then_kitchen()
    {
        var room = Room(rotation: MapRotation.Cycle);
        byte a = room.AddPlayer("a"), b = room.AddPlayer("b");
        Assert.Equal(1, Drain(room).OfType<WelcomePacket>().First().StageId);
        room.Tick();
        var seen = new List<byte> { Drain(room).OfType<RoundStartPacket>().Single().StageId };
        for (int round = 0; round < 4; round++)
        {
            TickToPlaying(room);
            FinishRound(room, a, b);
            var start = Drain(room).OfType<RoundStartPacket>().Single();
            Assert.Equal(room.Stage.Id, start.StageId);
            seen.Add(start.StageId);
            // Everyone is back on the new map's spawn points.
            Assert.Equal(room.Stage.SpawnPoints[0].X, room.GetPlayer(a).State.X);
        }
        Assert.Equal(new byte[] { 1, 2, 3, 4, 1 }, seen.ToArray());
    }

    [Fact]
    public void Random_rotation_never_repeats_a_map_and_visits_all_of_them()
    {
        var room = Room(rotation: MapRotation.Random, seed: 7);
        byte a = room.AddPlayer("a"), b = room.AddPlayer("b");
        room.Tick();
        var seen = new List<byte> { room.Stage.Id };
        for (int round = 0; round < 20; round++)
        {
            TickToPlaying(room);
            FinishRound(room, a, b);
            seen.Add(room.Stage.Id);
        }
        for (int i = 1; i < seen.Count; i++) Assert.NotEqual(seen[i - 1], seen[i]);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, seen.Distinct().OrderBy(x => x).ToArray());
    }

    [Fact]
    public void Cancelled_countdown_keeps_the_map_for_the_retry()
    {
        var room = Room(rotation: MapRotation.Cycle);
        byte a = room.AddPlayer("a"), b = room.AddPlayer("b");
        room.Tick();
        TickToPlaying(room);
        FinishRound(room, a, b);
        Assert.Equal(2, room.Stage.Id);

        room.RemovePlayer(b, LeaveReason.Disconnected);
        room.Tick();
        Assert.Equal(RoomState.Waiting, room.State);
        room.AddPlayer("b2");
        room.Outbox.Clear();
        room.Tick();
        Assert.Equal(RoomState.Countdown, room.State);
        Assert.Equal(2, Drain(room).OfType<RoundStartPacket>().Single().StageId);
    }

    [Fact]
    public void Fixed_map_never_changes_and_snapshots_carry_the_hazard_kind()
    {
        var room = Room(Stage.Winter);
        byte a = room.AddPlayer("a"), b = room.AddPlayer("b");
        room.Tick();
        Assert.Equal(2, Drain(room).OfType<RoundStartPacket>().Single().StageId);
        TickToPlaying(room);
        room.SpawnKnife(3500);
        room.Tick();
        var snap = Drain(room).OfType<SnapshotPacket>().Last();
        Assert.Equal(HazardKind.Icicle, Assert.Single(snap.Knives).Kind);
        Assert.Equal(Stage.Winter.KnifeSpawnY, snap.Knives[0].Y);
        FinishRound(room, a, b);
        Assert.Same(Stage.Winter, room.Stage);
    }

    // ---------------------------------------------------------------- winter eaves

    private static KnifeState DropUntilLanded(Stage stage, HazardKind kind, int x, out int fallingTicks)
    {
        var k = new KnifeState { Id = 1, Kind = kind, Phase = KnifePhase.Falling, X = x, Y = stage.KnifeSpawnY };
        fallingTicks = 0;
        while (k.Phase == KnifePhase.Falling)
        {
            KnifePhysics.Step(ref k, stage, null, out _, out _);
            fallingTicks++;
            Assert.True(fallingTicks < 100);
        }
        return k;
    }

    [Fact]
    public void Icicles_speed_up_go_through_ice_sills_and_stop_on_wood()
    {
        var w = Stage.Winter;
        Assert.Equal(new[] { 70, 140, 980, 1000, 1000 },
            new[] { 1, 2, 14, 15, 30 }.Select(t => HazardSpec.Icicle.SpeedAt(t)).ToArray());

        var k = DropUntilLanded(w, HazardKind.Icicle, 3500, out int ticks); // under the left ice sill
        Assert.Equal(0, k.Y);
        Assert.Equal(19, ticks);
        Assert.Equal(4800, DropUntilLanded(w, HazardKind.Icicle, 7000, out _).Y);  // left beam
        Assert.Equal(1600, DropUntilLanded(w, HazardKind.Icicle, 12000, out _).Y); // bench
        Assert.Equal(0, w.KnifeLandingY(3500, w.KnifeSpawnY));
        // A knife at the same spot would have stuck in the sill.
        Assert.Equal(2400, w.HazardLandingY(HazardKind.Knife, 3500, w.KnifeSpawnY));
    }

    [Fact]
    public void Icicles_spawn_only_in_the_eave_slots()
    {
        var room = new GameRoom(new RoomConfig { MinPlayers = 1, Seed = 3, Stage = Stage.Winter });
        room.AddPlayer("a");
        for (int i = 0; i < CountdownTicks + 400; i++)
        {
            room.Tick();
            foreach (var k in room.Knives)
            {
                Assert.Equal(HazardKind.Icicle, k.Kind);
                Assert.Equal(500, k.X % 1000);
            }
        }
    }

    [Fact]
    public void Walking_on_ice_eases_in_and_slides_on_release()
    {
        var w = Stage.Winter;
        var s = PlayerState.SpawnAt(12000, 0, 1); // frozen puddle 8..16
        var speeds = new List<int>();
        for (int i = 0; i < 9; i++)
        {
            PlayerPhysics.Step(ref s, InputButtons.Right, w);
            speeds.Add(s.Vx);
        }
        Assert.Equal(new[] { 50, 100, 150, 200, 250, 300, 350, 400, 400 }, speeds.ToArray());

        int start = s.X;
        for (int i = 0; i < 12; i++) PlayerPhysics.Step(ref s, InputButtons.None, w);
        Assert.Equal(0, s.Vx);
        Assert.Equal(1400, s.X - start);

        // Snow at x 2: instant start and stop.
        var n = PlayerState.SpawnAt(2000, 0, 1);
        PlayerPhysics.Step(ref n, InputButtons.Right, w);
        Assert.Equal(MoveSpeed, n.Vx);
        PlayerPhysics.Step(ref n, InputButtons.None, w);
        Assert.Equal(0, n.Vx);
    }

    [Fact]
    public void Ice_sills_are_slippery_too_and_a_dash_ends_in_a_short_slide()
    {
        var w = Stage.Winter;
        var s = PlayerState.SpawnAt(3000, 2400, 1);
        s.Grounded = true;
        PlayerPhysics.Step(ref s, InputButtons.Right, w);
        Assert.Equal(IceAccel, s.Vx);

        var d = PlayerState.SpawnAt(9000, 0, 1);
        PlayerPhysics.Step(ref d, InputButtons.Dash, w);
        for (int i = 1; i < DashTicks; i++) PlayerPhysics.Step(ref d, InputButtons.None, w);
        Assert.Equal(DashSpeed, d.Vx);
        PlayerPhysics.Step(ref d, InputButtons.None, w);
        Assert.Equal(MoveSpeed - IceAccel, d.Vx); // dash speed is dropped to walking speed, then it slides
    }

    private static int KnockDistance(Stage stage, int x)
    {
        var states = new[]
        {
            new PlayerState { X = x - 500, Y = 0, Facing = 1, DashTicks = 2, Grounded = true },
            PlayerState.SpawnAt(x, 0, -1),
        };
        DashCollision.Resolve(states, new[] { true, true }, 2, null, stage);
        Assert.Equal(DashStunTicks, states[1].StunTicks);
        var t = states[1];
        for (int i = 0; i < 30; i++) PlayerPhysics.Step(ref t, InputButtons.None, stage);
        Assert.Equal(0, t.Vx);
        return t.X - x;
    }

    [Fact]
    public void A_dash_hit_on_ice_slides_about_twice_as_far()
    {
        var states = new[]
        {
            new PlayerState { X = 11500, Y = 0, Facing = 1, DashTicks = 2, Grounded = true },
            PlayerState.SpawnAt(12000, 0, -1),
        };
        DashCollision.Resolve(states, new[] { true, true }, 2, null, Stage.Winter);
        Assert.Equal(0, states[1].Vy);          // no lift on ice
        Assert.True(states[1].Grounded);

        int ice = KnockDistance(Stage.Winter, 10000);
        int snow = KnockDistance(Stage.Winter, 1500);
        Assert.InRange(snow, 2000, 2800);
        Assert.InRange(ice, 5500, 6400); // about 5.9: the 1 s dash stun keeps the ice decay (vx/8) going longer (dash-side-knife.md 2.3)
    }

    [Fact]
    public void Boxes_slide_farther_on_ice()
    {
        int Slide(int x)
        {
            var boxes = new[] { PushableState.Spawn(1, new PushableSpawn(x, 0, 1000, 1000)) };
            boxes[0].Vx = PushableDashSpeed;
            for (int i = 0; i < 80; i++) PushablePhysics.Step(boxes, 1, Stage.Winter, null, null, 0, null);
            Assert.Equal(0, boxes[0].Vx);
            return boxes[0].X - x;
        }
        Assert.True(Slide(8600) > Slide(1000) * 3 / 2);
    }

    // ---------------------------------------------------------------- warehouse

    private static void TickUntilNoHazards(GameRoom room, int max = 120)
    {
        for (int i = 0; i < max && room.Knives.Count > 0 && room.State == RoomState.Playing; i++) room.Tick();
    }

    [Fact]
    public void A_falling_crate_kills_then_stays_as_a_box_on_the_surface_it_hit()
    {
        var room = Room(Stage.Warehouse);
        byte a = room.AddPlayer("a"), b = room.AddPlayer("b"), c = room.AddPlayer("c");
        TickToPlaying(room);
        Place(room, a, 6000);
        Place(room, b, 21000, 2400); // under the right rack's top shelf
        room.GetPlayer(b).State.Grounded = true;
        Place(room, c, 1500);
        room.SpawnKnife(6000);   // floor
        room.SpawnKnife(12000);  // hanging pallet
        Assert.All(room.Knives, k => Assert.Equal(HazardKind.Crate, k.Kind));
        for (int i = 0; i < 70 && room.Pushables.Length < 4; i++) room.Tick();

        Assert.False(room.GetPlayer(a).Alive);
        Assert.Equal(4, room.Pushables.Length);
        Assert.Empty(room.Knives);
        Assert.Contains(room.Pushables, p => p.X == 6000 && p.Y == 0 && p.Width == 1400 && p.Height == 1200);
        Assert.Contains(room.Pushables, p => p.X == 12000 && p.Y == 3600);
        Assert.Equal(2, room.RoundCratesLanded);
    }

    [Fact]
    public void Crates_stack_two_high_and_a_third_shatters()
    {
        var room = PlayingRoom(Stage.Warehouse, out byte a, out byte b);
        Place(room, a, 1500);
        Place(room, b, 22500);
        for (int n = 0; n < 3; n++)
        {
            room.SpawnKnife(6000);
            TickUntilNoHazards(room);
        }
        var stack = room.Pushables.Where(p => p.X == 6000).OrderBy(p => p.Y).Select(p => p.Y).ToArray();
        Assert.Equal(new[] { 0, 1200 }, stack);
        Assert.Equal(2, room.RoundCratesLanded);
    }

    [Fact]
    public void Past_six_resting_crates_the_oldest_crumble_away()
    {
        var room = PlayingRoom(Stage.Warehouse, out byte a, out byte b);
        Place(room, a, 1500);
        Place(room, b, 22500);
        foreach (int x in new[] { 6000, 10000, 12000, 14000, 18000, 2000 }) room.SpawnKnife(x);
        for (int i = 0; i < 80 && room.RoundCratesLanded < 6; i++) room.Tick();
        Assert.Equal(6, room.RoundCratesLanded);
        Assert.Equal(8, room.Pushables.Length);
        Assert.Equal(new ushort[] { 1, 2 }, room.Pushables.Where(p => p.Crumble > 0).Select(p => p.Id).ToArray());

        for (int i = 0; i < CrateCrumbleTicks; i++) room.Tick();
        Assert.Equal(MaxRestingCrates, room.Pushables.Length);
        Assert.DoesNotContain(room.Pushables, p => p.Id <= 2);

        var snap = Drain(room).OfType<SnapshotPacket>().Last();
        Assert.Equal(MaxRestingCrates, snap.Pushables.Count);
    }

    [Fact]
    public void Landed_crates_are_cleared_when_the_next_round_starts()
    {
        var room = PlayingRoom(Stage.Warehouse, out byte a, out byte b);
        Place(room, b, 22500);
        room.SpawnKnife(10000);
        TickUntilNoHazards(room);
        Assert.Equal(3, room.Pushables.Length);
        FinishRound(room, a, b);
        Assert.Equal(new ushort[] { 1, 2 }, room.Pushables.Select(p => p.Id).ToArray());
    }

    // ---------------------------------------------------------------- dessert shop

    [Fact]
    public void Ice_cream_platforms_melt_from_ten_seconds_down_to_their_minimum()
    {
        var d = Stage.Dessert;
        Assert.Same(d, d.AtRoundTick(0));
        Assert.Same(d, d.AtRoundTick(MeltDelayTicks));
        var t300 = d.AtRoundTick(MeltDelayTicks + 100);
        Assert.Equal((1700, 6300), (t300.Platforms[0].Left, t300.Platforms[0].Right)); // strawberry: 2 per tick per side
        Assert.Equal((10800, 13200), (t300.Platforms[3].Left, t300.Platforms[3].Right)); // chocolate: 3 per tick
        Assert.Same(t300, d.AtRoundTick(MeltDelayTicks + 100)); // cached
        Assert.Same(d, t300.Base);

        var late = d.AtRoundTick(RoundTimeLimitTicks);
        Assert.Equal(new[] { 1600, 1600, 2000, 1000 }, late.Platforms.Select(p => p.Width).ToArray());
        Assert.Equal(new[] { 4000, 20000, 12000, 12000 }, late.Platforms.Select(p => p.CenterX).ToArray());
        // Minimums reached at 26.7 s (chocolate), 52.5 s (strawberry, mint) and 72.5 s (vanilla).
        Assert.Equal(1600, d.AtRoundTick(1050).Platforms[0].Width);
        Assert.True(d.AtRoundTick(1040).Platforms[0].Width > 1600);
        Assert.Equal(2000, d.AtRoundTick(1450).Platforms[2].Width);
        Assert.True(d.AtRoundTick(1440).Platforms[2].Width > 2000);
    }

    [Fact]
    public void A_jelly_on_the_melting_edge_falls_off_when_the_edge_passes_it()
    {
        var d = Stage.Dessert;
        var s = PlayerState.SpawnAt(6400, 2000, 1); // body 6.0..6.8 on strawberry 1.5..6.5
        s.Grounded = true;
        for (uint t = 201; t <= 440; t++) PlayerPhysics.Step(ref s, InputButtons.None, d.AtRoundTick(t));
        Assert.Equal(2000, s.Y);
        for (uint t = 441; t <= 500; t++) PlayerPhysics.Step(ref s, InputButtons.None, d.AtRoundTick(t));
        Assert.Equal(0, s.Y);
    }

    [Fact]
    public void Melting_happens_in_the_room_only_while_playing()
    {
        var room = PlayingRoom(Stage.Dessert, out byte a, out byte b);
        Place(room, a, 1000);
        Place(room, b, 23000);
        Assert.Equal(5000, room.CurrentStage.Platforms[0].Width);
        for (int i = 0; i < MeltDelayTicks + 100; i++) room.Tick();
        Assert.Equal(4600, room.CurrentStage.Platforms[0].Width);
        // Snapshots carry roundTick, so a client computes the same shape.
        var snap = Drain(room).OfType<SnapshotPacket>().Last();
        Assert.Equal(4600, Stage.ById(4).AtRoundTick(snap.RoundTick).Platforms[0].Width);
    }

    [Fact]
    public void Forks_are_wider_than_knives()
    {
        bool Hits(HazardKind kind, int dx)
        {
            var k = new KnifeState { Id = 1, Kind = kind, Phase = KnifePhase.Falling, X = 600, Y = 14000 };
            var body = PlayerState.SpawnAt(600 + dx, 0, 1).Bounds;
            while (k.Phase == KnifePhase.Falling)
            {
                KnifePhysics.Step(ref k, Stage.Dessert, null, out bool dangerous, out Aabb swept);
                if (dangerous && swept.Overlaps(body)) return true;
            }
            return false;
        }
        Assert.True(Hits(HazardKind.Fork, 620));
        Assert.False(Hits(HazardKind.Knife, 620));
        Assert.False(Hits(HazardKind.Fork, 660));
        Assert.Equal(new[] { 1400, 400, 500 }, new[] { HazardSpec.Crate, HazardSpec.Icicle, HazardSpec.Fork }.Select(s => s.Width).ToArray());
    }

    // ---------------------------------------------------------------- bots

    [Fact]
    public void Bots_see_crates_as_wide_and_do_not_hide_from_icicles_under_ice()
    {
        var crate = new KnifeState { Id = 1, Kind = HazardKind.Crate, Phase = KnifePhase.Warning, X = 6000, Y = 14000, Timer = 10 };
        var me = PlayerState.SpawnAt(7000, 0, 1);
        var d = Assert.Single(DodgeBrain.FindDangers(me, new[] { crate }, Stage.Warehouse, 2));
        Assert.Equal(6000 - 720 - HitShapes.BodyHalfWidth - DodgeBrain.Margin, d.Left);
        Assert.True(d.Contains(7000));

        // On the icy window sill an icicle goes through it: no Down + Jump, walk out instead.
        var icicle = new KnifeState { Id = 2, Kind = HazardKind.Icicle, Phase = KnifePhase.Warning, X = 3500, Y = 11800, Timer = 20 };
        var onSill = PlayerState.SpawnAt(3500, 2400, 1);
        onSill.Grounded = true;
        var b = DodgeBrain.Decide(onSill, new[] { icicle }, Stage.Winter, 2, InputButtons.None);
        Assert.Equal(InputButtons.None, b & (InputButtons.Down | InputButtons.Jump));
        Assert.NotEqual(InputButtons.None, b & (InputButtons.Left | InputButtons.Right));
        // Under the sill is no roof either.
        Assert.Single(DodgeBrain.FindDangers(PlayerState.SpawnAt(3500, 0, 1), new[] { icicle }, Stage.Winter, 2));
        // Under the wooden beam it is.
        icicle.X = 7000;
        Assert.Empty(DodgeBrain.FindDangers(PlayerState.SpawnAt(7000, 0, 1), new[] { icicle }, Stage.Winter, 2));
    }

    [Fact]
    public void Bot_fall_time_matches_the_icicle_acceleration()
    {
        Assert.Equal(2, DodgeBrain.TicksToFall(HazardSpec.Knife, 0, 1400));
        Assert.Equal(1, DodgeBrain.TicksToFall(HazardSpec.Knife, 0, 1399));
        Assert.Equal(18, DodgeBrain.TicksToFall(HazardSpec.Icicle, 0, 11800)); // lands on tick 19
        Assert.Equal(1, DodgeBrain.TicksToFall(HazardSpec.Icicle, 15, 1500));
    }
}
