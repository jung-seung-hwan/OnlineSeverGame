using OnlineSeverGame.Shared.Protocol;
using OnlineSeverGame.Shared.Simulation;

namespace OnlineSeverGame.Server;

public sealed class RoomConfig
{
    public int MinPlayers = 2;
    public Stage Stage = Stage.Default;
    public int Seed = Environment.TickCount;
    /// <summary>Off in tests that place knives by hand.</summary>
    public bool AutoSpawnKnives = true;
    public int MaxQueuedInputs = 8;
    /// <summary>Grace period after Playing begins before the first knife warning.</summary>
    public int FirstKnifeDelayTicks = GameConstants.TickRate;
}

/// <summary>A frame to send: Target 0 = everyone in the room.</summary>
public readonly record struct Outgoing(byte Target, byte[] Frame);

public sealed class RoomPlayer
{
    public byte Id;
    public string Name;
    public PlayerState State;
    public bool Alive;
    public bool InRound;
    public uint LastInputSeq;
    public uint LastReceivedSeq;
    public InputButtons LastButtons;
    internal readonly Queue<(uint Seq, InputButtons Buttons)> Inputs = new();
}

/// <summary>One participant's fate in the current round. Kept after a disconnect so results can list them.</summary>
internal sealed class RoundEntry
{
    public byte Id;
    public string Name;
    public EliminationCause Cause; // None = still alive
    public uint DeathTick;
}

/// <summary>
/// The whole game for one room, driven only by the game-loop thread: no locks, no sockets.
/// Network events go in through AddPlayer / RemovePlayer / QueueInput, then Tick() advances one
/// 50 ms step and leaves the frames to send in Outbox.
/// </summary>
public sealed class GameRoom
{
    private readonly RoomConfig _config;
    private readonly Stage _stage;
    private readonly Random _random;
    private readonly SortedDictionary<byte, RoomPlayer> _players = new();
    private readonly List<RoundEntry> _round = new();
    private readonly List<KnifeState> _knives = new();
    private readonly List<DashHit> _hits = new();
    private ushort _nextKnifeId = 1;
    private uint _nextSpawnTick;
    private readonly Action<string> _log;

    public GameRoom(RoomConfig config, Action<string> log = null)
    {
        _config = config;
        _stage = config.Stage;
        _random = new Random(config.Seed);
        _log = log ?? (_ => { });
    }

    public RoomState State { get; private set; } = RoomState.Waiting;
    public uint ServerTick { get; private set; }
    public ushort RoundNumber { get; private set; }
    /// <summary>Ticks since Playing began.</summary>
    public uint RoundTick { get; private set; }
    public int StateTimer { get; private set; }
    public int CompletedRounds { get; private set; }
    public RoundEndPacket LastRoundEnd { get; private set; }
    public Stage Stage => _stage;

    public List<Outgoing> Outbox { get; } = new();
    public IReadOnlyList<KnifeState> Knives => _knives;
    public int PlayerCount => _players.Count;
    public IEnumerable<RoomPlayer> Players => _players.Values;

    public RoomPlayer GetPlayer(byte id) => _players.TryGetValue(id, out var p) ? p : null;

    // ---------------------------------------------------------------- network events

    /// <summary>Returns the new player id (1..8), or 0 if the room is full.</summary>
    public byte AddPlayer(string name)
    {
        if (_players.Count >= ProtocolConstants.MaxPlayers) return 0;
        byte id = 1;
        while (_players.ContainsKey(id)) id++;

        name = (name ?? "").Trim();
        if (name.Length == 0) name = "Player" + id;
        var player = new RoomPlayer { Id = id, Name = name, State = SpawnState(id) };
        _players.Add(id, player);

        switch (State)
        {
            case RoomState.Waiting:
                player.Alive = true;
                break;
            case RoomState.Countdown:
                // The round has not started yet: join it.
                player.Alive = true;
                player.InRound = true;
                _round.Add(new RoundEntry { Id = id, Name = name });
                break;
            default:
                // Playing / Ended: spectate until the next round.
                break;
        }

        Send(id, new WelcomePacket { PlayerId = id, ServerTick = ServerTick, StageId = _stage.Id });
        foreach (var p in _players.Values)
            Send(id, new PlayerJoinedPacket { PlayerId = p.Id, Name = p.Name });
        foreach (var p in _players.Values)
            if (p.Id != id) Send(p.Id, new PlayerJoinedPacket { PlayerId = id, Name = name });
        if (State == RoomState.Countdown) Broadcast(BuildRoundStart());

        _log($"join  #{id} '{name}' ({_players.Count}/{ProtocolConstants.MaxPlayers}) state={State}");
        return id;
    }

