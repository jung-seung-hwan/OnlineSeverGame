using OnlineSeverGame.Shared.Protocol;
using OnlineSeverGame.Shared.Simulation;

namespace OnlineSeverGame.Server;

public sealed class RoomConfig
{
    public int MinPlayers = 2;
    /// <summary>The first map when Rotation starts as Fixed (otherwise Maps[0]).</summary>
    public Stage Stage = Stage.Default;
    /// <summary>The starting rotation; the host may change it (MapRequest).</summary>
    public MapRotation Rotation = MapRotation.Fixed;
    /// <summary>Maps in cycle order; also the ones the host may pick.</summary>
    public IReadOnlyList<Stage> Maps = Stage.All;
    public int Seed = Environment.TickCount;
    /// <summary>Off in tests that place knives by hand.</summary>
    public bool AutoSpawnKnives = true;
    public int MaxQueuedInputs = 8;
    /// <summary>Grace period after Playing begins before the first knife warning.</summary>
    public int FirstKnifeDelayTicks = GameConstants.TickRate;
    /// <summary>Log every dash push. Off by default: the round summary carries the count.</summary>
    public bool LogPushes;
    /// <summary>
    /// Judge hazards with the old boxes (hazard spec box vs the 800 x 1200 movement box) instead of the drawn
    /// shapes (HitShapes). Only for before/after comparisons (--legacy-hitbox).
    /// </summary>
    public bool LegacyHitbox;
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
    /// <summary>Order of arrival (1, 2, ...). The host is the lowest one still here.</summary>
    public long JoinOrder;
    public InputButtons LastButtons;
    /// <summary>Dashes started this round and the server tick of the last one (0 = none yet). For logs and tests.</summary>
    public int RoundDashes;
    public uint LastDashTick;
    /// <summary>Shortest gap between two dash starts this round, in ticks (0 = fewer than two dashes).</summary>
    public int MinDashGap;
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
    private Stage _stage;
    private readonly Random _random;
    private readonly Random _mapRandom;
    private MapRotation _rotation;
    /// <summary>The map the next new countdown switches to (the current one until a round on it starts playing).</summary>
    private Stage _nextStage;
    /// <summary>The host picked _nextStage; the rotation does not overwrite it.</summary>
    private bool _nextChosen;
    /// <summary>A round has started playing on the current map, so the next countdown moves on.</summary>
    private bool _mapPlayed;
    private long _joinCounter;
    private RoomInfoPacket _sentRoomInfo;
    private readonly SortedDictionary<byte, RoomPlayer> _players = new();
    private readonly List<RoundEntry> _round = new();
    private readonly List<KnifeState> _knives = new();
    private readonly List<DashHit> _hits = new();
    private readonly List<StompHit> _stomps = new();
    private PushableState[] _boxes;
    private ushort _nextBoxId;
    private ushort _nextKnifeId = 1;
    private uint _nextSpawnTick;
    private int _roundPushes;
    private int _roundDashes;
    private int _roundStomps;
    private int _roundCrouchTicks;
    private int _roundBoxHits;
    private int _roundBoxPushTicks;
    private long _roundBoxTravel;
    private int _roundHazards;
    private int _roundCratesLanded;
    private int _roundCratesCrumbled;
    private int _roundCratesShattered;
    private readonly HitStats _hitStats = new();
    private readonly Action<string> _log;

    public GameRoom(RoomConfig config, Action<string> log = null)
    {
        _config = config;
        _rotation = config.Rotation;
        _stage = config.Rotation == MapRotation.Fixed ? config.Stage : config.Maps[0];
        _nextStage = _stage;
        _random = new Random(config.Seed);
        _mapRandom = new Random(config.Seed ^ 0x5EED);
        _log = log ?? (_ => { });
        ResetPushables();
    }

