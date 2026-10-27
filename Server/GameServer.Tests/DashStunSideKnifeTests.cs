using OnlineSeverGame.BotClient;
using OnlineSeverGame.Shared.Protocol;
using OnlineSeverGame.Shared.Simulation;
using static OnlineSeverGame.Shared.Simulation.GameConstants;

namespace OnlineSeverGame.Server.Tests;

/// <summary>Dash stun, stun immunity and side knives (docs/work/dash-side-knife.md).</summary>
public class DashStunSideKnifeTests
{
    private static readonly bool[] Both = { true, true };

    private static PlayerState Dashing(int x, sbyte facing) =>
        new PlayerState { X = x, Y = 0, Grounded = true, Facing = facing, DashTicks = 2, Vx = facing * DashSpeed };

    private static PlayerState Run(PlayerState s, int ticks, InputButtons buttons = InputButtons.None)
    {
        for (int i = 0; i < ticks; i++) PlayerPhysics.Step(ref s, buttons, Stage.Default);
        return s;
    }

    private static GameRoom PlayingRoom(int players, out byte[] ids, Stage stage = null, bool autoSide = false)
    {
        var room = new GameRoom(new RoomConfig
        {
            MinPlayers = 1, AutoSpawnKnives = false, AutoSpawnSideKnives = autoSide, Seed = 1, Stage = stage ?? Stage.Default,
        });
        ids = Enumerable.Range(0, players).Select(i => room.AddPlayer("P" + i)).ToArray();
        room.Tick();
        for (int i = 0; i < CountdownTicks; i++) room.Tick();
        Assert.Equal(RoomState.Playing, room.State);
        room.Outbox.Clear();
        return room;
    }

    // ------------------------------------------------------------------ dash stun and immunity

    [Fact]
    public void Dash_hit_flattens_for_one_second_then_gives_one_second_of_immunity()
    {
        var states = new[] { Dashing(5000, 1), PlayerState.SpawnAt(5500, 0, -1) };
        var hits = new List<DashHit>();
        DashCollision.Resolve(states, Both, 2, hits);
        Assert.True(Assert.Single(hits).Stunned);
        var t = states[1];
        Assert.Equal((DashStunTicks, true), (t.StunTicks, t.Stomped));

        t = Run(t, DashStunTicks - 1, InputButtons.Right);
        Assert.Equal((1, true, (byte)0), (t.StunTicks, t.Stomped, t.ImmuneTicks));
        t = Run(t, 1, InputButtons.Right);
        Assert.Equal((0, false, (byte)StunImmunityTicks), (t.StunTicks, t.Stomped, t.ImmuneTicks));
        Assert.False(t.CanBeStunned);

        int x = t.X;
        t = Run(t, StunImmunityTicks - 1, InputButtons.Right);
        Assert.Equal(1, t.ImmuneTicks);
        Assert.True(t.X > x, "the player moves freely while immune");
        t = Run(t, 1);
        Assert.Equal(0, t.ImmuneTicks);
        Assert.True(t.CanBeStunned);
    }

    [Fact]
    public void Immune_player_hit_by_a_dash_is_only_pushed()
    {
        var target = PlayerState.SpawnAt(5500, 0, -1);
        target.ImmuneTicks = 12;
        var states = new[] { Dashing(5000, 1), target };
        var hits = new List<DashHit>();
        DashCollision.Resolve(states, Both, 2, hits);
        Assert.False(Assert.Single(hits).Stunned);
        Assert.Equal(KnockbackSpeed, states[1].Vx);
        Assert.Equal(StunTicks, states[1].StunTicks); // the short knockback stagger
        Assert.False(states[1].Stomped);
        Assert.Equal(12, states[1].ImmuneTicks);      // not renewed, not extended

        var t = Run(states[1], StunTicks);
        Assert.Equal((0, (byte)(12 - StunTicks)), (t.StunTicks, t.ImmuneTicks)); // a plain push grants no new immunity
    }

