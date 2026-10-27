using System;
using OnlineSeverGame.Client.Input;
using OnlineSeverGame.Client.Model;
using OnlineSeverGame.Client.UI;
using OnlineSeverGame.Client.View;
using OnlineSeverGame.Shared.Simulation;
using UnityEngine;

namespace OnlineSeverGame.Client.Game
{
    // 씬에 하나만 둔다. 카메라·스테이지·뷰·UI를 코드로 만들고 세션을 바꿔 끼운다.
    //
    // 명령줄 인자(빌드·테스트용):
    //   -name <이름>  -server <host:port>  -autoconnect  -preview  -autoplay [chase](무작위 입력 / 가까운 상대 쫓아 대시)
    //   -screenshot <파일경로> -screenshotDelay <초> [-screenshotEvery <초>]  -quitAfter <초>
    //   (-screenshotEvery 를 주면 파일경로의 {n} 을 번호로 바꿔 여러 장 찍는다)
    public sealed class GameBootstrap : MonoBehaviour
    {
        public string PlayerName = "Player";
        public string ServerAddress = "127.0.0.1:7777";

        public IGameSession Session { get; private set; }
        public Camera WorldCamera { get; private set; }
        public StageLayout Layout { get; private set; }
        public string LastStatus { get; private set; } = "";

        InputCollector _input;
        StageView _stage;
        WorldView _world;
        string _screenshotPath;
        float _screenshotAt = -1f;
        float _screenshotEvery = -1f;
        int _screenshotCount;
        float _quitAt = -1f;
        ClientScreen _loggedScreen = ClientScreen.Connect;

        void Awake()
        {
            Application.runInBackground = true; // 창 두 개로 시험할 때 비활성 창도 계속 돌아야 한다.
            Application.targetFrameRate = 60;
            QualitySettings.vSyncCount = 0;

            PlayerName = PlayerPrefs.GetString("name", "Player" + UnityEngine.Random.Range(100, 1000));
            ServerAddress = PlayerPrefs.GetString("server", ServerAddress);

            Layout = StageLayout.FromShared(Stage.Default);
            WorldCamera = CreateCamera();
            _stage = new GameObject("Stage").AddComponent<StageView>();
            _stage.Build(Layout);
            _world = new GameObject("World").AddComponent<WorldView>();
            _world.Init(Layout);
            _input = gameObject.AddComponent<InputCollector>();
            var ui = gameObject.AddComponent<GameUI>();
            ui.Game = this;

            ParseArgs();
        }

        void ParseArgs()
        {
            var args = Environment.GetCommandLineArgs();
            bool autoConnect = false, preview = false;
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                string next = i + 1 < args.Length ? args[i + 1] : null;
                switch (a)
                {
                    case "-name": if (next != null) PlayerName = next; break;
                    case "-server": if (next != null) ServerAddress = next; break;
                    case "-autoconnect": autoConnect = true; break;
                    case "-preview": preview = true; break;
                    case "-autoplay":
                        _input.AutoPlay = true;
                        _input.AutoChase = next == "chase";
                        break;
                    case "-screenshot": _screenshotPath = next; break;
                    case "-screenshotDelay": if (float.TryParse(next, out var d)) _screenshotAt = d; break;
                    case "-screenshotEvery": if (float.TryParse(next, out var e)) _screenshotEvery = e; break;
                    case "-quitAfter": if (float.TryParse(next, out var q)) _quitAt = q; break;
                }
            }
            if (_screenshotPath != null && _screenshotAt < 0f) _screenshotAt = 3f;
            if (preview) StartPreview();
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
                Session.Update(Time.deltaTime, _input);
                _world.Apply(Session.World);
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

        public void StartPreview()
        {
            CloseSession();
            SavePrefs();
            LastStatus = "";
            Session = new PreviewSession(Layout, PlayerName);
        }

        public void Connect()
        {
            CloseSession();
            SavePrefs();
            LastStatus = "";
            Session = new NetworkSession(Layout, PlayerName, ServerAddress);
        }

        public void Disconnect(string reason)
        {
            CloseSession();
            LastStatus = reason ?? "";
        }

        void CloseSession()
        {
            Session?.Close();
            Session = null;
            _world.Clear();
        }

        void SavePrefs()
        {
            PlayerPrefs.SetString("name", PlayerName);
            PlayerPrefs.SetString("server", ServerAddress);
            PlayerPrefs.Save();
        }

        void OnApplicationQuit() => CloseSession();
    }
}
