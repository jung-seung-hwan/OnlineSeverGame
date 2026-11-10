using OnlineSeverGame.BotClient;
using OnlineSeverGame.Shared.Protocol;
using OnlineSeverGame.Shared.Simulation;
using Xunit.Abstractions;
using static OnlineSeverGame.Shared.Simulation.GameConstants;

namespace OnlineSeverGame.Server.Tests;

/// <summary>Jump pads (docs/work/jump-pad.md): launching, the cooldown, pushing, map events, reach, safety, prediction.</summary>
public class JumpPadTests
{
    private readonly ITestOutputHelper _out;

    public JumpPadTests(ITestOutputHelper output) => _out = output;

    /// <summary>A flat floor with a jump pad in the middle (launch 1500) and a high platform to its right.</summary>
    private static readonly Stage PadArena = new Stage(
        id: 1, name: "PadArena", width: 24000, viewHeight: 13500, knifeSpawnY: 14000,
        platforms: new[] { new Platform(15000, 19000, 9600) },
        spawnPoints: new[] { new SpawnPoint(4000, 0, 1), new SpawnPoint(20000, 0, -1) },
        pushables: new[] { PushableSpawn.JumpPad(12000, 1500) });

    private static PushableState[] Boxes(Stage s) =>
        s.Pushables.Select((p, i) => PushableState.Spawn((ushort)(i + 1), p)).ToArray();

    private static int PadIndex(IReadOnlyList<PushableState> boxes)
    {
        for (int i = 0; i < boxes.Count; i++)
            if (boxes[i].IsJumpPad) return i;
        return -1;
    }

    private static PlayerState Falling(int x, int y) => new PlayerState { X = x, Y = y, Facing = 1 };

    // ---------------------------------------------------------------- launching

    [Fact]
    public void Landing_on_the_pad_launches_with_its_speed_and_starts_the_cooldown()
    {
        var boxes = Boxes(PadArena);
        var p = Falling(12000, 2000);
        BoxContact c = BoxContact.None;
        for (int i = 0; i < 20 && c.LaunchBox < 0; i++) PlayerPhysics.Step(ref p, InputButtons.None, PadArena, boxes, out c);
        Assert.Equal(0, c.LaunchBox);
        Assert.Equal(-1, c.GroundBox);
        Assert.Equal((500, 1500, false), (p.Y, p.Vy, p.Grounded));
        Assert.Equal(JumpPadCooldownTicks, p.PadCooldown);
        PlayerPhysics.Step(ref p, InputButtons.None, PadArena, boxes, out _);
        Assert.Equal((500 + 1400, 1400, JumpPadCooldownTicks - 1), (p.Y, p.Vy, (int)p.PadCooldown));
    }

    [Fact]
    public void Left_alone_a_player_bounces_once_then_stands_on_the_pad()
    {
        var boxes = Boxes(PadArena);
        var p = Falling(12000, 600);
        int launches = 0, firstLaunch = -1, backOn = -1, apex = 0;
        for (int i = 0; i < 20 * TickRate; i++)
        {
            PlayerPhysics.Step(ref p, InputButtons.None, PadArena, boxes, out var c);
            apex = Math.Max(apex, p.Y);
            if (c.LaunchBox >= 0) { launches++; if (firstLaunch < 0) firstLaunch = i; }
            if (firstLaunch >= 0 && backOn < 0 && p.Grounded) backOn = i;
        }
        int flight = backOn - firstLaunch;
        _out.WriteLine($"flight {flight} ticks, apex feet {apex}");
        Assert.Equal(1, launches);
        Assert.Equal((500, true), (p.Y, p.Grounded));
        Assert.Equal(11000, apex);              // 1500: feet 10.5 above the pad top
        Assert.Equal(30, flight);               // up 15, down 15 ...
        Assert.True(flight < JumpPadCooldownTicks); // ... so it lands back during the cooldown and is not relaunched
    }