    [Fact]
    public void Dash_on_a_flat_player_pushes_without_extending_or_unflattening()
    {
        var target = new PlayerState { X = 5500, Y = 0, Grounded = true, Facing = -1, StunTicks = 25, Stomped = true };
        var states = new[] { Dashing(5000, 1), target };
        DashCollision.Resolve(states, Both, 2);
        Assert.Equal((25, true), (states[1].StunTicks, states[1].Stomped));
        Assert.Equal(KnockbackSpeed, states[1].Vx);

        target.StunTicks = 3;
        states = new[] { Dashing(5000, 1), target };
        DashCollision.Resolve(states, Both, 2);
        Assert.Equal((StunTicks, true), (states[1].StunTicks, states[1].Stomped)); // long enough for the push
    }

    [Fact]
    public void Crouching_target_flies_half_as_far_but_is_stunned_the_same()
    {
        var target = PlayerState.SpawnAt(5500, 0, -1);
        target.Crouching = true;
        var states = new[] { Dashing(5000, 1), target };
        DashCollision.Resolve(states, Both, 2);
        Assert.Equal(CrouchKnockbackSpeed, states[1].Vx);
        Assert.Equal((DashStunTicks, true), (states[1].StunTicks, states[1].Stomped));
    }

    [Fact]
    public void Head_on_dashers_are_both_flattened()
    {
        var states = new[] { Dashing(5000, 1), Dashing(5600, -1) };
        DashCollision.Resolve(states, Both, 2);
        Assert.True(states[0].Stomped && states[1].Stomped);
    }

    [Fact]
    public void Flying_back_after_a_dash_hit_is_drawn_and_hit_as_knockback_then_flat_on_landing()
    {
        var states = new[] { Dashing(5000, 1), PlayerState.SpawnAt(5500, 0, -1) };
        DashCollision.Resolve(states, Both, 2);
        var t = states[1];
        Assert.Same(HitShapes.Stunned, HitShapes.OfPlayer(t));
        for (int i = 0; i < DashStunTicks && !t.Grounded; i++) t = Run(t, 1);
        Assert.True(t.Grounded && t.StunTicks > 0);
        Assert.Same(HitShapes.Stomped, HitShapes.OfPlayer(t));
    }

    private static (PlayerState[] Before, PlayerState[] After) StompSetup(PlayerState target)
    {
        var before = new[] { new PlayerState { X = 1500, Y = 1300, Vy = -200, Facing = 1 }, target };
        var after = (PlayerState[])before.Clone();
        after[0].Y = 1100;
        return (before, after);
    }

    [Fact]
    public void Immune_player_stomped_is_not_stunned_but_the_stomper_bounces()
    {
        var target = PlayerState.SpawnAt(1500, 0, 1);
        target.ImmuneTicks = 5;
        var (before, after) = StompSetup(target);
        var hits = new List<StompHit>();
        StompCollision.Resolve(before, after, Both, 2, hits);
        Assert.False(Assert.Single(hits).Stunned);
        Assert.Equal((0, false), (after[1].StunTicks, after[1].Stomped));
        Assert.Equal(StompBounce, after[0].Vy);
    }

    [Fact]
    public void Stomp_during_a_plain_knockback_stagger_flattens()
    {
        var target = PlayerState.SpawnAt(1500, 0, 1);
        target.StunTicks = 4; // pushed while immune earlier, immunity now over
        var (before, after) = StompSetup(target);
        StompCollision.Resolve(before, after, Both, 2);
        Assert.Equal((StompStunTicks, true), (after[1].StunTicks, after[1].Stomped));
    }

    [Fact]
    public void Stomp_flat_ends_into_immunity_too()
    {
        var t = new PlayerState { X = 1500, Y = 0, Grounded = true, Facing = 1, StunTicks = 1, Stomped = true };
        t = Run(t, 1);
        Assert.Equal(StunImmunityTicks, t.ImmuneTicks);
    }

