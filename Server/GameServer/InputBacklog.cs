namespace OnlineSeverGame.Server;

/// <summary>
/// Watches one player's input queue. The server takes one input per tick, so a burst (the client catching up after a
/// frame hitch, or a server tick that ran late) leaves inputs waiting for good: every later input then waits as many
/// ticks in the queue. When the queue kept more than the slack waiting after every tick for a whole window, the next
/// tick applies two of the player's inputs (two physics steps) and the backlog shrinks by one. Both inputs are really
/// applied, so presses are kept and the client's prediction (one step per input) stays right.
/// When inputs arrive right around the tick boundary, a queue of one is a jitter buffer rather than a backlog: taking
/// it away leaves the queue empty a tick later (the server then repeats the last buttons). So a catch-up followed by
/// an empty queue within one window doubles the window (up to the maximum); a catch-up that was not halves it again.
/// Also keeps the numbers for the per-second (--verbose) and per-round logs. Game-loop thread only.
/// </summary>
public sealed class InputBacklog
{
    private int _windowTicks;
    private int _windowMin = int.MaxValue;
    private int _window; // 0 = not started: the base window
    private int _watchTicks; // > 0: ticks left to see whether the last catch-up emptied the queue
    private bool _catchUp;
    private int _arrivedThisTick;

    // Since the last per-second log.
    private int _secArrived;
    private int _secMin = int.MaxValue;
    private int _secMax;
    private int _secBursts;
    private int _secRepeats;
    private int _secCatchUps;

    // Since the round's countdown.
    private int _ticks;
    private long _queueSum;
    private int _queueMax;
    private int _repeats;
    private int _bursts;
    private int _maxArrivals;
    private int _dropped;

    /// <summary>Ticks this round that applied two inputs.</summary>
    public int CatchUps { get; private set; }

    /// <summary>Ticks the queue must stay above the slack before the next catch-up.</summary>
    public int WindowTicks => _window;

    public void Arrived()
    {
        _arrivedThisTick++;
        _secArrived++;
    }

    /// <summary>The old overflow drop (more than MaxQueuedInputs waiting) threw one away.</summary>
    public void Dropped() => _dropped++;

    /// <summary>How many inputs to apply this tick: 0 (queue empty, repeat the last buttons), 1, or 2 (catch up).</summary>
    public int TakeCount(int queued, bool trim)
    {
        if (queued == 0) return 0;
        return trim && _catchUp && queued >= 2 ? 2 : 1;
    }

    /// <param name="taken">What TakeCount returned this tick.</param>
    /// <param name="remaining">Inputs still waiting after this tick's take.</param>
    /// <param name="windowTicks">Base window; doubled up to maxWindowTicks while catch-ups keep emptying the queue.</param>
    public void AfterTick(int taken, int remaining, int windowTicks, int maxWindowTicks, int slack)
    {
        if (_window < windowTicks) _window = windowTicks;
        if (_arrivedThisTick >= 2)
        {
            _bursts++;
            _secBursts++;
        }
        if (_arrivedThisTick > _maxArrivals) _maxArrivals = _arrivedThisTick;
        _arrivedThisTick = 0;
        if (taken == 0)
        {
            _repeats++;
            _secRepeats++;
        }
        if (taken == 2)
        {
            CatchUps++;
            _secCatchUps++;
        }

        if (_watchTicks > 0)
        {
            if (taken == 0)
            {
                _window = Math.Min(_window * 2, maxWindowTicks);
                _watchTicks = 0;
            }
            else if (--_watchTicks == 0)
            {
                _window = Math.Max(_window / 2, windowTicks);
            }
        }
        if (taken == 2) _watchTicks = _window;

        // A catch-up chance is good for the one tick after its window; then measure a fresh window.
        if (_catchUp)
        {
            _catchUp = false;
            _windowTicks = 0;
            _windowMin = int.MaxValue;
        }
        else
        {
            if (remaining < _windowMin) _windowMin = remaining;
            if (++_windowTicks >= _window)
            {
                _catchUp = _windowMin > slack;
                _windowTicks = 0;
                _windowMin = int.MaxValue;
            }
        }

        _ticks++;
        _queueSum += remaining;
        if (remaining > _queueMax) _queueMax = remaining;
        if (remaining < _secMin) _secMin = remaining;
        if (remaining > _secMax) _secMax = remaining;
    }

    /// <summary>"q 3 (min 2 max 4) in 20 bursts 1 empty 0 catch-up 0", then starts a new second.</summary>
    public string TakeSecondSummary(int queued)
    {
        string s = $"q {queued} (min {(_secMin == int.MaxValue ? 0 : _secMin)} max {_secMax}) in {_secArrived} " +
                   $"bursts {_secBursts} empty {_secRepeats} catch-up {_secCatchUps}";
        _secArrived = 0;
        _secMin = int.MaxValue;
        _secMax = 0;
        _secBursts = 0;
        _secRepeats = 0;
        _secCatchUps = 0;
        return s;
    }

    public string RoundSummary() =>
        $"queue avg {(_ticks == 0 ? 0 : _queueSum / (double)_ticks):0.00} max {_queueMax}, catch-up {CatchUps}, " +
        $"empty {_repeats}, bursts {_bursts} (max {_maxArrivals} at once), dropped {_dropped}";

    public void ResetRound()
    {
        _ticks = 0;
        _queueSum = 0;
        _queueMax = 0;
        _repeats = 0;
        _bursts = 0;
        _maxArrivals = 0;
        _dropped = 0;
        CatchUps = 0;
    }
}