    /// <summary>Disconnect = leave. During Playing an alive participant is eliminated on the spot.</summary>
    public void RemovePlayer(byte id, LeaveReason reason)
    {
        if (!_players.Remove(id, out var player)) return;
        if (State == RoomState.Playing && player.InRound && player.Alive)
            Eliminate(player, EliminationCause.Disconnected);
        if (State == RoomState.Countdown)
            _round.RemoveAll(e => e.Id == id);

        Broadcast(new PlayerLeftPacket { PlayerId = id, Reason = reason });
        _log($"leave #{id} '{player.Name}' ({reason}) ({_players.Count}/{ProtocolConstants.MaxPlayers}) state={State}");
    }

    public void QueueInput(byte id, uint seq, InputButtons buttons)
    {
        if (!_players.TryGetValue(id, out var p)) return;
        if (seq <= p.LastReceivedSeq) return; // duplicate or out of order
        p.LastReceivedSeq = seq;
        p.Inputs.Enqueue((seq, buttons));
        while (p.Inputs.Count > _config.MaxQueuedInputs)
        {
            // Client is running ahead of us: drop the oldest; it counts as processed (never applied).
            var dropped = p.Inputs.Dequeue();
            p.LastInputSeq = dropped.Seq;
        }
    }

    /// <summary>Starts a knife warning at x. Used by the spawner and by tests.</summary>
    public void SpawnKnife(int x)
    {
        int min = GameConstants.KnifeHalfWidth;
        int max = _stage.Width - GameConstants.KnifeHalfWidth;
        x = Math.Clamp(x, min, max);
        _knives.Add(KnifeState.Warn(_nextKnifeId, x, _stage));
        _nextKnifeId = (ushort)(_nextKnifeId == ushort.MaxValue ? 1 : _nextKnifeId + 1);
    }

    // ---------------------------------------------------------------- tick

    public void Tick()
    {
        ServerTick++;
        if (State == RoomState.Playing) RoundTick++;

        StepPlayers();
        ResolveDashes();

        if (State == RoomState.Playing)
        {
            if (_config.AutoSpawnKnives) SpawnKnivesOnSchedule();
            StepKnives();
            CheckRoundEnd();
        }

        AdvanceRoomState();
        Broadcast(BuildSnapshot());
    }

    private void StepPlayers()
    {
        foreach (var p in _players.Values)
        {
            if (p.Inputs.Count > 0)
            {
                var input = p.Inputs.Dequeue();
                p.LastInputSeq = input.Seq;
                p.LastButtons = input.Buttons;
            }
            // Empty queue: repeat the last held buttons. Press edges come from PrevButtons, so this
            // never re-triggers a jump or dash.
            if (p.Alive) PlayerPhysics.Step(ref p.State, p.LastButtons, _stage);
        }
    }

    private void ResolveDashes()
    {
        int n = _players.Count;
        var list = new RoomPlayer[n];
        _players.Values.CopyTo(list, 0);
        var states = new PlayerState[n];
        var active = new bool[n];
        for (int i = 0; i < n; i++)
        {
            states[i] = list[i].State;
            active[i] = list[i].Alive;
        }
        _hits.Clear();
        DashCollision.Resolve(states, active, n, _hits);
        for (int i = 0; i < n; i++) list[i].State = states[i];
        foreach (var hit in _hits)
            _log($"push  #{list[hit.Attacker].Id} -> #{list[hit.Target].Id}");
    }