    [Fact]
    public void Mashing_jump_on_the_pad_launches_at_most_once_per_cooldown()
    {
        var boxes = Boxes(PadArena);
        var p = Falling(12000, 600);
        var launches = new List<int>();
        for (int i = 0; i < 60 * TickRate; i++)
        {
            // Jump on every grounded tick (press edge: release on the others), steer back over the pad.
            InputButtons b = p.Grounded && (p.PrevButtons & InputButtons.Jump) == 0 ? InputButtons.Jump : InputButtons.None;
            if (p.X < 11800) b |= InputButtons.Right;
            else if (p.X > 12200) b |= InputButtons.Left;
            PlayerPhysics.Step(ref p, b, PadArena, boxes, out var c);
            if (c.LaunchBox >= 0) launches.Add(i);
        }
        var gaps = launches.Zip(launches.Skip(1), (a, b) => b - a).ToList();
        _out.WriteLine($"{launches.Count} launches in 60 s, gaps {string.Join(",", gaps.Distinct())}");
        Assert.True(gaps.All(g => g >= JumpPadCooldownTicks));
        Assert.True(launches.Count <= 60 * TickRate / JumpPadCooldownTicks + 1);
        Assert.True(launches.Count >= 20); // the pad stays usable: a normal jump after the bounce relaunches
    }

    [Fact]
    public void Stunned_flat_and_crouching_players_are_launched_too()
    {
        var boxes = Boxes(PadArena);
        // Knocked onto the pad by a dash hit: lying flat waits in the air (the flat stun only counts down on the ground).
        var flat = Falling(12000, 600);
        flat.StunTicks = 20;
        flat.Stomped = true;
        PlayerPhysics.Step(ref flat, InputButtons.None, PadArena, boxes, out var c1);
        Assert.Equal(0, c1.LaunchBox);
        Assert.Equal((1500, (byte)20), (flat.Vy, flat.StunTicks));
        for (int i = 0; i < 10; i++) PlayerPhysics.Step(ref flat, InputButtons.None, PadArena, boxes, out _);
        Assert.Equal((byte)20, flat.StunTicks);

        var ducking = Falling(12000, 600);
        PlayerPhysics.Step(ref ducking, InputButtons.Down, PadArena, boxes, out var c2);
        Assert.Equal(0, c2.LaunchBox);
        Assert.False(ducking.Crouching);
    }

    [Fact]
    public void A_dash_over_the_pad_top_ends_the_dash_and_launches()
    {
        var boxes = Boxes(PadArena);
        var p = new PlayerState { X = 10000, Y = 500, Facing = 1 };
        PlayerPhysics.Step(ref p, InputButtons.Right | InputButtons.Dash, PadArena, boxes, out var c);
        Assert.Equal(0, c.LaunchBox);
        Assert.Equal((0, 1500), ((int)p.DashTicks, p.Vy));
        PlayerPhysics.Step(ref p, InputButtons.Right, PadArena, boxes, out _);
        Assert.True(p.Vy == 1400 && p.Y > 1500);
    }

    [Fact]
    public void Walking_off_a_higher_box_onto_the_pad_launches_but_standing_on_it_does_not()
    {
        var boxes = Boxes(PadArena);
        var standing = new PlayerState { X = 12000, Y = 500, Grounded = true, Facing = 1 };
        for (int i = 0; i < 40; i++)
        {
            PlayerPhysics.Step(ref standing, InputButtons.None, PadArena, boxes, out var c);
            Assert.Equal(-1, c.LaunchBox);
            Assert.Equal(0, c.GroundBox);
        }
    }

    // ---------------------------------------------------------------- pushing, walls, map events

    private static GameRoom PlayingRoom(Stage stage, int players, out byte[] ids, Action<string> log = null, bool logPushes = false)
    {
        var room = new GameRoom(new RoomConfig
        {
            MinPlayers = Math.Max(1, players), AutoSpawnKnives = false, AutoSpawnSideKnives = false, Seed = 1, Stage = stage,
            LogPushes = logPushes,
        }, log ?? (_ => { }));
        ids = Enumerable.Range(0, players).Select(i => room.AddPlayer("P" + i)).ToArray();
        for (int i = 0; i < CountdownTicks + 5 && room.State != RoomState.Playing; i++) room.Tick();
        Assert.Equal(RoomState.Playing, room.State);
        room.Outbox.Clear();
        return room;
    }

