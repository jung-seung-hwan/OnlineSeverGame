using System;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Threading;
using OnlineSeverGame.Shared.Protocol;

namespace OnlineSeverGame.Client.Net
{
    public enum NetEventKind
    {
        Connected,
        Packet,
        Disconnected,
    }

    public struct NetEvent
    {
        public NetEventKind Kind;
        public IPacket Packet;
        public string Reason; // Disconnected 일 때
    }

    // TCP 연결 하나. 스레드 3개를 쓴다.
    //   수신 스레드: 연결 → 읽기 → FrameDecoder → PacketCodec.DecodePayload → 이벤트 큐
    //   송신 스레드: 송신 큐에서 프레임을 꺼내 쓴다(메인 스레드가 소켓 쓰기에 막히지 않게).
    //   메인 스레드: Send() 로 넣고 TryDequeue() 로 이벤트를 꺼낸다. Unity API 는 메인 스레드에서만.
    public sealed class NetClient : IDisposable
    {
        const int ConnectTimeoutMs = 5000;

        readonly ConcurrentQueue<NetEvent> _events = new ConcurrentQueue<NetEvent>();
        readonly BlockingCollection<byte[]> _sendQueue = new BlockingCollection<byte[]>(new ConcurrentQueue<byte[]>(), 256);
        readonly object _closeLock = new object();
        TcpClient _tcp;
        volatile NetworkStream _stream;
        Thread _recvThread;
        Thread _sendThread;
        volatile bool _closed;
        bool _disconnectReported;

        public string Host { get; private set; }
        public int Port { get; private set; }
        public bool IsConnected => _stream != null && !_closed;

        // 수신 측 통계(디버그 표시용)
        public long BytesReceived => Interlocked.Read(ref _bytesReceived);
        public long BytesSent => Interlocked.Read(ref _bytesSent);
        long _bytesReceived;
        long _bytesSent;

        public void Connect(string host, int port)
        {
            Host = host;
            Port = port;
            _recvThread = new Thread(ReceiveLoop) { IsBackground = true, Name = "NetClient.Receive" };
            _recvThread.Start();
        }

        public bool TryDequeue(out NetEvent e) => _events.TryDequeue(out e);

        // 메인 스레드에서 호출. 연결 전이거나 닫혔으면 버린다.
        public void Send(IPacket packet)
        {
            if (_closed || _stream == null) return;
            byte[] frame = PacketCodec.EncodeFrame(packet);
            if (!_sendQueue.TryAdd(frame))
                Fail("송신 큐가 가득 찼습니다 (서버 응답 없음)");
        }

        void ReceiveLoop()
        {
            try
            {
                var tcp = new TcpClient { NoDelay = true, ReceiveBufferSize = 64 * 1024, SendBufferSize = 16 * 1024 };
                lock (_closeLock)
                {
                    if (_closed) { tcp.Close(); return; }
                    _tcp = tcp;
                }
                var connect = tcp.ConnectAsync(Host, Port);
                if (!connect.Wait(ConnectTimeoutMs))
                {
                    Fail($"서버에 연결할 수 없습니다 ({Host}:{Port}, 시간 초과)");
                    return;
                }
                if (connect.IsFaulted) throw connect.Exception.GetBaseException();
                if (_closed) return;

                _stream = tcp.GetStream();
                _sendThread = new Thread(SendLoop) { IsBackground = true, Name = "NetClient.Send" };
                _sendThread.Start();
                _events.Enqueue(new NetEvent { Kind = NetEventKind.Connected });

                var decoder = new FrameDecoder();
                var buffer = new byte[16 * 1024];
                while (!_closed)
                {
                    int n = _stream.Read(buffer, 0, buffer.Length);
                    if (n <= 0)
                    {
                        Fail("서버가 연결을 끊었습니다");
                        return;
                    }
                    Interlocked.Add(ref _bytesReceived, n);
                    decoder.Push(buffer, 0, n);
                    while (decoder.TryReadFrame(out byte[] payload))
                    {
                        IPacket packet = PacketCodec.DecodePayload(payload);
                        _events.Enqueue(new NetEvent { Kind = NetEventKind.Packet, Packet = packet });
                    }
                }
            }
            catch (PacketFormatException ex)
            {
                Fail("잘못된 패킷: " + ex.Message);
            }
            catch (Exception ex)
            {
                if (!_closed) Fail(Describe(ex));
            }
        }

        void SendLoop()
        {
            try
            {
                foreach (byte[] frame in _sendQueue.GetConsumingEnumerable())
                {
                    if (_closed) break;
                    _stream.Write(frame, 0, frame.Length);
                    Interlocked.Add(ref _bytesSent, frame.Length);
                }
            }
            catch (Exception ex)
            {
                if (!_closed) Fail(Describe(ex));
            }
        }

        static string Describe(Exception ex)
        {
            if (ex is AggregateException agg) ex = agg.GetBaseException();
            if (ex is System.IO.IOException && ex.InnerException != null) ex = ex.InnerException;
            if (ex is SocketException se)
            {
                switch (se.SocketErrorCode)
                {
                    case SocketError.ConnectionRefused: return "서버가 응답하지 않습니다 (연결 거부)";
                    case SocketError.ConnectionReset: return "서버와의 연결이 끊어졌습니다";
                    case SocketError.HostNotFound:
                    case SocketError.NoData: return "서버 주소를 찾을 수 없습니다";
                    case SocketError.TimedOut: return "서버 응답 시간 초과";
                }
                return "네트워크 오류: " + se.SocketErrorCode;
            }
            return "네트워크 오류: " + ex.Message;
        }

        // 오류로 끊김. 이벤트는 한 번만 보낸다.
        void Fail(string reason)
        {
            lock (_closeLock)
            {
                if (_disconnectReported) return;
                _disconnectReported = true;
            }
            _events.Enqueue(new NetEvent { Kind = NetEventKind.Disconnected, Reason = reason });
            CloseSocket();
        }

        void CloseSocket()
        {
            lock (_closeLock)
            {
                if (_closed) return;
                _closed = true;
                _sendQueue.CompleteAdding();
                try { _tcp?.Client?.Shutdown(SocketShutdown.Both); } catch (Exception) { }
                try { _tcp?.Close(); } catch (Exception) { }
            }
        }

        // 사용자가 나갈 때. Disconnected 이벤트는 보내지 않는다.
        public void Dispose()
        {
            lock (_closeLock) _disconnectReported = true;
            CloseSocket();
        }
    }
}
