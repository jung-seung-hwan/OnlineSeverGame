using OnlineSeverGame.BotClient;
using OnlineSeverGame.Shared.Protocol;
using OnlineSeverGame.Shared.Simulation;
using static OnlineSeverGame.Shared.Simulation.GameConstants;

namespace OnlineSeverGame.Server.Tests;

public class DodgeBrainTests
{
    private static readonly Stage S = Stage.Default;
    private const int Reaction = 2;

    private static KnifeState Warning(int x, int timerLeft = 10) =>
        new KnifeState { Id = 1, Phase = KnifePhase.Warning, X = x, Y = S.KnifeSpawnY, Timer = (ushort)timerLeft };

    private static KnifeState Falling(int x, int y) => new KnifeState { Id = 2, Phase = KnifePhase.Falling, X = x, Y = y };

    [Fact]
    public void Walks_out_from_under_a_warned_knife_toward_the_nearer_side()
    {
        var me = PlayerState.SpawnAt(1500, 0, 1);
        var b = DodgeBrain.Decide(me, new[] { Warning(1700) }, S, Reaction, InputButtons.Right);
        Assert.Equal(InputButtons.Left, b & (InputButtons.Left | InputButtons.Right));
        Assert.Equal(InputButtons.None, b & InputButtons.Dash); // plenty of time to walk
    }

    [Fact]
    public void Dashes_when_walking_is_too_slow()
    {
        var me = PlayerState.SpawnAt(1500, 0, 1);
        // Tip 1400 above my head: 2 ticks to impact, the edge is ~800 away.
        var b = DodgeBrain.Decide(me, new[] { Falling(1500, PlayerHeight + 1400) }, S, Reaction, InputButtons.None);
        Assert.NotEqual(InputButtons.None, b & (InputButtons.Left | InputButtons.Right));
        Assert.Equal(InputButtons.Dash, b & InputButtons.Dash);
    }

    [Fact]
    public void Ignores_knives_that_land_on_a_platform_above()
    {
        var me = PlayerState.SpawnAt(6000, 0, 1); // under the left low platform
        Assert.Empty(DodgeBrain.FindDangers(me, new[] { Warning(6000) }, S, Reaction));
        Assert.Equal(InputButtons.Right, DodgeBrain.Decide(me, new[] { Warning(6000) }, S, Reaction, InputButtons.Right));
    }

    [Fact]
    public void Drops_through_its_platform_when_the_knife_would_land_on_it()
    {
        var me = new PlayerState { X = 6000, Y = 2400, Grounded = true, Facing = 1 };
        var b = DodgeBrain.Decide(me, new[] { Warning(6000) }, S, Reaction, InputButtons.None);
        Assert.Equal(InputButtons.Down, b & InputButtons.Down);
    }

    [Fact]
    public void Does_not_notice_a_warning_before_its_reaction_time()
    {
        var me = PlayerState.SpawnAt(1500, 0, 1);
        var fresh = Warning(1500, KnifeWarningTicks - 1); // shown for 1 tick
        Assert.Empty(DodgeBrain.FindDangers(me, new[] { fresh }, S, reactionTicks: 4));
        Assert.Single(DodgeBrain.FindDangers(me, new[] { fresh }, S, reactionTicks: 1));
    }

    [Fact]
    public void Wandering_stops_before_stepping_under_a_knife()
    {
        var me = PlayerState.SpawnAt(21600, 0, 1);
        var b = DodgeBrain.Decide(me, new[] { Warning(23000) }, S, Reaction, InputButtons.Right | InputButtons.Jump);
        Assert.Equal(InputButtons.Jump, b);
    }

    [Fact]
    public void Picks_a_gap_between_two_knives_or_beyond_them()
    {
        var me = PlayerState.SpawnAt(1200, 0, 1);
        var knives = new[] { Warning(1200), Warning(2400) };
        var dangers = DodgeBrain.FindDangers(me, knives, S, Reaction);
        var b = DodgeBrain.Decide(me, knives, S, Reaction, InputButtons.None);
        int dir = (b & InputButtons.Left) != 0 ? -1 : 1;
        // Whichever way it goes, the target side is not covered by the other knife.
        int target = dir < 0 ? dangers[0].Left - 1 : dangers[1].Right + 1;
        Assert.DoesNotContain(dangers, d => d.Contains(target));
    }
}

public class PushLoggingTests
{
    [Fact]
    public void Pushes_are_counted_per_round_and_logged_individually_only_when_verbose()
    {
        foreach (bool verbose in new[] { false, true })
        {
            var log = new List<string>();
            var room = new GameRoom(new RoomConfig { MinPlayers = 2, AutoSpawnKnives = false, LogPushes = verbose }, log.Add);
            byte a = room.AddPlayer("a");
            byte b = room.AddPlayer("b");
            room.Tick();
            for (int i = 0; i < CountdownTicks; i++) room.Tick();
            room.GetPlayer(a).State = PlayerState.SpawnAt(12000, 0, 1);
            room.GetPlayer(b).State = PlayerState.SpawnAt(13500, 0, -1);
            room.QueueInput(a, 1, InputButtons.Dash);
            room.Tick();
            room.RemovePlayer(b, LeaveReason.Disconnected);
            room.Tick();

            Assert.Equal(verbose ? 1 : 0, log.Count(l => l.StartsWith("push")));
            Assert.Contains(log, l => l.Contains("END") && l.Contains("pushes: 1"));
        }
    }
}