    [Fact]
    public void Room_counts_dash_stuns_and_snapshots_carry_flat_and_immunity()
    {
        var room = PlayingRoom(2, out var ids);
        var a = room.GetPlayer(ids[0]);
        var b = room.GetPlayer(ids[1]);
        a.State = PlayerState.SpawnAt(5000, 0, 1);
        b.State = PlayerState.SpawnAt(6000, 0, -1);
        room.QueueInput(ids[0], 1, InputButtons.Dash | InputButtons.Right);
        room.QueueInput(ids[1], 1, InputButtons.None);
        room.Tick();
        Assert.Equal(1, room.RoundDashStuns);
        Assert.True(b.State.Stomped);
        for (int i = 0; i < DashStunTicks; i++) room.Tick();
        Assert.Equal((0, false, (byte)StunImmunityTicks), (b.State.StunTicks, b.State.Stomped, b.State.ImmuneTicks));

        var snap = room.Outbox.Select(o => PacketCodec.DecodePayload(o.Frame, 2, o.Frame.Length - 2)).OfType<SnapshotPacket>().Last();
        var mine = snap.Players.Single(p => p.PlayerId == ids[1]);
        Assert.Equal(b.State.ImmuneTicks, mine.State.ImmuneTicks);
    }

    [Fact]
    public void Prediction_replays_immunity_bit_for_bit()
    {
        var s = new PlayerState { X = 5000, Y = 0, Grounded = true, Facing = 1, StunTicks = 3, Stomped = true };
        var w = new PacketWriter();
        new PlayerSnapshot { PlayerId = 1, Alive = true, State = s }.Write(w);
        var copy = PlayerSnapshot.Read(new PacketReader(w.ToArray())).State;
        var inputs = new[] { InputButtons.Right, InputButtons.Right, InputButtons.None, InputButtons.Jump, InputButtons.Left };
        foreach (var b in inputs)
        {
            PlayerPhysics.Step(ref s, b, Stage.Default);
            PlayerPhysics.Step(ref copy, b, Stage.Default);
        }
        Assert.Equal(s.ToString(), copy.ToString());
        Assert.True(s.ImmuneTicks > 0);
    }

    // ------------------------------------------------------------------ side knife lanes

    [Fact]
    public void Lanes_are_low_and_head_height_over_each_hiding_surface()
    {
        Assert.Equal(new[] { 350, 1000 }, SideKnifePhysics.Lanes(Stage.Kitchen));
        Assert.Equal(new[] { 350, 1000, 2750, 3400 }, SideKnifePhysics.Lanes(Stage.Winter));
        Assert.Equal(new[] { 350, 1000, 2750, 3400 }, SideKnifePhysics.Lanes(Stage.Warehouse));
        Assert.Equal(new[] { 350, 1000, 3950, 4600 }, SideKnifePhysics.Lanes(Stage.Dessert));
    }

    /// <summary>Heights above the take-off point on each tick of a jump (index 0 = the tick the jump is pressed).</summary>
    private static int[] JumpHeights()
    {
        var s = PlayerState.SpawnAt(1500, 0, 1);
        var ys = new List<int>();
        var b = InputButtons.Jump;
        do
        {
            PlayerPhysics.Step(ref s, b, Stage.Default);
            b = InputButtons.None;
            ys.Add(s.Y);
        } while (!s.Grounded);
        return ys.ToArray();
    }

    private enum Outcome { Misses, Duck, Jump, Undodgeable }

    /// <summary>
    /// What a player standing on a surface at height t must do about a side knife on the lane at y: nothing,
    /// crouch, or jump at the right moment. Checked with the knife flying the whole way past the player.
    /// </summary>
    private static Outcome Judge(int t, int laneY, sbyte dir)
    {
        const int px = 12000;
        bool HitsAlong(Func<int, PlayerState> poseAt)
        {
            var k = new SideKnifeState { Dir = dir, Phase = SideKnifePhase.Flying, X = px - dir * 4000, Y = laneY };
            for (int tick = 0; tick < 40; tick++)
            {
                SideKnifePhysics.Step(ref k, 24000, out _, out int from);
                if (HitShapes.SideKnifeHits(poseAt(tick), dir, laneY, from, k.X)) return true;
            }
            return false;
        }
        var stand = new PlayerState { X = px, Y = t, Grounded = true, Facing = 1 };
        if (!HitsAlong(_ => stand)) return Outcome.Misses;
        var crouch = stand;
        crouch.Crouching = true;
        if (!HitsAlong(_ => crouch)) return Outcome.Duck;
        int[] jump = JumpHeights();
        for (int start = 0; start < 15; start++)
        {
            bool hit = HitsAlong(tick =>
            {
                int i = tick - start;
                if (i < 0 || i >= jump.Length) return stand;
                return new PlayerState { X = px, Y = t + jump[i], Facing = 1 };
            });
            if (!hit) return Outcome.Jump;
        }
        return Outcome.Undodgeable;
    }

