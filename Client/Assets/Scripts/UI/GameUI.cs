using System;
using OnlineSeverGame.Client.Game;
using OnlineSeverGame.Client.Model;
using OnlineSeverGame.Shared.Protocol;
using UnityEngine;
using UnityEngine.UIElements;

namespace OnlineSeverGame.Client.UI
{
    // 화면 UI 의 뿌리(UI Toolkit, docs/work/ui-revamp.md). 예전 IMGUI 화면을 같은 기능 그대로 다시 만들었다.
    //   접속·접속 중(ConnectView) → 대기·카운트다운(LobbyView) → 진행·관전(HudView) → 결과(ResultView, HUD 위)
    //   설정(SettingsView)은 어느 화면에서나 위에 뜬다(버튼 또는 Esc). 이름표·내 대시 게이지는 NameTagLayer.
    // 크기: 1280×720 을 기준 점으로 두고 창이 바뀌면 min(폭/1280, 높이/720) 배로 키운다. 그래서 어느 창에서도
    // 1280×720 상자가 통째로 들어가고(잘리지 않음), 글자는 그 크기로 다시 그려져 흐려지지 않는다.
    // 맵 선택 키(방장): 1~4 다음 맵, R 교체 방식(순환 → 무작위 → 고정). 접속 뒤 어느 화면에서나 된다(OnGUI 키 이벤트).
    // 글은 모두 Loc 표(한국어/영어). 매 프레임 지금 언어로 글을 다시 넣으므로(바뀐 것만) 언어를 바꾸면 바로 바뀐다.
    public sealed class GameUI : MonoBehaviour
    {
        public const float RefWidth = 1280f, RefHeight = 720f;

        public GameBootstrap Game;

        PanelSettings _panelSettings;
        UIDocument _doc;
        VisualElement _veil;
        ConnectView _connect;
        LobbyView _lobby;
        HudView _hud;
        ResultView _result;
        SettingsView _settings;
        NameTagLayer _tags;
        bool _hooksDirty;
        bool _built;

        ClientScreen _shown = (ClientScreen)(-1);
        float _veilAt = -10f;

        public VisualElement Root { get; private set; }
        public float PanelScale { get; private set; } = 1f;
        // 패널 점 단위 화면 크기(폭은 1280 이상, 높이는 720 이상)
        public Vector2 PanelSize => new Vector2(UnityEngine.Screen.width / PanelScale, UnityEngine.Screen.height / PanelScale);
        public ClientScreen Shown => _shown;
        public bool SettingsOpen => _settings != null && _settings.IsOpen;
        public float Now => Time.unscaledTime;

        void Start()
        {
            _panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            _panelSettings.name = "JellyPanel";
            _panelSettings.themeStyleSheet = Resources.Load<ThemeStyleSheet>("UI/JellyTheme");
            _panelSettings.scaleMode = PanelScaleMode.ConstantPixelSize;
            _panelSettings.scale = 1f;
            _panelSettings.sortingOrder = 10;
            _panelSettings.clearColor = false;

            var go = new GameObject("UI Document");
            go.SetActive(false);
            go.transform.SetParent(transform, false);
            _doc = go.AddComponent<UIDocument>();
            _doc.panelSettings = _panelSettings;
            go.SetActive(true);

            Root = _doc.rootVisualElement;
            var sheet = Resources.Load<StyleSheet>("UI/Jelly");
            if (sheet != null) Root.styleSheets.Add(sheet);
            else Debug.LogError("[UI] Resources/UI/Jelly.uss not found");
            Root.AddToClassList("ui-root");
            Root.pickingMode = PickingMode.Ignore;
            UiFonts.Apply(Root);

            _tags = new NameTagLayer(this);
            Root.Add(_tags.Root);
            _lobby = new LobbyView(this);
            Root.Add(_lobby.Root);
            _hud = new HudView(this);
            Root.Add(_hud.Root);
            _result = new ResultView(this);
            Root.Add(_result.Root);
            _connect = new ConnectView(this);
            Root.Add(_connect.Root);
            _veil = UiKit.El("layer");
            _veil.pickingMode = PickingMode.Ignore;
            _veil.style.backgroundColor = new Color(0.08f, 0.06f, 0.11f, 1f);
            _veil.style.opacity = 0f;
            Root.Add(_veil);
            _settings = new SettingsView(this);
            Root.Add(_settings.Root);

            UiHooks.Changed += () => _hooksDirty = true;
            _built = true;

            foreach (var a in Environment.GetCommandLineArgs())
                if (a == "-uitest") gameObject.AddComponent<UiTest>().Ui = this;
        }

