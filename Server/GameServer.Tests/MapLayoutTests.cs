using OnlineSeverGame.Shared.Protocol;
using OnlineSeverGame.Shared.Simulation;
using static OnlineSeverGame.Shared.Simulation.GameConstants;

namespace OnlineSeverGame.Server.Tests;

/// <summary>
/// The new map layouts and mid-round events (docs/work/map-layout.md): moving / vanishing platforms, riding them,
/// the four events, and the numeric checks of chapter 3 (room for eight, no spot safe from both falling and side
/// hazards, every lane survivable from every surface, every platform reachable from the floor and back).
/// </summary>
public class MapLayoutTests
{
    private static readonly uint[] SampleTicks = Enumerable.Range(0, 361).Select(i => (uint)(i * 10)).ToArray();

    // ---------------------------------------------------------------- moving and vanishing platforms

    [Fact]
    public void Crane_pallet_shuttles_between_its_ends_and_pauses_at_each()
    {
        var w = Stage.Warehouse;
        Platform At(uint t) => w.AtRoundTick(t).Platforms[Stage.WarehousePalletIndex];
        Assert.Equal((6500, 10500, 8400), (At(0).Left, At(0).Right, At(0).Top));
        Assert.Equal(6500, At(40).Left);   // waits 2 s
        Assert.Equal(6570, At(41).Left);
        Assert.Equal(70, At(41).Dx);
        Assert.Equal(13500, At(140).Left); // 7 units in 5 s
        Assert.Equal(13500, At(180).Left);
        Assert.Equal(0, At(180).Dx);
        Assert.Equal(9300, At(240).Left);  // on the way back
        Assert.Equal(-70, At(240).Dx);
        Assert.Equal(6500, At(280).Left);  // cycle 14 s
        Assert.All(SampleTicks, t => Assert.Equal(8400, At(t).Top));
    }

    [Fact]
    public void Cake_lift_goes_up_to_the_wafer_and_back_down()
    {
        var d = Stage.Dessert;
        Platform At(uint t) => d.AtRoundTick(t).Platforms[Stage.DessertLiftIndex];
        Assert.Equal((17000, 19500, 1200), (At(0).Left, At(0).Right, At(0).Top));
        Assert.Equal(1200, At(40).Top);
        Assert.Equal(60, At(100).Dy);
        Assert.Equal(7200, At(140).Top);   // level with the wafer shelf
        Assert.Equal(d.Platforms[6].Top, At(160).Top);
        Assert.Equal(-60, At(200).Dy);
        Assert.Equal(1200, At(280).Top);
        Assert.All(SampleTicks, t => Assert.Equal(17000, At(t).Left));
    }

    [Fact]
    public void Snow_awnings_collapse_in_turn_and_are_never_both_gone()
    {
        var w = Stage.Winter;
        bool Gone(uint t, int i) => w.AtRoundTick(t).Platforms[i].Gone;
        for (uint t = 0; t <= RoundTimeLimitTicks; t++)
            Assert.False(Gone(t, 2) && Gone(t, 5), $"both awnings gone at {t}");
        Assert.False(Gone(199, 2));
        Assert.True(Gone(200, 2));
        Assert.True(Gone(279, 2));
        Assert.False(Gone(280, 2));
        Assert.True(Gone(440, 2));
        Assert.True(Gone(320, 5));
        Assert.False(Gone(400, 5));
        Assert.True(w.AtRoundTick(160).Platforms[2].Crumbling);
        Assert.False(w.AtRoundTick(159).Platforms[2].Crumbling);
        Assert.False(w.AtRoundTick(200).Platforms[2].Crumbling);
        // While it is gone an icicle falls straight through to the (icy) floor.
        var gone = w.AtRoundTick(220);
        Assert.Equal(0, gone.HazardLandingY(HazardKind.Icicle, 12500, w.KnifeSpawnY));
        Assert.Equal(7200, w.AtRoundTick(100).HazardLandingY(HazardKind.Icicle, 12500, w.KnifeSpawnY));
    }