    [Theory]
    [InlineData(1)]
    [InlineData(-1)]
    public void Head_lanes_are_ducked_and_low_lanes_jumped_on_their_own_surface(int dir)
    {
        foreach (var stage in Stage.All)
            foreach (int surface in stage.SideKnifeSurfaces)
            {
                Assert.Equal(Outcome.Jump, Judge(surface, surface + SideKnifeLowOffset, (sbyte)dir));
                Assert.Equal(Outcome.Duck, Judge(surface, surface + SideKnifeHeadOffset, (sbyte)dir));
            }
    }

    [Fact]
    public void Every_lane_can_be_survived_from_every_surface_of_its_map()
    {
        foreach (var stage in Stage.All)
        {
            var surfaces = new SortedSet<int> { 0 };
            foreach (var p in stage.Platforms) surfaces.Add(p.Top);
            foreach (var b in stage.Pushables) surfaces.Add(b.Y + b.Height);
            if (stage.Hazard == HazardKind.Crate) { surfaces.Add(1200); surfaces.Add(2400); } // landed crates, two high
            foreach (int t in surfaces)
                foreach (int lane in SideKnifePhysics.Lanes(stage))
                    Assert.True(Judge(t, lane, 1) != Outcome.Undodgeable, $"{stage.Name}: lane {lane} from surface {t}");
        }
    }

    [Fact]
    public void Design_table_spot_checks()
    {
        Assert.Equal(Outcome.Jump, Judge(1000, 1000, 1));     // kitchen butter block top vs floor head lane
        Assert.Equal(Outcome.Duck, Judge(1600, 2750, 1));     // winter bench vs sill low lane
        Assert.Equal(Outcome.Misses, Judge(1600, 3400, -1));  // winter bench vs sill head lane
        Assert.Equal(Outcome.Misses, Judge(1200, 3400, 1));   // warehouse crate top
        Assert.Equal(Outcome.Misses, Judge(2000, 3950, 1));   // dessert strawberry vs vanilla low lane
        Assert.Equal(Outcome.Jump, Judge(700, 1000, 1));      // macaron top vs floor head lane
        Assert.Equal(Outcome.Misses, Judge(4800, 3400, 1));   // beam / upper rack above everything
    }

    [Fact]
    public void Flat_stunned_players_are_under_head_lanes_and_knocked_back_ones_are_not()
    {
        var flat = new PlayerState { X = 12000, Y = 0, Grounded = true, Facing = 1, StunTicks = 10, Stomped = true };
        var knocked = new PlayerState { X = 12000, Y = 0, Grounded = true, Facing = 1, StunTicks = 5 };
        Assert.False(HitShapes.SideKnifeHits(flat, 1, SideKnifeHeadOffset, 10000, 14000));
        Assert.True(HitShapes.SideKnifeHits(flat, 1, SideKnifeLowOffset, 10000, 14000));
        Assert.True(HitShapes.SideKnifeHits(knocked, 1, SideKnifeHeadOffset, 10000, 14000));
    }

    [Fact]
    public void A_knife_that_jumps_past_the_body_in_one_tick_still_hits()
    {
        var p = PlayerState.SpawnAt(12000, 0, 1);
        Assert.True(HitShapes.SideKnifeHits(p, 1, 600, 9000, 16000));
        Assert.True(HitShapes.SideKnifeHits(p, -1, 600, 16000, 9000));
        Assert.False(HitShapes.SideKnifeHits(p, 1, 600, 9000, 11000 - 520)); // tip stops short of the belly
    }

    // ------------------------------------------------------------------ side knife motion and spawning

