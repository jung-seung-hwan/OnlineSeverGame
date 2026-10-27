using OnlineSeverGame.Shared.Protocol;
using OnlineSeverGame.Shared.Simulation;
using static OnlineSeverGame.Shared.Simulation.GameConstants;

namespace OnlineSeverGame.Server.Tests;

/// <summary>Dash cooldown, stomp, crouch and pushable boxes (docs/work/gameplay-2.md).</summary>
public class GameplayTwoTests
{
    private static readonly Stage S = Stage.Default;

    /// <summary>A stage with no platforms and one box, to keep box tests independent of the kitchen layout.</summary>
    private static readonly Stage Flat = new Stage(9, "Flat", 24000, 13500, 14000, new Platform[0],
        new[] { new SpawnPoint(2000, 0, 1), new SpawnPoint(22000, 0, -1) },
        new[] { new PushableSpawn(12000, 0, 1200, 1000) });

    private static PlayerState Run(PlayerState s, InputButtons buttons, int ticks, Stage stage = null)
    {
        for (int i = 0; i < ticks; i++) PlayerPhysics.Step(ref s, buttons, stage ?? S);
        return s;
    }

    private static GameRoom PlayingRoom(int players, out byte[] ids, Stage stage = null)
    {
        var room = new GameRoom(new RoomConfig { MinPlayers = 2, AutoSpawnKnives = false, Seed = 1, Stage = stage ?? S });
        ids = Enumerable.Range(0, players).Select(i => room.AddPlayer("P" + i)).ToArray();
        room.Tick();
        for (int i = 0; i < CountdownTicks; i++) room.Tick();
        Assert.Equal(RoomState.Playing, room.State);
        room.Outbox.Clear();
        return room;
    }

    // ------------------------------------------------------------------ dash cooldown

    [Fact]
    public void Mashing_dash_dashes_once_every_40_ticks()
    {
        // Press on every other tick, like a player hammering the key, for 6 seconds.
        var s = PlayerState.SpawnAt(1500, 0, 1);
        var starts = new List<int>();
        for (int t = 0; t < 120; t++)
        {
            PlayerPhysics.Step(ref s, t % 2 == 0 ? InputButtons.Dash : InputButtons.None, S);
            if (s.DashTicks == DashTicks) starts.Add(t);
            if (s.X > 20000) s.X = 1500; // stay off the wall
        }
        Assert.Equal(new[] { 0, 40, 80 }, starts);
        Assert.Equal(40, DashCooldownTicks);
    }

    [Fact]
    public void Room_applies_mashed_dash_inputs_two_seconds_apart()
    {
        var room = PlayingRoom(2, out var ids);
        for (uint seq = 1; seq <= 100; seq++)
        {
            room.QueueInput(ids[0], seq, seq % 2 == 1 ? InputButtons.Dash : InputButtons.None);
            room.Tick();
            var me = room.GetPlayer(ids[0]);
            if (me.State.X > 20000 || me.State.X < 3000) me.State.X = 6000;
        }
        var p = room.GetPlayer(ids[0]);
        Assert.Equal(3, p.RoundDashes); // ticks 1, 41, 81
        Assert.Equal(DashCooldownTicks, p.MinDashGap);
    }

    // ------------------------------------------------------------------ crouch

    [Fact]
    public void Holding_down_on_the_ground_crouches_low_and_slow()
    {
        var s = Run(PlayerState.SpawnAt(1500, 0, 1), InputButtons.Down, 1);
        Assert.True(s.Crouching);
        Assert.Equal(CrouchHeight, s.Bounds.Top - s.Bounds.Bottom);
        Assert.Equal(720, CrouchHeight);

        int x = s.X;
        s = Run(s, InputButtons.Down | InputButtons.Right, 5);
        Assert.Equal(x + 5 * CrouchMoveSpeed, s.X);
        Assert.Equal(160, CrouchMoveSpeed);

        s = Run(s, InputButtons.Right, 1);
        Assert.False(s.Crouching);
        Assert.Equal(PlayerHeight, s.Bounds.Top - s.Bounds.Bottom);
        Assert.Equal(MoveSpeed, s.Vx);
    }

    [Fact]
    public void Down_in_the_air_does_not_crouch_or_pass_platforms()
    {
        var s = new PlayerState { X = 6000, Y = 4000, Facing = 1 };
        s = Run(s, InputButtons.Down, 1);
        Assert.False(s.Crouching);
        s = Run(s, InputButtons.Down, 30);
        Assert.Equal(2400, s.Y); // lands on the shelf: Down alone no longer drops through
        Assert.True(s.Crouching);
    }