    [Fact]
    public void Riders_move_with_the_pallet_and_the_lift()
    {
        var w = Stage.Warehouse;
        var s = PlayerState.SpawnAt(8500, 8400, 1);
        for (uint t = 1; t <= 140; t++) PlayerPhysics.Step(ref s, InputButtons.None, w.AtRoundTick(t));
        Assert.Equal((15500, 8400, true), (s.X, s.Y, s.Grounded));

        var d = Stage.Dessert;
        var r = PlayerState.SpawnAt(18250, 1200, -1);
        for (uint t = 1; t <= 140; t++)
        {
            PlayerPhysics.Step(ref r, InputButtons.None, d.AtRoundTick(t));
            Assert.True(r.Grounded, $"left the lift at {t}");
        }
        Assert.Equal(7200, r.Y);
        for (uint t = 141; t <= 280; t++) PlayerPhysics.Step(ref r, InputButtons.None, d.AtRoundTick(t));
        Assert.Equal((1200, true), (r.Y, r.Grounded));

        // A box on the pallet rides along too.
        var boxes = new[] { new PushableState { Id = 1, X = 8500, Y = 8400, Width = 1400, Height = 1200, Grounded = true } };
        for (uint t = 1; t <= 140; t++) PushablePhysics.Step(boxes, 1, w.AtRoundTick(t), null, null, 0, null);
        Assert.Equal((15500, 8400), (boxes[0].X, boxes[0].Y));
    }

    [Fact]
    public void Down_plus_jump_drops_off_a_moving_pallet_and_walking_off_its_end_falls()
    {
        var w = Stage.Warehouse;
        var s = PlayerState.SpawnAt(10000, 8400, 1);
        for (uint t = 1; t <= 60; t++) PlayerPhysics.Step(ref s, InputButtons.Down, w.AtRoundTick(t));
        Assert.Equal(8400, s.Y);
        PlayerPhysics.Step(ref s, InputButtons.Down | InputButtons.Jump, w.AtRoundTick(61));
        PlayerPhysics.Step(ref s, InputButtons.None, w.AtRoundTick(62));
        Assert.True(s.Y < 8400 && !s.Grounded);

        var e = PlayerState.SpawnAt(17000, 8400, 1);
        uint tick = 140;
        for (int i = 0; i < 40; i++) PlayerPhysics.Step(ref e, InputButtons.Right, w.AtRoundTick(++tick));
        Assert.True(e.Y < 8400);
    }

    [Fact]
    public void A_player_on_a_vanishing_awning_falls_when_it_goes()
    {
        var w = Stage.Winter;
        var s = PlayerState.SpawnAt(12500, 7200, 1);
        for (uint t = 150; t < 200; t++) PlayerPhysics.Step(ref s, InputButtons.None, w.AtRoundTick(t));
        Assert.Equal((7200, true), (s.Y, s.Grounded)); // still there while it shakes
        for (uint t = 200; t < 240; t++) PlayerPhysics.Step(ref s, InputButtons.None, w.AtRoundTick(t));
        Assert.Equal((0, true), (s.Y, s.Grounded));
    }

    // ---------------------------------------------------------------- events

    [Fact]
    public void Each_map_has_its_event_warning_two_seconds_ahead_every_forty_seconds()
    {
        var kinds = new[] { MapEventKind.Fire, MapEventKind.Wind, MapEventKind.Conveyor, MapEventKind.Syrup };
        for (int m = 0; m < 4; m++)
        {
            Stage stage = Stage.All[m];
            MapEventSpec spec = stage.EventSpec!;
            Assert.Equal(kinds[m], spec.Kind);
            MapEventState At(uint t) => stage.AtRoundTick(t).Event;
            Assert.Equal(MapEventPhase.None, stage.Event.Phase);
            Assert.Equal(MapEventPhase.None, At(559).Phase);
            Assert.Equal((MapEventPhase.Warning, 40, 0), (At(560).Phase, At(560).TicksLeft, At(560).Index));
            Assert.Equal(MapEventPhase.Warning, At(599).Phase);
            Assert.Equal((MapEventPhase.Active, (sbyte)1), (At(600).Phase, At(600).Dir));
            Assert.Equal(MapEventPhase.Active, At((uint)(599 + spec.ActiveTicks)).Phase);
            Assert.Equal(MapEventPhase.None, At((uint)(600 + spec.ActiveTicks)).Phase);
            Assert.Equal((MapEventPhase.Warning, 1), (At(1360).Phase, At(1360).Index));
            Assert.Equal((MapEventPhase.Active, (sbyte)-1), (At(1400).Phase, At(1400).Dir));
            Assert.Equal(MapEventPhase.Active, At(2200).Phase);
            // Never during countdown, waiting or results (round tick 0).
            Assert.Equal(MapEventPhase.None, stage.AtRoundTick(0).Event.Phase);
        }
        Assert.Equal(new[] { 160, 200, 240, 240 }, Stage.All.Select(s => s.EventSpec!.ActiveTicks).ToArray());
        // Fire lights burners 1 and 3, then 2 and 4.
        var k = Stage.Kitchen;
        Assert.Equal(new[] { 10000, 17000 }, k.AtRoundTick(600).Event.Spans.Select(s => s.Left).ToArray());
        Assert.Equal(new[] { 13500, 20500 }, k.AtRoundTick(1400).Event.Spans.Select(s => s.Left).ToArray());
        Assert.All(k.AtRoundTick(600).Event.Spans.Concat(k.AtRoundTick(1400).Event.Spans),
            sp => Assert.Contains(Stage.KitchenBurners, b => b.Left == sp.Left && b.Right == sp.Right));
        // Syrup covers the left half, then the right half.
        Assert.Equal(500, Stage.Dessert.AtRoundTick(600).Event.Spans[0].Left);
        Assert.Equal(12500, Stage.Dessert.AtRoundTick(1400).Event.Spans[0].Left);
    }