    [Fact]
    public void Walking_and_dashing_push_the_pad_like_a_box_and_friction_stops_it()
    {
        var room = PlayingRoom(PadArena, 2, out var ids);
        var p = room.GetPlayer(ids[0]);
        room.GetPlayer(ids[1]).State = PlayerState.SpawnAt(22000, 0, -1);
        p.State = PlayerState.SpawnAt(12000 - 600 - 400 - 100, 0, 1);
        uint seq = 0;
        for (int i = 0; i < 10; i++) { room.QueueInput(ids[0], ++seq, InputButtons.Right); room.Tick(); }
        int pad = PadIndex(room.Pushables);
        int walked = room.Pushables[pad].X - 12000;
        Assert.InRange(walked, 8 * (PushableWalkSpeed - PushableFriction), 10 * PushableWalkSpeed);
        Assert.InRange(room.Pushables[pad].Left - PlayerHalfWidth - p.State.X, 0, PushableWalkSpeed); // at its side, not on it

        // Step back, then dash into it: it slides about 4.9 u and stops.
        for (int i = 0; i < 6; i++) { room.QueueInput(ids[0], ++seq, InputButtons.Left); room.Tick(); }
        for (int i = 0; i < 45; i++) { room.QueueInput(ids[0], ++seq, i == 0 ? InputButtons.Right | InputButtons.Dash : InputButtons.None); room.Tick(); }
        int start = 12000 + walked;
        int slid = room.Pushables[pad].X - start;
        Assert.Equal(0, room.Pushables[pad].Vx);
        Assert.InRange(slid, 4500, 5500);
        Assert.Equal(0, room.Pushables[pad].Y);
    }

    [Fact]
    public void The_pad_stops_at_the_walls()
    {
        foreach (int dir in new[] { 1, -1 })
        {
            var boxes = Boxes(PadArena);
            boxes[0].X = dir > 0 ? 22000 : 2000;
            boxes[0].Vx = dir * PushableDashSpeed;
            for (int i = 0; i < 40; i++) PushablePhysics.Step(boxes, 1, PadArena, null, null, 0, null);
            Assert.Equal(dir > 0 ? PadArena.Width - JumpPadWidth / 2 : JumpPadWidth / 2, boxes[0].X);
            Assert.Equal(0, boxes[0].Vx);
        }
    }

    private static uint ActiveEventTick(Stage stage, Func<MapEventState, bool> want)
    {
        for (uint t = 1; t < RoundTimeLimitTicks; t++)
        {
            var e = stage.AtRoundTick(t).Event;
            if (e.IsActive && want(e)) return t;
        }
        throw new InvalidOperationException("no such event tick");
    }

    private static int SlideDistance(Stage stage, uint fromTick, int x)
    {
        var boxes = Boxes(stage);
        int pad = PadIndex(boxes);
        boxes[pad].X = x;
        boxes[pad].Vx = PushableDashSpeed;
        var one = new[] { boxes[pad] };
        for (uint t = fromTick; t < fromTick + 40; t++) PushablePhysics.Step(one, 1, stage.AtRoundTick(t), null, null, 0, null);
        return one[0].X - x;
    }

