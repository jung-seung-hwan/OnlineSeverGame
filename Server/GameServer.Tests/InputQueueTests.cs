using OnlineSeverGame.Shared.Protocol;
using OnlineSeverGame.Shared.Simulation;
using static OnlineSeverGame.Shared.Simulation.GameConstants;

namespace OnlineSeverGame.Server.Tests;

/// <summary>Working off a standing input backlog (docs/protocol.md 8, docs/work/input-latency.md).</summary>
public class InputQueueTests
{
    private const int StartX = 1000;

    private static GameRoom PlayingRoom(bool trim, out RoomPlayer me, int maxWindowTicks = 160)
    {
        var room = new GameRoom(new RoomConfig
        {
            MinPlayers = 2, AutoSpawnKnives = false, AutoSpawnSideKnives = false, Seed = 1, TrimInputQueue = trim, InputTrimMaxWindowTicks = maxWindowTicks,
        });
        byte a = room.AddPlayer("a");
        room.AddPlayer("b");
        room.Tick(); // Waiting -> Countdown
        for (int i = 0; i < CountdownTicks; i++) room.Tick();
        Assert.Equal(RoomState.Playing, room.State);
        me = room.GetPlayer(a);
        me.State = PlayerState.SpawnAt(StartX, 0, 1);
        return room;
    }

    /// <summary>
    /// The client sends `burst` inputs at once (a frame hitch), then one per tick. Returns the queue length after each
    /// tick and checks that every input taken was applied exactly once: odd seqs hold Right and even ones Left, so
    /// after seq n the player is one MoveSpeed right of the start when n is odd and back at the start when it is even
    /// (a missed or repeated input would leave it one step off).
    /// </summary>
    private static List<int> RunBurstThenSteady(bool trim, int burst, int ticks, out RoomPlayer me)
    {
        static InputButtons buttons(uint s) => s % 2 == 1 ? InputButtons.Right : InputButtons.Left;
        var room = PlayingRoom(trim, out me);
        uint seq = 0;
        for (int i = 0; i < burst; i++) room.QueueInput(me.Id, ++seq, buttons(seq));
        var queued = new List<int>();
        for (int t = 0; t < ticks; t++)
        {
            room.QueueInput(me.Id, ++seq, buttons(seq));
            room.Tick();
            queued.Add(me.Inputs.Count);
            Assert.Equal(seq - (uint)me.Inputs.Count, me.LastInputSeq);
            Assert.Equal(StartX + (int)(me.LastInputSeq % 2) * MoveSpeed, me.State.X);
        }
        return queued;
    }

    [Fact]
    public void Standing_backlog_is_worked_off_and_every_input_is_applied_once()
    {
        var queued = RunBurstThenSteady(trim: true, burst: 4, ticks: 80, out var me);

        Assert.Equal(4, queued[0]);
        Assert.Equal(0, queued[^1]);
        Assert.Equal(4, me.Backlog.CatchUps);
        // Never more than one extra input per tick, so it shrinks by at most one at a time.
        for (int i = 1; i < queued.Count; i++) Assert.True(queued[i - 1] - queued[i] <= 1);
        // Down to nothing waiting within 4 windows of 11 ticks (plus up to one window already running).
        Assert.True(queued.IndexOf(0) <= 4 * 11 + 10);
    }

    [Fact]
    public void Without_trim_the_backlog_stays()
    {
        var queued = RunBurstThenSteady(trim: false, burst: 4, ticks: 80, out var me);

        Assert.All(queued, q => Assert.Equal(4, q));
        Assert.Equal(0, me.Backlog.CatchUps);
    }

    [Fact]
    public void Jitter_that_sometimes_empties_the_queue_is_not_trimmed()
    {
        var room = PlayingRoom(trim: true, out var me);
        uint seq = 0;
        // Two inputs arrive before every other tick, none before the rest: the queue goes 1, 0, 1, 0, ...
        for (int t = 0; t < 60; t++)
        {
            if (t % 2 == 0)
            {
                room.QueueInput(me.Id, ++seq, InputButtons.Right);
                room.QueueInput(me.Id, ++seq, InputButtons.Right);
            }
            room.Tick();
            Assert.True(me.Inputs.Count <= 1);
        }
        Assert.Equal(0, me.Backlog.CatchUps);
        Assert.Equal(seq, me.LastInputSeq);
    }

    [Fact]
    public void A_dash_press_in_the_second_input_of_a_catch_up_tick_still_dashes()
    {
        // Find the tick that applies two inputs, then press Dash in the second one of them.
        RunBurstThenSteady(trim: true, burst: 3, ticks: 30, out _, seqs: out var catchUp);
        uint second = catchUp.First();

        var room = PlayingRoom(trim: true, out var me);
        InputButtons Buttons(uint s) => s == second ? InputButtons.Dash : InputButtons.None;
        uint seq = 0;
        for (int i = 0; i < 3; i++) room.QueueInput(me.Id, ++seq, Buttons(seq));
        while (me.LastInputSeq < second)
        {
            room.QueueInput(me.Id, ++seq, Buttons(seq));
            room.Tick();
        }
        Assert.Equal(second, me.LastInputSeq);
        Assert.Equal(1, me.RoundDashes);
        Assert.Equal(DashTicks, me.State.DashTicks); // started on the tick's last step
    }

    /// <summary>Same as the other overload; also returns the last seq applied on each tick that applied two.</summary>
    private static void RunBurstThenSteady(bool trim, int burst, int ticks, out RoomPlayer me, out List<uint> seqs)
    {
        var room = PlayingRoom(trim, out me);
        seqs = new List<uint>();
        uint seq = 0;
        for (int i = 0; i < burst; i++) room.QueueInput(me.Id, ++seq, InputButtons.None);
        for (int t = 0; t < ticks; t++)
        {
            uint before = me.LastInputSeq;
            room.QueueInput(me.Id, ++seq, InputButtons.None);
            room.Tick();
            if (me.LastInputSeq - before == 2) seqs.Add(me.LastInputSeq);
        }
        Assert.NotEmpty(seqs);
    }