    [Fact]
    public void Dash_from_a_crouch_stands_up_and_dashes()
    {
        var s = Run(PlayerState.SpawnAt(1500, 0, 1), InputButtons.Down, 3);
        s = Run(s, InputButtons.Down | InputButtons.Dash, 1);
        Assert.True(s.IsDashing);
        Assert.False(s.Crouching);
        Assert.Equal(DashSpeed, s.Vx);
    }

    [Fact]
    public void Crouching_target_gets_half_knockback()
    {
        var states = new[]
        {
            new PlayerState { X = 10000, Facing = 1, DashTicks = 3, Grounded = true },
            new PlayerState { X = 10500, Facing = -1, Grounded = true, Crouching = true },
            new PlayerState { X = 15000, Facing = 1, DashTicks = 3, Grounded = true },
            new PlayerState { X = 15500, Facing = -1, Grounded = true },
        };
        DashCollision.Resolve(states, new[] { true, true, true, true }, 4);
        Assert.Equal(CrouchKnockbackSpeed, states[1].Vx);
        Assert.Equal(CrouchKnockbackLift, states[1].Vy);
        Assert.Equal(KnockbackSpeed / 2, states[1].Vx);
        Assert.False(states[1].Crouching);
        Assert.Equal(KnockbackSpeed, states[3].Vx);
        Assert.Equal(KnockbackLift, states[3].Vy);
    }

    // ------------------------------------------------------------------ stomp

    [Fact]
    public void Falling_onto_a_head_stuns_the_target_and_bounces_the_stomper()
    {
        var room = PlayingRoom(2, out var ids);
        var top = room.GetPlayer(ids[0]);
        var under = room.GetPlayer(ids[1]);
        under.State = PlayerState.SpawnAt(1500, 0, 1);
        top.State = new PlayerState { X = 1700, Y = PlayerHeight + 250, Vy = -200, Facing = 1 };

        room.Tick(); // feet go from 1450 to 1150: through the head at 1200
        Assert.Equal(1, room.RoundStomps);
        Assert.Equal(StompStunTicks, under.State.StunTicks);
        Assert.True(under.State.Stomped);
        Assert.Equal(StompBounce, top.State.Vy);
        Assert.Equal(PlayerHeight, top.State.Y);
        Assert.False(top.State.Grounded);

        // Stunned for 30 ticks in total, flattened until it ends, then control returns.
        for (int i = 0; i < StompStunTicks - 1; i++)
        {
            room.QueueInput(ids[1], (uint)(i + 1), InputButtons.Right);
            room.Tick();
        }
        Assert.Equal(1, under.State.StunTicks);
        Assert.True(under.State.Stomped);
        Assert.Equal(1500, under.State.X);
        room.QueueInput(ids[1], 100, InputButtons.Right);
        room.Tick();
        Assert.Equal(0, under.State.StunTicks);
        Assert.False(under.State.Stomped);
        room.QueueInput(ids[1], 101, InputButtons.Right);
        room.Tick();
        Assert.Equal(1500 + MoveSpeed, under.State.X);
    }

    [Fact]
    public void Already_stunned_target_is_not_stunned_again()
    {
        var before = new[]
        {
            new PlayerState { X = 1500, Y = 1300, Vy = -200, Facing = 1 },
            new PlayerState { X = 1500, Y = 0, Grounded = true, Facing = 1, StunTicks = 5, Stomped = true },
        };
        var after = (PlayerState[])before.Clone();
        after[0].Y = 1100;
        var hits = new List<StompHit>();
        StompCollision.Resolve(before, after, new[] { true, true }, 2, hits);
        Assert.Single(hits);
        Assert.False(hits[0].Stunned);
        Assert.Equal(5, after[1].StunTicks); // not renewed: nobody can be pinned forever
        Assert.Equal(StompBounce, after[0].Vy);
    }

