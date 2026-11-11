using System;
using OnlineSeverGame.Client.Input;
using OnlineSeverGame.Client.Juice;
using OnlineSeverGame.Client.Model;
using OnlineSeverGame.Client.Net;
using OnlineSeverGame.Client.UI;
using OnlineSeverGame.Client.View;
using OnlineSeverGame.Shared.Protocol;
using OnlineSeverGame.Shared.Simulation;
using UnityEngine;

namespace OnlineSeverGame.Client.Game
{
    // 씬에 하나만 둔다. 카메라·스테이지·뷰·UI를 코드로 만들고 세션을 바꿔 끼운다.
    //
    // 명령줄 인자(빌드·테스트용):
    //   -character jelly|bear|pudding(이번 실행만, 저장 안 함)
    //   -host(켜자마자 방 만들기) -autostart(방장이면 대기 화면에서 바로 '시작')  -port <N>(방 만들기 포트, 기본 7777)
    //   -demoaddress(Store 스크린샷용: 방장 주소 칸에 예시 주소, UPnP 안 함)
    //   -offline [easy|normal|hard](켜자마자 혼자 하기: 이 PC 안의 서버 + 봇 7명, docs/work/bots.md)
    //   -bots <N>(방장이면 대기 화면에서 서버 봇 N명을 넣는다. Store 스크린샷용)
    //   -name <이름>  -server <host:port>  -autoconnect  -preview  -autoplay [chase|stomp|mash|motion|pad](무작위 입력 / 가까운 상대 쫓아 대시 / 쫓아 밟기 / 대시 연타 / 동작 차례로 보이기 / 점프 발판 밀고 타기)
    //   -nostomppredict(밟기 튕김 예측 끄기, 비교 시험용)
    //   -screenshot <파일경로> -screenshotDelay <초> [-screenshotEvery <초>]  -quitAfter <초>
    //   (-screenshotEvery 를 주면 파일경로의 {n} 을 번호로 바꿔 여러 장 찍는다)
    //   -touchshot <파일경로>: 위험물이 처음 내 몸에 닿아 보이는 프레임마다 찍는다({n} = 번호, 최대 12장. 칼 판정 확인용)
    //   -blinkshot <파일경로>: 기절 면역으로 흐리게 깜빡이는 캐릭터가 보이는 프레임을 2초 간격으로 찍는다({n}, 최대 6장)
    //   -uitest [-uitestshots <폴더>]: 버튼 클릭 자동 시험(UI/UiTest.cs, docs/work/ui-revamp.md 7장)
    public sealed class GameBootstrap : MonoBehaviour
    {
        public string PlayerName = "Player";
        // 참가할 방장의 주소(host:port). 처음에는 비어 있고, 마지막으로 참가한 주소를 PlayerPrefs가 기억한다.
        // 방장이 방을 만들면 이 PC 안에 서버가 뜨고(LocalHost) 127.0.0.1 로 붙는다(docs/work/player-host.md).
        public string ServerAddress = "";
        public int HostPort = LocalHost.DefaultPort;
        // 혼자 하기 봇 난이도(PlayerPrefs "botLevel" 로 기억)
        public BotLevel OfflineLevel = BotLevel.Normal;

        // 이 PC가 방장으로 연 서버(없으면 null)
        public LocalHost Host { get; private set; }
        public bool IsHosting => Host != null;

        public IGameSession Session { get; private set; }
        public Camera WorldCamera { get; private set; }
        public StageLayout Layout { get; private set; }
        public string LastStatus { get; private set; } = "";

        InputCollector _input;
        StageView _stage;
        WorldView _world;
        JuiceDirector _juice; // 소리·파티클·멈춤·슬로모션·카메라 연출(docs/work/juice-sound.md)
        string _screenshotPath;
        float _screenshotAt = -1f;
        float _screenshotEvery = -1f;
        int _screenshotCount;
        float _quitAt = -1f;
        string _touchShotPath;
        string _blinkShotPath;
        int _blinkShots;
        float _nextBlinkShot;
        int _touchShots;
        int _seenTouches;
        ClientScreen _loggedScreen = ClientScreen.Connect;
        int _builtLayoutVersion;
        bool _autoStart, _autoStarted;
        int _autoBots;

