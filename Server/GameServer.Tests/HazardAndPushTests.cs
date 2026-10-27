using OnlineSeverGame.Shared.Simulation;
using static OnlineSeverGame.Shared.Simulation.GameConstants;

namespace OnlineSeverGame.Server.Tests;

public class KnifeTests
{
    private static readonly Stage S = Stage.Default;

    [Fact]
    public void Warns_then_falls_then_lands_on_the_floor_then_disappears()
    {
        var k = KnifeState.Warn(1, 1500, S);
        for (int i = 0; i < KnifeWarningTicks - 1; i++)
        {
            Assert.True(KnifePhysics.Step(ref k, S, out bool dangerous, out _));
            Assert.False(dangerous);
            Assert.Equal(KnifePhase.Warning, k.Phase);
        }
        KnifePhysics.Step(ref k, S, out _, out _);
        Assert.Equal(KnifePhase.Falling, k.Phase);
        Assert.Equal(S.KnifeSpawnY, k.Y);

        int falls = 0;
        while (k.Phase == KnifePhase.Falling)
        {
            KnifePhysics.Step(ref k, S, out bool dangerous, out _);
            Assert.True(dangerous);
            falls++;
        }
        Assert.Equal((S.KnifeSpawnY + KnifeFallSpeed - 1) / KnifeFallSpeed, falls);
        Assert.Equal(0, k.Y);
        Assert.Equal(KnifePhase.Landed, k.Phase);

        for (int i = 0; i < KnifeLandedTicks - 1; i++)
        {
            Assert.True(KnifePhysics.Step(ref k, S, out bool dangerous, out _));
            Assert.False(dangerous);
        }
        Assert.False(KnifePhysics.Step(ref k, S, out _, out _));
    }

    [Fact]
    public void Lands_on_a_platform_above_the_floor()
    {
        var k = new KnifeState { Id = 1, Phase = KnifePhase.Falling, X = 6000, Y = S.KnifeSpawnY };
        for (int i = 0; i < 40 && k.Phase == KnifePhase.Falling; i++) KnifePhysics.Step(ref k, S, out _, out _);
        Assert.Equal(2400, k.Y);
    }

    [Fact]
    public void Swept_area_covers_the_whole_drop_so_nothing_tunnels()
    {
        var k = new KnifeState { Id = 1, Phase = KnifePhase.Falling, X = 1500, Y = 5000 };
        KnifePhysics.Step(ref k, S, out bool dangerous, out Aabb swept);
        Assert.True(dangerous);
        Assert.Equal(new Aabb(1300, 5000 - KnifeFallSpeed, 1700, 5000 + KnifeHeight), swept);

        // A thin target sitting between the old and new tip positions is hit.
        var between = new Aabb(1000, 4600, 2000, 4650);
        Assert.True(swept.Overlaps(between));
    }

    [Fact]
    public void Knife_beside_the_player_misses()
    {
        var k = new KnifeState { Id = 1, Phase = KnifePhase.Falling, X = 1500, Y = 600 };
        KnifePhysics.Step(ref k, S, out _, out Aabb swept);
        var nearby = PlayerState.SpawnAt(1500 + KnifeHalfWidth + PlayerHalfWidth, 0, 1); // edges touch
        Assert.False(swept.Overlaps(nearby.Bounds));
        var under = PlayerState.SpawnAt(1500 + KnifeHalfWidth + PlayerHalfWidth - 1, 0, 1);
        Assert.True(swept.Overlaps(under.Bounds));
    }
}

public class DashPushTests
{
    private static PlayerState Dashing(int x, sbyte facing) =>
        new PlayerState { X = x, Y = 0, Grounded = true, Facing = facing, DashTicks = 2, Vx = facing * DashSpeed };

    [Fact]
    public void Dasher_knocks_the_other_player_along_its_facing_and_stops()
    {
        var states = new[] { Dashing(5000, 1), PlayerState.SpawnAt(5500, 0, -1) };
        var hits = new List<DashHit>();
        DashCollision.Resolve(states, new[] { true, true }, 2, hits);

        Assert.Equal(KnockbackSpeed, states[1].Vx);
        Assert.Equal(KnockbackLift, states[1].Vy);
        Assert.Equal(DashStunTicks, states[1].StunTicks);
        Assert.False(states[1].Grounded);
        Assert.False(states[0].IsDashing);
        Assert.Equal(0, states[0].Vx);
        Assert.Equal(new DashHit(0, 1, true), Assert.Single(hits));
    }

    [Fact]
    public void Dash_from_the_right_pushes_left()
    {
        var states = new[] { PlayerState.SpawnAt(5000, 0, 1), Dashing(5300, -1) };
        DashCollision.Resolve(states, new[] { true, true }, 2);
        Assert.Equal(-KnockbackSpeed, states[0].Vx);
        Assert.Equal(DashStunTicks, states[0].StunTicks);
    }

    [Fact]
    public void Two_dashers_bounce_away_from_each_other()
    {
        var states = new[] { Dashing(5000, 1), Dashing(5600, -1) };
        DashCollision.Resolve(states, new[] { true, true }, 2);
        Assert.Equal(-KnockbackSpeed, states[0].Vx);
        Assert.Equal(KnockbackSpeed, states[1].Vx);
        Assert.Equal(DashStunTicks, states[0].StunTicks);
        Assert.Equal(DashStunTicks, states[1].StunTicks);
    }

    [Fact]
    public void No_push_without_dash_without_overlap_or_against_the_dead()
    {
        var walking = new[] { PlayerState.SpawnAt(5000, 0, 1), PlayerState.SpawnAt(5100, 0, -1) };
        DashCollision.Resolve(walking, new[] { true, true }, 2);
        Assert.Equal(0, walking[0].StunTicks + walking[1].StunTicks);

        var apart = new[] { Dashing(5000, 1), PlayerState.SpawnAt(5000 + PlayerWidth, 0, -1) }; // edges touch
        DashCollision.Resolve(apart, new[] { true, true }, 2);
        Assert.Equal(0, apart[1].StunTicks);

        var deadTarget = new[] { Dashing(5000, 1), PlayerState.SpawnAt(5200, 0, -1) };
        DashCollision.Resolve(deadTarget, new[] { true, false }, 2);
        Assert.Equal(0, deadTarget[1].StunTicks);
        Assert.True(deadTarget[0].IsDashing);
    }

    [Fact]
    public void Knocked_player_drifts_away_over_the_stun()
    {
        var states = new[] { Dashing(5000, 1), PlayerState.SpawnAt(5500, 0, -1) };
        DashCollision.Resolve(states, new[] { true, true }, 2);
        var target = states[1];
        for (int i = 0; i < DashStunTicks; i++) PlayerPhysics.Step(ref target, InputButtons.Left, Stage.Default);
        Assert.True(target.X > 5500 + 2000, $"moved only to {target.X}");
        Assert.Equal(0, target.StunTicks);
    }
}