    [Fact]
    public void Wind_pushes_everyone_and_half_as_much_crouching()
    {
        var w = Stage.Winter;
        PlayerState Run(int x, InputButtons b, uint from, int ticks)
        {
            var s = PlayerState.SpawnAt(x, 0, 1);
            for (uint t = from; t < from + ticks; t++) PlayerPhysics.Step(ref s, b, w.AtRoundTick(t));
            return s;
        }
        Assert.Equal(20000, Run(20000, InputButtons.None, 590, 10).X);          // warning only
        Assert.Equal(21200, Run(20000, InputButtons.None, 600, 10).X);          // 120 per tick to the right
        Assert.Equal(20600, Run(20000, InputButtons.Down, 600, 10).X);          // crouching: 60
        Assert.Equal(20000 - 2800, Run(20000, InputButtons.Left, 600, 10).X);   // against it: 280 per tick
        Assert.Equal(20000 - 1200, Run(20000, InputButtons.None, 1400, 10).X);  // the second gust blows left
        Assert.Equal(23600, Run(23000, InputButtons.None, 600, 20).X);          // stops at the wall
        // In the air too.
        var j = PlayerState.SpawnAt(20000, 0, 1);
        PlayerPhysics.Step(ref j, InputButtons.Jump, w.AtRoundTick(600));
        Assert.Equal(20120, j.X);
        // Wind on the other maps does nothing.
        var k = PlayerState.SpawnAt(6000, 0, 1);
        for (uint t = 600; t < 610; t++) PlayerPhysics.Step(ref k, InputButtons.None, Stage.Kitchen.AtRoundTick(t));
        Assert.Equal(6000, k.X);
    }

    [Fact]
    public void Conveyor_carries_players_and_boxes_on_the_belt_only()
    {
        var w = Stage.Warehouse;
        var on = PlayerState.SpawnAt(11000, 0, 1);
        var off = PlayerState.SpawnAt(3000, 0, 1);
        for (uint t = 600; t < 610; t++)
        {
            PlayerPhysics.Step(ref on, InputButtons.None, w.AtRoundTick(t));
            PlayerPhysics.Step(ref off, InputButtons.None, w.AtRoundTick(t));
        }
        Assert.Equal(11000 + 1500, on.X);
        Assert.Equal(3000, off.X);

        // Jumping off the belt: no carry while in the air.
        var j = PlayerState.SpawnAt(11000, 0, 1);
        PlayerPhysics.Step(ref j, InputButtons.Jump, w.AtRoundTick(600)); // grounded at the start: carried once
        int x1 = j.X;
        PlayerPhysics.Step(ref j, InputButtons.None, w.AtRoundTick(601));
        Assert.Equal(x1, j.X);

        var boxes = new[] { new PushableState { Id = 1, X = 12000, Y = 0, Width = 1400, Height = 1200, Grounded = true } };
        for (uint t = 1400; t < 1410; t++) PushablePhysics.Step(boxes, 1, w.AtRoundTick(t), null, null, 0, null);
        Assert.Equal(12000 - 1500, boxes[0].X); // second run goes left
    }