        void Awake()
        {
            Application.runInBackground = true; // 창 두 개로 시험할 때 비활성 창도 계속 돌아야 한다.
            Application.targetFrameRate = 60;
            QualitySettings.vSyncCount = 0;

            _ = Loc.Current; // 언어를 메인 스레드에서 먼저 읽어 둔다(접속 스레드의 오류 문구도 이 언어로)
            PlayerName = PlayerPrefs.GetString("name", "Player" + UnityEngine.Random.Range(100, 1000));
            ServerAddress = PlayerPrefs.GetString("server", ServerAddress);
            if (ServerAddress == "wildmagic.co.kr:7771") ServerAddress = ""; // 예전 시험용 공개 서버 기본값은 지운다
            int savedLevel = PlayerPrefs.GetInt("botLevel", (int)BotLevel.Normal);
            if (ProtocolConstants.IsValidBotLevel((byte)savedLevel)) OfflineLevel = (BotLevel)savedLevel;

            Layout = StageLayout.FromShared(Stage.Default);
            WorldCamera = CreateCamera();
            _stage = new GameObject("Stage").AddComponent<StageView>();
            _stage.Build(Layout);
            _world = new GameObject("World").AddComponent<WorldView>();
            _world.Init(Layout);
            _builtLayoutVersion = Layout.Version;
            _input = gameObject.AddComponent<InputCollector>();
            var ui = gameObject.AddComponent<GameUI>();
            ui.Game = this;
            _juice = gameObject.AddComponent<JuiceDirector>();
            _juice.Init(this, _world);

            ParseArgs();
        }

        void ParseArgs()
        {
            var args = Environment.GetCommandLineArgs();
            bool autoConnect = false, preview = false, autoHost = false, autoOffline = false;
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                string next = i + 1 < args.Length ? args[i + 1] : null;
                switch (a)
                {
                    case "-name": if (next != null) PlayerName = next; break;
                    case "-server": if (next != null) ServerAddress = next; break;
                    case "-autoconnect": autoConnect = true; break;
                    case "-host": autoHost = true; break;
                    case "-autostart": _autoStart = true; break;
                    case "-bots": if (int.TryParse(next, out var nb) && nb > 0) _autoBots = Mathf.Min(nb, 7); break;
                    case "-demoaddress": LocalHost.DemoAddress = true; break;
                    case "-port": if (int.TryParse(next, out var port) && port > 0 && port < 65536) HostPort = port; break;
                    case "-preview": preview = true; break;
                    case "-offline":
                        autoOffline = true;
                        if (next == "easy") OfflineLevel = BotLevel.Easy;
                        else if (next == "normal") OfflineLevel = BotLevel.Normal;
                        else if (next == "hard") OfflineLevel = BotLevel.Hard;
                        break;
                    case "-lang": if (next != null && Loc.TryParse(next, out var lang)) Loc.Override(lang); break;
                    case "-character": if (next != null && CharacterChoice.TryParse(next, out var kind)) CharacterChoice.Override(kind); break;
                    case "-autoplay":
                        _input.AutoPlay = true;
                        _input.AutoChase = next == "chase" || next == "stomp";
                        _input.AutoStomp = next == "stomp";
                        _input.AutoMash = next == "mash";
                        _input.AutoMotion = next == "motion";
                        _input.AutoPad = next == "pad";
                        break;
                    case "-screenshot": _screenshotPath = next; break;
                    case "-screenshotDelay": if (float.TryParse(next, out var d)) _screenshotAt = d; break;
                    case "-screenshotEvery": if (float.TryParse(next, out var e)) _screenshotEvery = e; break;
                    case "-nostomppredict": NetworkSession.StompPrediction = false; break;
                    case "-quitAfter": if (float.TryParse(next, out var q)) _quitAt = q; break;
                    case "-touchshot": _touchShotPath = next; break;
                    case "-blinkshot": _blinkShotPath = next; break;
                }
            }
            if (_screenshotPath != null && _screenshotAt < 0f) _screenshotAt = 3f;
            if (preview) StartPreview();
            else if (autoOffline) PlayOffline(OfflineLevel);
            else if (autoHost) HostGame();
            else if (autoConnect) Connect();
        }