    private void SpawnKnivesOnSchedule()
    {
        if (RoundTick < _nextSpawnTick) return;
        int count = Math.Min(GameConstants.KnivesPerSpawnMax, 1 + (int)(RoundTick / GameConstants.KnivesPerSpawnStepTicks));
        for (int i = 0; i < count; i++)
            SpawnKnife(_random.Next(GameConstants.KnifeHalfWidth, _stage.Width - GameConstants.KnifeHalfWidth + 1));
        int interval = Math.Max(GameConstants.KnifeSpawnIntervalMin,
            GameConstants.KnifeSpawnIntervalStart - (int)(RoundTick / GameConstants.KnifeSpawnIntervalStepTicks));
        _nextSpawnTick = RoundTick + (uint)interval;
    }

    private void StepKnives()
    {
        for (int i = _knives.Count - 1; i >= 0; i--)
        {
            var k = _knives[i];
            bool keep = KnifePhysics.Step(ref k, _stage, out bool dangerous, out Aabb swept);
            if (!keep)
            {
                _knives.RemoveAt(i);
                continue;
            }
            _knives[i] = k;
            if (!dangerous) continue;
            foreach (var p in _players.Values)
            {
                if (p.Alive && p.InRound && swept.Overlaps(p.State.Bounds))
                {
                    Eliminate(p, EliminationCause.Knife);
                    _log($"death #{p.Id} '{p.Name}' by knife#{k.Id} at roundTick {RoundTick}");
                }
            }
        }
    }

    private void Eliminate(RoomPlayer p, EliminationCause cause)
    {
        p.Alive = false;
        var entry = _round.Find(e => e.Id == p.Id && e.Cause == EliminationCause.None);
        if (entry != null)
        {
            entry.Cause = cause;
            entry.DeathTick = RoundTick;
        }
    }

    private void CheckRoundEnd()
    {
        int alive = 0;
        foreach (var e in _round)
            if (e.Cause == EliminationCause.None) alive++;
        bool lastStanding = _round.Count >= 2 ? alive <= 1 : alive == 0;
        bool timeUp = RoundTick >= GameConstants.RoundTimeLimitTicks;
        if (lastStanding || timeUp) EndRound(timeUp && !lastStanding);
    }

    private void EndRound(bool timeUp)
    {
        // Winners: survivors; else everyone killed by a knife on the latest death tick (simultaneous death
        // = shared win). Disconnected players never win.
        var winners = _round.Where(e => e.Cause == EliminationCause.None).ToList();
        if (winners.Count == 0)
        {
            var knifeDeaths = _round.Where(e => e.Cause == EliminationCause.Knife).ToList();
            if (knifeDeaths.Count > 0)
            {
                uint last = knifeDeaths.Max(e => e.DeathTick);
                winners = knifeDeaths.Where(e => e.DeathTick == last).ToList();
            }
        }

        var packet = new RoundEndPacket { RoundNumber = RoundNumber, EndTick = ServerTick };
        foreach (var w in winners) packet.Winners.Add(w.Id);

        // Rank: winners first, then longer survival, then knife before disconnect. Ties share a place.
        (int, uint, int) Key(RoundEntry e) => (
            winners.Contains(e) ? 1 : 0,
            e.Cause == EliminationCause.None ? RoundTick : e.DeathTick,
            e.Cause == EliminationCause.None ? 2 : e.Cause == EliminationCause.Knife ? 1 : 0);
        var ordered = _round.OrderByDescending(Key).ToList();
        for (int i = 0; i < ordered.Count; i++)
        {
            var e = ordered[i];
            int place = 1 + ordered.Count(o => Key(o).CompareTo(Key(e)) > 0);
            packet.Results.Add(new RoundResult
            {
                PlayerId = e.Id,
                Place = (byte)place,
                SurvivedTicks = e.Cause == EliminationCause.None ? RoundTick : e.DeathTick,
                Cause = e.Cause,
            });
        }

        State = RoomState.Ended;
        StateTimer = GameConstants.ResultTicks;
        _knives.Clear();
        LastRoundEnd = packet;
        CompletedRounds++;
        Broadcast(packet);

        string names = winners.Count == 0 ? "none" : string.Join(", ", winners.Select(w => $"#{w.Id} '{w.Name}'"));
        _log($"round {RoundNumber} END after {RoundTick} ticks ({RoundTick / (float)GameConstants.TickRate:0.0}s)" +
             $"{(timeUp ? " [time limit]" : "")} winners: {names}");
        foreach (var r in packet.Results)
            _log($"    place {r.Place}: #{r.PlayerId} survived {r.SurvivedTicks} ticks ({r.Cause})");
    }

