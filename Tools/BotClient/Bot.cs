using System.Diagnostics;
using System.Net.Sockets;
using OnlineSeverGame.Shared.Protocol;
using OnlineSeverGame.Shared.Simulation;

namespace OnlineSeverGame.BotClient;

/// <summary>
/// One fake player: a receive thread that decodes frames and an input thread that sends buttons every
/// 50 ms with an increasing seq, like a real client would. Buttons are a random wander, overridden by
/// DodgeBrain (when dodging is on) to get out from under knives it has noticed.
/// </summary>
public sealed class Bot
{
    private readonly int _index;
    private readonly string _name;
    private readonly Random _random;
    private readonly bool _verbose;
    private readonly bool _dodge;
    private readonly int _reactionTicks;
    private volatile SnapshotPacket _latest;
    private TcpClient _client;
    private NetworkStream _stream;
    private volatile bool _running;

    // Random input policy state (input thread only)
    private int _dir;
    private int _dirTicksLeft;

    public Bot(int index, string name, int seed, bool verbose, bool dodge)
    {
        _index = index;
        _name = name;
        _random = new Random(seed);
        _verbose = verbose;
        _dodge = dodge;
        _reactionTicks = _random.Next(2, 9); // 0.1 - 0.4 s to notice a warning
    }

    public int ReactionTicks => _reactionTicks;

    public string Name => _name;
    public volatile byte PlayerId;
    public volatile bool Alive;
    public volatile bool Connected;
    public volatile RoomState RoomState;
    public int RoundsEnded;           // Interlocked
    public long PlayingSinceMs = -1;  // Interlocked; ms on the shared clock when Playing was first seen this round
    public uint LastAckSeq;
    public uint Seq;
    public static readonly Stopwatch Clock = Stopwatch.StartNew();

    public void Start(string host, int port)
    {
        _client = new TcpClient { NoDelay = true };
        _client.Connect(host, port);
        _stream = _client.GetStream();
        _running = true;
        Connected = true;
        SendPacket(new HelloPacket { Name = _name });
        new Thread(ReceiveLoop) { IsBackground = true, Name = $"bot{_index}-recv" }.Start();
        new Thread(InputLoop) { IsBackground = true, Name = $"bot{_index}-input" }.Start();
    }

    /// <summary>Abrupt disconnect (RST), as if the game crashed or the cable was pulled.</summary>
    public void Drop()
    {
        _running = false;
        Connected = false;
        try
        {
            _client.Client.LingerState = new LingerOption(true, 0);
            _client.Close();
        }
        catch { /* ignore */ }
    }

    public void Stop()
    {
        _running = false;
        Connected = false;
        try { _client.Close(); } catch { /* ignore */ }
    }

    private void SendPacket(IPacket packet)
    {
        byte[] frame = PacketCodec.EncodeFrame(packet);
        lock (this) _stream.Write(frame, 0, frame.Length);
    }

    private void InputLoop()
    {
        double next = Clock.Elapsed.TotalMilliseconds;
        try
        {
            while (_running)
            {
                Seq++;
                SendPacket(new InputPacket { Seq = Seq, Buttons = NextButtons() });
                if (Seq % 100 == 0) SendPacket(new PingPacket { ClientTime = (uint)Clock.ElapsedMilliseconds });

                next += GameConstants.TickMilliseconds;
                double wait = next - Clock.Elapsed.TotalMilliseconds;
                if (wait > 0) Thread.Sleep((int)wait);
            }
        }
        catch (Exception) when (!_running) { }
        catch (Exception ex)
        {
            Console.WriteLine($"[{_name}] send failed: {ex.Message}");
            Connected = false;
        }
    }

    private InputButtons NextButtons()
    {
        InputButtons wander = Wander();
        var snap = _latest;
        if (!_dodge || snap == null || snap.RoomState != RoomState.Playing) return wander;
        foreach (var p in snap.Players)
        {
            if (p.PlayerId != PlayerId) continue;
            if (!p.Alive) return wander;
            return DodgeBrain.Decide(p.State, snap.Knives, Stage.Default, _reactionTicks, wander);
        }
        return wander;
    }

    private InputButtons Wander()
    {
        if (--_dirTicksLeft <= 0)
        {
            _dir = _random.Next(-1, 2);
            _dirTicksLeft = _random.Next(5, 25);
        }
        var b = InputButtons.None;
        if (_dir < 0) b |= InputButtons.Left;
        if (_dir > 0) b |= InputButtons.Right;
        if (_random.Next(100) < 12) b |= InputButtons.Jump;
        if (_random.Next(100) < 6) b |= InputButtons.Dash;
        if (_random.Next(100) < 3) b |= InputButtons.Down;
        return b;
    }

    private void ReceiveLoop()
    {
        var buffer = new byte[8192];
        var decoder = new FrameDecoder();
        try
        {
            while (_running)
            {
                int n = _stream.Read(buffer, 0, buffer.Length);
                if (n == 0) break;
                decoder.Push(buffer, 0, n);
                while (decoder.TryReadFrame(out byte[] payload))
                    Handle(PacketCodec.DecodePayload(payload));
            }
        }
        catch (Exception ex) when (_running)
        {
            Console.WriteLine($"[{_name}] receive failed: {ex.Message}");
        }
        catch (Exception) { }
        if (_running) Console.WriteLine($"[{_name}] disconnected by server");
        Connected = false;
        _running = false;
    }

    private void Handle(IPacket packet)
    {
        switch (packet)
        {
            case WelcomePacket w:
                PlayerId = w.PlayerId;
                Console.WriteLine($"[{_name}] welcome: player #{w.PlayerId}, server tick {w.ServerTick}");
                break;

            case RejectPacket r:
                Console.WriteLine($"[{_name}] rejected: {r.Reason}");
                _running = false;
                break;

            case RoundStartPacket rs:
                if (_verbose)
                    Console.WriteLine($"[{_name}] round {rs.RoundNumber} countdown {rs.CountdownTicks} ticks, " +
                                      $"participants {string.Join(",", rs.Participants)}");
                break;

            case SnapshotPacket s:
                _latest = s;
                if (s.RoomState == RoomState.Playing && RoomState != RoomState.Playing)
                    Interlocked.Exchange(ref PlayingSinceMs, Clock.ElapsedMilliseconds);
                RoomState = s.RoomState;
                foreach (var p in s.Players)
                {
                    if (p.PlayerId != PlayerId) continue;
                    if (Alive && !p.Alive && s.RoomState == RoomState.Playing && _verbose)
                        Console.WriteLine($"[{_name}] died at round tick {s.RoundTick}");
                    Alive = p.Alive;
                    LastAckSeq = p.LastInputSeq;
                }
                break;

            case RoundEndPacket re:
                Interlocked.Exchange(ref PlayingSinceMs, -1);
                Interlocked.Increment(ref RoundsEnded);
                if (_verbose)
                {
                    Console.WriteLine($"[{_name}] ROUND {re.RoundNumber} END, winners: " +
                                      (re.Winners.Count == 0 ? "none" : string.Join(", ", re.Winners.Select(w => "#" + w))));
                    foreach (var r in re.Results)
                        Console.WriteLine($"[{_name}]    place {r.Place}: #{r.PlayerId} {r.SurvivedTicks} ticks {r.Cause}");
                }
                break;

            case PlayerLeftPacket left:
                if (_verbose) Console.WriteLine($"[{_name}] player #{left.PlayerId} left ({left.Reason})");
                break;
        }
    }
}