        Camera CreateCamera()
        {
            var cam = Camera.main;
            if (cam == null)
            {
                var go = new GameObject("Main Camera") { tag = "MainCamera" };
                cam = go.AddComponent<Camera>();
            }
            cam.orthographic = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.08f, 0.08f, 0.1f);
            cam.transform.position = new Vector3(0, 0, -10);
            return cam;
        }

        void FitCamera()
        {
            // 스테이지 전체가 항상 보이도록 화면 비율에 맞춘다.
            float aspect = Mathf.Max(0.1f, (float)UnityEngine.Screen.width / Mathf.Max(1, UnityEngine.Screen.height));
            float margin = 0.6f;
            float halfH = Layout.Height / 2f + margin;
            float halfWAsH = (Layout.Width / 2f + margin) / aspect;
            WorldCamera.orthographicSize = Mathf.Max(halfH, halfWAsH);
            WorldCamera.transform.position = new Vector3(Layout.Width / 2f, Layout.Height / 2f - 0.5f, -10);
        }

        void Update()
        {
            FitCamera();

            var screen = Session?.State.Screen ?? ClientScreen.Connect;
            // 대기·카운트다운 중에도 서버가 이동을 허용한다.
            _input.Enabled = screen == ClientScreen.Lobby || screen == ClientScreen.Countdown || screen == ClientScreen.Playing;

            if (Session != null)
            {
                if (_input.AutoChase) UpdateChaseTarget();
                if (_input.AutoPad) UpdatePadTarget();
                // 세션은 실제 시간으로 돈다(연출의 멈춤·슬로모션은 Time.timeScale 로 화면만 늦춘다)
                Session.Update(Time.unscaledDeltaTime, _input);
                // 이모트: 접속 화면(이름 입력)만 빼고. 살아 있는지는 세션·서버가 본다.
                var emote = _input.TakeEmote();
                if (emote != 0 && screen != ClientScreen.Connect) Session.SendEmote(emote);
                if (_autoBots > 0 && Session.State.IsHost && screen == ClientScreen.Lobby)
                {
                    for (; _autoBots > 0; _autoBots--) Session.RequestBot(BotAction.Add, 0);
                }
                if (_autoStart && !_autoStarted && Session.State.IsHost && Session.State.HostStarts && screen == ClientScreen.Lobby)
                {
                    _autoStarted = true;
                    Session.RequestStart();
                }
                RebuildIfMapChanged();
                _juice.Present(Session); // 보여 줄 시각의 그림으로 바꾸고 사건(소리·파티클)을 낸다
                _world.Apply(Session.World);
                _stage.Refresh(Layout);
                if (Session.State.Screen != _loggedScreen)
                {
                    _loggedScreen = Session.State.Screen;
                    Debug.Log($"[Client] screen {_loggedScreen} round={Session.State.RoundNumber} alive={Session.State.AliveCount}/{Session.State.ParticipantCount}");
                }
                if (!string.IsNullOrEmpty(Session.State.Status) && Session.State.Screen == ClientScreen.Connect)
                {
                    // 세션이 스스로 끊긴 경우(서버 종료 등)
                    Disconnect(Session.State.Status);
                }
            }

            if (_touchShotPath != null && Session != null && Session.State.HazardTouches != _seenTouches)
            {
                _seenTouches = Session.State.HazardTouches;
                if (_touchShots < 12)
                {
                    string path = _touchShotPath.Replace("{n}", (++_touchShots).ToString("00"));
                    ScreenCapture.CaptureScreenshot(path);
                    Debug.Log($"[Client] touchshot {path} touch={_seenTouches} round={Session.State.RoundNumber}");
                }
            }

            if (_blinkShotPath != null && Session != null && _blinkShots < 6 && Time.realtimeSinceStartup >= _nextBlinkShot)
            {
                foreach (var p in Session.World.Players)
                {
                    if (!p.Alive || PlayerView.BlinkAlpha(p.ImmuneTicks) >= 1f) continue;
                    string path = _blinkShotPath.Replace("{n}", (++_blinkShots).ToString("00"));
                    ScreenCapture.CaptureScreenshot(path);
                    Debug.Log($"[Client] blinkshot {path} player #{p.Id} immune={p.ImmuneTicks}");
                    _nextBlinkShot = Time.realtimeSinceStartup + 2f;
                    break;
                }
            }

            float t = Time.realtimeSinceStartup;
            if (_screenshotPath != null && _screenshotAt >= 0f && t >= _screenshotAt)
            {
                string path = _screenshotPath.Replace("{n}", (++_screenshotCount).ToString("00"));
                ScreenCapture.CaptureScreenshot(path);
                Debug.Log($"[Client] screenshot {path} screen={Session?.State.Screen}");
                _screenshotAt = _screenshotEvery > 0f ? _screenshotAt + _screenshotEvery : -1f;
            }
            if (_quitAt >= 0f && t >= _quitAt) Application.Quit();
        }

        // -autoplay pad: 점프 발판 윗면 가운데까지의 거리(docs/work/jump-pad.md)
        void UpdatePadTarget()
        {
            _input.HasChaseTarget = false;
            Vector2 me = default;
            bool found = false;
            foreach (var p in Session.World.Players)
                if (p.IsLocal && p.Alive) { me = p.Position; found = true; _input.PadGrounded = p.Grounded; }
            if (!found) return;
            foreach (var b in Session.World.Boxes)
            {
                if (!b.JumpPad) continue;
                _input.ChaseDelta = b.Position + new Vector2(0f, b.Size.y) - me;
                _input.PadHalfWidth = b.Size.x / 2f;
                _input.HasChaseTarget = true;
            }
        }

        void UpdateChaseTarget()
        {
            _input.HasChaseTarget = false;
            Vector2 me = default;
            bool found = false;
            foreach (var p in Session.World.Players)
                if (p.IsLocal && p.Alive) { me = p.Position; found = true; }
            if (!found) return;
            float best = float.MaxValue;
            foreach (var p in Session.World.Players)
            {
                if (p.IsLocal || !p.Alive) continue;
                float d = (p.Position - me).sqrMagnitude;
                if (d < best) { best = d; _input.ChaseDelta = p.Position - me; _input.HasChaseTarget = true; }
            }
        }

        // 세션이 새 맵을 받으면(Welcome, RoundStart) 레이아웃 Version 이 오른다. 지형·장식·뷰를 다시 만든다.
        void RebuildIfMapChanged()
        {
            if (Layout.Version == _builtLayoutVersion) return;
            _builtLayoutVersion = Layout.Version;
            _stage.Build(Layout);
            _world.Init(Layout);
            Debug.Log($"[Client] map {Layout.Source.Id} '{Layout.Source.Name}' built");
        }

        public void StartPreview()
        {
            CloseSession();
            Layout.SetStage(Stage.Default); // 미리보기는 주방만
            RebuildIfMapChanged();
            SavePrefs();
            LastStatus = "";
            Session = new PreviewSession(Layout, PlayerName);
        }

        // 친구의 방(ServerAddress)에 참가한다.
        public void Connect()
        {
            CloseSession();
            SavePrefs();
            LastStatus = "";
            Session = new NetworkSession(Layout, PlayerName, ServerAddress);
        }

        // 방 만들기: 이 PC 안에 서버를 띄우고 방장으로 들어간다. 포트를 못 열면 접속 화면에 이유를 보인다.
        public void HostGame()
        {
            CloseSession();
            SavePrefs();
            LastStatus = "";
            var host = new LocalHost();
            if (!host.Start(HostPort, out string error))
            {
                LastStatus = error;
                return;
            }
            Host = host;
            Session = new NetworkSession(Layout, PlayerName, "127.0.0.1:" + HostPort);
        }

        // 혼자 하기: 이 PC 안에 127.0.0.1 전용 서버를 봇 7명과 띄우고 들어간다. 판정·기절·옆 칼·맵은 온라인과 똑같다.
        public void PlayOffline(BotLevel level)
        {
            CloseSession();
            OfflineLevel = level;
            SavePrefs();
            LastStatus = "";
            var host = new LocalHost();
            if (!host.Start(0, out string error, offline: true, botLevel: level))
            {
                LastStatus = error;
                return;
            }
            Host = host;
            Session = new NetworkSession(Layout, PlayerName, "127.0.0.1:" + host.Port);
        }

        public bool IsOffline => Host != null && Host.Offline;

        public void Disconnect(string reason)
        {
            CloseSession();
            LastStatus = reason ?? "";
        }

        void CloseSession(bool quitting = false)
        {
            Session?.Close();
            Session = null;
            _world.Clear();
            Host?.Stop(blocking: quitting); // 방장이 나가면 방도 닫힌다(다른 사람은 '연결 끊김'으로 접속 화면에)
            Host = null;
        }

        void SavePrefs()
        {
            if (UiTest.Active) return; // 클릭 자동 시험은 사용자 저장값(이름·서버 주소)을 바꾸지 않는다
            PlayerPrefs.SetString("name", PlayerName);
            PlayerPrefs.SetString("server", ServerAddress);
            PlayerPrefs.SetInt("botLevel", (int)OfflineLevel);
            PlayerPrefs.Save();
        }

        void OnApplicationQuit() => CloseSession(quitting: true);
    }
}
