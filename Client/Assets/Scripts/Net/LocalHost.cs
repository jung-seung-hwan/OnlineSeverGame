using System;
using System.Net.Sockets;
using System.Threading;
using OnlineSeverGame.Server;
using OnlineSeverGame.Shared.Protocol;
using UnityEngine;

namespace OnlineSeverGame.Client.Net
{
    // 방장 PC에서 게임 서버를 이 프로세스 안에 띄운다(docs/work/player-host.md).
    //  - 서버 코드는 Shared/Server(콘솔 서버와 같은 GameServer). 스레드 셋(받기·게임 루프·연결마다 송수신)은 서버가 직접 만든다.
    //  - 첫 판은 방장이 '시작'을 눌러야 시작(HostStarts), 혼자서도 시작할 수 있다(MinPlayers 1).
    //  - 공유기 포트는 UPnP로 열어 본다(백그라운드 스레드). 결과와 내부·공인 주소를 화면에 보여 준다.
    //  - 로그에는 접속한 사람의 IP를 적지 않는다(LogAddresses = false). 로그 줄은 Unity 로그(Player.log)로 간다.
    public sealed class LocalHost
    {
        public enum PortState
        {
            Trying,  // UPnP 시도 중
            Opened,  // 공유기가 포트를 열어 줌
            Failed,  // UPnP 없음·거절: 직접 포트포워딩 필요
        }

        public const string UpnpDescription = "Jelly Survival";

        // Store 스크린샷용(-demoaddress): 공유기에 묻지 않고 문서용 예시 주소(203.0.113.x, 192.168.0.x)와
        // '자동으로 열었습니다'를 보인다. 서버는 진짜로 열린다.
        public static bool DemoAddress;

        readonly object _gate = new object();
        GameServer _server;
        UpnpPortMapper _mapper;
        Thread _upnpThread;
        PortState _portState = PortState.Trying;
        string _lanIp = "", _publicIp = "", _upnpError = "";

        public int Port { get; private set; }

        public PortState Ports { get { lock (_gate) return _portState; } }
        public string LanIp { get { lock (_gate) return _lanIp; } }
        public string PublicIp { get { lock (_gate) return _publicIp; } }
        public string UpnpError { get { lock (_gate) return _upnpError; } }

        // 서버를 연다. 실패하면(포트 사용 중 등) false 와 사용자에게 보일 문구.
        public bool Start(int port, out string error)
        {
            error = null;
            Port = port;
            Log.Sink = line => Debug.Log("[Host] " + line);
            var server = new GameServer(new ServerOptions
            {
                Port = port, MinPlayers = 1, HostStarts = true, LogAddresses = false, Seed = Environment.TickCount,
            });
            try
            {
                server.Start();
            }
            catch (SocketException ex)
            {
                Debug.LogWarning($"[Host] cannot listen on {port}: {ex.SocketErrorCode}");
                error = ex.SocketErrorCode == SocketError.AddressAlreadyInUse
                    ? Model.Loc.F("host.portBusy", port)
                    : Model.Loc.F("host.startFailed", ex.SocketErrorCode);
                return false;
            }
            _server = server;
            lock (_gate) _lanIp = LocalAddress.GetLanIPv4() ?? "";

            if (DemoAddress)
            {
                lock (_gate)
                {
                    _lanIp = "192.168.0.10";
                    _publicIp = "203.0.113.25";
                    _portState = PortState.Opened;
                }
                return true;
            }
            _mapper = new UpnpPortMapper(port, UpnpDescription);
            var mapper = _mapper;
            _upnpThread = new Thread(() => MapPort(mapper)) { IsBackground = true, Name = "upnp" };
            _upnpThread.Start();
            return true;
        }

        void MapPort(UpnpPortMapper mapper)
        {
            var result = mapper.TryMap();
            lock (_gate)
            {
                if (!string.IsNullOrEmpty(result.LocalIp)) _lanIp = result.LocalIp;
                _publicIp = result.ExternalIp ?? "";
                _upnpError = result.Error ?? "";
                _portState = result.Success ? PortState.Opened : PortState.Failed;
            }
            Debug.Log(result.Success
                ? $"[Host] UPnP opened TCP {Port} (public {result.ExternalIp})"
                : $"[Host] UPnP failed: {result.Error}");
        }

        // 공인 IP가 사설 대역이면(공유기 두 대 등) 밖에서는 이 주소로 들어올 수 없다.
        public bool PublicIpIsPrivate
        {
            get
            {
                string ip = PublicIp;
                return !string.IsNullOrEmpty(ip) && LocalAddress.IsPrivate(ip);
            }
        }

        // 친구에게 알려 줄 주소: 인터넷용(공인 IP) 이 있으면 그것, 없으면 같은 공유기용(내부 IP).
        public string ShareAddress
        {
            get
            {
                string pub = PublicIp, lan = LanIp;
                if (!string.IsNullOrEmpty(pub) && !LocalAddress.IsPrivate(pub)) return pub + ":" + Port;
                return string.IsNullOrEmpty(lan) ? "" : lan + ":" + Port;
            }
        }

        // 서버를 닫는다. 연결을 정리하고(최대 1초), 공유기 포트는 백그라운드에서 닫는다(blocking=true 면 최대 2초 기다림).
        public void Stop(bool blocking)
        {
            var server = _server;
            _server = null;
            if (server != null)
            {
                server.StopAndWait(1000);
                Debug.Log("[Host] server stopped");
            }
            var mapper = _mapper;
            var upnp = _upnpThread;
            _mapper = null;
            _upnpThread = null;
            if (mapper == null) return;
            var unmap = new Thread(() =>
            {
                upnp?.Join(5000); // 아직 여는 중이면 끝난 뒤에 닫는다
                mapper.Unmap();
            }) { IsBackground = true, Name = "upnp-unmap" };
            unmap.Start();
            if (blocking) unmap.Join(2500);
        }

        public static int DefaultPort => ProtocolConstants.DefaultPort;
    }
}