    [Fact]
    public void Syrup_halves_walking_but_not_jumping_or_dashing()
    {
        var d = Stage.Dessert;
        PlayerState Step1(int x, InputButtons b, uint t)
        {
            var s = PlayerState.SpawnAt(x, 0, 1);
            PlayerPhysics.Step(ref s, b, d.AtRoundTick(t));
            return s;
        }
        Assert.Equal(200, Step1(5000, InputButtons.Right, 600).Vx);
        Assert.Equal(400, Step1(15000, InputButtons.Right, 600).Vx);   // the other half is dry
        Assert.Equal(400, Step1(5000, InputButtons.Right, 590).Vx);    // warning: still dry
        Assert.Equal(80, Step1(5000, InputButtons.Right | InputButtons.Down, 600).Vx);
        Assert.Equal(JumpVelocity - Gravity, Step1(5000, InputButtons.Jump, 600).Vy);
        Assert.Equal(DashSpeed, Step1(5000, InputButtons.Dash, 600).Vx);
        Assert.Equal(200, Step1(15000, InputButtons.Right, 1400).Vx);  // second run: right half
        Assert.Equal(400, Step1(5000, InputButtons.Right, 1400).Vx);
    }

    [Fact]
    public void Fire_eliminates_on_lit_burners_only_and_counts_as_a_hazard_death()
    {
        var room = new GameRoom(new RoomConfig
        {
            MinPlayers = 2, AutoSpawnKnives = false, AutoSpawnSideKnives = false, Seed = 1, Stage = Stage.Kitchen,
        }, _ => { });
        byte a = room.AddPlayer("a"), b = room.AddPlayer("b"), c = room.AddPlayer("c");
        room.Tick();
        for (int i = 0; i < CountdownTicks; i++) room.Tick();
        Assert.Equal(RoomState.Playing, room.State);
        room.GetPlayer(a).State = PlayerState.SpawnAt(11500, 0, 1);   // burner 1 (lit first)
        room.GetPlayer(b).State = PlayerState.SpawnAt(15000, 0, 1);   // burner 2 (not lit the first time)
        room.GetPlayer(c).State = PlayerState.SpawnAt(14000, 2400, 1); // tray shelf over the stove
        while (room.RoundTick < 599) room.Tick();
        Assert.True(room.GetPlayer(a).Alive); // the warning is harmless
        room.Tick();
        Assert.False(room.GetPlayer(a).Alive);
        Assert.True(room.GetPlayer(b).Alive);
        Assert.True(room.GetPlayer(c).Alive);

        room.GetPlayer(c).State = PlayerState.SpawnAt(18500, 0, 1);   // steps onto burner 3
        room.Outbox.Clear();
        room.Tick();
        Assert.Equal(RoomState.Ended, room.State);
        var end = room.LastRoundEnd!;
        Assert.Equal(new[] { b }, end.Winners.ToArray());
        Assert.Equal(EliminationCause.MapEvent, end.Results.Single(r => r.PlayerId == a).Cause);
        Assert.Equal(EliminationCause.MapEvent, end.Results.Single(r => r.PlayerId == c).Cause);
    }

    [Fact]
    public void Fire_spares_a_jumper_above_the_flames_but_not_a_crouching_one()
    {
        var k = Stage.Kitchen.AtRoundTick(600);
        var above = new PlayerState { X = 11500, Y = 900, Facing = 1 };
        Assert.False(MapEventPhysics.FireHits(k, above));
        var low = new PlayerState { X = 11500, Y = 700, Facing = 1 };
        Assert.True(MapEventPhysics.FireHits(k, low));
        var crouch = new PlayerState { X = 11500, Y = 0, Facing = 1, Grounded = true, Crouching = true };
        Assert.True(MapEventPhysics.FireHits(k, crouch));
        // The drawn body's edge decides: half a body width off the burner is safe.
        Assert.False(MapEventPhysics.FireHits(k, PlayerState.SpawnAt(13000 + HitShapes.BodyHalfWidth, 0, 1)));
        Assert.True(MapEventPhysics.FireHits(k, PlayerState.SpawnAt(13000 + HitShapes.BodyHalfWidth - 20, 0, 1)));
        Assert.False(MapEventPhysics.FireHits(Stage.Kitchen.AtRoundTick(590), low)); // warning
    }