    [Fact]
    public void Ice_lets_the_pad_slide_farther_syrup_stops_it_sooner_and_the_belt_carries_it()
    {
        int plain = SlideDistance(Stage.Kitchen, 0, 4000);
        int ice = SlideDistance(Stage.Winter, 0, 7000);                   // winter floor ice 6.5 ~ 14
        uint syrupTick = ActiveEventTick(Stage.Dessert, e => e.SpanContains(3000));
        int syrup = SlideDistance(Stage.Dessert, syrupTick, 2000);
        _out.WriteLine($"dash-pushed pad slides: floor {plain}, ice {ice}, syrup {syrup}");
        Assert.InRange(plain, 4500, 5500);
        Assert.True(ice > plain * 3 / 2);
        Assert.True(syrup < plain * 2 / 3);

        // The conveyor moves the pad standing on the belt (its start x 12.0 is on it) every tick.
        var w = Stage.Warehouse;
        uint beltTick = ActiveEventTick(w, e => e.Kind == MapEventKind.Conveyor);
        var boxes = Boxes(w);
        int pad = PadIndex(boxes);
        var one = new[] { boxes[pad] };
        var e0 = w.AtRoundTick(beltTick).Event;
        PushablePhysics.Step(one, 1, w.AtRoundTick(beltTick), null, null, 0, null);
        Assert.Equal(12000 + e0.Dir * e0.Strength, one[0].X);
    }

    // ---------------------------------------------------------------- server

    [Fact]
    public void The_server_springs_the_pad_and_the_snapshot_carries_the_pad_and_the_cooldown()
    {
        var logs = new List<string>();
        var room = PlayingRoom(PadArena, 2, out var ids, logs.Add, logPushes: true);
        room.GetPlayer(ids[1]).State = PlayerState.SpawnAt(22000, 0, -1);
        room.GetPlayer(ids[0]).State = Falling(12000, 900);
        for (int i = 0; i < 5 && room.Pushables[0].Spring == 0; i++) room.Tick();
        Assert.Equal((byte)JumpPadSpringTicks, room.Pushables[0].Spring);
        Assert.Contains(logs, l => l.StartsWith("pad   #1 launched by pad#1"));

        var snap = room.Outbox.Select(o => PacketCodec.DecodePayload(o.Frame, 2, o.Frame.Length - 2)).OfType<SnapshotPacket>().Last();
        var box = snap.Pushables.Single();
        Assert.Equal((1500, (byte)JumpPadSpringTicks), ((int)box.Launch, box.Spring));
        Assert.True(box.IsJumpPad);
        Assert.Equal((byte)JumpPadCooldownTicks, snap.Players.Single(p => p.PlayerId == ids[0]).State.PadCooldown);

        room.Tick();
        Assert.Equal((byte)(JumpPadSpringTicks - 1), room.Pushables[0].Spring);
    }

    [Fact]
    public void A_crate_landing_on_the_pad_shatters_and_the_pad_never_crumbles()
    {
        var room = PlayingRoom(Stage.Warehouse, 2, out var ids);
        room.GetPlayer(ids[0]).State = PlayerState.SpawnAt(1500, 0, 1);
        room.GetPlayer(ids[1]).State = PlayerState.SpawnAt(22500, 0, -1);
        room.SpawnKnife(12000);
        for (int i = 0; i < 80 && room.Knives.Count > 0; i++) room.Tick();
        Assert.Equal(3, room.Pushables.Length);
        Assert.Equal(0, room.RoundCratesLanded);
        Assert.All(room.Pushables, b => Assert.Equal(0, b.Crumble));
    }

    [Fact]
    public void A_box_that_comes_to_rest_on_the_pad_crumbles()
    {
        var room = PlayingRoom(Stage.Warehouse, 2, out var ids);
        room.GetPlayer(ids[0]).State = PlayerState.SpawnAt(1500, 0, 1);
        room.GetPlayer(ids[1]).State = PlayerState.SpawnAt(22500, 0, -1);
        int pad = PadIndex(room.Pushables);
        room.Pushables[0].X = 12000;           // drop start box 1 onto the pad from above
        room.Pushables[0].Y = 2000;
        room.Pushables[0].Grounded = false;
        for (int i = 0; i < 10; i++) room.Tick();
        Assert.True(room.Pushables.Length == 3 && room.Pushables[0].Crumble > 0 && room.Pushables[0].Y == 500);
        for (int i = 0; i < CrateCrumbleTicks; i++) room.Tick();
        Assert.Equal(2, room.Pushables.Length);
        Assert.Contains(room.Pushables, b => b.IsJumpPad && b.Crumble == 0);
        _ = pad;
    }