    public RoomState State { get; private set; } = RoomState.Waiting;
    public uint ServerTick { get; private set; }
    public ushort RoundNumber { get; private set; }
    /// <summary>Ticks since Playing began.</summary>
    public uint RoundTick { get; private set; }
    public int StateTimer { get; private set; }
    public int CompletedRounds { get; private set; }
    public RoundEndPacket LastRoundEnd { get; private set; }
    /// <summary>The current map as defined.</summary>
    public Stage Stage => _stage;
    /// <summary>The current map's shape this tick (melting platforms shrink during Playing).</summary>
    public Stage CurrentStage => _stage.AtRoundTick(State == RoomState.Playing ? RoundTick : 0);
    public int RoundCratesLanded => _roundCratesLanded;
    /// <summary>The map the next new round will use.</summary>
    public Stage NextStage => _nextStage;
    public MapRotation Rotation => _rotation;
    public bool NextChosen => _nextChosen;
    /// <summary>The earliest-joined player still connected (0 = empty room). Only the host may change maps.</summary>
    public byte HostId
    {
        get
        {
            RoomPlayer host = null;
            foreach (var p in _players.Values)
                if (host == null || p.JoinOrder < host.JoinOrder) host = p;
            return host?.Id ?? 0;
        }
    }

    public List<Outgoing> Outbox { get; } = new();
    public IReadOnlyList<KnifeState> Knives => _knives;
    /// <summary>Boxes, writable so tests can place them.</summary>
    public PushableState[] Pushables => _boxes;
    public int RoundStomps => _roundStomps;
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
        var player = new RoomPlayer { Id = id, Name = name, State = SpawnState(id), JoinOrder = ++_joinCounter };
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
        // Everyone else learns about a host change at the end of the tick; the newcomer needs it now.
        Send(id, BuildRoomInfo());

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

    /// <summary>
    /// The host asks for the next map (stageId 0 = keep) and/or the rotation (0 = keep). Anyone else is ignored,
    /// as are maps not in RoomConfig.Maps. It never changes the round in progress: the map switches at the next new
    /// countdown (a countdown counts as the round in progress). Returns true when accepted.
    /// </summary>
    public bool RequestMap(byte playerId, byte stageId, MapRotation rotation)
    {
        if (!_players.ContainsKey(playerId)) return false;
        if (playerId != HostId)
        {
            _log($"map   request from #{playerId} ignored: not the host (#{HostId})");
            return false;
        }
        Stage pick = null;
        if (stageId != 0)
        {
            foreach (var m in _config.Maps)
                if (m.Id == stageId) pick = m;
            if (pick == null)
            {
                _log($"map   request from #{playerId} ignored: no map {stageId}");
                return false;
            }
        }
        bool validRotation = rotation == MapRotation.Cycle || rotation == MapRotation.Random || rotation == MapRotation.Fixed;
        if (rotation != 0 && !validRotation)
        {
            _log($"map   request from #{playerId} ignored: no rotation {(byte)rotation}");
            return false;
        }
        if (validRotation) _rotation = rotation;
        if (pick != null)
        {
            _nextStage = pick;
            _nextChosen = true;
        }
        else if (!_nextChosen)
        {
            _nextStage = _mapPlayed ? MapAfter(_stage) : _stage;
        }
        _log($"map   host #{playerId} set next map {_nextStage.Id} '{_nextStage.Name}', rotation {_rotation} (now on {_stage.Id} '{_stage.Name}', state {State})");
        return true;
    }

    /// <summary>Starts a warning for the map's hazard (knife, icicle, crate, fork) at x. Used by the spawner and by tests.</summary>
    public void SpawnKnife(int x)
    {
        int half = HazardSpec.Of(_stage.Hazard).HalfWidth;
        int min = half;
        int max = _stage.Width - half;
        x = Math.Clamp(x, min, max);
        _knives.Add(KnifeState.Warn(_nextKnifeId, x, _stage));
        _nextKnifeId = (ushort)(_nextKnifeId == ushort.MaxValue ? 1 : _nextKnifeId + 1);
    }