    [Theory]
    [InlineData(2, 21000, -1)]    // winter: walking about in the wind
    [InlineData(3, 6200, -1)]    // warehouse: standing and crouching on the conveyor (walking would meet a box the belt shoves: server only)
    [InlineData(3, 0, Stage.WarehousePalletIndex)]  // warehouse: riding the crane pallet
    [InlineData(4, 0, Stage.DessertLiftIndex)]      // dessert: riding the lift, then syrup
    public void Prediction_matches_the_server_through_events_and_moving_platforms(int map, int x, int platform)
    {
        var stage = Stage.ById((byte)map)!;
        var room = new GameRoom(new RoomConfig
        {
            MinPlayers = 1, AutoSpawnKnives = false, AutoSpawnSideKnives = false, Seed = 1, Stage = stage,
        }, _ => { });
        byte id = room.AddPlayer("p");
        room.Tick();
        for (int i = 0; i < CountdownTicks; i++) room.Tick();
        var p = room.GetPlayer(id);
        while (room.RoundTick < 520) room.Tick();
        int y = 0;
        if (platform >= 0)
        {
            var on = stage.AtRoundTick(room.RoundTick).Platforms[platform];
            x = on.CenterX;
            y = on.Top;
        }
        p.State = PlayerState.SpawnAt(x, y, 1);
        var predicted = p.State;
        var rng = new Random(map * 100 + platform);
        var choices = new[] { InputButtons.None, InputButtons.Left, InputButtons.Right, InputButtons.Down,
            InputButtons.Right | InputButtons.Jump, InputButtons.Left | InputButtons.Dash, InputButtons.None };
        uint seq = 0;
        for (int i = 0; i < 400 && p.Alive; i++)
        {
            InputButtons b = i < 100 ? InputButtons.None : choices[rng.Next(choices.Length)];
            if (map == 3 && platform < 0)
            {
                b &= InputButtons.Down;
                if (predicted.X > 17000) break; // near the belt's end the box behind catches up and shoves (server only)
            }
            var boxes = room.Pushables.ToArray();
            room.QueueInput(id, ++seq, b);
            room.Tick();
            PlayerPhysics.Step(ref predicted, b, stage.AtRoundTick(room.RoundTick), boxes, out _);
            var s = p.State;
            Assert.True((s.X, s.Y, s.Vx, s.Vy, s.Grounded) == (predicted.X, predicted.Y, predicted.Vx, predicted.Vy, predicted.Grounded),
                $"tick {room.RoundTick}: server ({s.X},{s.Y}) v({s.Vx},{s.Vy}) vs predicted ({predicted.X},{predicted.Y}) v({predicted.Vx},{predicted.Vy})");
        }
    }

    // ---------------------------------------------------------------- chapter 3: numeric checks

    [Fact]
    public void Eight_players_have_room_at_every_tick()
    {
        foreach (var stage in Stage.All)
            foreach (uint t in SampleTicks)
            {
                var s = stage.AtRoundTick(t);
                long width = s.Width;
                if (s.Event.IsActive && s.Event.Kind == MapEventKind.Fire)
                    foreach (var f in s.Event.Spans) width -= f.Right - f.Left + 2 * HitShapes.BodyHalfWidth;
                foreach (var p in s.Platforms)
                    if (!p.Gone) width += p.Width;
                Assert.True(width >= 8 * 2 * HitShapes.BodyHalfWidth, $"{stage.Name} at {t}: {width}");
            }
    }

    /// <summary>A place someone can stand: surface height and the x range of its top.</summary>
    private readonly record struct Surface(int Y, int Left, int Right, string What);

    private static List<Surface> StandingSurfaces(Stage s, IReadOnlyList<PushableState> boxes)
    {
        var list = new List<Surface> { new Surface(0, 0, s.Width, "floor") };
        for (int i = 0; i < s.Platforms.Count; i++)
            if (!s.Platforms[i].Gone)
                list.Add(new Surface(s.Platforms[i].Top, s.Platforms[i].Left, s.Platforms[i].Right, "platform " + i));
        foreach (var b in boxes) list.Add(new Surface(b.Top, b.Left, b.Right, "box " + b.Id));
        if (s.Hazard == HazardKind.Crate)
        {
            // Landed crates, one or two high, wherever a crate can come to rest.
            int n = list.Count;
            for (int i = 0; i < n; i++)
            {
                list.Add(list[i] with { Y = list[i].Y + 1200, What = list[i].What + " + crate" });
                list.Add(list[i] with { Y = list[i].Y + 2400, What = list[i].What + " + 2 crates" });
            }
        }
        return list;
    }