    [Fact]
    public void Every_map_has_one_jump_pad_in_the_middle_clear_of_the_spawns()
    {
        foreach (var stage in Stage.All)
        {
            var pads = stage.Pushables.Where(p => p.IsJumpPad).ToList();
            Assert.Single(pads);
            var pad = pads[0];
            Assert.Equal((12000, 0, JumpPadWidth, JumpPadHeight), (pad.X, pad.Y, pad.Width, pad.Height));
            foreach (var sp in stage.SpawnPoints)
                Assert.True(Math.Abs(sp.X - pad.X) >= pad.Width / 2 + HitShapes.BodyHalfWidth, $"{stage.Name} spawn {sp.X}");
            foreach (var other in stage.Pushables.Where(p => !p.IsJumpPad))
                Assert.True(Math.Abs(other.X - pad.X) >= (other.Width + pad.Width) / 2 + 2 * PlayerHalfWidth, $"{stage.Name} box {other.X}");
        }
    }

    // ---------------------------------------------------------------- reach

    /// <summary>Feet height each map's pad must carry a player onto (docs/work/jump-pad.md 2).</summary>
    public static IEnumerable<object[]> Targets() => new[]
    {
        new object[] { 1, 9600 },  // kitchen: ladle rail
        new object[] { 2, 9600 },  // winter: stone ledge
        new object[] { 3, 8400 },  // warehouse: crane pallet
        new object[] { 4, 9600 },  // dessert: cherry cake top
    };

    [Theory]
    [MemberData(nameof(Targets))]
    public void A_launch_from_each_maps_pad_reaches_its_top_surface(int map, int targetY)
    {
        var stage = Stage.ById((byte)map)!;
        var boxes = Boxes(stage);
        int pad = PadIndex(boxes);
        // From a normal jump on the floor next to the pad it is out of reach.
        Assert.True(JumpVelocity * JumpVelocity / Gravity / 2 < targetY);
        var reached = new List<string>();
        for (uint t0 = 0; t0 <= 280 && reached.Count == 0; t0 += 20)
            foreach (int dir in new[] { -1, 1 })
                for (int from = 0; from <= 20; from += 2)
                    for (int len = 2; len <= 24; len += 2)
                    {
                        var p = Falling(boxes[pad].X, boxes[pad].Top + 100);
                        bool launched = false;
                        for (int i = 0; i < 80; i++)
                        {
                            InputButtons b = launched && i >= from && i < from + len ? (dir < 0 ? InputButtons.Left : InputButtons.Right) : InputButtons.None;
                            PlayerPhysics.Step(ref p, b, stage.AtRoundTick(t0 + (uint)i), boxes, out var c);
                            if (c.LaunchBox >= 0) { launched = true; from += i; }
                            if (launched && p.Grounded) break;
                        }
                        if (p.Grounded && p.Y == targetY) reached.Add($"t0 {t0} dir {dir} from {from} len {len}");
                    }
        _out.WriteLine(reached.Count > 0 ? reached[0] : "none");
        Assert.NotEmpty(reached);
    }

    // ---------------------------------------------------------------- safety (docs/work/jump-pad.md 3)

    private static bool LaneHits(in PlayerState p, Stage stage, int laneY) =>
        HitShapes.SideHazardSeparation(p, stage.SideHazard, 1, laneY, p.X - 6000, p.X + 6000) < 0 ||
        HitShapes.SideHazardSeparation(p, stage.SideHazard, -1, laneY, p.X + 6000, p.X - 6000) < 0;