    [Fact]
    public void Backlog_asks_for_a_catch_up_only_after_a_full_window_above_the_slack()
    {
        var b = new InputBacklog();
        for (int t = 0; t < 9; t++)
        {
            Assert.Equal(1, b.TakeCount(3, trim: true));
            b.AfterTick(1, 2, windowTicks: 10, maxWindowTicks: 160, slack: 1);
        }
        Assert.Equal(1, b.TakeCount(3, trim: true));
        b.AfterTick(1, 1, windowTicks: 10, maxWindowTicks: 160, slack: 1); // the window's minimum is 1: not above the slack
        Assert.Equal(1, b.TakeCount(3, trim: true));

        for (int t = 0; t < 10; t++) b.AfterTick(1, 2, windowTicks: 10, maxWindowTicks: 160, slack: 1);
        Assert.Equal(2, b.TakeCount(3, trim: true));
        Assert.Equal(1, b.TakeCount(3, trim: false));
        Assert.Equal(1, b.TakeCount(1, trim: true)); // needs two waiting
        Assert.Equal(0, b.TakeCount(0, trim: true));
        b.AfterTick(1, 2, windowTicks: 10, maxWindowTicks: 160, slack: 1); // the chance is for one tick only
        Assert.Equal(1, b.TakeCount(3, trim: true));
    }

    [Fact]
    public void A_catch_up_that_empties_the_queue_doubles_the_window_and_a_clean_one_halves_it()
    {
        var b = new InputBacklog();
        void Ticks(int count, int taken, int remaining)
        {
            for (int t = 0; t < count; t++) b.AfterTick(taken, remaining, windowTicks: 10, maxWindowTicks: 40, slack: 0);
        }

        Ticks(10, 1, 1);
        Assert.Equal(2, b.TakeCount(2, trim: true));
        Ticks(1, 2, 0);
        Ticks(1, 0, 0); // the input after the catch-up was late: the queue of one was a jitter buffer
        Assert.Equal(20, b.WindowTicks);

        Ticks(19, 1, 1); // ends the window that saw the empty tick
        Ticks(19, 1, 1);
        Assert.Equal(1, b.TakeCount(2, trim: true));
        Ticks(1, 1, 1);
        Assert.Equal(2, b.TakeCount(2, trim: true));
        Ticks(1, 2, 0);
        Ticks(1, 0, 0);
        Assert.Equal(40, b.WindowTicks);
        Ticks(39 + 40, 1, 1);
        Assert.Equal(2, b.TakeCount(2, trim: true));
        Ticks(1, 2, 0);
        Ticks(1, 0, 0);
        Assert.Equal(40, b.WindowTicks); // the maximum

        Ticks(39 + 40, 1, 1);
        Assert.Equal(2, b.TakeCount(2, trim: true));
        Ticks(1, 2, 1);
        Ticks(40, 1, 0); // no empty tick within a window after this one
        Assert.Equal(20, b.WindowTicks);
    }

    [Fact]
    public void Inputs_arriving_on_the_tick_boundary_do_not_keep_emptying_the_queue()
    {
        int backingOff = EmptyTicksWithLateInputs(maxWindowTicks: 160, out int catchUps);
        int fixedWindow = EmptyTicksWithLateInputs(maxWindowTicks: 10, out int fixedCatchUps);

        Assert.True(fixedWindow >= 15, $"{fixedWindow} empty, {fixedCatchUps} catch-ups"); // about one per catch-up
        Assert.True(backingOff <= fixedWindow / 4, $"{backingOff} vs {fixedWindow} empty, {catchUps} vs {fixedCatchUps} catch-ups");
        Assert.True(catchUps < fixedCatchUps / 4, $"{catchUps} vs {fixedCatchUps}");
    }

    /// <summary>One input per tick, but about one in eight lands just after the tick it was meant for.</summary>
    private static int EmptyTicksWithLateInputs(int maxWindowTicks, out int catchUps)
    {
        var room = PlayingRoom(trim: true, out var me, maxWindowTicks);
        me.Backlog.ResetRound();
        uint seq = 1;
        room.QueueInput(me.Id, seq, InputButtons.None); // one already waiting
        var rng = new Random(3);
        int ticks = 1200;
        for (int t = 0; t < ticks; t++)
        {
            bool late = rng.Next(8) == 0;
            if (!late) room.QueueInput(me.Id, ++seq, InputButtons.None);
            room.Tick();
            if (late) room.QueueInput(me.Id, ++seq, InputButtons.None);
        }
        catchUps = me.Backlog.CatchUps;
        string s = me.Backlog.RoundSummary();
        return int.Parse(s.Split("empty ")[1].Split(',')[0]);
    }

    [Fact]
    public void Round_summary_counts_bursts_empty_ticks_and_catch_ups()
    {
        var room = PlayingRoom(trim: true, out var me);
        me.Backlog.ResetRound();
        room.Tick(); // nothing queued: repeat
        room.QueueInput(me.Id, 1, InputButtons.None);
        room.QueueInput(me.Id, 2, InputButtons.None);
        room.QueueInput(me.Id, 3, InputButtons.None);
        room.Tick();
        string s = me.Backlog.RoundSummary();
        Assert.Contains("max 2", s);
        Assert.Contains("empty 1", s);
        Assert.Contains("bursts 1 (max 3 at once)", s);
    }
}