    private static IReadOnlyList<PushableState> StartBoxes(Stage s) =>
        s.Pushables.Select((p, i) => PushableState.Spawn((ushort)(i + 1), p)).ToArray();

    [Fact]
    public void No_spot_is_safe_from_both_falling_and_side_hazards()
    {
        var problems = new List<string>();
        foreach (var stage in Stage.All)
        {
            var boxes = StartBoxes(stage);
            HazardKind kind = stage.Hazard;
            int[] lanes = SideKnifePhysics.Lanes(stage);
            int half = HazardSpec.Of(kind).HalfWidth;
            int[] hazardXs = stage.HazardSlots.Count > 0
                ? stage.HazardSlots.ToArray()
                : Enumerable.Range(0, (stage.Width - 2 * half) / 50 + 1).Select(i => half + i * 50).ToArray();
            foreach (uint t in SampleTicks)
            {
                Stage s = stage.AtRoundTick(t);
                int[] landing = hazardXs.Select(hx => PushablePhysics.HazardLandingY(s, boxes, kind, hx, s.KnifeSpawnY)).ToArray();
                foreach (var surf in StandingSurfaces(s, boxes))
                {
                    for (int x = Math.Max(PlayerHalfWidth, surf.Left - PlayerHalfWidth + 50);
                         x <= Math.Min(s.Width - PlayerHalfWidth, surf.Right + PlayerHalfWidth - 50); x += 200)
                    {
                        foreach (bool crouch in new[] { false, true })
                        {
                            var p = new PlayerState { X = x, Y = surf.Y, Grounded = true, Facing = 1, Crouching = crouch };
                            bool side = false;
                            foreach (int lane in lanes)
                                if (HitShapes.SideHazardSeparation(p, stage.SideHazard, 1, lane, -3000, s.Width + 3000) < 0)
                                {
                                    side = true;
                                    break;
                                }
                            if (side) continue;
                            bool fall = false;
                            for (int h = 0; h < hazardXs.Length && !fall; h++)
                            {
                                if (Math.Abs(hazardXs[h] - x) > 1600) continue;
                                if (HitShapes.Separation(p, kind, hazardXs[h], s.KnifeSpawnY, landing[h]) < 0) fall = true;
                            }
                            if (!fall && problems.Count < 20)
                                problems.Add($"{stage.Name} t={t} {surf.What} y={surf.Y} x={x}{(crouch ? " crouching" : "")}");
                        }
                    }
                }
            }
        }
        Assert.True(problems.Count == 0, "safe spots:\n" + string.Join("\n", problems));
    }

    [Fact]
    public void Every_lane_can_be_survived_from_every_surface_at_every_tick()
    {
        foreach (var stage in Stage.All)
        {
            var heights = new SortedSet<int>();
            foreach (uint t in SampleTicks)
                foreach (var surf in StandingSurfaces(stage.AtRoundTick(t), StartBoxes(stage)))
                    heights.Add(surf.Y);
            foreach (int h in heights)
                foreach (int lane in SideKnifePhysics.Lanes(stage))
                    foreach (sbyte dir in new sbyte[] { 1, -1 })
                        Assert.True(DashStunSideKnifeTests.Judge(h, lane, dir, stage.SideHazard) != DashStunSideKnifeTests.Outcome.Undodgeable,
                            $"{stage.Name}: {stage.SideHazard} lane {lane} from surface {h} dir {dir}");
        }
    }

    // Reachability: a frozen copy of the map at one tick, then real PlayerPhysics jumps and walks between surfaces.

    private static Stage Frozen(Stage at) => new Stage(at.Id, at.Name, at.Width, at.ViewHeight, at.KnifeSpawnY,
        at.Platforms.Where(p => !p.Gone).Select(p => new Platform(p.Left, p.Right, p.Top, p.Ice)).ToArray(),
        at.SpawnPoints.ToArray(), floorIce: at.FloorIce.ToArray());

    /// <summary>Surface key: (top, left, right); the floor is (0, 0, width).</summary>
    private static (int, int, int) SurfaceUnder(Stage s, in PlayerState p)
    {
        if (p.Y == 0) return (0, 0, s.Width);
        foreach (var pl in s.Platforms)
            if (pl.Top == p.Y && p.X - PlayerHalfWidth < pl.Right && pl.Left < p.X + PlayerHalfWidth) return (pl.Top, pl.Left, pl.Right);
        return (-1, -1, -1);
    }