    [Fact]
    public void Standing_or_crouching_on_the_pad_anywhere_meets_a_floor_lane()
    {
        // The pad only ever moves along the floor, so its top is always 0.5 above the floor's lanes: on it, the floor
        // head lane meets the body standing and crouching (no ducking under it there; step off or jump).
        foreach (var stage in Stage.All)
        {
            int head = SideKnifePhysics.Lanes(stage)[1];
            for (int x = JumpPadWidth / 2; x <= stage.Width - JumpPadWidth / 2; x += 500)
                foreach (bool crouch in new[] { false, true })
                {
                    var p = new PlayerState { X = x, Y = JumpPadHeight, Grounded = true, Crouching = crouch, Facing = 1 };
                    Assert.True(LaneHits(p, stage, head), $"{stage.Name} x {x} crouch {crouch}");
                }
        }
    }

    /// <summary>The map with only its floor (no platforms, no event), the same hazards, and its jump pad or none.</summary>
    private static Stage FloorOnly(Stage map, bool pad) =>
        new Stage(map.Id, map.Name, map.Width, map.ViewHeight, map.KnifeSpawnY, new Platform[0],
            map.SpawnPoints.ToArray(), pad ? new[] { PushableSpawn.JumpPad(12000, map.Pushables.First(b => b.IsJumpPad).Launch) } : null,
            hazard: map.Hazard, schedule: map.Schedule, sideHazard: map.SideHazard);

    /// <summary>Jump on every landing and steer back over x 12.0: the most a player can stay up on the pad.</summary>
    private static InputButtons Masher(in PlayerState p)
    {
        InputButtons b = p.Grounded && (p.PrevButtons & InputButtons.Jump) == 0 ? InputButtons.Jump : InputButtons.None;
        if (p.X < 11800) b |= InputButtons.Right;
        else if (p.X > 12200) b |= InputButtons.Left;
        return b;
    }

    /// <summary>
    /// Mash Jump for 60 s over x 12.0 (on the pad, or on the bare floor when pad is false) on the floor-only copy of
    /// the map: (launches, airborne ticks, ticks with the whole body above the floor's head lane, longest such stretch).
    /// </summary>
    private static (int launches, int air, int above, int longest) Mash(Stage map, bool pad)
    {
        var stage = FloorOnly(map, pad);
        var boxes = Boxes(stage);
        int laneTop = SideKnifePhysics.Lanes(stage)[1] + SideHazardSpec.Of(stage.SideHazard).HalfThickness;
        var p = pad ? Falling(12000, 700) : PlayerState.SpawnAt(12000, 0, 1);
        int launches = 0, above = 0, stretch = 0, longest = 0, air = 0;
        for (int i = 0; i < 60 * TickRate; i++)
        {
            PlayerPhysics.Step(ref p, Masher(p), stage, boxes, out var c);
            if (c.LaunchBox >= 0) launches++;
            if (!p.Grounded) air++;
            if (p.Y > laneTop) { above++; stretch++; longest = Math.Max(longest, stretch); }
            else stretch = 0;
        }
        return (launches, air, above, longest);
    }

    [Fact]
    public void Bouncing_on_the_pad_keeps_nobody_up_much_longer_than_hopping_on_the_floor()
    {
        // Side hazards are dodged by jumping over the low lane and ducking (or jumping) the head lane anyway; what
        // matters is that the pad gives no new way to stay out of their reach. Compare the best the pad allows
        // (jump again on every landing) with hopping on the bare floor.
        const int ticks = 60 * TickRate;
        foreach (var stage in Stage.All)
        {
            var onPad = Mash(stage, true);
            var floor = Mash(stage, false);
            _out.WriteLine($"{stage.Name}: pad {onPad.launches} launches, airborne {onPad.air * 100 / ticks}%, above the floor lanes " +
                           $"{onPad.above * 100 / ticks}% (longest {onPad.longest} ticks) | floor hops: airborne {floor.air * 100 / ticks}%, " +
                           $"above {floor.above * 100 / ticks}% (longest {floor.longest} ticks)");
            Assert.True(onPad.launches <= ticks / JumpPadCooldownTicks + 1);
            // One launch keeps the body above the lanes for at most 1.5 s: a hazard warned (1 s) while the player is
            // up is still crossing the map when they come down (47 ~ 88 ticks).
            Assert.True(onPad.longest <= 30, $"{stage.Name}: {onPad.longest}");
        }
    }