    // ---------------------------------------------------------------- tick

    public void Tick()
    {
        ServerTick++;
        if (State == RoomState.Playing) RoundTick++;

        StepWorld();

        if (State == RoomState.Playing)
        {
            if (_config.AutoSpawnKnives) SpawnKnivesOnSchedule();
            StepKnives();
            CheckRoundEnd();
        }

        AdvanceRoomState();
        BroadcastRoomInfoIfChanged();
        Broadcast(BuildSnapshot());
    }

    private void BroadcastRoomInfoIfChanged()
    {
        var info = BuildRoomInfo();
        if (info.SameAs(_sentRoomInfo)) return;
        _sentRoomInfo = info;
        Broadcast(info);
    }

    /// <summary>
    /// Players step (each sees the boxes as they were at the start of the tick), then dash pushes, then
    /// stomps, then boxes take the pushes, slide and carry whoever stands on them.
    /// </summary>
    private void StepWorld()
    {
        Stage stage = CurrentStage;
        TickCrumblingBoxes();
        int n = _players.Count;
        var list = new RoomPlayer[n];
        _players.Values.CopyTo(list, 0);
        var before = new PlayerState[n];
        var states = new PlayerState[n];
        var active = new bool[n];
        var contacts = new BoxContact[n];

        for (int i = 0; i < n; i++)
        {
            var p = list[i];
            before[i] = p.State;
            active[i] = p.Alive;
            contacts[i] = BoxContact.None;
            bool repeated = p.Inputs.Count == 0;
            if (!repeated)
            {
                var input = p.Inputs.Dequeue();
                p.LastInputSeq = input.Seq;
                p.LastButtons = input.Buttons;
            }
            // Empty queue: repeat the last held buttons. Press edges come from PrevButtons, so this
            // never re-triggers a jump or dash.
            if (p.Alive)
            {
                PlayerPhysics.Step(ref p.State, p.LastButtons, stage, _boxes, out contacts[i]);
                // DashTicks is still at its full value only on the tick the dash started.
                if (p.State.DashTicks == GameConstants.DashTicks) RecordDash(p, repeated);
                if (p.State.Crouching) _roundCrouchTicks++;
            }
            states[i] = p.State;
        }

        _hits.Clear();
        DashCollision.Resolve(states, active, n, _hits, stage);
        _roundPushes += _hits.Count;
        if (_config.LogPushes)
            foreach (var hit in _hits)
                _log($"push  #{list[hit.Attacker].Id} -> #{list[hit.Target].Id}" +
                     (before[hit.Target].Crouching ? " (crouching, half knockback)" : ""));

        _stomps.Clear();
        StompCollision.Resolve(before, states, active, n, _stomps);
        _roundStomps += _stomps.Count;
        if (_config.LogPushes)
            foreach (var st in _stomps)
                _log($"stomp #{list[st.Stomper].Id} -> #{list[st.Target].Id}" + (st.Stunned ? " stun" : " (already stunned)"));

        for (int i = 0; i < n; i++)
        {
            var c = contacts[i];
            if (!active[i] || c.PushBox < 0) continue;
            PushablePhysics.ApplyPush(ref _boxes[c.PushBox], c.PushDir, c.PushDashing);
            if (c.PushDashing)
            {
                _roundBoxHits++;
                if (_config.LogPushes) _log($"box   #{list[i].Id} dashed box#{_boxes[c.PushBox].Id} dir {c.PushDir}");
            }
            else _roundBoxPushTicks++;
        }
        if (_boxes.Length > 0)
        {
            var moved = new int[_boxes.Length];
            PushablePhysics.Step(_boxes, _boxes.Length, stage, states, active, n, moved);
            int minX = GameConstants.PlayerHalfWidth, maxX = _stage.Width - GameConstants.PlayerHalfWidth;
            for (int i = 0; i < n; i++)
            {
                int g = contacts[i].GroundBox;
                if (!active[i] || g < 0 || !states[i].Grounded || moved[g] == 0) continue;
                states[i].X = Math.Clamp(states[i].X + moved[g], minX, maxX); // ride along
            }
            for (int i = 0; i < _boxes.Length; i++)
                _roundBoxTravel += Math.Abs(moved[i]);
        }

        for (int i = 0; i < n; i++) list[i].State = states[i];
    }

