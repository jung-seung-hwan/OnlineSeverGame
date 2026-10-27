using System.Net.Sockets;
using OnlineSeverGame.Shared.Protocol;
using OnlineSeverGame.Shared.Simulation;

namespace OnlineSeverGame.Server;

public enum NetEventKind
{
    Join,
    Input,
    Leave,
}

/// <summary>What a receive thread hands to the game loop.</summary>
public readonly record struct NetEvent(
    NetEventKind Kind, Connection Connection, string Name = null, ushort Version = 0,
    uint Seq = 0, InputButtons Buttons = InputButtons.None, LeaveReason Reason = LeaveReason.Disconnected);

/// <summary>
/// One TCP client. Two threads of its own:
///  - receive thread: blocking reads → FrameDecoder → packets → NetEvents on the server queue
///    (Ping is answered right here, it needs no game state);
///  - send thread: drains a bounded frame queue to the socket so a slow client never blocks the game loop.
/// Close() is idempotent and posts exactly one Leave event.
/// </summary>
public sealed class Connection
{
    public const int HelloTimeoutMs = 5000;
    public const int IdleTimeoutMs = 10000;
    public const int MaxQueuedFrames = 256;

    private static int _nextId;

    private readonly TcpClient _client;
    private readonly NetworkStream _stream;
    private readonly GameServer _server;
    private readonly object _sendLock = new();
    private readonly Queue<byte[]> _sendQueue = new();
    private bool _flushThenClose; // guarded by _sendLock
    private int _closed;           // 0/1, Interlocked

    public Connection(TcpClient client, GameServer server)
    {
        _client = client;
        _client.NoDelay = true;
        _stream = client.GetStream();
        _server = server;
        Id = Interlocked.Increment(ref _nextId);
        Remote = client.Client.RemoteEndPoint?.ToString() ?? "?";
    }

    public int Id { get; }
    public string Remote { get; }

    /// <summary>Assigned by the game loop after a successful join; only the game loop reads or writes it.</summary>
    public byte PlayerId { get; set; }

    public bool IsClosed => Volatile.Read(ref _closed) != 0;

    public void Start()
    {
        new Thread(ReceiveLoop) { IsBackground = true, Name = $"recv-{Id}" }.Start();
        new Thread(SendLoop) { IsBackground = true, Name = $"send-{Id}" }.Start();
    }

    /// <summary>Queues a complete frame. Thread-safe. A client that stops reading is dropped.</summary>
    public void Send(byte[] frame)
    {
        bool overflow = false;
        lock (_sendLock)
        {
            if (_closed != 0 || _flushThenClose) return;
            if (_sendQueue.Count >= MaxQueuedFrames) overflow = true;
            else
            {
                _sendQueue.Enqueue(frame);
                Monitor.Pulse(_sendLock);
            }
        }
        if (overflow)
        {
            Log.Info($"conn {Id} send queue overflow, dropping");
            Close(LeaveReason.Timeout);
        }
    }

    /// <summary>Sends what is queued (e.g. a Reject), then closes.</summary>
    public void CloseAfterFlush()
    {
        lock (_sendLock)
        {
            _flushThenClose = true;
            Monitor.Pulse(_sendLock);
        }
    }

    public void Close(LeaveReason reason)
    {
        if (Interlocked.Exchange(ref _closed, 1) != 0) return;
        lock (_sendLock) Monitor.Pulse(_sendLock);
        try { _client.Close(); } catch { /* already gone */ }
        _server.Events.Enqueue(new NetEvent(NetEventKind.Leave, this, Reason: reason));
    }

    private void ReceiveLoop()
    {
        var buffer = new byte[4096];
        var decoder = new FrameDecoder();
        bool joined = false;
        LeaveReason reason = LeaveReason.Disconnected;
        _client.ReceiveTimeout = HelloTimeoutMs;
        try
        {
            while (true)
            {
                int n = _stream.Read(buffer, 0, buffer.Length);
                if (n == 0) break; // orderly shutdown by the peer
                decoder.Push(buffer, 0, n);
                while (decoder.TryReadFrame(out byte[] payload))
                {
                    IPacket packet = PacketCodec.DecodePayload(payload);
                    if (!joined)
                    {
                        if (packet is not HelloPacket hello) throw new PacketFormatException("Expected Hello first");
                        joined = true;
                        _client.ReceiveTimeout = IdleTimeoutMs;
                        _server.Events.Enqueue(new NetEvent(NetEventKind.Join, this, hello.Name, hello.ProtocolVersion));
                        continue;
                    }
                    switch (packet)
                    {
                        case InputPacket input:
                            _server.Events.Enqueue(new NetEvent(NetEventKind.Input, this, Seq: input.Seq, Buttons: input.Buttons));
                            break;
                        case PingPacket ping:
                            Send(PacketCodec.EncodeFrame(new PongPacket { ClientTime = ping.ClientTime, ServerTick = _server.CurrentTick }));
                            break;
                        default:
                            throw new PacketFormatException($"Unexpected {packet.Type} from client");
                    }
                }
            }
        }
        catch (IOException ex) when (ex.InnerException is SocketException { SocketErrorCode: SocketError.TimedOut })
        {
            reason = LeaveReason.Timeout;
        }
        catch (PacketFormatException ex)
        {
            Log.Info($"conn {Id} protocol error: {ex.Message}");
            reason = LeaveReason.ProtocolError;
        }
        catch (IOException) { }
        catch (ObjectDisposedException) { }
        catch (SocketException) { }
        Close(reason);
    }

    private void SendLoop()
    {
        try
        {
            while (true)
            {
                byte[] frame;
                lock (_sendLock)
                {
                    while (_sendQueue.Count == 0 && _closed == 0 && !_flushThenClose)
                        Monitor.Wait(_sendLock);
                    if (_closed != 0) return;
                    if (_sendQueue.Count == 0) break; // flushed, now close
                    frame = _sendQueue.Dequeue();
                }
                _stream.Write(frame, 0, frame.Length);
            }
        }
        catch (IOException) { }
        catch (ObjectDisposedException) { }
        catch (SocketException) { }
        Close(LeaveReason.Disconnected);
    }
}