    [Fact]
    public void Where_nothing_is_above_it_a_falling_hazard_hits_the_player_on_the_pad()
    {
        // The pad is no roof: hazards stop on its top. Under a platform the platform is the roof, and there the floor
        // head lane still meets the body on the pad (the test above), so the pad top is never a safe spot.
        foreach (var stage in Stage.All)
        {
            int open = 0;
            for (int x = JumpPadWidth / 2; x <= stage.Width - JumpPadWidth / 2; x += 500)
            {
                var boxes = Boxes(stage);
                int pad = PadIndex(boxes);
                boxes[pad].X = x;
                var only = new[] { boxes[pad] };
                int landing = PushablePhysics.HazardLandingY(stage, only, stage.Hazard, x, stage.KnifeSpawnY);
                if (landing != JumpPadHeight) continue;
                open++;
                var onPad = new PlayerState { X = x, Y = JumpPadHeight, Grounded = true, Facing = 1 };
                var ducking = onPad;
                ducking.Crouching = true;
                Assert.True(HitShapes.Hits(onPad, stage.Hazard, x, stage.KnifeSpawnY, landing), $"{stage.Name} x {x}");
                Assert.True(HitShapes.Hits(ducking, stage.Hazard, x, stage.KnifeSpawnY, landing), $"{stage.Name} x {x} crouching");
            }
            Assert.True(open > 0, stage.Name);
        }
    }

    [Fact]
    public void Bouncing_on_the_pad_does_not_escape_a_hazard_falling_on_it()
    {
        // Whatever moment of the bounce loop a hazard is warned at the pad, staying on the loop gets hit: the hazard
        // sweeps the whole column, and the pad (no roof) does not stop it above the player.
        foreach (var map in Stage.All)
        {
            var stage = FloorOnly(map, true);
            var boxes = Boxes(stage);
            int hits = 0, phases = 0;
            for (int phase = 0; phase < 50; phase++)
            {
                var p = Falling(12000, 700);
                for (int i = 0; i < 60 + phase; i++) PlayerPhysics.Step(ref p, Masher(p), stage, boxes, out _);
                var k = KnifeState.Warn(1, 12000, stage);
                k.Kind = stage.Hazard;
                k.Timer = (ushort)HazardSpec.Of(stage.Hazard).WarningTicks;
                bool hit = false;
                for (int i = 0; i < 200 && !hit; i++)
                {
                    PlayerPhysics.Step(ref p, Masher(p), stage, boxes, out _);
                    int fromY = k.Y;
                    if (!KnifePhysics.Step(ref k, stage, boxes, out bool dangerous, out _)) break;
                    if (dangerous && HitShapes.Hits(p, k.Kind, k.X, fromY, k.Y)) hit = true;
                }
                phases++;
                if (hit) hits++;
            }
            _out.WriteLine($"{map.Name}: hit in {hits} of {phases} loop phases");
            Assert.Equal(phases, hits);
        }
    }

