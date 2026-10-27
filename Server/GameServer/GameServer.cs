using System.Net;
using System.Net.Sockets;
using OnlineSeverGame.Shared.Protocol;
using OnlineSeverGame.Shared.Simulation;

namespace OnlineSeverGame.Server;

public sealed class ServerOptions
{
    public int Port = ProtocolConstants.DefaultPort;
    public int MinPlayers = 2;
    public int Seed = Environment.TickCount;
    /// <summary>Stop the server after this many rounds (0 = run forever). For scripted checks.</summary>
    public int ExitAfterRounds;
    /// <summary>Log every dash push instead of only the per-round count.</summary>
    public bool Verbose;
}

/// <summary>
/// Threads:
///  - accept thread: TcpListener.AcceptTcpClient loop, starts a Connection per client;
///  - per connection: a receive thread and a send thread (see Connection);
///  - game loop thread: every 50 ms drains the event queue into the GameRoom, ticks it, dispatches frames.
/// GameRoom and the player→connection map are touched only by the game loop thread.
/// </summary>
public sealed class GameServer
{
    private readonly ServerOptions _options;
    private readonly GameRoom _room;
    private readonly TcpListener _listener;
    private readonly Dictionary<byte, Connection> _byPlayer = new();
    private readonly List<Connection> _all = new(); // guarded by itself, for shutdown
    private Thread _acceptThread;
    private Thread _loopThread;
    private volatile bool _running;
    private int _currentTick;

    public GameServer(ServerOptions options)
    {
        _options = options;
        _room = new GameRoom(new RoomConfig { MinPlayers = options.MinPlayers, Seed = options.Seed, LogPushes = options.Verbose }, msg => Log.Info(msg));
        _listener = new TcpListener(IPAddress.Any, options.Port);
    }

    public LockedQueue<NetEvent> Events { get; } = new();

    /// <summary>Last processed server tick; read by receive threads to answer Ping.</summary>
    public uint CurrentTick => (uint)Volatile.Read(ref _currentTick);

    public void Start()
    {
        _listener.Start();
        _running = true;
        _acceptThread = new Thread(AcceptLoop) { IsBackground = true, Name = "accept" };
        _loopThread = new Thread(GameLoop) { Name = "game-loop" };
        _acceptThread.Start();
        _loopThread.Start();
        Log.Info($"server listening on port {_options.Port}, {GameConstants.TickRate} ticks/s, " +
                 $"min players {_options.MinPlayers}, seed {_options.Seed}, stage '{_room.Stage.Name}'");
    }

    public void Stop()
    {
        _running = false;
    }

    public void WaitForExit() => _loopThread.Join();

    private void AcceptLoop()
    {
        while (_running)
        {
            TcpClient client;
            try
            {
                client = _listener.AcceptTcpClient();
            }
            catch (SocketException) when (!_running) { break; }
            catch (ObjectDisposedException) { break; }
            catch (SocketException ex)
            {
                Log.Info($"accept failed: {ex.SocketErrorCode}");
                continue;
            }

            var conn = new Connection(client, this);
            lock (_all) _all.Add(conn);
            Log.Info($"conn {conn.Id} accepted from {conn.Remote}");
            conn.Start();
        }
    }

    private void GameLoop()
    {
        var events = new List<NetEvent>();
        using var timer = new TickTimer(GameConstants.TickMilliseconds);
        Log.Info($"game loop timer: {(timer.HighResolution ? "1 ms system timer resolution" : "default system timer resolution")}");
        int stopCountdown = -1;

        while (_running)
        {
            events = Events.Swap(events);
            foreach (var e in events) Handle(e);

            _room.Tick();
            Volatile.Write(ref _currentTick, (int)_room.ServerTick);
            Dispatch();

            if (_options.ExitAfterRounds > 0 && stopCountdown < 0 && _room.CompletedRounds >= _options.ExitAfterRounds)
            {
                Log.Info($"completed {_room.CompletedRounds} round(s), stopping (--exit-after-rounds)");
                stopCountdown = GameConstants.TickRate; // one more second so RoundEnd reaches everyone
            }
            if (stopCountdown > 0 && --stopCountdown == 0) break;

            if (_room.ServerTick % (GameConstants.TickRate * 30) == 0)
            {
                var (avg, max) = timer.TakeStats();
                Log.Info($"tick {_room.ServerTick}: {_room.PlayerCount} players, state {_room.State}, " +
                         $"tick late avg {avg:0.00} ms / max {max:0.00} ms, skipped {timer.SkippedTicks}");
            }

            timer.WaitNext();
        }

        Shutdown();
    }

    private void Handle(NetEvent e)
    {
        var conn = e.Connection;
        switch (e.Kind)
        {
            case NetEventKind.Join:
            {
                if (conn.IsClosed) return;
                if (e.Version != ProtocolConstants.Version)
                {
                    Reject(conn, RejectReason.VersionMismatch);
                    return;
                }
                byte id = _room.AddPlayer(e.Name);
                if (id == 0)
                {
                    Reject(conn, RejectReason.RoomFull);
                    return;
                }
                conn.PlayerId = id;
                _byPlayer[id] = conn;
                break;
            }

            case NetEventKind.Input:
                if (conn.PlayerId != 0 && _byPlayer.TryGetValue(conn.PlayerId, out var owner) && owner == conn)
                    _room.QueueInput(conn.PlayerId, e.Seq, e.Buttons);
                break;

            case NetEventKind.Leave:
                lock (_all) _all.Remove(conn);
                if (conn.PlayerId != 0 && _byPlayer.TryGetValue(conn.PlayerId, out var current) && current == conn)
                {
                    _byPlayer.Remove(conn.PlayerId);
                    _room.RemovePlayer(conn.PlayerId, e.Reason);
                }
                Log.Info($"conn {conn.Id} closed ({e.Reason})");
                break;
        }
    }

    private void Reject(Connection conn, RejectReason reason)
    {
        Log.Info($"conn {conn.Id} rejected: {reason}");
        conn.Send(PacketCodec.EncodeFrame(new RejectPacket { Reason = reason }));
        conn.CloseAfterFlush();
    }

    private void Dispatch()
    {
        foreach (var o in _room.Outbox)
        {
            if (o.Target == 0)
            {
                foreach (var c in _byPlayer.Values) c.Send(o.Frame);
            }
            else if (_byPlayer.TryGetValue(o.Target, out var c))
            {
                c.Send(o.Frame);
            }
        }
        _room.Outbox.Clear();
    }

    private void Shutdown()
    {
        _running = false;
        try { _listener.Stop(); } catch { /* ignore */ }
        Connection[] all;
        lock (_all) all = _all.ToArray();
        foreach (var c in all) c.CloseAfterFlush();
        Thread.Sleep(200);
        foreach (var c in all) c.Close(LeaveReason.Disconnected);
        Log.Info("server stopped");
    }
}