    private void RecordDash(RoomPlayer p, bool repeated)
    {
        int gap = p.LastDashTick == 0 ? 0 : (int)(ServerTick - p.LastDashTick);
        if (gap > 0 && (p.MinDashGap == 0 || gap < p.MinDashGap)) p.MinDashGap = gap;
        p.LastDashTick = ServerTick;
        p.RoundDashes++;
        _roundDashes++;
        if (_config.LogPushes)
            _log($"dash  #{p.Id} tick {ServerTick} seq {p.LastInputSeq}" + (gap > 0 ? $" gap {gap}" : "") +
                 (repeated ? " (repeated input)" : ""));
    }

    /// <summary>Puts the map's boxes back where they start; crates that landed during the round vanish.</summary>
    private void ResetPushables()
    {
        _boxes = new PushableState[_stage.Pushables.Count];
        for (int i = 0; i < _boxes.Length; i++)
            _boxes[i] = PushableState.Spawn((ushort)(i + 1), _stage.Pushables[i]);
        _nextBoxId = (ushort)(_boxes.Length + 1);
    }

    private void SpawnKnivesOnSchedule()
    {
        if (RoundTick < _nextSpawnTick) return;
        HazardSchedule schedule = _stage.Schedule;
        int count = schedule.CountAt(RoundTick);
        int half = HazardSpec.Of(_stage.Hazard).HalfWidth;
        var slots = _stage.HazardSlots;
        for (int i = 0; i < count; i++)
            SpawnKnife(slots.Count > 0 ? slots[_random.Next(slots.Count)] : _random.Next(half, _stage.Width - half + 1));
        _roundHazards += count;
        _nextSpawnTick = RoundTick + (uint)schedule.IntervalAt(RoundTick);
    }

    private void StepKnives()
    {
        Stage stage = CurrentStage;
        for (int i = _knives.Count - 1; i >= 0; i--)
        {
            var k = _knives[i];
            KnifePhase before = k.Phase;
            int fromY = k.Y;
            bool keep = KnifePhysics.Step(ref k, stage, _boxes, out bool dangerous, out _);
            if (!keep)
            {
                _knives.RemoveAt(i);
                continue;
            }
            _knives[i] = k;
            if (!dangerous) continue;
            foreach (var p in _players.Values)
            {
                if (!p.Alive || !p.InRound) continue;
                // Both rules are measured every tick so the round summary can compare them; only one decides.
                int visual = HitShapes.Separation(p.State, k.Kind, k.X, fromY, k.Y);
                int legacy = HitShapes.LegacySeparation(p.State, k.Kind, k.X, fromY, k.Y);
                bool hit = _config.LegacyHitbox ? legacy < 0 : visual < 0;
                _hitStats.Record(k.Id, p.Id, hit, visual, legacy, _config.LegacyHitbox);
                if (hit)
                {
                    Eliminate(p, EliminationCause.Knife);
                    _log($"death #{p.Id} '{p.Name}' by {k.Kind.ToString().ToLowerInvariant()}#{k.Id} at roundTick {RoundTick}" +
                         $" (drawn-shape gap {visual / 1000.0:0.00} u, old-box gap {legacy / 1000.0:0.00} u)");
                }
            }
            if (k.Phase != KnifePhase.Falling) _hitStats.Finish(k.Id, k.Kind, _config.LegacyHitbox, _config.LogPushes ? _log : null);
            if (k.Kind == HazardKind.Crate && before == KnifePhase.Falling && k.Phase == KnifePhase.Landed && LandCrate(k))
                _knives.RemoveAt(i);
        }
    }