    // ---------------------------------------------------------------- prediction

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void Prediction_matches_the_server_through_pad_launches_and_pushes(int map)
    {
        var stage = Stage.ById((byte)map)!;
        var room = PlayingRoom(stage, 1, out var ids);
        var p = room.GetPlayer(ids[0]);
        while (room.RoundTick < 100) room.Tick();
        p.State = PlayerState.SpawnAt(9000, 0, 1);
        var predicted = p.State;
        var rng = new Random(map);
        var choices = new[] { InputButtons.Right, InputButtons.Right | InputButtons.Jump, InputButtons.Left, InputButtons.None,
            InputButtons.Right | InputButtons.Dash, InputButtons.Down, InputButtons.Left | InputButtons.Jump, InputButtons.Right };
        uint seq = 0;
        int launches = 0, pushTicks = 0;
        int padStart = room.Pushables[PadIndex(room.Pushables)].X;
        for (int i = 0; i < 400 && p.Alive; i++)   // ends at round tick ~500, before the first map event
        {
            // First walk into the pad (a push), then mostly ride it like a bot, with random input in between.
            var now = room.Pushables.ToArray();
            InputButtons b = i < 15 ? InputButtons.Right
                : i % 100 < 60 ? DodgeBrain.SeekPad(predicted, now, stage.AtRoundTick(room.RoundTick + 1)) : choices[rng.Next(choices.Length)];
            if ((b & predicted.PrevButtons & InputButtons.Jump) != 0) b &= ~InputButtons.Jump;
            int padX = now[PadIndex(now)].X;
            if (Math.Abs(predicted.X - padX) > 3000) b = (b & ~(InputButtons.Left | InputButtons.Right)) | (predicted.X < padX ? InputButtons.Right : InputButtons.Left);
            var boxes = room.Pushables.ToArray();
            room.QueueInput(ids[0], ++seq, b);
            room.Tick();
            PlayerPhysics.Step(ref predicted, b, stage.AtRoundTick(room.RoundTick), boxes, out var c);
            if (c.LaunchBox >= 0) launches++;
            if (c.PushBox >= 0 && boxes[c.PushBox].IsJumpPad) pushTicks++;
            var s = p.State;
            Assert.True((s.X, s.Y, s.Vx, s.Vy, s.Grounded, s.PadCooldown) ==
                        (predicted.X, predicted.Y, predicted.Vx, predicted.Vy, predicted.Grounded, predicted.PadCooldown),
                $"tick {room.RoundTick}: server {s} vs predicted {predicted}");
        }
        int padEnd = room.Pushables[PadIndex(room.Pushables)].X;
        _out.WriteLine($"{stage.Name}: {launches} launches, {pushTicks} push ticks, pad {padStart} -> {padEnd}");
        Assert.True(launches >= 2, $"{launches} launches");
        Assert.True(pushTicks >= 1 && padEnd != padStart, "the pad was pushed");
    }

    // ---------------------------------------------------------------- bots

    [Fact]
    public void A_bot_seeking_the_pad_walks_jumps_and_lands_on_it()
    {
        foreach (var stage in Stage.All)
        {
            var boxes = Boxes(stage);
            int pad = PadIndex(boxes);
            foreach (int startX in new[] { boxes[pad].X - 5000, boxes[pad].X + 4000 })
            {
                var p = PlayerState.SpawnAt(startX, 0, 1);
                bool launched = false;
                for (int i = 0; i < 100 && !launched; i++)
                {
                    InputButtons b = DodgeBrain.SeekPad(p, boxes, stage);
                    if ((b & p.PrevButtons & InputButtons.Jump) != 0) b &= ~InputButtons.Jump; // Bot releases a held jump
                    PlayerPhysics.Step(ref p, b, stage, boxes, out var c);
                    launched = c.LaunchBox >= 0;
                }
                Assert.True(launched, $"{stage.Name} from {startX}");
                Assert.Equal(InputButtons.None, DodgeBrain.SeekPad(p, boxes)); // ride over (cooldown)
            }
        }
    }

    [Fact]
    public void A_fleeing_bot_hops_over_the_pad_in_its_way()
    {
        var boxes = Boxes(PadArena);
        // Standing just left of the pad, a knife about to fall on it: it flees right, over the pad.
        var me = PlayerState.SpawnAt(12000 - 600 - 400 - 300, 0, 1);
        var knife = new KnifeState { Id = 1, Kind = HazardKind.Knife, Phase = KnifePhase.Falling, X = me.X - 600, Y = 4000 };
        InputButtons b = DodgeBrain.Decide(me, new[] { knife }, PadArena, 0, InputButtons.None, boxes);
        Assert.True((b & InputButtons.Jump) != 0, b.ToString());
        Assert.True(DodgeBrain.BoxAhead(me, 1, boxes));
        Assert.False(DodgeBrain.BoxAhead(me, -1, boxes));
    }
}