    private static HashSet<(int, int, int)> Reachable(Stage s, (int, int, int) from)
    {
        var seen = new HashSet<(int, int, int)> { from };
        var queue = new Queue<(int, int, int)>();
        queue.Enqueue(from);
        while (queue.Count > 0)
        {
            var (y, l, r) = queue.Dequeue();
            for (int x = Math.Max(PlayerHalfWidth, l - PlayerHalfWidth + 50); x <= Math.Min(s.Width - PlayerHalfWidth, r + PlayerHalfWidth - 50); x += 250)
                foreach (int dir in new[] { -1, 0, 1 })
                    foreach (int mode in new[] { 0, 1, 2 }) // 0 walk off, 1 jump, 2 down + jump
                    {
                        if (mode == 2 && y == 0) continue;
                        if (mode == 0 && dir == 0) continue;
                        foreach (int hold in new[] { 0, 2, 4, 6, 8, 10, 12, 14, 16, 20, 30 })
                        {
                            if (hold == 0 && dir != 0) continue;
                            var p = new PlayerState { X = x, Y = y, Grounded = true, Facing = 1 };
                            InputButtons Dir() => dir < 0 ? InputButtons.Left : dir > 0 ? InputButtons.Right : InputButtons.None;
                            bool left = false;
                            for (int tick = 0; tick < 90; tick++)
                            {
                                InputButtons b = tick < Math.Max(hold, 1) ? Dir() : InputButtons.None;
                                if (tick == 0 && mode == 1) b |= InputButtons.Jump;
                                if (tick == 0 && mode == 2) b |= InputButtons.Down | InputButtons.Jump;
                                if (tick == 0 && mode == 2) { p.PrevButtons = InputButtons.Down; }
                                PlayerPhysics.Step(ref p, b, s);
                                if (!p.Grounded) left = true;
                                if (left && p.Grounded && tick >= hold) break;
                            }
                            if (!p.Grounded) continue;
                            var key = SurfaceUnder(s, p);
                            if (key.Item1 >= 0 && seen.Add(key)) queue.Enqueue(key);
                        }
                    }
        }
        return seen;
    }

    private static void AssertAllReachable(Stage at, string when, params int[] expectUnreachable)
    {
        var s = Frozen(at);
        var floor = (0, 0, s.Width);
        var fromFloor = Reachable(s, floor);
        for (int i = 0; i < at.Platforms.Count; i++)
        {
            var p = at.Platforms[i];
            if (p.Gone) continue;
            var key = (p.Top, p.Left, p.Right);
            if (expectUnreachable.Contains(i))
            {
                Assert.DoesNotContain(key, fromFloor);
                continue;
            }
            Assert.True(fromFloor.Contains(key), $"{at.Name} {when}: platform {i} ({p.Left}~{p.Right}, {p.Top}) not reachable from the floor");
            Assert.True(Reachable(s, key).Contains(floor), $"{at.Name} {when}: no way down from platform {i}");
        }
    }

    [Fact]
    public void Every_platform_can_be_reached_from_the_floor_and_left_again()
    {
        AssertAllReachable(Stage.Kitchen, "start");
        AssertAllReachable(Stage.Winter, "start");
        AssertAllReachable(Stage.Winter.AtRoundTick(240), "awning 1 gone");      // stone ledge still reachable on the right
        AssertAllReachable(Stage.Winter.AtRoundTick(360), "awning 2 gone");      // ... and on the left
        AssertAllReachable(Stage.Warehouse, "pallet at the left end");
        AssertAllReachable(Stage.Warehouse.AtRoundTick(160), "pallet at the right end");
        // Dessert: lift down (the wafer is reached from the cherry top), lift up, and everything melted.
        AssertAllReachable(Stage.Dessert, "start");
        AssertAllReachable(Stage.Dessert.AtRoundTick(160), "lift up");
        AssertAllReachable(Stage.Dessert.AtRoundTick(1260), "melted, lift up");
        // With the lift up, it is reached from the cherry top or the wafer.
        var up = Frozen(Stage.Dessert.AtRoundTick(160));
        var lift = Stage.Dessert.AtRoundTick(160).Platforms[Stage.DessertLiftIndex];
        Assert.Contains((lift.Top, lift.Left, lift.Right), Reachable(up, (9600, 14500, 17000)));
    }
}