    [Fact]
    public void Warns_for_a_second_then_flies_across_and_leaves()
    {
        var k = SideKnifeState.Warn(1, 1, 1000, Stage.Default);
        Assert.Equal(-SideKnifeLength, k.X);
        for (int i = 0; i < SideKnifeWarningTicks; i++)
        {
            Assert.Equal(SideKnifePhase.Warning, k.Phase);
            Assert.True(SideKnifePhysics.Step(ref k, 24000, out bool dangerous, out _));
            Assert.False(dangerous);
        }
        Assert.Equal(SideKnifePhase.Flying, k.Phase);
        int flown = 0;
        while (SideKnifePhysics.Step(ref k, 24000, out bool dangerous, out int from))
        {
            Assert.True(dangerous);
            Assert.Equal(SideKnifeSpeed, k.X - from);
            flown++;
        }
        Assert.InRange(flown, 66, 69); // 27 units at 8 u/s
        Assert.True(k.TailX > 24000 + SideKnifeLength);

        var left = SideKnifeState.Warn(2, -1, 1000, Stage.Default);
        Assert.Equal(24000 + SideKnifeLength, left.X);
        Assert.Equal(left.X, SideKnifePhysics.Advance(left, SideKnifeWarningTicks, 24000).X);
        Assert.Equal(left.X - 3 * SideKnifeSpeed, SideKnifePhysics.Advance(left, SideKnifeWarningTicks + 3, 24000).X);
    }

    [Fact]
    public void Interval_starts_at_six_seconds_after_twenty_and_tightens_to_one_and_a_half()
    {
        Assert.Equal(-1, SideKnifePhysics.IntervalAt(399));
        Assert.Equal(120, SideKnifePhysics.IntervalAt(400));
        Assert.Equal(80, SideKnifePhysics.IntervalAt(1200));
        Assert.Equal(50, SideKnifePhysics.IntervalAt(1800));
        Assert.Equal(30, SideKnifePhysics.IntervalAt(2200));
        Assert.Equal(30, SideKnifePhysics.IntervalAt(3600));
    }

    [Fact]
    public void Room_spawns_the_first_side_knife_at_twenty_seconds_and_all_fly_the_same_way()
    {
        var room = PlayingRoom(1, out var ids, Stage.Warehouse, autoSide: true);
        var p = room.GetPlayer(ids[0]);
        var seen = new Dictionary<ushort, SideKnifeState>();
        for (int i = 0; i < 150 * TickRate; i++)
        {
            p.Alive = true; // keep the solo player around for the measurement
            p.State = PlayerState.SpawnAt(12000, 4800, 1); // out of every lane's reach
            room.Tick();
            if (room.RoundTick < SideKnifeStartTick) Assert.Empty(room.SideKnives);
            var dirs = room.SideKnives.Select(k => k.Dir).Distinct().Count();
            Assert.True(dirs <= 1, "two side knives out at once fly different ways");
            foreach (var k in room.SideKnives) seen.TryAdd(k.Id, k);
        }
        Assert.Equal(RoomState.Playing, room.State);
        // 20..150 s: 6 s down to 1.5 s from 110 s on.
        Assert.InRange(seen.Count, 50, 70);
        var lanes = SideKnifePhysics.Lanes(Stage.Warehouse);
        Assert.All(seen.Values, k => Assert.Contains(k.Y, lanes));
        Assert.Equal(lanes.Length, seen.Values.Select(k => k.Y).Distinct().Count());
        Assert.Contains(seen.Values, k => k.Dir > 0);
        Assert.Contains(seen.Values, k => k.Dir < 0);
    }

    [Fact]
    public void Side_knife_flies_through_boxes_and_kills_standing_but_not_ducking_players()
    {
        var room = PlayingRoom(3, out var ids);
        var behindBox = room.GetPlayer(ids[0]);
        var ducking = room.GetPlayer(ids[1]);
        var high = room.GetPlayer(ids[2]);
        behindBox.State = PlayerState.SpawnAt(13200, 0, 1); // right of the butter block (11.4..12.6), knife from the left
        ducking.State = PlayerState.SpawnAt(16000, 0, 1);
        high.State = PlayerState.SpawnAt(12000, 4800, 1);  // on the high platform
        room.SpawnSideKnife(1, SideKnifeHeadOffset);
        for (int i = 0; i < 100; i++)
        {
            room.QueueInput(ids[1], (uint)(i + 1), InputButtons.Down);
            room.Tick();
        }
        Assert.False(behindBox.Alive);
        Assert.True(ducking.Alive);
        Assert.True(high.Alive);
        Assert.Empty(room.SideKnives);
    }