    private void AdvanceRoomState()
    {
        switch (State)
        {
            case RoomState.Waiting:
                if (_players.Count >= _config.MinPlayers) StartCountdown();
                break;

            case RoomState.Countdown:
                if (_players.Count < _config.MinPlayers)
                {
                    _log("countdown cancelled: not enough players");
                    EnterWaiting();
                    break;
                }
                if (--StateTimer <= 0)
                {
                    State = RoomState.Playing;
                    StateTimer = 0;
                    RoundTick = 0;
                    _nextSpawnTick = (uint)_config.FirstKnifeDelayTicks;
                    _log($"round {RoundNumber} PLAYING with {_round.Count} players");
                }
                break;

            case RoomState.Ended:
                if (--StateTimer <= 0)
                {
                    if (_players.Count >= _config.MinPlayers) StartCountdown();
                    else EnterWaiting();
                }
                break;
        }
    }

    private void StartCountdown()
    {
        RoundNumber++;
        State = RoomState.Countdown;
        StateTimer = GameConstants.CountdownTicks;
        RoundTick = 0;
        _knives.Clear();
        _round.Clear();
        foreach (var p in _players.Values)
        {
            p.Alive = true;
            p.InRound = true;
            p.State = SpawnState(p.Id);
            _round.Add(new RoundEntry { Id = p.Id, Name = p.Name });
        }
        Broadcast(BuildRoundStart());
        _log($"round {RoundNumber} countdown, players: {string.Join(", ", _round.Select(e => "#" + e.Id))}");
    }

    private void EnterWaiting()
    {
        State = RoomState.Waiting;
        StateTimer = 0;
        RoundTick = 0;
        _knives.Clear();
        _round.Clear();
        foreach (var p in _players.Values)
        {
            p.Alive = true;
            p.InRound = false;
            p.State = SpawnState(p.Id);
        }
    }

    private PlayerState SpawnState(byte id)
    {
        var sp = _stage.SpawnPoints[(id - 1) % _stage.SpawnPoints.Count];
        return PlayerState.SpawnAt(sp.X, sp.Y, sp.Facing);
    }

    // ---------------------------------------------------------------- packets

    private RoundStartPacket BuildRoundStart()
    {
        var packet = new RoundStartPacket
        {
            RoundNumber = RoundNumber,
            CountdownTicks = (ushort)StateTimer,
            PlayStartTick = ServerTick + (uint)StateTimer,
        };
        foreach (var e in _round) packet.Participants.Add(e.Id);
        return packet;
    }

    private SnapshotPacket BuildSnapshot()
    {
        var s = new SnapshotPacket
        {
            ServerTick = ServerTick,
            RoomState = State,
            RoundNumber = RoundNumber,
            RoundTick = State == RoomState.Playing ? RoundTick : 0,
            StateTimer = (ushort)Math.Max(0, StateTimer),
        };
        foreach (var p in _players.Values)
        {
            s.Players.Add(new PlayerSnapshot
            {
                PlayerId = p.Id, Alive = p.Alive, InRound = p.InRound, State = p.State, LastInputSeq = p.LastInputSeq,
            });
        }
        int knives = Math.Min(_knives.Count, 255);
        for (int i = 0; i < knives; i++) s.Knives.Add(_knives[i]);
        return s;
    }

    private void Send(byte target, IPacket packet) => Outbox.Add(new Outgoing(target, PacketCodec.EncodeFrame(packet)));

    private void Broadcast(IPacket packet) => Outbox.Add(new Outgoing(0, PacketCodec.EncodeFrame(packet)));
}
