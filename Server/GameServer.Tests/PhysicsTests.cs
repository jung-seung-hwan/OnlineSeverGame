using OnlineSeverGame.Shared.Simulation;
using static OnlineSeverGame.Shared.Simulation.GameConstants;

namespace OnlineSeverGame.Server.Tests;

public class PhysicsTests
{
    private static readonly Stage S = Stage.Default;

    private static PlayerState Run(PlayerState s, InputButtons buttons, int ticks)
    {
        for (int i = 0; i < ticks; i++) PlayerPhysics.Step(ref s, buttons, S);
        return s;
    }

    [Fact]
    public void Falls_with_gravity_and_lands_on_the_floor()
    {
        var s = new PlayerState { X = 1500, Y = 3000, Facing = 1 };
        PlayerPhysics.Step(ref s, InputButtons.None, S);
        Assert.Equal(-Gravity, s.Vy);
        Assert.Equal(3000 - Gravity, s.Y);

        s = Run(s, InputButtons.None, 40);
        Assert.Equal(0, s.Y);
        Assert.Equal(0, s.Vy);
        Assert.True(s.Grounded);
    }

    [Fact]
    public void Fall_speed_is_capped()
    {
        var s = Run(new PlayerState { X = 1500, Y = 100000, Facing = 1 }, InputButtons.None, 30);
        Assert.Equal(-MaxFallSpeed, s.Vy);
    }

    [Fact]
    public void Walks_at_move_speed_and_turns()
    {
        var s = PlayerState.SpawnAt(1500, 0, 1);
        s = Run(s, InputButtons.Right, 3);
        Assert.Equal(1500 + 3 * MoveSpeed, s.X);
        Assert.True(s.Grounded);
        s = Run(s, InputButtons.Left, 1);
        Assert.Equal(-1, s.Facing);
        Assert.Equal(-MoveSpeed, s.Vx);
    }

    [Fact]
    public void Walls_stop_the_player()
    {
        var s = Run(PlayerState.SpawnAt(1000, 0, -1), InputButtons.Left, 10);
        Assert.Equal(PlayerHalfWidth, s.X);
        s = Run(PlayerState.SpawnAt(S.Width - 1000, 0, 1), InputButtons.Right, 10);
        Assert.Equal(S.Width - PlayerHalfWidth, s.X);
    }

    [Fact]
    public void Jump_reaches_2800_and_triggers_once_per_press()
    {
        var s = PlayerState.SpawnAt(1500, 0, 1);
        int apex = 0;
        int jumps = 0;
        bool wasGrounded = true;
        for (int i = 0; i < 60; i++) // jump held the whole time
        {
            PlayerPhysics.Step(ref s, InputButtons.Jump, S);
            apex = Math.Max(apex, s.Y);
            if (wasGrounded && !s.Grounded) jumps++;
            wasGrounded = s.Grounded;
        }
        Assert.Equal(2800, apex);
        Assert.Equal(1, jumps);
        Assert.True(s.Grounded);
    }

    [Fact]
    public void Cannot_jump_in_the_air()
    {
        var s = new PlayerState { X = 1500, Y = 2000, Facing = 1 };
        PlayerPhysics.Step(ref s, InputButtons.Jump, S);
        Assert.True(s.Vy < 0);
    }

    [Fact]
    public void Jumps_up_through_a_platform_and_lands_on_it()
    {
        var s = PlayerState.SpawnAt(6000, 0, 1); // under the left low platform (3000..9000, top 2400)
        s = Run(s, InputButtons.Jump, 1);
        s = Run(s, InputButtons.None, 30);
        Assert.Equal(2400, s.Y);
        Assert.True(s.Grounded);
    }

    [Fact]
    public void Holding_down_drops_through_a_platform_but_not_the_floor()
    {
        var s = new PlayerState { X = 6000, Y = 2400, Grounded = true, Facing = 1 };
        s = Run(s, InputButtons.None, 5);
        Assert.Equal(2400, s.Y);
        s = Run(s, InputButtons.Down, 30);
        Assert.Equal(0, s.Y);
        Assert.True(s.Grounded);
    }

    [Fact]
    public void Walking_off_a_platform_edge_falls()
    {
        var s = new PlayerState { X = 8800, Y = 2400, Grounded = true, Facing = 1 };
        s = Run(s, InputButtons.Right, 3); // past 9000 + half width
        Assert.False(s.Grounded);
        s = Run(s, InputButtons.None, 30);
        Assert.Equal(0, s.Y);
    }