    /// <summary>
    /// A falling crate touched down. It becomes a pushable box there unless it hit a stack that is already
    /// two high, where it shatters (stays a harmless Landed hazard for a moment). Returns true when the
    /// hazard became a box and should be removed.
    /// </summary>
    private bool LandCrate(KnifeState k)
    {
        HazardSpec spec = HazardSpec.Crate;
        int left = k.X - spec.HalfWidth, right = k.X + spec.HalfWidth;
        for (int i = 0; i < _boxes.Length; i++)
        {
            var b = _boxes[i];
            if (b.Top == k.Y && b.Left < right && left < b.Right && StackHeight(i) >= GameConstants.CrateStackLimit)
            {
                _roundCratesShattered++;
                if (_config.LogPushes) _log($"crate {k.Id} shattered on a full stack at x={k.X}");
                return false;
            }
        }

        var box = new PushableState
        {
            Id = _nextBoxId, X = k.X, Y = k.Y, Width = (ushort)spec.Width, Height = (ushort)spec.Height, Grounded = true,
        };
        _nextBoxId = (ushort)(_nextBoxId == ushort.MaxValue ? _stage.Pushables.Count + 1 : _nextBoxId + 1);
        Array.Resize(ref _boxes, _boxes.Length + 1);
        _boxes[^1] = box;
        _roundCratesLanded++;
        if (_config.LogPushes) _log($"crate {k.Id} landed at ({k.X},{k.Y}) -> box#{box.Id}");

        // Too many crates resting: the oldest ones start to crumble. Boxes are kept oldest first.
        int solid = 0;
        foreach (var b in _boxes)
            if (b.Crumble == 0) solid++;
        for (int i = 0; i < _boxes.Length && solid > GameConstants.MaxRestingCrates; i++)
        {
            if (_boxes[i].Crumble != 0) continue;
            _boxes[i].Crumble = GameConstants.CrateCrumbleTicks;
            solid--;
        }
        return true;
    }

    /// <summary>How many boxes high box i stands: 1 on the ground or a platform, 2 on another box, ...</summary>
    private int StackHeight(int i)
    {
        int height = 1;
        for (int guard = 0; guard < _boxes.Length; guard++)
        {
            var b = _boxes[i];
            int below = -1;
            for (int j = 0; j < _boxes.Length; j++)
            {
                var o = _boxes[j];
                if (j != i && o.Top == b.Y && o.Left < b.Right && b.Left < o.Right) below = j;
            }
            if (below < 0) break;
            height++;
            i = below;
        }
        return height;
    }

    /// <summary>Counts down crumbling crates and removes the ones whose time is up (boxes above them fall).</summary>
    private void TickCrumblingBoxes()
    {
        int kept = 0;
        for (int i = 0; i < _boxes.Length; i++)
        {
            var b = _boxes[i];
            if (b.Crumble > 0 && --b.Crumble == 0)
            {
                _roundCratesCrumbled++;
                continue;
            }
            _boxes[kept++] = b;
        }
        if (kept != _boxes.Length) Array.Resize(ref _boxes, kept);
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
        _log($"round {RoundNumber} END on map {_stage.Id} '{_stage.Name}' after {RoundTick} ticks ({RoundTick / (float)GameConstants.TickRate:0.0}s)" +
             $"{(timeUp ? " [time limit]" : "")} winners: {names}, pushes: {_roundPushes}, dashes: {_roundDashes}" +
             $" (min gap {MinDashGapOfRound()} ticks), stomps: {_roundStomps}, crouch ticks: {_roundCrouchTicks}, " +
             $"box dash hits: {_roundBoxHits}, box walk-push ticks: {_roundBoxPushTicks}, box travel: {_roundBoxTravel / 1000.0:0.0} u" +
             (_boxes.Length > 0 ? $" (box#1 now x={_boxes[0].X})" : "") +
             $", hazards: {_roundHazards} {_stage.Hazard.ToString().ToLowerInvariant()}" +
             (_stage.Hazard == HazardKind.Crate
                 ? $", crates landed: {_roundCratesLanded}, crumbled: {_roundCratesCrumbled}, shattered: {_roundCratesShattered}, boxes now: {_boxes.Length}"
                 : ""));
        _log("    " + _hitStats.TakeSummary(_config.LegacyHitbox));
        foreach (var r in packet.Results)
            _log($"    place {r.Place}: #{r.PlayerId} survived {r.SurvivedTicks} ticks ({r.Cause})");
    }