    [Fact]
    public void Last_two_killed_by_the_same_side_knife_share_the_win_with_cause_side_knife()
    {
        var room = PlayingRoom(2, out var ids);
        room.GetPlayer(ids[0]).State = PlayerState.SpawnAt(12000, 0, 1);
        room.GetPlayer(ids[1]).State = PlayerState.SpawnAt(12000, 0, -1);
        room.SpawnSideKnife(-1, SideKnifeLowOffset);
        for (int i = 0; i < 100 && room.State == RoomState.Playing; i++) room.Tick();
        Assert.Equal(RoomState.Ended, room.State);
        Assert.Equal(2, room.LastRoundEnd.Winners.Count);
        Assert.All(room.LastRoundEnd.Results, r => Assert.Equal(EliminationCause.SideKnife, r.Cause));
        Assert.Empty(room.SideKnives); // cleared at the end of the round
    }

    [Fact]
    public void Snapshot_round_trips_side_knives()
    {
        var s = new SnapshotPacket();
        s.SideKnives.Add(new SideKnifeState { Id = 7, Dir = -1, Phase = SideKnifePhase.Flying, X = -300, Y = 3400, Timer = 12 });
        var w = new PacketWriter();
        s.Write(w);
        var back = SnapshotPacket.Read(new PacketReader(w.ToArray()));
        var k = Assert.Single(back.SideKnives);
        Assert.Equal((7, -1, SideKnifePhase.Flying, -300, 3400, 12),
            ((int)k.Id, (int)k.Dir, k.Phase, k.X, k.Y, (int)k.Timer));
    }

    // ------------------------------------------------------------------ bots

    /// <summary>A dodging bot (inputs applied lag ticks late, like a real connection) against one side knife.</summary>
    private static bool BotSurvives(Stage stage, int surface, int lane, sbyte dir, int lag)
    {
        var me = new PlayerState { X = 12000, Y = surface, Grounded = true, Facing = 1 };
        var k = SideKnifeState.Warn(1, dir, lane, stage);
        var pending = new Queue<InputButtons>();
        for (int i = 0; i < lag; i++) pending.Enqueue(InputButtons.None);
        // A floor of the surface's height everywhere, so the bot has room to land.
        var ground = new Stage(9, "Test", 24000, 13500, 14000, surface > 0 ? new[] { new Platform(0, 24000, surface) } : new Platform[0],
            new[] { new SpawnPoint(12000, surface, 1) });
        for (int tick = 0; tick < 120; tick++)
        {
            pending.Enqueue(DodgeBrain.Decide(me, new KnifeState[0], ground, reactionTicks: 4, InputButtons.None, null, new[] { k }, lag));
            PlayerPhysics.Step(ref me, pending.Dequeue(), ground);
            if (!SideKnifePhysics.Step(ref k, stage.Width, out bool dangerous, out int from)) return true;
            if (dangerous && HitShapes.SideKnifeHits(me, k.Dir, k.Y, from, k.X)) return false;
        }
        return true;
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(5)]
    public void Dodging_bot_ducks_head_knives_and_jumps_low_ones_on_every_map(int lag)
    {
        foreach (var stage in Stage.All)
            foreach (int surface in stage.SideKnifeSurfaces)
                foreach (sbyte dir in new sbyte[] { 1, -1 })
                {
                    Assert.True(BotSurvives(stage, surface, surface + SideKnifeHeadOffset, dir, lag), $"{stage.Name} head {surface} dir {dir}");
                    Assert.True(BotSurvives(stage, surface, surface + SideKnifeLowOffset, dir, lag), $"{stage.Name} low {surface} dir {dir}");
                }
    }

    [Fact]
    public void Bot_does_not_jump_into_a_lane_above_it()
    {
        var me = PlayerState.SpawnAt(12000, 0, 1);
        var k = new SideKnifeState { Dir = 1, Phase = SideKnifePhase.Flying, X = 9000, Y = 2750 };
        var b = DodgeBrain.Decide(me, new KnifeState[0], Stage.Warehouse, 2, InputButtons.Jump | InputButtons.Right, null, new[] { k });
        Assert.Equal(InputButtons.Right, b);
    }
}