        void LateUpdate()
        {
            if (!_built) return;
            PanelScale = Mathf.Max(0.1f, Mathf.Min(UnityEngine.Screen.width / RefWidth, UnityEngine.Screen.height / RefHeight));
            if (!Mathf.Approximately(_panelSettings.scale, PanelScale)) _panelSettings.scale = PanelScale;

            if (_hooksDirty)
            {
                _hooksDirty = false;
                _settings.RebuildSections();
                _hud.RebuildSlots();
            }

            var session = Game.Session;
            var state = session?.State;
            var screen = state?.Screen ?? ClientScreen.Connect;
            if (screen != _shown) OnScreenChanged(_shown, screen);

            float now = Now;
            bool title = screen == ClientScreen.Connect || screen == ClientScreen.Connecting;
            bool lobby = screen == ClientScreen.Lobby || screen == ClientScreen.Countdown;
            bool hud = screen == ClientScreen.Playing || screen == ClientScreen.Spectating || screen == ClientScreen.Result;
            _connect.SetVisible(title, now);
            _lobby.SetVisible(lobby, now);
            _hud.SetVisible(hud, now);
            _result.SetVisible(screen == ClientScreen.Result, now);
            _tags.SetVisible(!title, now);

            if (title) _connect.Refresh(screen, now);
            if (lobby) _lobby.Refresh(session, now);
            if (hud) _hud.Refresh(session, now);
            if (screen == ClientScreen.Result) _result.Refresh(session, now);
            if (!title) _tags.Refresh(session);
            _settings.Refresh(screen, now);

            _connect.AnimateEnter(now);
            _lobby.AnimateEnter(now);
            _hud.AnimateEnter(now);
            _result.AnimateEnter(now);

            // 화면 묶음이 바뀔 때(타이틀 ↔ 게임) 어두운 막이 0.35초 동안 걷힌다.
            float v = 1f - (now - _veilAt) / 0.35f;
            float veil = v > 0f ? 0.75f * v * v : 0f;
            if (!Mathf.Approximately(_veil.style.opacity.value, veil)) _veil.style.opacity = veil;

            HandleKeys(screen);
        }

        void OnScreenChanged(ClientScreen from, ClientScreen to)
        {
            bool wasTitle = from == ClientScreen.Connect || from == ClientScreen.Connecting || (int)from < 0;
            bool isTitle = to == ClientScreen.Connect || to == ClientScreen.Connecting;
            if (wasTitle != isTitle && (int)from >= 0) _veilAt = Now;
            if (!isTitle) _connect.BlurFields();
            _shown = to;
            UiHooks.RaiseScreenChanged(from, to);
        }

        void HandleKeys(ClientScreen screen)
        {
            if (UnityEngine.Input.GetKeyDown(KeyCode.Escape)) _settings.Toggle();
            if (screen == ClientScreen.Connect && !SettingsOpen &&
                (UnityEngine.Input.GetKeyDown(KeyCode.Return) || UnityEngine.Input.GetKeyDown(KeyCode.KeypadEnter)))
                _connect.TryJoin();
        }

        // 방장 맵 선택 키: 예전과 같은 처리(키 이벤트로 받아 쓰고 Use). 접속·접속 중 화면에서는 이름 입력과 겹치므로 받지 않는다.
        void OnGUI()
        {
            var e = Event.current;
            if (e.type != EventType.KeyDown) return;
            var session = Game != null ? Game.Session : null;
            if (session == null || !session.State.IsHost) return;
            var screen = session.State.Screen;
            if (screen == ClientScreen.Connect || screen == ClientScreen.Connecting) return;
            if (e.keyCode >= KeyCode.Alpha1 && e.keyCode <= KeyCode.Alpha4)
                session.RequestMap((byte)(e.keyCode - KeyCode.Alpha1 + 1), 0);
            else if (e.keyCode >= KeyCode.Keypad1 && e.keyCode <= KeyCode.Keypad4)
                session.RequestMap((byte)(e.keyCode - KeyCode.Keypad1 + 1), 0);
            else if (e.keyCode == KeyCode.R)
                session.RequestMap(0, MapPicker.NextRotation(session.State.Rotation));
            else return;
            e.Use();
        }

        // ---------------------------------------------------------------- 뷰들이 쓰는 동작

        public void OpenSettings() => _settings.Open();
        public void CloseSettings() => _settings.Close();
        public void ToggleSettings() => _settings.Toggle();
        public void Leave() => Game.Disconnect("");
    }

    // 화면 하나. 보이기 시작하면 0.28초 동안 아래에서 통통 튀어 올라오며 나타난다.
    public abstract class UiView
    {
        public VisualElement Root { get; protected set; }
        protected readonly GameUI Ui;
        bool _visible;
        float _shownAt = -10f;
        bool _settled = true;
        protected float EnterOffset = 22f;

        protected UiView(GameUI ui) { Ui = ui; }

        public bool Visible => _visible;

        public void SetVisible(bool visible, float now)
        {
            if (visible && !_visible) { _shownAt = now; _settled = false; }
            _visible = visible;
            UiKit.Show(Root, visible);
        }

        // 보인 뒤 지난 시간(초)
        protected float Age(float now) => now - _shownAt;

        public void AnimateEnter(float now)
        {
            if (!_visible || _settled) return;
            float t = (now - _shownAt) / 0.28f;
            if (t >= 1f)
            {
                Root.style.opacity = 1f;
                Root.style.translate = new Translate(0, 0);
                _settled = true;
                return;
            }
            Root.style.opacity = UiKit.EaseOut(t);
            Root.style.translate = new Translate(0, (1f - UiKit.EaseOutBack(t)) * EnterOffset);
        }
    }
}