    private int MinDashGapOfRound()
    {
        int min = 0;
        foreach (var p in _players.Values)
            if (p.MinDashGap > 0 && (min == 0 || p.MinDashGap < min)) min = p.MinDashGap;
        return min;
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
                    _mapPlayed = true;
                    // Decided now so the waiting room and the results can show it (random included).
                    if (!_nextChosen) _nextStage = MapAfter(_stage);
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
        // A cancelled countdown never played, so _nextStage is still this map unless the host picked another.
        _stage = _nextStage;
        _nextChosen = false;
        _mapPlayed = false;
        RoundNumber++;
        State = RoomState.Countdown;
        StateTimer = GameConstants.CountdownTicks;
        RoundTick = 0;
        _roundPushes = 0;
        _roundDashes = 0;
        _roundStomps = 0;
        _roundCrouchTicks = 0;
        _roundBoxHits = 0;
        _roundBoxPushTicks = 0;
        _roundBoxTravel = 0;
        _roundHazards = 0;
        _roundCratesLanded = 0;
        _roundCratesCrumbled = 0;
        _roundCratesShattered = 0;
        ResetPushables();
        _knives.Clear();
        _round.Clear();
        foreach (var p in _players.Values)
        {
            p.Alive = true;
            p.InRound = true;
            p.State = SpawnState(p.Id);
            p.RoundDashes = 0;
            p.LastDashTick = 0;
            p.MinDashGap = 0;
            _round.Add(new RoundEntry { Id = p.Id, Name = p.Name });
        }
        Broadcast(BuildRoundStart());
        _log($"round {RoundNumber} countdown on map {_stage.Id} '{_stage.Name}', players: {string.Join(", ", _round.Select(e => "#" + e.Id))}");
    }

    /// <summary>What the rotation gives after a round on this map: the next in order, a random other one, or the same.</summary>
    private Stage MapAfter(Stage current)
    {
        var maps = _config.Maps;
        if (maps.Count <= 1) return maps.Count == 1 ? maps[0] : current;
        int index = 0;
        for (int i = 0; i < maps.Count; i++)
            if (maps[i] == current) index = i;
        switch (_rotation)
        {
            case MapRotation.Cycle:
                return maps[(index + 1) % maps.Count];
            case MapRotation.Random:
                int pick = _mapRandom.Next(maps.Count - 1);
                return maps[pick >= index ? pick + 1 : pick];
            default:
                return current;
        }
    }

    private void EnterWaiting()
    {
        State = RoomState.Waiting;
        StateTimer = 0;
        RoundTick = 0;
        _knives.Clear();
        ResetPushables();
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
            StageId = _stage.Id,
        };
        foreach (var e in _round) packet.Participants.Add(e.Id);
        return packet;
    }

    private RoomInfoPacket BuildRoomInfo() => new()
    {
        HostId = HostId, NextStageId = _nextStage.Id, Rotation = _rotation, NextChosen = _nextChosen,
    };

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
        for (int i = 0; i < _boxes.Length && i < 255; i++) s.Pushables.Add(_boxes[i]);
        return s;
    }

    private void Send(byte target, IPacket packet) => Outbox.Add(new Outgoing(target, PacketCodec.EncodeFrame(packet)));

    private void Broadcast(IPacket packet) => Outbox.Add(new Outgoing(0, PacketCodec.EncodeFrame(packet)));
}