    [Fact]
    public void Side_by_side_or_rising_players_do_not_stomp()
    {
        var before = new[]
        {
            new PlayerState { X = 1500, Y = 0, Grounded = true, Facing = 1 },
            new PlayerState { X = 1900, Y = 0, Grounded = true, Facing = 1 },
            new PlayerState { X = 6000, Y = 1000, Vy = 500, Facing = 1 },
            new PlayerState { X = 6000, Y = 0, Grounded = true, Facing = 1 },
        };
        var after = (PlayerState[])before.Clone();
        after[2].Y = 1500; // jumping up past the head is not a stomp
        var hits = new List<StompHit>();
        StompCollision.Resolve(before, after, new[] { true, true, true, true }, 4, hits);
        Assert.Empty(hits);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Predicted_bounce_matches_the_server(bool targetJumps)
    {
        // The client steps itself and the others; the others repeat their last held buttons (from the
        // snapshot), which is what the server does with them when no new input changes anything.
        var room = PlayingRoom(2, out var ids);
        var stomper = room.GetPlayer(ids[0]);
        var target = room.GetPlayer(ids[1]);
        target.State = PlayerState.SpawnAt(1500, 0, 1);
        stomper.State = new PlayerState { X = 1800, Y = 3800, Facing = 1 };
        if (targetJumps)
        {
            room.QueueInput(ids[1], 1, InputButtons.Jump); // held from now on: one jump, then lands
            room.QueueInput(ids[0], 1, InputButtons.None);
            room.Tick();
        }
        var predicted = stomper.State;
        var other = target.State;
        var otherBefore = new List<PlayerState> { other };
        var otherAfter = new List<PlayerState> { other };

        for (uint seq = 2; seq <= 40; seq++)
        {
            room.QueueInput(ids[0], seq, InputButtons.None);
            room.Tick();
            otherBefore[0] = other;
            PlayerPhysics.Step(ref other, other.PrevButtons, S);
            otherAfter[0] = other;
            var before = predicted;
            PlayerPhysics.Step(ref predicted, InputButtons.None, S);
            StompCollision.PredictBounce(before, ref predicted, otherBefore, otherAfter);
            Assert.Equal(stomper.State, predicted);
            if (target.State.StunTicks > 0) other.StunTicks = target.State.StunTicks; // learned from the next snapshot
            other.Stomped = target.State.Stomped;
        }
        Assert.True(room.RoundStomps >= 2); // bounced once, came down again on the stunned target
    }

    [Fact]
    public void Dead_players_are_not_stomped()
    {
        var room = PlayingRoom(3, out var ids);
        room.GetPlayer(ids[1]).State = PlayerState.SpawnAt(1500, 0, 1);
        room.GetPlayer(ids[1]).Alive = false;
        room.GetPlayer(ids[0]).State = new PlayerState { X = 1500, Y = PlayerHeight + 50, Vy = -200, Facing = 1 };
        room.Tick();
        Assert.Equal(0, room.RoundStomps);
    }

    // ------------------------------------------------------------------ pushable boxes

    [Fact]
    public void Kitchen_has_one_box_and_some_decorations()
    {
        Assert.Single(S.Pushables);
        Assert.True(S.Decorations.Count >= 3);
        var room = PlayingRoom(2, out _);
        Assert.Single(room.Pushables);
        Assert.Equal((ushort)1, room.Pushables[0].Id);
    }

    [Fact]
    public void Walking_into_a_box_pushes_it_at_walking_push_speed()
    {
        var room = PlayingRoom(2, out var ids, Flat);
        var p = room.GetPlayer(ids[0]);
        p.State = PlayerState.SpawnAt(11400 - PlayerHalfWidth - 100, 0, 1);
        for (uint seq = 1; seq <= 20; seq++)
        {
            room.QueueInput(ids[0], seq, InputButtons.Right);
            room.Tick();
        }
        var box = room.Pushables[0];
        Assert.True(box.X > 12000 + 15 * (PushableWalkSpeed - PushableFriction));
        Assert.True(p.State.X <= box.Left - PlayerHalfWidth + PushableWalkSpeed); // never inside the box
        Assert.True(p.State.X < 11000 + 20 * MoveSpeed); // slowed down by the box
    }

    [Fact]
    public void Dashing_into_a_box_launches_it_and_ends_the_dash_then_friction_stops_it()
    {
        var room = PlayingRoom(2, out var ids, Flat);
        var p = room.GetPlayer(ids[0]);
        p.State = PlayerState.SpawnAt(9000, 0, 1);
        room.QueueInput(ids[0], 1, InputButtons.Dash);
        for (int i = 0; i < 3; i++) room.Tick();
        Assert.False(p.State.IsDashing);
        Assert.Equal(11400 - PlayerHalfWidth, p.State.X);

        int stoppedAt = -1;
        for (int i = 0; i < 40; i++)
        {
            room.Tick();
            if (room.Pushables[0].Vx == 0) { stoppedAt = i; break; }
        }
        Assert.True(stoppedAt > 5, $"box stopped after {stoppedAt} ticks");
        int travelled = room.Pushables[0].X - 12000;
        Assert.InRange(travelled, 3500, 6000); // ~4.9 u with 700 start and 50 friction
    }

    [Fact]
    public void Players_stand_on_a_box_and_ride_it()
    {
        var room = PlayingRoom(2, out var ids, Flat);
        var rider = room.GetPlayer(ids[0]);
        rider.State = new PlayerState { X = 12000, Y = 1500, Facing = 1 };
        for (int i = 0; i < 10; i++) room.Tick();
        Assert.Equal(1000, rider.State.Y);
        Assert.True(rider.State.Grounded);

        room.Pushables[0].Vx = 600;
        room.Tick();
        Assert.Equal(12000 + 600 - PushableFriction, room.Pushables[0].X);
        Assert.Equal(12000 + 600 - PushableFriction, rider.State.X);
    }

    [Fact]
    public void Sliding_box_shoves_a_player_and_stops_at_the_wall()
    {
        var boxes = new[] { new PushableState { Id = 1, X = 20000, Y = 0, Vx = 700, Width = 1200, Height = 1000, Grounded = true } };
        var players = new[] { PlayerState.SpawnAt(21200, 0, 1) };
        var moved = new int[1];
        PushablePhysics.Step(boxes, 1, Flat, players, new[] { true }, 1, moved);
        Assert.Equal(20650, boxes[0].X);
        Assert.Equal(boxes[0].Right + PlayerHalfWidth, players[0].X);

        for (int i = 0; i < 10; i++) PushablePhysics.Step(boxes, 1, Flat, players, new[] { true }, 1, moved);
        Assert.True(players[0].X <= Flat.Width - PlayerHalfWidth);
        Assert.True(boxes[0].Right <= players[0].X - PlayerHalfWidth);
    }

    [Fact]
    public void A_player_inside_a_box_can_walk_out()
    {
        var boxes = new[] { new PushableState { Id = 1, X = 12000, Y = 0, Width = 1200, Height = 1000, Grounded = true } };
        var s = PlayerState.SpawnAt(12100, 0, 1);
        for (int i = 0; i < 5; i++) PlayerPhysics.Step(ref s, InputButtons.Right, Flat, boxes, out _);
        Assert.Equal(12100 + 5 * MoveSpeed, s.X);
    }

    [Fact]
    public void Box_falls_and_lands_on_a_platform()
    {
        var boxes = new[] { new PushableState { Id = 1, X = 6000, Y = 4000, Width = 1200, Height = 1000 } };
        for (int i = 0; i < 20; i++) PushablePhysics.Step(boxes, 1, S, null, null, 0, null);
        Assert.Equal(2400, boxes[0].Y);
        Assert.True(boxes[0].Grounded);
    }

    [Fact]
    public void Knives_stick_into_a_box_top()
    {
        var boxes = new[] { new PushableState { Id = 1, X = 12000, Y = 0, Width = 1200, Height = 1000, Grounded = true } };
        Assert.Equal(4800, PushablePhysics.KnifeLandingY(S, boxes, 12000, S.KnifeSpawnY)); // lid above wins
        Assert.Equal(1000, PushablePhysics.KnifeLandingY(Flat, boxes, 12000, Flat.KnifeSpawnY));

        var k = new KnifeState { Id = 1, Phase = KnifePhase.Falling, X = 12000, Y = 1500 };
        KnifePhysics.Step(ref k, Flat, boxes, out _, out _);
        Assert.Equal(KnifePhase.Landed, k.Phase);
        Assert.Equal(1000, k.Y);
    }

    [Fact]
    public void Prediction_with_the_snapshot_boxes_matches_the_server()
    {
        // Walk onto the box, crouch, jump off, dash into it: replay from a mid snapshot must match.
        var boxes = new[] { PushableState.Spawn(1, Flat.Pushables[0]) };
        var rng = new Random(9);
        var inputs = Enumerable.Range(0, 60).Select(_ => (InputButtons)rng.Next(32)).ToArray();
        var server = PlayerState.SpawnAt(10800, 0, 1);
        PlayerState snap = default;
        for (int i = 0; i < inputs.Length; i++)
        {
            PlayerPhysics.Step(ref server, inputs[i], Flat, boxes, out _);
            if (i == 29) snap = server;
        }
        var client = snap;
        for (int i = 30; i < inputs.Length; i++) PlayerPhysics.Step(ref client, inputs[i], Flat, boxes, out _);
        Assert.Equal(server, client);
    }

    [Fact]
    public void Boxes_reset_at_the_next_round_and_go_out_in_snapshots()
    {
        var room = PlayingRoom(2, out var ids, Flat);
        room.Pushables[0].X = 3000;
        room.Tick();
        var snap = room.Outbox.Select(o => PacketCodec.DecodePayload(o.Frame, 2, o.Frame.Length - 2))
            .OfType<SnapshotPacket>().Last();
        Assert.Equal(3000, snap.Pushables.Single().X);

        room.RemovePlayer(ids[1], LeaveReason.Disconnected); // round ends, then the next one starts
        room.AddPlayer("again");
        for (int i = 0; i < ResultTicks + 2; i++) room.Tick();
        Assert.Equal(RoomState.Countdown, room.State);
        Assert.Equal(12000, room.Pushables[0].X);
    }
}