    [Fact]
    public void Middle_platform_is_reachable_from_a_low_platform_only()
    {
        // From the floor the 4800 platform is out of reach.
        var floor = Run(PlayerState.SpawnAt(12000, 0, 1), InputButtons.Jump, 1);
        floor = Run(floor, InputButtons.None, 30);
        Assert.Equal(0, floor.Y);

        // From the left low platform, jump while moving right onto the middle one.
        var s = new PlayerState { X = 8500, Y = 2400, Grounded = true, Facing = 1 };
        s = Run(s, InputButtons.Jump | InputButtons.Right, 1);
        s = Run(s, InputButtons.Right, 6);
        s = Run(s, InputButtons.None, 30);
        Assert.Equal(4800, s.Y);
    }

    [Fact]
    public void Dash_moves_4800_without_gravity_then_cools_down()
    {
        var s = new PlayerState { X = 3000, Y = 6000, Facing = 1 }; // mid-air
        PlayerPhysics.Step(ref s, InputButtons.Dash, S);
        Assert.True(s.IsDashing);
        for (int i = 1; i < DashTicks; i++) PlayerPhysics.Step(ref s, InputButtons.Dash, S); // held, no re-trigger
        Assert.Equal(3000 + DashTicks * DashSpeed, s.X);
        Assert.Equal(6000, s.Y);
        Assert.Equal(0, s.Vy);

        PlayerPhysics.Step(ref s, InputButtons.None, S);
        Assert.False(s.IsDashing);
        Assert.True(s.Vy < 0);

        // Cooldown runs from the dash start: pressed again too early does nothing.
        var t = PlayerState.SpawnAt(3000, 0, 1);
        PlayerPhysics.Step(ref t, InputButtons.Dash, S);
        for (int i = 1; i < DashCooldownTicks - 1; i++) PlayerPhysics.Step(ref t, InputButtons.None, S);
        PlayerPhysics.Step(ref t, InputButtons.Dash, S); // tick 20 since start: cooldown still 1 before decrement
        Assert.False(t.IsDashing);
        PlayerPhysics.Step(ref t, InputButtons.None, S);
        PlayerPhysics.Step(ref t, InputButtons.Dash, S);
        Assert.True(t.IsDashing);
    }

    [Fact]
    public void Dash_goes_the_held_direction()
    {
        var s = PlayerState.SpawnAt(12000, 0, 1);
        PlayerPhysics.Step(ref s, InputButtons.Left | InputButtons.Dash, S);
        Assert.Equal(-DashSpeed, s.Vx);
        Assert.Equal(-1, s.Facing);
    }

    [Fact]
    public void Stun_ignores_input_and_bleeds_speed()
    {
        var s = new PlayerState { X = 12000, Y = 0, Grounded = true, Facing = 1, Vx = KnockbackSpeed, StunTicks = 2 };
        PlayerPhysics.Step(ref s, InputButtons.Left | InputButtons.Jump, S);
        Assert.Equal(KnockbackSpeed - KnockbackSpeed / 4, s.Vx);
        Assert.Equal(1, s.Facing);
        Assert.Equal(0, s.Y);
        PlayerPhysics.Step(ref s, InputButtons.Left, S);
        PlayerPhysics.Step(ref s, InputButtons.Left, S);
        Assert.Equal(-MoveSpeed, s.Vx);
    }

    [Fact]
    public void Client_replay_from_snapshot_matches_server_exactly()
    {
        // Server applies seq 1..30; a snapshot was taken after seq 18. The client resets to it and replays
        // 19..30 — the docs/protocol.md reconciliation rule — and must land on the same state.
        var rng = new Random(5);
        var inputs = Enumerable.Range(0, 30).Select(_ => (InputButtons)rng.Next(32)).ToArray();
        var server = PlayerState.SpawnAt(6000, 0, 1);
        PlayerState snapshotAt18 = default;
        for (int i = 0; i < inputs.Length; i++)
        {
            PlayerPhysics.Step(ref server, inputs[i], S);
            if (i == 17) snapshotAt18 = server;
        }
        var client = snapshotAt18;
        for (int i = 18; i < inputs.Length; i++) PlayerPhysics.Step(ref client, inputs[i], S);
        Assert.Equal(server, client);
    }

    [Fact]
    public void Knife_landing_height_follows_platforms()
    {
        Assert.Equal(0, S.KnifeLandingY(1500, S.KnifeSpawnY));
        Assert.Equal(2400, S.KnifeLandingY(6000, S.KnifeSpawnY));
        Assert.Equal(4800, S.KnifeLandingY(12000, S.KnifeSpawnY));
    }
}
